namespace SKECS.GA.Tests;

public sealed partial class CompilerTests
{
    [Fact]
    public void ConstructorChainInitializesOnceAndContinuesAfterCalleeReturn()
    {
        const string entry = """
            public void Execute(ref Position p, in Velocity input)
            {
                Cell cell = new Cell(60 / input.X, input.Flag) { Y = 35 / input.Y };
                p.Point.X = cell.X;
                p.Point.Y = cell.Y + cell.Extra;
            }
            """;
        const string source = """
            public struct Cell
            {
                public readonly int X = Seed(5);
                public int Y;
                public int Extra = Seed(17);
                public Cell(int size, bool skip) : this(b: 30 / size, a: 20 / size, skip: skip)
                {
                    Extra += size;
                }
                private Cell(int a, int b, bool skip) : this()
                {
                    if (skip) return;
                    Y = a + b;
                }
                private static int Seed(int x) => 100 / x;
            }
            """;
        var result = Analyze(entry, source);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        var program = result.Program!;
        uint[] Row(uint x, uint y) => program.Fields.Select(field => field.Path switch
        {
            "X" => x, "Y" => y, "Flag" => 1u, _ => 99u
        }).ToArray();
        int Find(string name) => Array.FindIndex(program.Fields.ToArray(), field => field.Path == name);
        var row = Row(6, 5);
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(20u, row[Find("Point.X")]);
        Assert.Equal(22u, row[Find("Point.Y")]);
        var fault = Assert.IsType<GaKernelFault>(GaInterpreter.Execute(program, Row(6, 0)));
        Assert.Equal(5ul, fault.EvaluationOrdinal);
        Assert.Equal("35 / input.Y".Length, fault.Source.Length);
        var changed = Analyze(entry, source.Replace("Seed(17)", "Seed(20)", StringComparison.Ordinal));
        Assert.NotEqual(program.SourceDigest, Assert.IsType<GaProgram>(changed.Program).SourceDigest);
    }

    [Fact]
    public void DirectImplicitDefaultSkipsFieldInitializersButExplicitThisExecutesThem()
    {
        var result = Analyze("""
            public void Execute(ref Position p)
            {
                Cell first = new();
                Cell second = default;
                var third = new Cell(4);
                p.Point.X = first.X + second.X;
                p.Point.Y = third.X;
            }
            """, """
            public struct Cell
            {
                public int X = 14;
                public Cell(int increment) : this() { X += increment; }
            }
            """);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        uint[] row = new uint[result.Program!.WordCount];
        Assert.Null(GaInterpreter.Execute(result.Program, row));
        Assert.Equal(new uint[] { 0, 18 }, row);
    }
}
