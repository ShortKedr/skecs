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
            _ => throw new ArgumentException(
                "Usage: ga [cpu|auto|vulkan <slang-adapter> <native-bridge> <output-directory>]"),
        };

        var program = CompileMovement();
        if (policy == GaPolicy.RequireGpu)
        {
            await RunOnVulkanAsync(program, args[1], args[2], args[3]);
            return;
        }

        // When no GPU artifact is supplied, startup can report its CPU decision using the system identity.
        var contract = new GaContractIdentity(
            program.SystemId,
            ContractId,
            program.SourceDigest,
            "",
            "",
            GaProgram.NumericVersion,
            GaProgram.SubsetVersion,
            GaProgram.IrVersion);
        var request = new GaSystemRequest(contract, []);
        using var startup = GaStartup.Select(policy, [request], []);
        PrintDecision(startup.Decisions.Single());

        await RunMovementAsync(program);
    }

    private static async Task RunOnVulkanAsync(
        GaProgram program,
        string slangAdapter,
        string nativeBridge,
        string outputDirectory)
    {
        using var backend = GaVulkanBackend.Open(Path.GetFullPath(nativeBridge));
        var bundle = await GaVulkanPackager.Build(
            program, ContractId, Path.GetFullPath(slangAdapter), outputDirectory, "movement");

        // The Vulkan adapter advertises an experimental profile and probes the device at startup.
        var request = new GaSystemRequest(bundle.Manifest.Contract, [new GaArtifactCandidate(bundle)]);
        using var startup = GaStartup.Select(GaPolicy.RequireGpu, [request], backend.Devices, allowExperimental: true);
        var decision = startup.Decisions.Single();
        PrintDecision(decision);
        if (!startup.Succeeded || decision.Route != GaRoute.Gpu)
        {
            throw new InvalidOperationException("Vulkan did not select a GPU for the movement kernel.");
        }

        var gpuKernel = (GaPackedKernel)startup.GetPreparedKernel(program.SystemId);
        await RunMovementAsync(program, gpuKernel);
    }

    private static GaProgram CompileMovement()
    {
        using var source = typeof(Sample).Assembly.GetManifestResourceStream("Skecs.Samples.Ga.Movement.cs")
            ?? throw new InvalidOperationException("The movement source is missing from the sample assembly.");
        using var reader = new StreamReader(source);

        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string[] assemblies =
        [
            typeof(object).Assembly.Location,
            Path.Combine(runtimeDirectory, "System.Runtime.dll"),
            typeof(World).Assembly.Location,
            typeof(SecsSystemAttribute).Assembly.Location,
        ];
        var syntaxTree = CSharpSyntaxTree.ParseText(reader.ReadToEnd(), path: "Movement.cs");
        var references = assemblies.Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "GaMovement",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var movementType = compilation.GetTypeByMetadataName(typeof(Movement).FullName!)!;
        var executeMethod = movementType.GetMembers(nameof(Movement.Execute)).OfType<IMethodSymbol>().Single();
        var result = GaCompiler.Compile(compilation, executeMethod);
        return result.Program ?? throw new InvalidDataException(string.Join(Environment.NewLine, result.Diagnostics));
    }

    private static async Task RunMovementAsync(GaProgram program, GaPackedKernel? gpuKernel = null)
    {
        Position[] positions =
        [
            new() { X = 0, Y = 0 },
            new() { X = 10, Y = -4 },
        ];
        Velocity[] velocities =
        [
            new() { X = 3, Y = 1 },
            new() { X = -1, Y = 2 },
        ];
        int wordsPerRow = program.WordCount;
        var cpuRows = new uint[positions.Length * wordsPerRow];
        for (var row = 0; row < positions.Length; row++)
        {
            Pack(program, positions[row], velocities[row], cpuRows.AsSpan(row * wordsPerRow, wordsPerRow));
        }

        // The dispatch packet starts with a row count followed by the packed components.
        var gpuInput = new uint[1 + cpuRows.Length];
        gpuInput[0] = (uint)positions.Length;
        cpuRows.CopyTo(gpuInput, 1);
        var expectedRow = new uint[wordsPerRow];
        var movement = new Movement();

        for (var tick = 1; tick <= TickCount; tick++)
        {
            for (var row = 0; row < positions.Length; row++)
            {
                var rowOffset = row * wordsPerRow;
                var fault = GaInterpreter.Execute(program, cpuRows.AsSpan(rowOffset, wordsPerRow));
                if (fault is { } cpuFault)
                {
                    throw new InvalidDataException($"CPU fault {cpuFault.Kind} at tick {tick}, row {row}.");
                }

                // Call the original C# kernel directly for the reference result.
                movement.Execute(ref positions[row], velocities[row]);
                Pack(program, positions[row], velocities[row], expectedRow);
                if (!cpuRows.AsSpan(rowOffset, wordsPerRow).SequenceEqual(expectedRow))
                {
                    throw new InvalidDataException($"The GA interpreter diverged at tick {tick}, row {row}.");
                }
            }

            if (gpuKernel is not null)
            {
                var dispatch = gpuKernel.BeginDispatch(gpuInput, wordsPerRow);
                var result = await dispatch.DrainAsync();
                if (result.FindFirstFault() is { } gpuFault)
                {
                    throw new InvalidDataException(
                        $"GPU fault {gpuFault.Fault.Kind} at tick {tick}, row {gpuFault.Row}.");
                }

                if (result.RowCount != positions.Length || !result.Output.SequenceEqual(cpuRows))
                {
                    throw new InvalidDataException($"Vulkan readback diverged from the CPU at tick {tick}.");
                }

                // Feed the device's own output into the next tick, leaving the row-count prefix in place.
                result.Output.CopyTo(gpuInput.AsSpan(1));
            }
        }

        if (positions[0] is not { X: 15, Y: 5 } || positions[1] is not { X: 5, Y: 6 })
        {
            throw new InvalidDataException("The movement fixture ended at unexpected positions.");
        }

        Console.WriteLine("Final positions after 5 ticks: (15, 5), (5, 6).");
        if (gpuKernel is not null)
        {
            Console.WriteLine("Vulkan matched the CPU on every tick.");
        }
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
        {
            Console.WriteLine($"{device.Api}: {device.Device} ({decision.Qualification}).");
        }

        if (decision.Detail is { } detail)
        {
            Console.WriteLine(detail);
        }
    }
}
