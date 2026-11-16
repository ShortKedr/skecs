using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SKECS.Numerics;

namespace SKECS.GA.Tests;

public sealed class GaTypedEventTests
{
    [Fact]
    public void InterpreterDistinguishesEventsAndReductionLeavesWithTheSamePayloadType()
    {
        var program = Compile("context.Emit(true); context.Contribute(false); context.Contribute(-7); context.Contribute(true);");
        Assert.False(program.UsesCommands);
        Assert.Equal(2, program.ReductionTypeCount);
        Assert.Equal(new[] { false, true, true }, program.EventTypes.Select(type => type.IsContribution));
        Assert.Equal(program.EventTypes[0].Id, program.EventTypes[1].Id);

        uint[] words = new uint[4 * (GaOutputRecord.Words + 1)];
        Assert.Null(GaInterpreter.ExecutePackedOutputs(program, [], words, out int count));
        Assert.Equal(4, count);
        Assert.Equal(new uint[]
        {
            GaOutputRecord.EmitTypedBase, 0, 0, 1,
            GaOutputRecord.ContributeBase + 1, 0, 0, 0,
            GaOutputRecord.ContributeBase + 2, 0, 0, unchecked((uint)-7),
            GaOutputRecord.ContributeBase + 1, 0, 0, 1
        }, words);
    }

    [Fact]
    public void InterpreterDiscardsAnEarlierReductionLeafWhenItsRowFaults()
    {
        var program = Compile("context.Contribute(42); row.Result = 70 / row.Divisor;");
        Assert.Equal(new[] { "Result", "Divisor" }, program.Fields.Select(field => field.Path));
        uint[] row = [7, 0];
        uint[] words = Enumerable.Repeat(uint.MaxValue, GaOutputRecord.Words + 1).ToArray();
        var fault = GaInterpreter.ExecutePackedOutputs(program, row, words, out int count);
        Assert.Equal(GaFaultKind.DivideByZero, fault!.Value.Kind);
        Assert.Equal(0, count);
        Assert.Equal(new uint[] { 7, 0 }, row);
        Assert.All(words, word => Assert.Equal(uint.MaxValue, word));
    }

    [Theory]
    [InlineData("", "context.Contribute(8);")]
    [InlineData("[GaSnapshot(GaScalar.Int32)]", "context.Contribute(context.ReadSnapshot<int>(0, 1, 0, -3));")]
    [InlineData("[GaReadBuffer(typeof(uint))]", "context.Contribute(context.ReadBuffer<uint>(0));")]
    public void GeneratedAdaptersCompileWithBoundReductions(string attributes, string statements)
    {
        var compilation = CreateCompilation(statements + " context.Contribute(default(InternalLeaf));", attributes);
        var program = Compile(compilation);
        string cpu = GaCpuAdapterEmitter.Emit(program, "FixtureCpu");
        string codec = GaRowCodecEmitter.Emit(program, "FixtureCodec");
        Assert.Contains("ForContribution<", cpu);
        Assert.Contains("CreateEventTypes(", codec);
        Assert.Contains(" ReductionTypeCount = 2;", codec);
        var generated = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(cpu), CSharpSyntaxTree.ParseText(codec));
        Assert.DoesNotContain(generated.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void PackedContributionsFoldInDomainOrderAndShareEventOrdinals()
    {
        using var world = new World();
        world.CreateEntity(new FixtureRow(1));
        var work = new DeterministicWorkload<byte>();
        var digits = work.AddReduction(9, static (prior, leaf) => prior * 10 + leaf);
        var replay = new ReplayReduction(digits);
        var order = new DeterministicDomainOrder("typed-reduction-test", 1);
        work.AddCommitGroup(SystemStage.Update, order, group =>
            group.AddSystem(new DeterministicQuery().With<FixtureRow>(), replay.Execute));
        using var runner = new DeterministicRunner<byte>(world, work);
        for (int tick = 1; tick <= 2; tick++)
        {
            runner.BeginTick(tick, 0, Fixed.One);
            Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(1));
            Assert.Equal(921, runner.LastCompleted!.GetReduction(digits));
            var published = Assert.Single(runner.LastCompleted.Events);
            Assert.Equal(57, published.Get<int>());
            Assert.Equal(1, published.Key.LocalOrdinal);
        }
    }

    [Fact]
    public void InterpreterPacksTypedAndLegacyEventsBetweenTicketCommands()
    {
        var program = Compile("""
            context.SetDomainOrder(-3, 4);
            var ticket = context.Create();
            context.Add(ticket, new Addition { Count = 21 });
            context.Emit(new Payload
            {
                Tag = 0xF10000EDu,
                Nested = new Detail { Delta = -17, Code = Code.Selected },
                Enabled = true,
                Amount = Fixed.FromRaw(-4294967294L)
            });
            context.SetDomainOrder(-5, -7);
            context.Emit(17);
            context.Emit(default(Payload));
            context.Emit(Fixed.FromRaw(0x123456789ABCDEF0L));
            context.SetDomainOrder(0, 2);
            context.Destroy(ticket);
            """);

        Assert.Equal(new[] { "global::Payload", "global::SKECS.Numerics.Fixed" },
            program.EventTypes.Select(type => type.Id));
        Assert.Equal(new[] { "Amount", "Enabled", "Nested.Code", "Nested.Delta", "Tag" },
            program.EventTypes[0].Fields.Select(field => field.Path));
        Assert.Equal("global::Code", program.EventTypes[0].Fields[2].ClrEnumType);
        Assert.Equal(6, program.EventPayloadWords);
        Assert.Equal(1, program.AdditionPayloadWords);
        Assert.Equal(6, program.OutputPayloadWords);
        Assert.Equal(7, program.EventCapacity);

        uint[] words = Enumerable.Repeat(uint.MaxValue, 7 * 12).ToArray();
        Assert.Null(GaInterpreter.ExecutePackedOutputs(program, [], words, out int count));
        Assert.Equal(7, count);
        uint earlier = unchecked((uint)-5);
        uint later = unchecked((uint)-3);
        Assert.Equal(new uint[]
        {
            GaOutputRecord.CreateEntityKind, 0, 0, 0, 0, 0, 0, 0, 0, 1, later, 4,
            GaOutputRecord.AddTicketBase, 0, 0, 21, 0, 0, 0, 0, 0, 1, later, 4,
            GaOutputRecord.EmitTypedBase, 0, 0, 2, uint.MaxValue, 1, 0xFEDCBA98, unchecked((uint)-17), 0xF10000ED, 1, later, 4,
            GaOutputRecord.EmitInt32Kind, 17, 0, 0, 0, 0, 0, 0, 0, 1, earlier, unchecked((uint)-7),
            GaOutputRecord.EmitTypedBase, 0, 0, 0, 0, 0, 0, 0, 0, 1, earlier, unchecked((uint)-7),
            GaOutputRecord.EmitTypedBase + 1, 0, 0, 0x9ABCDEF0, 0x12345678, 0, 0, 0, 0, 1, earlier, unchecked((uint)-7),
            GaOutputRecord.DestroyTicketKind, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 2
        }, words);
    }

    [Fact]
    public void InterpreterPreservesInitializerEvaluationOrderAndFaultAtomicity()
    {
        var program = Compile("""
            row.Result = -8;
            context.Emit(17);
            context.Emit(new Payload
            {
                Tag = checked(row.Seed + 1u),
                Nested = new Detail { Delta = 120 / row.Divisor }
            });
            """);

        Assert.Equal(new[] { "Result", "Seed", "Divisor" }, program.Fields.Select(field => field.Path));
        Check(uint.MaxValue, GaFaultKind.Overflow, 0);
        Check(21, GaFaultKind.DivideByZero, 1);

        void Check(uint seed, GaFaultKind kind, ulong ordinal)
        {
            uint[] row = [7, seed, 0];
            uint[] words = Enumerable.Repeat(uint.MaxValue,
                program.EventCapacity * (GaOutputRecord.Words + program.OutputPayloadWords)).ToArray();
            var fault = GaInterpreter.ExecutePackedOutputs(program, row, words, out int count);
            Assert.Equal(kind, fault!.Value.Kind);
            Assert.Equal(ordinal, fault.Value.EvaluationOrdinal);
            Assert.Equal(0, count);
            Assert.Equal(new uint[] { 7, seed, 0 }, row);
            Assert.All(words, word => Assert.Equal(uint.MaxValue, word));
        }
    }

    [Fact]
    public void LegacyInt32EmitKeepsItsRecordAndEmptyTypedPayloadStillHasAType()
    {
        var old = Compile("context.Emit(67);");
        Assert.Empty(old.EventTypes);
        Assert.Equal(0, old.EventPayloadWords);
        int[] events = [0];
        Assert.Null(GaInterpreter.Execute(old, [], events, out int count));
        Assert.Equal(1, count);
        Assert.Equal(67, events[0]);

        var marker = Compile("context.Emit(default(Marker));");
        Assert.Equal("global::Marker", Assert.Single(marker.EventTypes).Id);
        Assert.Empty(marker.EventTypes[0].Fields);
        Assert.Equal(0, marker.OutputPayloadWords);
        uint[] words = Enumerable.Repeat(uint.MaxValue, GaOutputRecord.Words).ToArray();
        Assert.Null(GaInterpreter.ExecutePackedOutputs(marker, [], words, out count));
        Assert.Equal(1, count);
        Assert.Equal(new uint[] { GaOutputRecord.EmitTypedBase, 0, 0 }, words);
    }

    [Fact]
    public void WiderAdditionPadsTypedScalarEventsAndEqualWidthTypesHaveDifferentSchemas()
    {
        var program = Compile("""
            context.Add(7, 9, default(Payload));
            context.Emit(true);
            context.Emit(0xF10000EDu);
            context.Emit(Code.Selected);
            """);
        Assert.Equal(6, program.AdditionPayloadWords);
        Assert.Equal(1, program.EventPayloadWords);
        Assert.Equal(6, program.OutputPayloadWords);
        Assert.Equal(3, program.EventTypes.Count);
        Assert.All(program.EventTypes, type => Assert.Equal("", Assert.Single(type.Fields).Path));

        int width = GaOutputRecord.Words + program.OutputPayloadWords;
        uint[] words = Enumerable.Repeat(uint.MaxValue, 4 * width).ToArray();
        Assert.Null(GaInterpreter.ExecutePackedOutputs(program, [], words, out int count));
        Assert.Equal(4, count);
        Assert.Equal(new uint[] { GaOutputRecord.AddEntityBase, 7, 9, 0, 0, 0, 0, 0, 0 }, words[..width]);
        Assert.Equal(new uint[] { GaOutputRecord.EmitTypedBase, 0, 0, 1, 0, 0, 0, 0, 0 }, words[width..(2 * width)]);
        Assert.Equal(new uint[] { GaOutputRecord.EmitTypedBase + 1, 0, 0, 0xF10000ED, 0, 0, 0, 0, 0 },
            words[(2 * width)..(3 * width)]);
        Assert.Equal(new uint[] { GaOutputRecord.EmitTypedBase + 2, 0, 0, 0xFEDCBA98, 0, 0, 0, 0, 0 },
            words[(3 * width)..]);

        var boolCodec = GaRowCodecEmitter.Emit(Compile("context.Emit(true);"));
        var uintCodec = GaRowCodecEmitter.Emit(Compile("context.Emit(1u);"));
        string SchemaDigest(string code) => code.Split('\n').Single(line => line.Contains(" ComponentSchemaDigest = "));
        Assert.NotEqual(SchemaDigest(boolCodec), SchemaDigest(uintCodec));
    }

    [Fact]
    public void SlangClearsOmittedFieldsAndRecordPaddingWithoutClearingAssignedWords()
    {
        var program = Compile("""
            context.Emit(new Payload { Enabled = true, Tag = 13u, Amount = Fixed.FromRaw(8589934593L) });
            context.Emit(0xF0000001u);
            context.Emit(57);
            """);
        Assert.Equal(6, program.OutputPayloadWords);

        string slang = GaSlangEmitter.EmitVulkan(program).Source;
        var clearRanges = Regex.Matches(slang,
            @"for \(uint (t\d+) = (\d+u); \1 < (\d+u); \+\+\1\)\s+gaOutput\[t\d+ \+ \1\] = 0u;")
            .Select(loop => (loop.Groups[2].Value, loop.Groups[3].Value));
        Assert.Equal(new[] { ("6u", "8u"), ("4u", "9u"), ("3u", "9u") }, clearRanges);
    }

    [Fact]
    public void RunnerReplaysTypedPayloadsInDomainOrderWithOriginalLocalOrdinals()
    {
        using var world = new World();
        var entity = world.CreateEntity(new FixtureRow(1));
        var order = new DeterministicDomainOrder("typed-event-test", 1);
        var work = new DeterministicWorkload<byte>().AddCommitGroup(SystemStage.Update, order, group =>
            group.AddSystem(new DeterministicQuery().With<FixtureRow>(), new ReplayTyped()));
        using var runner = new DeterministicRunner<byte>(world, work);
        runner.BeginTick(8, 0, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(1));

        Assert.Collection(runner.LastCompleted!.Events,
            item =>
            {
                Assert.Equal(-37, item.Get<int>());
                Assert.Equal(2, item.Key.LocalOrdinal);
                Assert.Equal(new DeterministicDomainKey(-3, 0), item.Key.DomainKey);
            },
            item =>
            {
                Assert.Equal(new DecodedPayload(true, Fixed.FromRaw(-4294967294L)), item.Get<DecodedPayload>());
                Assert.Equal(1, item.Key.LocalOrdinal);
                Assert.Equal(new DeterministicDomainKey(-2, 3), item.Key.DomainKey);
                Assert.Equal(entity.Id, item.Key.SourceId);
                Assert.Equal(entity.Version, item.Key.SourceGeneration);
            });
        Assert.Equal(2, world.GetStorageStatistics().EntityCount);
    }

    [Theory]
    [InlineData(0)] // Unbound event index.
    [InlineData(1)] // Nonzero event header argument.
    [InlineData(2)] // Noncanonical Boolean.
    [InlineData(3)] // Nonzero padding.
    [InlineData(4)] // A reduction opcode must not bind to an event.
    public void InvalidTypedOutputIsRejectedBeforeAnEarlierCreateOrEventIsEnqueued(int corruption)
    {
        using var world = new World();
        world.CreateEntity(new FixtureRow(1));
        var work = new DeterministicWorkload<byte>().AddSystem(SystemStage.Update,
            new DeterministicQuery().With<FixtureRow>(), new RejectTyped(corruption));
        using var runner = new DeterministicRunner<byte>(world, work);
        runner.BeginTick(1, 0, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(1));
        var published = Assert.Single(runner.LastCompleted!.Events);
        Assert.Equal(91, published.Get<int>());
        Assert.Equal(0, published.Key.LocalOrdinal);
        Assert.Equal(1, world.GetStorageStatistics().EntityCount);
    }

    [Theory]
    [InlineData("context.Emit(17L);")]
    [InlineData("context.Emit(default(UnsupportedField));")]
    [InlineData("context.Emit(new UnsupportedConstructor());")]
    [InlineData("var alias = context; alias.Emit(default(Payload));")]
    [InlineData("context.Contribute(17L);")]
    [InlineData("var alias = context; alias.Contribute(default(Payload));")]
    public void CompilerDiagnosesUnsupportedTypedOutputCalls(string statements)
    {
        var result = Analyze(statements);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id is "GA0001" or "GA0004" or "GA0006");
    }

    private readonly record struct FixtureRow(int Value);
    private readonly record struct DecodedPayload(bool Enabled, Fixed Amount);

    private static readonly GaTypedEvent[] Bindings = [GaTypedEvent.For<DecodedPayload>(Decode, 3, [0])];

    private static DecodedPayload Decode(ReadOnlySpan<uint> words) =>
        new(words[0] != 0, Fixed.FromRaw(unchecked((long)((ulong)words[1] | ((ulong)words[2] << 32)))));

    private readonly struct ReplayReduction(DeterministicReduction<int> reduction) : IDeterministicJobProcessor<byte>
    {
        private readonly GaTypedEvent[] _bindings =
            [GaTypedEvent.ForContribution<int>(reduction, static words => unchecked((int)words[0]), 1, [])];

        public void Execute(DeterministicJob<byte> job)
        {
            const int width = GaOutputRecord.Words + 1 + GaOutputRecord.DomainWordCount;
            Span<uint> records = stackalloc uint[3 * width];
            GaOutputRecord.Contribute(0).Write(records[..width]);
            records[GaOutputRecord.Words] = 1;
            GaOutputRecord.WriteDomainOrder(records[..width], true, 2, 0);
            var posted = records.Slice(width, width);
            GaOutputRecord.EmitInt32(57).Write(posted);
            GaOutputRecord.WriteDomainOrder(posted, true, 1, 0);
            var early = records[(2 * width)..];
            GaOutputRecord.Contribute(0).Write(early);
            early[GaOutputRecord.Words] = 2;
            GaOutputRecord.WriteDomainOrder(early, true, 0, 0);
            GaOutputRecord.ApplyPackedSource(job.ForEntity(0), records, width, [], [], true, _bindings);
        }
    }

    private readonly struct ReplayTyped : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            const int width = GaOutputRecord.Words + 3 + GaOutputRecord.DomainWordCount;
            Span<uint> records = stackalloc uint[3 * width];
            GaOutputRecord.CreateEntity().Write(records[..width]);
            GaOutputRecord.WriteDomainOrder(records[..width], true, -1, 0);
            var typed = records.Slice(width, width);
            GaOutputRecord.EmitTyped(0).Write(typed);
            typed[GaOutputRecord.Words] = 1;
            typed[GaOutputRecord.Words + 1] = 2;
            typed[GaOutputRecord.Words + 2] = uint.MaxValue;
            GaOutputRecord.WriteDomainOrder(typed, true, -2, 3);
            GaOutputRecord.EmitInt32(-37).Write(records[(2 * width)..]);
            GaOutputRecord.WriteDomainOrder(records[(2 * width)..], true, -3, 0);
            GaOutputRecord.ApplyPackedSource(job.ForEntity(0), records, width, [], [], true, Bindings);
        }
    }

    private readonly struct RejectTyped(int corruption) : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            const int width = GaOutputRecord.Words + 4;
            Span<uint> records = stackalloc uint[3 * width];
            GaOutputRecord.CreateEntity().Write(records[..width]);
            GaOutputRecord.EmitInt32(77).Write(records.Slice(width, width));
            var typed = records[(2 * width)..];
            var record = corruption == 4 ? GaOutputRecord.Contribute(0) : GaOutputRecord.EmitTyped(corruption == 0 ? 1 : 0);
            record.Write(typed);
            typed[1] = corruption == 1 ? 17u : 0u;
            typed[GaOutputRecord.Words] = corruption == 2 ? 2u : 1u;
            typed[^1] = corruption == 3 ? 19u : 0u;

            var source = job.ForEntity(0);
            try
            {
                GaOutputRecord.ApplyPackedSource(source, records, width, [], [], false, Bindings);
            }
            catch (InvalidDataException)
            {
                source.Emit(91);
            }
        }
    }

    private static GaProgram Compile(string statements) => Compile(CreateCompilation(statements));

    private static GaProgram Compile(CSharpCompilation compilation)
    {
        var result = Analyze(compilation);
        Assert.True(result.Program is not null, string.Join("\n", result.Diagnostics));
        return result.Program!;
    }

    private static GaCompilationResult Analyze(string statements) => Analyze(CreateCompilation(statements));

    private static CSharpCompilation CreateCompilation(string statements, string attributes = "")
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            using SKECS.GA;
            using SKECS.Numerics;
            public struct Row { public int Result, Divisor; public uint Seed; }
            public enum Code : uint { Selected = 0xFEDCBA98u }
            public struct Detail { public int Delta; public Code Code; }
            public struct Payload { public uint Tag; public Detail Nested; public bool Enabled; public Fixed Amount; }
            public struct Addition { public int Count; }
            public struct Marker { }
            internal struct InternalLeaf { public uint Word; }
            public struct UnsupportedField { public double Amount; }
            public struct UnsupportedConstructor
            {
                public int Amount;
                public UnsupportedConstructor() { Amount = 12; }
            }
            [SecsSystem]
            public partial struct TypedEventSystem
            {
                {{attributes}}
                public void Execute(in SystemContext context, ref Row row)
                {
                    {{statements}}
                }
            }
            """, path: "TypedEventFixture.cs");
        string runtime = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");
        return CSharpCompilation.Create("TypedEventFixture", [tree],
            new[] { typeof(object).Assembly.Location, runtime, typeof(SystemContext).Assembly.Location, typeof(Fixed).Assembly.Location }
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static GaCompilationResult Analyze(CSharpCompilation compilation)
    {
        var entry = (IMethodSymbol)compilation.GetTypeByMetadataName("TypedEventSystem")!.GetMembers("Execute").Single();
        return GaCompiler.Compile(compilation, entry);
    }
}
