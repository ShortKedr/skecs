using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SKECS.GA.Tests;

public sealed class GaOwnContextTests
{
    private const string Accesses = """
        [GaAccess(typeof(Heat), GaOwnAccess.ReadWrite)]
        [GaAccess(typeof(Hull), GaOwnAccess.Read)]
        [GaAccess(typeof(Shield), GaOwnAccess.OptionalRead)]
        [GaAccess(typeof(Marker), GaOwnAccess.OptionalRead)]
        """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RequiredFieldsAndOptionalCopiesKeepDistinctPresence(bool shield, bool tag)
    {
        var program = Compile(Accesses, """
            bool present = context.TryRead<Shield>(out var copy);
            context.Own<Heat>().Temperature += copy.Amount + context.Own<Hull>().Scale;
            row.Current = context.Own<Heat>().Temperature;
            row.Copied = copy.Enabled;
            row.Flag = context.Has<Marker>();
            context.Emit(present ? copy.Amount : -1);
            """);
        Assert.Equal(GaComponentOrigin.Query, Component(program, "Row").Origin);
        Assert.Equal(GaComponentOrigin.Own, Component(program, "Heat").Origin);
        Assert.Equal(GaFieldAccess.Read, Component(program, "Hull").Access);
        Assert.Equal(GaComponentOrigin.Optional, Component(program, "Shield").Origin);
        Assert.Equal(GaComponentOrigin.Optional, Component(program, "Marker").Origin);
        Assert.DoesNotContain(program.Fields, field => field.Component == Component(program, "Marker").Id);
        var values = new Dictionary<string, uint>
        {
            ["Temperature"] = 11,
            ["Scale"] = 7,
            ["Amount"] = shield ? 13u : 0u,
            ["Enabled"] = shield ? 1u : 0u
        };
        string[] present = ["Heat", "Hull", .. (shield ? new[] { "Shield" } : []), .. (tag ? new[] { "Marker" } : [])];
        uint[] row = Pack(program, values, present);
        int[] events = [-99];
        Assert.Null(GaInterpreter.Execute(program, row, events, out int count));
        values["Current"] = values["Temperature"] = shield ? 31u : 18u;
        values["Copied"] = shield ? 1u : 0u;
        values["Flag"] = tag ? 1u : 0u;
        Assert.Equal(Pack(program, values, present), row);
        Assert.Equal(1, count);
        Assert.Equal(shield ? 13 : -1, events[0]);
    }

    [Fact]
    public void UnusedRequiredComponentFaultsBeforeRhsAndOutput()
    {
        var program = Compile(Accesses, """
            context.Emit(123);
            context.Own<Heat>().Temperature = 42 / row.Divisor;
            """);
        var values = new Dictionary<string, uint> { ["Temperature"] = 61 };
        uint[] row = Pack(program, values, "Heat");
        uint[] before = row.ToArray();
        int[] events = [-777];
        var fault = GaInterpreter.Execute(program, row, events, out int count);
        Assert.NotNull(fault);
        Assert.Equal(GaFaultKind.MissingOwnComponent, fault.Value.Kind);
        Assert.Equal(0UL, fault.Value.EvaluationOrdinal);
        Assert.Equal(Component(program, "Hull").OwnSource, fault.Value.Source);
        Assert.Equal(before, row);
        Assert.Equal(0, count);
        Assert.Equal(-777, events[0]);

        row = Pack(program, values, "Heat", "Hull");
        fault = GaInterpreter.Execute(program, row, events, out count);
        Assert.NotNull(fault);
        Assert.Equal(GaFaultKind.DivideByZero, fault.Value.Kind);
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(false, true, 109)]
    [InlineData(true, false, 109)]
    [InlineData(true, true, 27)]
    public void TryReadResultAndCopyFollowShortCircuit(bool probe, bool shield, int expected)
    {
        var program = Compile(Accesses, """
            if (row.Probe && context.TryRead<Shield>(out var copy))
                row.Current = copy.Amount;
            """);
        string[] present = ["Heat", "Hull", .. (shield ? new[] { "Shield" } : [])];
        var values = new Dictionary<string, uint>
        {
            ["Probe"] = probe ? 1u : 0u,
            ["Amount"] = shield ? 27u : 0u,
            ["Current"] = 109
        };
        uint[] row = Pack(program, values, present);
        Assert.Null(GaInterpreter.Execute(program, row));
        values["Current"] = (uint)expected;
        Assert.Equal(Pack(program, values, present), row);
    }

    [Theory]
    [InlineData("[GaAccess(typeof(Row), GaOwnAccess.ReadWrite)]", "row.Current = 1;")]
    [InlineData("[GaAccess(typeof(Heat), GaOwnAccess.Read)] [GaAccess(typeof(Heat), GaOwnAccess.ReadWrite)]", "row.Current = 1;")]
    [InlineData("[GaAccess(typeof(Heat), GaOwnAccess.Read)]", "context.Own<Heat>().Temperature = 10;")]
    [InlineData("[GaAccess(typeof(Heat), GaOwnAccess.OptionalRead)]", "row.Current = context.Own<Heat>().Temperature;")]
    [InlineData("[GaAccess(typeof(Heat), GaOwnAccess.ReadWrite)]", "row.Probe = context.TryRead<Heat>(out var heat);")]
    [InlineData("", "row.Probe = context.Has<Marker>();")]
    [InlineData("", "row.Probe = context.TryRead<Shield>(out var shield);")]
    [InlineData("", "row.Current = context.Own<Heat>().Temperature;")]
    public void RejectsUndeclaredOrConflictingAccess(string attributes, string body)
    {
        var result = Analyze(attributes, body);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id.StartsWith("GA", StringComparison.Ordinal));
    }

    private static GaComponent Component(GaProgram program, string name) =>
        Assert.Single(program.Components, component => component.Id == "global::" + name);

    private static uint[] Pack(GaProgram program, IReadOnlyDictionary<string, uint> values, params string[] present)
    {
        var words = program.Fields.Select(field => values.GetValueOrDefault(field.Path))
            .Concat(program.Components.Where(component => component.Origin != GaComponentOrigin.Query)
                .Select(component => present.Any(type => component.Id == "global::" + type) ? 1u : 0u))
            .ToArray();
        Assert.Equal(program.WordCount, words.Length);
        return words;
    }

    private static GaProgram Compile(string attributes, string body)
    {
        var result = Analyze(attributes, body);
        Assert.True(result.Program is not null, string.Join("\n", result.Diagnostics));
        return result.Program!;
    }

    private static GaCompilationResult Analyze(string attributes, string body)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            using SKECS.GA;
            public struct Row { public int Current, Divisor; public bool Copied, Flag, Probe; }
            public struct Heat { public int Temperature; }
            public struct Hull { public int Scale; }
            public struct Shield { public int Amount; public bool Enabled; }
            public struct Marker { }
            [SecsSystem]
            public partial struct Fixture
            {
                {{attributes}}
                public void Execute(in SystemContext context, ref Row row)
                {
                    {{body}}
                }
            }
            """, path: "OwnFixture.cs");
        string runtime = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");
        var compilation = CSharpCompilation.Create("OwnFixture", [tree],
            new[] { typeof(object).Assembly.Location, runtime, typeof(World).Assembly.Location, typeof(SystemContext).Assembly.Location }
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var entry = (IMethodSymbol)compilation.GetTypeByMetadataName("Fixture")!.GetMembers("Execute").Single();
        return GaCompiler.Compile(compilation, entry);
    }
}
