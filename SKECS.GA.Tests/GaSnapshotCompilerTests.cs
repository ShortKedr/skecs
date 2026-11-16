using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SKECS.GA.Tests;

public sealed class GaSnapshotCompilerTests
{
    [Fact]
    public void SnapshotReadUsesGenerationAndEvaluatesFallbackOnHit()
    {
        var compilation = Compile("context.ReadSnapshotInt32(value.Id, value.Generation, 100 / value.Divisor)");
        var program = compilation.Program;
        Assert.True(program is not null, string.Join("\n", compilation.Diagnostics));
        Assert.True(program!.UsesSnapshot);
        Assert.Equal(new[] { "Result", "Id", "Generation", "Divisor" }, program.Fields.Select(field => field.Path));

        var packet = GaSnapshotPacket.FromRows(new GaSnapshotLayout([GaScalar.Int32]), [new GaSnapshotKey(11, 3)],
            [unchecked((uint)-19)]);
        uint[] row = [7, 11, 3, 4];
        Assert.Null(GaInterpreter.Execute(program, row, [], out _, packet));
        Assert.Equal(unchecked((uint)-19), row[0]);

        row = [7, 11, 4, 4];
        Assert.Null(GaInterpreter.Execute(program, row, [], out _, packet));
        Assert.Equal(25u, row[0]);

        row = [7, 11, 3, 0];
        var fault = GaInterpreter.Execute(program, row, [], out _, packet);
        Assert.True(fault.HasValue);
        Assert.Equal(GaFaultKind.DivideByZero, fault.Value.Kind);
        Assert.Equal(0ul, fault.Value.EvaluationOrdinal);
        Assert.Equal(7u, row[0]);
        Assert.Throws<InvalidOperationException>(() => GaInterpreter.Execute(program, row));
    }

    [Fact]
    public void ReorderedNamedArgumentsAreRejected()
    {
        var compilation = Compile("context.ReadSnapshotInt32(generation: value.Generation, entityId: value.Id, fallback: 0)");
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "GA0004");
    }

    private static GaCompilationResult Compile(string expression)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            using SKECS.GA;
            public struct Row { public int Result; public int Id; public int Generation; public int Divisor; }
            [SecsSystem]
            public partial struct Motion
            {
                public void Execute(in SystemContext context, ref Row value)
                {
                    value.Result = {{expression}};
                }
            }
            """, path: "SnapshotMotion.cs");
        string runtime = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");
        var compilation = CSharpCompilation.Create("SnapshotFixture", [tree],
            new[] { typeof(object).Assembly.Location, runtime, typeof(SystemContext).Assembly.Location }
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var entry = (IMethodSymbol)compilation.GetTypeByMetadataName("Motion")!.GetMembers("Execute").Single();
        return GaCompiler.Compile(compilation, entry);
    }
}
