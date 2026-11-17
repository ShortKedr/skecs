using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SKECS.GA.Analysis;

namespace SKECS.GA.Tests;

public sealed class GaSystemAnalyzerTests
{
    private const string Contracts = """
        namespace SKECS.GA
        {
            [System.AttributeUsage(System.AttributeTargets.Struct)]
            public sealed class SecsSystemAttribute : System.Attribute { }
            public readonly struct SystemContext
            {
                public bool Has<T>() where T : unmanaged => false;
                public void Emit<T>(T value) where T : unmanaged { }
            }
        }
        namespace SKECS
        {
            public readonly struct Entity { public int Id => 0; public int Version => 1; }
        }
        """;

    private static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is not ("SKECS.dll" or "SKECS.GA.dll"))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    [Fact]
    public async Task OptInReportsTransitiveSourceAndPath()
    {
        const string source = """
            [SKECS.GA.SecsSystem]
            public partial struct Advance
            {
                public void Execute(ref int value) => value += Read();
                static int Read() => Utility.Read();
            }
            static class Utility { public static int Read() => System.DateTime.UtcNow.Second; }
            """;

        Assert.Empty(await Analyze(source, enabled: false));
        var clock = Assert.Single((await Analyze(source)).Where(diagnostic =>
            diagnostic.Id == "GA0003" && Text(diagnostic) == "System.DateTime.UtcNow"));
        Assert.Equal(DiagnosticSeverity.Error, clock.Severity);
        Assert.Contains("Advance.Execute -> Advance.Read -> Utility.Read", clock.GetMessage());
        var compilation = Compile(source);
        var entry = Assert.Single(GaSemanticAnalysis.GetEntryPoints(compilation.GetTypeByMetadataName("Advance")!, compilation));
        var direct = GaSemanticAnalysis.Analyze(entry, compilation);
        Assert.Contains(direct, diagnostic => diagnostic.GetMessage() == clock.GetMessage());
    }

    [Fact]
    public async Task TraversesCheckedAndUncheckedExpressionBodiedHelpers()
    {
        const string source = """
            [SKECS.GA.SecsSystem]
            partial struct Motion
            {
                public void Execute(ref int value) => value = Twice(value) + Clock();
                static int Twice(int x) => unchecked(x + x);
                static int Clock() => checked(System.DateTime.UtcNow.Second + 1);
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "GA0004");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "GA0003" && Text(diagnostic) == "System.DateTime.UtcNow");
    }

    [Fact]
    public async Task TraversesSourceGettersOperatorsAndConversions()
    {
        const string source = """
            struct Counts
            {
                public int Value;
                public readonly int Doubled => unchecked(Value + Value);
                public readonly int Clock { get => checked(System.DateTime.UtcNow.Second + 1); }
                public static int operator +(Counts count, int value) => checked(count.Value + value);
                public static explicit operator int(Counts count) => unchecked(count.Value - 1);
            }
            [SKECS.GA.SecsSystem]
            partial struct Motion
            {
                public void Execute(ref int value, in Counts counts) => value = counts.Doubled + (counts + 1) + (int)counts + counts.Clock;
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "GA0004");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "GA0003" && Text(diagnostic) == "System.DateTime.UtcNow");
    }

    [Fact]
    public async Task AdmitsBoundedLoopsReadonlyGetterSpecializationAndNestedInlineArray()
    {
        const string source = """
            using System.Runtime.CompilerServices;
            enum Mode : uint { Move = 1, Stop = 2 }
            [InlineArray(16)] struct Slots { private int item; }
            struct Inventory { public Slots Items; }
            struct Input
            {
                public int Count;
                public Mode Mode;
                public readonly int CountValue => Count;
            }
            [SKECS.GA.SecsSystem]
            partial struct Motion
            {
                public void Execute(SKECS.Entity entity, ref Inventory inventory, in Input input)
                {
                    for (int i = 0; i <= 15; ++i)
                    {
                        if (i == 5) continue;
                        inventory.Items[i] += Identity(input.CountValue) + entity.Id;
                    }
                    int j = input.Count;
                    while (j >= 1)
                    {
                        if (j == 12) break;
                        --j;
                    }
                    int k = 0;
                    while (k < 4)
                    {
                        ++k;
                        if (k == 2) continue;
                        inventory.Items[k] += 1;
                    }
                    int frozen = input.Count & 7;
                    for (int item = 0; item <= frozen; ++item)
                        inventory.Items[item] += 1;
                    switch (input.Mode)
                    {
                        case Mode.Move: inventory.Items[0] += j; break;
                        default: break;
                    }
                }
                static T Identity<T>(T value) where T : unmanaged => value;
            }
            """;

        Assert.Empty(await Analyze(source));
    }

    [Theory]
    [InlineData("value += System.Random.Shared.Next();", "", "GA0003")]
    [InlineData("value += (int)2.5;", "", "GA0002")]
    [InlineData("value = (int)System.Numerics.BitOperations.RotateLeft(3u, 1);", "", "GA0004")]
    [InlineData("value += Read();", "static int Read() => Read();", "GA0004")]
    [InlineData("value += Read<string>();", "static int Read<T>() => 1;", "GA0003")]
    [InlineData("Grow(ref value, in value);", "static void Grow(ref int left, in int right) => left += right;", "GA0005")]
    [InlineData("ref int alias = ref value; alias++;", "", "GA0005")]
    [InlineData("for (int i = 0; i < 10; i--) value++;", "", "GA0004")]
    [InlineData("for (int i = 0; i <= int.MaxValue; i++) value++;", "", "GA0004")]
    [InlineData("for (int i = 0; i >= int.MinValue; i--) value++;", "", "GA0004")]
    [InlineData("int bound = 10; for (int i = 0; i < bound; i++) Grow(ref bound);", "static void Grow(ref int input) => input++;", "GA0004")]
    [InlineData("int i = 0; while (i < 10) { if (value == 0) continue; i++; }", "", "GA0004")]
    [InlineData("int i = 0; while (i < 10) { ++i; if (value == 0) continue; --i; }", "", "GA0004")]
    [InlineData("int bound = value & 7; bound = value; for (int i = 0; i <= bound; ++i) value++;", "", "GA0004")]
    [InlineData("int bound = value & 7; Grow(ref bound); for (int i = 0; i <= bound; ++i) value++;", "static void Grow(ref int input) => input++;", "GA0004")]
    public async Task RejectsViolation(string body, string helper, string rule)
    {
        string source = $$"""
            [SKECS.GA.SecsSystem]
            partial struct Motion
            {
                public void Execute(ref int value) { {{body}} }
                {{helper}}
            }
            """;

        Assert.Contains(await Analyze(source), diagnostic => diagnostic.Id == rule);
    }

    [Fact]
    public async Task RejectsCapturedBattleAndUnregisteredContextOperations()
    {
        const string source = """
            sealed class Battle { }
            [SKECS.GA.SecsSystem]
            partial struct Capturing
            {
                private readonly Battle battle;
                public Capturing(Battle battle) => this.battle = battle;
                public void Execute(ref int input) { }
            }
            [SKECS.GA.SecsSystem]
            partial struct Scoped
            {
                public void Execute(in SKECS.GA.SystemContext context, ref int input)
                {
                    if (context.Has<int>()) context.Emit(input);
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "GA0003" && Text(diagnostic) == "battle");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "GA0004" && Text(diagnostic) == "context.Has<int>()");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "GA0006" && Text(diagnostic) == "context.Emit(input)");
    }

    [Fact]
    public async Task AdmitsRuntimeInt32EmitOnEntryContext()
    {
        const string source = """
            [SKECS.GA.SecsSystem]
            partial struct Events
            {
                public void Execute(in SKECS.GA.SystemContext context, in int input)
                {
                    if (input < 0) context.Emit(input);
                    context.Emit(checked(input + 1));
                }
            }
            """;

        Assert.Empty(await Analyze(source, useRuntime: true));
    }

    [Fact]
    public async Task RejectsEmitOnCopiedContextAndContextByValue()
    {
        const string source = """
            [SKECS.GA.SecsSystem]
            partial struct CopiedContext
            {
                public void Execute(in SKECS.GA.SystemContext context, in int input)
                {
                    var copy = context;
                    copy.Emit(input);
                }
            }
            [SKECS.GA.SecsSystem]
            partial struct ValueContext
            {
                public void Execute(SKECS.GA.SystemContext context, in int input) => context.Emit(input);
            }
            """;

        var diagnostics = await Analyze(source, useRuntime: true);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "GA0006" && Text(diagnostic) == "copy.Emit(input)");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "GA0005" && Text(diagnostic) == "context");
    }

    [Fact]
    public async Task FollowsOperatorAndGetterBodies()
    {
        const string source = """
            struct Input
            {
                public int Value;
                public static Input operator +(Input left, Input right) =>
                    new Input(left.Value + right.Value + Hidden);
                private Input(int value) { Value = value; }
                private static int Hidden => System.Environment.TickCount;
            }
            [SKECS.GA.SecsSystem]
            partial struct Motion
            {
                public void Execute(ref Input left) => left = left + left;
            }
            """;

        var diagnostic = Assert.Single((await Analyze(source)).Where(d => Text(d) == "System.Environment.TickCount"));
        Assert.Equal("GA0003", diagnostic.Id);
        Assert.Contains("Motion.Execute -> Input.op_Addition -> Input.get_Hidden", diagnostic.GetMessage());
    }

    [Fact]
    public async Task RejectsConstructorWithHiddenInitialization()
    {
        const string source = """
            struct Cell
            {
                public int Value = System.Environment.TickCount;
                public Cell() { }
            }
            [SKECS.GA.SecsSystem]
            partial struct Motion
            {
                public void Execute(ref Cell cell) => cell = new Cell();
            }
            """;

        Assert.Contains(await Analyze(source), diagnostic => diagnostic.Id == "GA0004" &&
            diagnostic.GetMessage().Contains("field initializers"));
    }

    [Fact]
    public async Task FollowsImplicitConversionOfCompoundAssignment()
    {
        const string source = """
            struct Counter
            {
                public int Value;
                public static Result operator +(Counter input, int amount) => default;
            }
            struct Result
            {
                public static implicit operator Counter(Result result)
                {
                    int value = System.Environment.TickCount;
                    return default;
                }
            }
            [SKECS.GA.SecsSystem]
            partial struct Motion
            {
                public void Execute(ref Counter counter) => counter += 1;
            }
            """;

        var diagnostic = Assert.Single((await Analyze(source)).Where(d => Text(d) == "System.Environment.TickCount"));
        Assert.Equal("GA0003", diagnostic.Id);
        Assert.Contains("Motion.Execute -> Result.op_Implicit", diagnostic.GetMessage());
    }

    [Fact]
    public async Task RejectsOverloadedShortCircuitWithImplicitTruthTest()
    {
        const string source = """
            struct Flag
            {
                public bool Value;
                public static bool operator false(Flag flag) => System.Environment.TickCount == 0;
                public static bool operator true(Flag flag) => flag.Value;
                public static Flag operator &(Flag left, Flag right) => left;
            }
            [SKECS.GA.SecsSystem]
            partial struct Motion
            {
                public void Execute(ref Flag flag) => flag = flag && flag;
            }
            """;

        Assert.Contains(await Analyze(source), diagnostic => diagnostic.Id == "GA0004" && Text(diagnostic) == "flag && flag");
    }

    [Fact]
    public async Task RejectsAliasedExplicitLayout()
    {
        const string source = """
            using System.Runtime.InteropServices;
            [StructLayout(LayoutKind.Explicit)]
            struct Aliased
            {
                [FieldOffset(0)] public uint Unsigned;
                [FieldOffset(0)] public int Signed;
            }
            [SKECS.GA.SecsSystem]
            partial struct Motion
            {
                public void Execute(ref Aliased value) => value.Signed++;
            }
            """;

        Assert.Contains(await Analyze(source), diagnostic => diagnostic.Id == "GA0001" && Text(diagnostic) == "value");
    }

    [Fact]
    public async Task RejectsInvalidEntryEvenWhenCompilerInvokesVerifierDirectly()
    {
        const string source = """
            [SKECS.GA.SecsSystem]
            partial struct Two
            {
                public void Execute(ref int input) { }
                public void Execute(ref uint input) { }
            }
            """;

        Assert.Equal("GA0001", Assert.Single(await Analyze(source)).Id);
        var compilation = Compile(source);
        var entry = (IMethodSymbol)compilation.GetTypeByMetadataName("Two")!.GetMembers("Execute")[0];
        Assert.Equal("GA0001", Assert.Single(GaSemanticAnalysis.Analyze(entry, compilation)).Id);
    }

    [Fact]
    public async Task AcceptsSnapshotReadOnTheEntryContext()
    {
        const string source = """
            using SKECS.GA;
            [SecsSystem]
            partial struct Motion
            {
                public void Execute(in SystemContext context, ref int result)
                {
                    result = context.ReadSnapshotInt32(entityId: 1, generation: 2, fallback: -3);
                }
            }
            """;

        Assert.Empty(await Analyze(source, useRuntime: true));
    }

    [Fact]
    public async Task RejectsSnapshotReadOnAnAliasedContext()
    {
        const string source = """
            using SKECS.GA;
            [SecsSystem]
            partial struct Motion
            {
                public void Execute(in SystemContext context, ref int result)
                {
                    var alias = context;
                    result = alias.ReadSnapshotInt32(1, 2, -3);
                }
            }
            """;

        Assert.Contains(await Analyze(source, useRuntime: true), diagnostic =>
            diagnostic.Id == "GA0004" && Text(diagnostic) == "alias.ReadSnapshotInt32(1, 2, -3)");
    }

    [Fact]
    public async Task AdmitsDestroyOnTheEntryContext()
    {
        const string source = """
            using SKECS.GA;
            [SecsSystem]
            partial struct Motion
            {
                public void Execute(in SystemContext context, in int id)
                {
                    context.Destroy(entityId: id, generation: 2);
                }
            }
            """;

        Assert.Empty(await Analyze(source, useRuntime: true));
    }

    [Fact]
    public async Task RejectsDestroyOnCopiedContextAndReorderedArguments()
    {
        const string source = """
            using SKECS.GA;
            [SecsSystem]
            partial struct Motion
            {
                public void Execute(in SystemContext context, in int id)
                {
                    var alias = context;
                    alias.Destroy(id, 2);
                    context.Destroy(generation: 2, entityId: id);
                }
            }
            """;

        var diagnostics = await Analyze(source, useRuntime: true);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "GA0006" && Text(diagnostic) == "alias.Destroy(id, 2)");
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "GA0006" && Text(diagnostic) == "context.Destroy(generation: 2, entityId: id)");
    }

    [Fact]
    public async Task AdmitsTicketCreationAndDestructionInTheSameEntryInvocation()
    {
        const string source = """
            using SKECS.GA;
            [SecsSystem]
            partial struct Motion
            {
                public void Execute(in SystemContext context, in int count)
                {
                    if (count > 0)
                    {
                        GaEntityTicket ticket = context.Create();
                        context.Destroy(ticket: ticket);
                    }
                    context.Create();
                }
            }
            """;
        Assert.Empty(await Analyze(source, useRuntime: true));
    }

    [Fact]
    public async Task RejectsPassingTicketsToHelpers()
    {
        const string source = """
            using SKECS.GA;
            [SecsSystem]
            partial struct Motion
            {
                public void Execute(in SystemContext context)
                {
                    var ticket = context.Create();
                    Keep(ticket);
                }
                private static void Keep(GaEntityTicket ticket) { }
            }
            """;

        Assert.Contains(await Analyze(source, useRuntime: true), diagnostic =>
            diagnostic.Id == "GA0006" && Text(diagnostic) == "ticket");
    }

    [Fact]
    public async Task AdmitsRemovalOfClosedNestedMarkerByEntityAndTicket()
    {
        const string source = """
            using SKECS.GA;
            public struct Outer<T> where T : unmanaged { public struct Marker { } }
            [SecsSystem]
            partial struct Motion
            {
                public void Execute(in SystemContext context)
                {
                    context.Remove<Outer<int>.Marker>(entityId: 3, generation: 2);
                    var ticket = context.Create();
                    context.Remove<Outer<int>.Marker>(ticket: ticket);
                }
            }
            """;
        Assert.Empty(await Analyze(source, useRuntime: true));
    }

    [Fact]
    public async Task RejectsRemovalOfComponentsInaccessibleFromGeneratedCode()
    {
        const string source = """
            using SKECS.GA;
            file struct FileMarker { }
            [SecsSystem]
            partial struct Motion
            {
                private struct PrivateMarker { }
                public void Execute(in SystemContext context)
                {
                    context.Remove<PrivateMarker>(3, 2);
                    context.Remove<FileMarker>(3, 2);
                }
            }
            """;
        var diagnostics = await Analyze(source, useRuntime: true);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "GA0006" && Text(diagnostic) == "context.Remove<PrivateMarker>(3, 2)");
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "GA0006" && Text(diagnostic) == "context.Remove<FileMarker>(3, 2)");
    }

    private static string Text(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source, bool enabled = true, bool useRuntime = false)
    {
        var compilation = Compile(source, useRuntime);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new GaSystemAnalyzer());
        var options = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, new BuildOptions(enabled));
        return await compilation.WithAnalyzers(analyzers, options).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation Compile(string source, bool useRuntime = false)
    {
        var fixture = CSharpSyntaxTree.ParseText(source, path: "Fixture.cs");
        var trees = useRuntime ? new[] { fixture } : new[] { CSharpSyntaxTree.ParseText(Contracts, path: "Contracts.cs"), fixture };
        var references = useRuntime ? References.Add(MetadataReference.CreateFromFile(typeof(SystemContext).Assembly.Location)) : References;
        var compilation = CSharpCompilation.Create("GaFixture",
            trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return compilation;
    }

    private sealed class BuildOptions(bool enabled) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new BuildPropertyOptions(enabled);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    private sealed class BuildPropertyOptions(bool enabled) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = enabled ? "true" : "false";
            return key == "build_property.SKECSGraphicsAccelerated";
        }
    }
}
