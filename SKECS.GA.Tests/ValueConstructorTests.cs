namespace SKECS.GA.Tests;

public sealed partial class CompilerTests
{
    [Fact]
    public void ValueConstructorOrdersArgumentsBodyAndInitializer()
    {
        const string entry = """
            public void Execute(ref Position p, in ConstructorInput input)
            {
                Constructed result = new(right: 32 / input.Right, left: 24 / input.Left, body: input.Body, skip: input.Skip)
                {
                    Y = 48 / input.Init
                };
                p.Point.X = result.X;
                p.Point.Y = result.Y + result.Extra;
            }
            """;
        const string helpers = """
            public struct ConstructorInput
            {
                public int Left, Right, Body, Init;
                public bool Skip;
            }
            public struct Constructed
            {
                public readonly int X;
                public int Y, Extra;
                public Constructed(int left, int right, int body, bool skip)
                {
                    X = left;
                    for (int i = 0; i < 2; i++)
                        if (skip)
                            return;
                    Y = right;
                    Extra = 40 / body;
                }
            }
            """;
        var analysis = Analyze(entry, helpers);
        Assert.True(analysis.Program != null, string.Join("\n", analysis.Diagnostics));
        var program = analysis.Program!;
        uint[] Row(uint left, uint right, uint body, uint init, bool skip) => program.Fields.Select(field => field.Path switch
        {
            "Left" => left, "Right" => right, "Body" => body, "Init" => init, "Skip" => skip ? 1u : 0u, _ => 99u
        }).ToArray();
        int x = Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.X");
        int y = Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.Y");
        var row = Row(3, 4, 5, 6, false);
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(8u, row[x]);
        Assert.Equal(16u, row[y]);
        row = Row(3, 4, 0, 6, true);
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(8u, row[y]);

        CheckFault(Row(0, 0, 0, 0, false), 0, "Movement.cs", "32 / input.Right");
        CheckFault(Row(0, 4, 0, 0, false), 1, "Movement.cs", "24 / input.Left");
        CheckFault(Row(3, 4, 0, 0, false), 2, "Helper.cs", "40 / body");
        CheckFault(Row(3, 4, 5, 0, false), 3, "Movement.cs", "48 / input.Init");
        CheckFault(Row(3, 4, 0, 0, true), 2, "Movement.cs", "48 / input.Init");
        var revised = Analyze(entry, helpers.Replace("40 / body", "50 / body", StringComparison.Ordinal));
        Assert.NotEqual(program.SourceDigest, Assert.IsType<GaProgram>(revised.Program).SourceDigest);

        void CheckFault(uint[] input, ulong ordinal, string file, string expression)
        {
            var fault = Assert.IsType<GaKernelFault>(GaInterpreter.Execute(program, input));
            Assert.Equal(GaFaultKind.DivideByZero, fault.Kind);
            Assert.Equal(ordinal, fault.EvaluationOrdinal);
            Assert.Equal(file, fault.Source.File);
            Assert.Equal(expression.Length, fault.Source.Length);
            if (file == "Helper.cs")
                Assert.Equal(helpers.IndexOf(expression, StringComparison.Ordinal), fault.Source.Start);
            Assert.Equal(99u, input[x]);
            Assert.Equal(99u, input[y]);
        }
    }

    [Fact]
    public void DefaultBypassesParameterlessConstructorAndGenericConstructorCopiesAggregateArgument()
    {
        const string helpers = """
            public struct Cell
            {
                public int Number;
                public Cell() => Number = 9;
            }
            public struct Box<T> where T : unmanaged
            {
                public T Item;
                public Box(T item) => Item = item;
            }
            """;
        var result = Analyze("""
            public void Execute(ref Position p, in Velocity v)
            {
                Cell zero = default;
                Cell constructed = new();
                var boxed = new Box<Vector>(new Vector { X = v.X, Y = v.Y });
                p.Point.X = boxed.Item.X + zero.Number;
                p.Point.Y = boxed.Item.Y + constructed.Number;
            }
            """, helpers);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        var program = result.Program!;
        var row = program.Fields.Select(field => field.Path switch { "X" => 4u, "Y" => 5u, _ => 0u }).ToArray();
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(4u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.X")]);
        Assert.Equal(14u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.Y")]);
    }

    [Fact]
    public void ConstructorBodyWithObjectInitializerStillRequiresPortableCode()
    {
        var result = Analyze("""
            public void Execute(ref Position p, in Velocity v)
            {
                var box = new Unsupported(v.X) { Value = 3 };
                p.Point.X = box.Value;
            }
            """, """
            public struct Unsupported
            {
                public int Value;
                public Unsupported(int seed) => Value = seed + System.Environment.TickCount;
            }
            """);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == "GA0003" && diagnostic.GetMessage().Contains("Unsupported..ctor"));
    }
}
