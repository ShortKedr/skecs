using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SKECS;
using SKECS.GA;
using SKECS.GA.Backend.Vulkan;

namespace Skecs.Samples.Ga;

internal static class Sample
{
    private const string ContractId = "samples.movement/1";
    private const int TickCount = 5;

    public static async Task RunAsync(string[] args)
    {
        var policy = args switch
        {
            [] or ["cpu"] => GaPolicy.CpuOnly,
            ["auto"] => GaPolicy.Auto,
            ["vulkan", _, _, _] => GaPolicy.RequireGpu,
            _ => throw new ArgumentException("Usage: ga [cpu|auto|vulkan <slang-adapter> <native-bridge> <output-directory>]")
        };

        var program = CompileMovement();
        using var backend = policy == GaPolicy.RequireGpu ? GaVulkanBackend.Open(Path.GetFullPath(args[2])) : null;
        var bundle = backend is null ? null :
            await GaVulkanPackager.Build(program, ContractId, Path.GetFullPath(args[1]), args[3], "movement");
        // With no artifact, startup only needs a system identity to report the CPU decision.
        var request = bundle is null
            ? new GaSystemRequest(new(program.SystemId, ContractId, program.SourceDigest, "", "",
                GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion), [])
            : new GaSystemRequest(bundle.Manifest.Contract, [new GaArtifactCandidate(bundle)]);
        // The Vulkan adapter advertises an experimental profile and probes the actual device at startup.
        using var startup = GaStartup.Select(policy, [request], backend?.Devices ?? [], allowExperimental: backend is not null);
        var decision = startup.Decisions.Single();
        PrintDecision(decision);
        if (!startup.Succeeded)
            throw new InvalidOperationException("Vulkan did not select a GPU for the movement kernel.");

        var kernel = decision.Route == GaRoute.Gpu ? (GaPackedKernel)startup.GetPreparedKernel(program.SystemId) : null;
        await RunMovementAsync(program, kernel);
    }

    private static GaProgram CompileMovement()
    {
        using var source = typeof(Sample).Assembly.GetManifestResourceStream("Skecs.Samples.Ga.Movement.cs")
            ?? throw new InvalidOperationException("The movement source is missing from the sample assembly.");
        using var reader = new StreamReader(source);
        string runtime = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");
        string[] assemblies = [typeof(object).Assembly.Location, runtime, typeof(World).Assembly.Location,
            typeof(SecsSystemAttribute).Assembly.Location];
        var compilation = CSharpCompilation.Create("GaMovement",
            [CSharpSyntaxTree.ParseText(reader.ReadToEnd(), path: "Movement.cs")],
            assemblies.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var execute = compilation.GetTypeByMetadataName(typeof(Movement).FullName!)!
            .GetMembers(nameof(Movement.Execute)).OfType<IMethodSymbol>().Single();
        var result = GaCompiler.Compile(compilation, execute);
        return result.Program ?? throw new InvalidDataException(string.Join(Environment.NewLine, result.Diagnostics));
    }

    private static async Task RunMovementAsync(GaProgram program, GaPackedKernel? kernel = null)
    {
        Position[] positions = [new(), new() { X = 10, Y = -4 }];
        Velocity[] velocities = [new() { X = 3, Y = 1 }, new() { X = -1, Y = 2 }];
        int width = program.WordCount;
        var cpuRows = new uint[positions.Length * width];
        for (int row = 0; row < positions.Length; row++)
            Pack(program, positions[row], velocities[row], cpuRows.AsSpan(row * width, width));

        // A device dispatch starts with a row count followed by the canonical packed component rows.
        var gpuInput = new uint[1 + cpuRows.Length];
        gpuInput[0] = (uint)positions.Length;
        cpuRows.CopyTo(gpuInput, 1);
        var expected = new uint[width];
        var movement = new Movement();

        for (int tick = 1; tick <= TickCount; tick++)
        {
            for (int row = 0; row < positions.Length; row++)
            {
                if (GaInterpreter.Execute(program, cpuRows.AsSpan(row * width, width)) is { } fault)
                    throw new InvalidDataException($"CPU fault {fault.Kind} at tick {tick}, row {row}.");
                movement.Execute(ref positions[row], velocities[row]);
                Pack(program, positions[row], velocities[row], expected);
                if (!cpuRows.AsSpan(row * width, width).SequenceEqual(expected))
                    throw new InvalidDataException($"The GA interpreter diverged at tick {tick}, row {row}.");
            }

            if (kernel is not null)
            {
                var result = await kernel.BeginDispatch(gpuInput, width).DrainAsync();
                if (result.FindFirstFault() is { } gpuFault)
                    throw new InvalidDataException($"GPU fault {gpuFault.Fault.Kind} at tick {tick}, row {gpuFault.Row}.");
                if (result.RowCount != positions.Length || !result.Output.SequenceEqual(cpuRows))
                    throw new InvalidDataException($"Vulkan readback diverged from the CPU at tick {tick}.");
                result.Output.CopyTo(gpuInput.AsSpan(1));
            }
        }

        if (positions[0] is not { X: 15, Y: 5 } || positions[1] is not { X: 5, Y: 6 })
            throw new InvalidDataException("The movement fixture ended at unexpected positions.");
        Console.WriteLine("Final positions after 5 ticks: (15, 5), (5, 6).");
        if (kernel is not null)
            Console.WriteLine("Vulkan matched the CPU on every tick.");
    }

    private static void Pack(GaProgram program, Position position, Velocity velocity, Span<uint> row)
    {
        for (int index = 0; index < program.Fields.Count; index++)
        {
            var field = program.Fields[index];
            int value = (field.Access, field.Path) switch
            {
                (GaFieldAccess.ReadWrite, nameof(Position.X)) => position.X,
                (GaFieldAccess.ReadWrite, nameof(Position.Y)) => position.Y,
                (GaFieldAccess.Read, nameof(Velocity.X)) => velocity.X,
                (GaFieldAccess.Read, nameof(Velocity.Y)) => velocity.Y,
                _ => throw new InvalidDataException($"Unexpected movement field {field.Component}.{field.Path}.")
            };
            row[index] = unchecked((uint)value);
        }
    }

    private static void PrintDecision(GaStartupDecision decision)
    {
        Console.WriteLine($"GA route: {decision.Route} ({decision.Reason}).");
        if (decision.Device is { } device)
            Console.WriteLine($"{device.Api}: {device.Device} ({decision.Qualification}).");
        if (decision.Detail is { } detail)
            Console.WriteLine(detail);
    }
}
