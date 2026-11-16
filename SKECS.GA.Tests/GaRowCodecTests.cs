using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SKECS.GA.Tests;

public sealed class GaRowCodecTests
{
    private const string Kernel = """
        using SKECS.GA;
        using SKECS.Numerics;

        namespace CodecFixture;

        public enum Mode : uint { Unset }
        public struct Point { public int X, Hold, Unused; }
        public struct State { public Fixed Time; public Mode Mode; public bool Enabled; public Point @event; }
        public struct Input { public Fixed Step; public uint Bits; public bool Enabled; public int Threshold; }
        public struct Tag { }
        public struct Alternate { }

        [SecsSystem(SystemId = "fixtures.row-codec")]
        public partial struct Movement
        {
            public void Execute(ref State state, in Input input, in Tag tag)
            {
                state.Time += input.Step;
                state.Mode = (Mode)((uint)state.Mode + input.Bits);
                state.Enabled = input.Enabled && state.@event.Hold > input.Threshold;
                state.@event.X += state.@event.Hold;
            }
        }
        """;

    private const string Driver = """
        using System;
        using SKECS;
        using SKECS.Numerics;
        using CodecFixture;

        public static class Driver
        {
            public static long[] Run(int capacity)
            {
                using var world = new World(new WorldOptions(chunkCapacity: capacity));
                var entities = new Entity[130];
                for (int i = 0; i < entities.Length; i++)
                {
                    var state = new State
                    {
                        Time = Fixed.FromRaw(long.MinValue + i), Mode = (Mode)(0xf0000000u + (uint)i),
                        Enabled = i % 2 == 0, @event = new Point { X = -i, Hold = i, Unused = 9 }
                    };
                    var input = new Input
                    {
                        Step = Fixed.FromRaw(-100L - i), Bits = (uint)i, Enabled = i % 2 != 0, Threshold = -1000 - i
                    };
                    entities[i] = world.CreateEntity(state, input);
                    if (i != 0) world.Add(entities[i], new Tag());
                    if (i % 2 == 0) world.Add(entities[i], new Alternate());
                }
                var workload = new DeterministicWorkload<byte>();
                workload.AddSystem(SystemStage.Update, GeneratedCodec.CreateQuery(), new Processor());
                using var runner = new DeterministicRunner<byte>(world, workload);
                runner.BeginTick(0, (byte)0, Fixed.One);
                while (runner.Advance(1) == DeterministicTickStatus.Pending) { }
                if (runner.Status != DeterministicTickStatus.Completed)
                    throw new InvalidOperationException("The row codec could not complete.", runner.Fault);

                var result = new long[entities.Length * 7];
                int next = 0;
                foreach (var entity in entities)
                {
                    var state = world.Get<State>(entity);
                    var input = world.Get<Input>(entity);
                    result[next++] = state.Time.Raw;
                    result[next++] = (uint)state.Mode;
                    result[next++] = state.Enabled ? 1 : 0;
                    result[next++] = state.@event.X;
                    result[next++] = state.@event.Hold;
                    result[next++] = state.@event.Unused;
                    result[next++] = input.Threshold;
                }
                return result;
            }

            private readonly struct Processor : IDeterministicJobProcessor<byte>
            {
                public void Execute(DeterministicJob<byte> job)
                {
                    const int width = GeneratedCodec.WordsPerRow;
                    Span<uint> words = stackalloc uint[width * job.Count];
                    var readJob = new DeterministicBatchReadJob<byte>(job);
                    GeneratedCodec.Pack(readJob, words);
                    try { GeneratedCodec.Pack(readJob, words[..^1]); throw new InvalidOperationException("Invalid pack width passed."); }
                    catch (ArgumentException) { }
                    if (job.Count > 2)
                    {
                        var malformed = words.ToArray();
                        malformed[0] = 0;
                        malformed[width + 6] = 2;
                        try
                        {
                            GeneratedCodec.Apply(job, job.Count, malformed);
                            throw new InvalidOperationException("Invalid boolean passed.");
                        }
                        catch (ArgumentException) { }
                        Span<uint> after = stackalloc uint[words.Length];
                        GeneratedCodec.Pack(readJob, after);
                        if (!words.SequenceEqual(after))
                            throw new InvalidOperationException("An invalid prefix changed an earlier row.");
                    }
                    try
                    {
                        GeneratedCodec.Apply(job, job.Count - 1, words);
                        throw new InvalidOperationException("Invalid apply width passed.");
                    }
                    catch (ArgumentException) { }
                    GeneratedCodec.Apply(job, 0, ReadOnlySpan<uint>.Empty);
                    for (int row = 0; row < job.Count; row++)
                    {
                        int i = job.JobId * DeterministicRunner<byte>.LogicalJobSize + row + 1;
                        var actual = words.Slice(row * width, width);
                        uint[] expected =
                        [
                            (uint)i, 0x80000000u, unchecked((uint)(-100 - i)), 0xffffffffu,
                            0xf0000000u + (uint)i, (uint)i, i % 2 == 0 ? 1u : 0u, i % 2 != 0 ? 1u : 0u,
                            (uint)i, unchecked((uint)(-1000 - i)), unchecked((uint)-i)
                        ];
                        if (!actual.SequenceEqual(expected))
                            throw new InvalidOperationException("Packed field order or bits differ.");
                        for (int word = 0; word < width; word++)
                            actual[word] = ~actual[word];
                        actual[6] = expected[6] ^ 1u;
                    }
                    GeneratedCodec.Apply(job, job.Count - 1, words[..^width]);
                }
            }
        }
        """;

    [Theory]
    [InlineData(3)]
    [InlineData(256)]
    public void PacksCanonicalRowsAndAppliesOnlyWrittenFieldsOfSuccessfulPrefix(int capacity)
    {
        string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            typeof(object).Assembly.Location, Path.Combine(runtime, "System.Runtime.dll"),
            typeof(World).Assembly.Location, typeof(SecsSystemAttribute).Assembly.Location
        }.Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("RowCodecFixture" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(Kernel, path: "Movement.cs")], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: true));
        var entry = compilation.GetTypeByMetadataName("CodecFixture.Movement")!.GetMembers("Execute").OfType<IMethodSymbol>().Single();
        var result = GaCompiler.Compile(compilation, entry);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        string generated = GaRowCodecEmitter.Emit(result.Program!, "GeneratedCodec");
        Assert.Equal(generated, GaRowCodecEmitter.Emit(result.Program!, "GeneratedCodec"));
        Assert.Equal(11, result.Program!.WordCount);
        using var stream = new MemoryStream();
        var emitted = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(generated, path: "RowCodec.g.cs"),
            CSharpSyntaxTree.ParseText(Driver, path: "Driver.cs")).Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var fixture = AssemblyLoadContext.Default.LoadFromStream(stream);
        var run = fixture.GetType("Driver")!.GetMethod("Run")!.CreateDelegate<Func<int, long[]>>();
        var actual = run(capacity);
        var expected = new List<long>();
        for (int i = 0; i < 130; i++)
        {
            bool applied = i != 0 && i % 64 != 0 && i != 129;
            long time = long.MinValue + i;
            uint mode = 0xf0000000u + (uint)i;
            bool enabled = i % 2 == 0;
            int x = -i;
            expected.AddRange([applied ? ~time : time, applied ? ~mode : mode,
                (applied ? !enabled : enabled) ? 1 : 0, applied ? ~x : x, i, 9, -1000 - i]);
        }
        Assert.Equal(expected, actual);
    }
}
