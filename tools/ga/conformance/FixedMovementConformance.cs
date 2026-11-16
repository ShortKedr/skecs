using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SKECS.GA;
using SKECS.GA.Backend.Vulkan;
using SKECS.Numerics;
using static GaConformanceIo;

internal static class FixedMovementConformance
{
    private const string Source = """
        using SKECS.GA;
        using SKECS.Numerics;
        public struct Position { public Fixed X; public int Cell; }
        public struct Velocity { public Fixed Delta, Step; public bool Divide; }
        [SecsSystem(SystemId = "fixtures.fixed-movement")]
        public partial struct Movement
        {
            public void Execute(ref Position position, in Velocity velocity)
            {
                var delta = velocity.Divide ? velocity.Delta / velocity.Step : velocity.Delta * velocity.Step;
                position.X += delta;
                position.Cell = position.X < Fixed.Zero ? -(int)position.X : (int)position.X;
            }
        }
        """;

    internal static async Task Compare(string compiler, string validate, string libraryPath, string directory, string[] devices)
    {
        var program = Compile();
        if (!program.UsesFixed || program.WordCount != 8)
            throw new InvalidDataException("Unexpected field layout for generated Fixed Movement.");
        var bundle = await GaVulkanPackager.Build(program, "fixtures.fixed-movement/1", compiler, directory, "fixed-movement");
        await Run(validate, "--target-env", "vulkan1.1", Path.Combine(directory, "fixed-movement.spv"));
        var invalidMap = "[{\"File\":\"FixedMovement.cs\",\"Start\":-1,\"Length\":1}]"u8.ToArray();
        var invalidManifest = bundle.Manifest with { SourceMapDigest = GaArtifactManifest.ComputeDigest(invalidMap) };
        var invalidBundle = GaArtifactBundle.Load(invalidManifest, invalidManifest.GetContentId(),
            bundle.Artifact, bundle.Reflection, invalidMap);
        foreach (string index in devices)
        {
            using var backend = GaVulkanBackend.Open(libraryPath);
            var device = backend.Devices[int.Parse(index, CultureInfo.InvariantCulture)];
            var invalidRequest = new GaSystemRequest(invalidBundle.Manifest.Contract, [new GaArtifactCandidate(invalidBundle)]);
            using var rejected = GaStartup.Select(GaPolicy.RequireGpu, [invalidRequest], [device], allowExperimental: true);
            if (rejected.Succeeded || rejected.Decisions[0].Reason != GaStartupReason.IncompatibleManifest)
                throw new InvalidDataException("Vulkan accepted a fault map with an invalid source span.");
            var request = new GaSystemRequest(bundle.Manifest.Contract, [new GaArtifactCandidate(bundle)]);
            using var startup = GaStartup.Select(GaPolicy.RequireGpu, [request], [device], allowExperimental: true);
            if (!startup.Succeeded || startup.GetPreparedKernel(program.SystemId) is not GaVulkanKernel kernel)
                throw new InvalidOperationException($"Fixed Movement backend startup failed: {startup.Decisions[0]}.");
            var positions = new long[67];
            var cells = new int[positions.Length];
            positions[1] = long.MaxValue;
            positions[2] = -(1L << 32);
            int width = program.WordCount;
            for (int tick = 0; tick < 8; tick++)
            {
                var payload = new uint[1 + positions.Length * width];
                payload[0] = (uint)positions.Length;
                var oracleRows = new uint[positions.Length * width];
                var faults = new GaKernelFault?[positions.Length];
                for (int row = 0; row < positions.Length; row++)
                {
                    long delta = row % 2 == 0 && row != 0 ? -0x60000000L : 0x60000000L;
                    long step = row == 2 ? 0 : 0x140000001L;
                    bool divide = row == 2 || row % 3 == 0;
                    uint[] words = Pack(program, positions[row], cells[row], delta, step, divide);
                    words.CopyTo(payload, 1 + row * width);
                    var expectedFault = ApplyOracle(ref positions[row], ref cells[row], delta, step, divide);
                    faults[row] = GaInterpreter.Execute(program, words);
                    if ((faults[row]?.Kind ?? 0) != expectedFault ||
                        !words.SequenceEqual(Pack(program, positions[row], cells[row], delta, step, divide)))
                        throw new InvalidDataException($"Fixed Movement CPU diverged at tick {tick}, row {row}.");
                    words.CopyTo(oracleRows, row * width);
                }

                var submission = kernel.BeginDispatch(payload, width);
                var result = submission.Drain();
                if (result.RowCount != positions.Length ||
                    result.FindFirstFault() is not { Row: 1, Fault: { Kind: GaFaultKind.Overflow, EvaluationOrdinal: 1 } } first ||
                    first.Fault != faults[1] || faults[2] is not { Kind: GaFaultKind.DivideByZero, EvaluationOrdinal: 0 })
                    throw new InvalidDataException($"Fixed Movement GPU {index} selected an unexpected fault at tick {tick}.");
                for (int row = 0; row < positions.Length; row++)
                {
                    if (result.ReadFault(row) != faults[row] ||
                        faults[row] == null && !result.Output.Slice(row * width, width).SequenceEqual(oracleRows.AsSpan(row * width, width)))
                        throw new InvalidDataException($"Fixed Movement GPU {index} diverged at tick {tick}, row {row}.");
                }
            }
            Console.WriteLine($"Vulkan device {index}: generated Fixed Movement matched all words and faults over 8 ticks of 67 rows.");
        }
    }

    private static GaFaultKind ApplyOracle(ref long position, ref int cell, long delta, long step, bool divide)
    {
        var velocity = Fixed.FromRaw(delta);
        var multiplier = Fixed.FromRaw(step);
        try
        {
            var next = Fixed.FromRaw(position) + (divide ? velocity / multiplier : velocity * multiplier);
            position = next.Raw;
            cell = next < Fixed.Zero ? -(int)next : (int)next;
            return 0;
        }
        catch (DivideByZeroException) { return GaFaultKind.DivideByZero; }
        catch (OverflowException) { return GaFaultKind.Overflow; }
    }

    private static uint[] Pack(GaProgram program, long position, int cell, long velocity, long step, bool divide)
    {
        var words = new uint[program.WordCount];
        int index = 0;
        foreach (var field in program.Fields)
        {
            long bits = field.Path switch
            {
                "X" => position,
                "Cell" => cell,
                "Delta" => velocity,
                "Step" => step,
                "Divide" => divide ? 1 : 0,
                _ => throw new InvalidDataException("Unexpected Fixed Movement field.")
            };
            words[index++] = unchecked((uint)bits);
            if (field.Type == GaScalar.Fixed)
                words[index++] = unchecked((uint)(bits >> 32));
        }
        return words;
    }

    private static GaProgram Compile()
    {
        string runtime = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");
        var compilation = CSharpCompilation.Create("FixedMovement",
            [CSharpSyntaxTree.ParseText(Source, path: "FixedMovement.cs")],
            new[] { typeof(object).Assembly.Location, runtime, typeof(Fixed).Assembly.Location, typeof(SecsSystemAttribute).Assembly.Location }
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var entry = compilation.GetTypeByMetadataName("Movement")!.GetMembers("Execute").OfType<IMethodSymbol>().Single();
        var result = GaCompiler.Compile(compilation, entry);
        return result.Program ?? throw new InvalidDataException(string.Join("\n", result.Diagnostics));
    }
}
