namespace SKECS.GA.Tests;

public sealed partial class CompilerTests
{
    [Fact]
    public void DirectOwnReadDoesNotCaptureUnrelatedInlineArray()
    {
        var result = Analyze("""
            [GaAccess(typeof(Inventory), GaOwnAccess.Read)]
            public void Execute(ref Position p, in SystemContext context)
            {
                p.Point.X = context.Own<Inventory>().Total;
            }
            """, """
            using System.Runtime.CompilerServices;
            [InlineArray(4)] public struct Slots { private int item; }
            public struct Inventory { public Slots Items; public int Total; }
            """);
        var program = Assert.IsType<GaProgram>(result.Program);
        Assert.Equal("Total", Assert.Single(program.Fields, field => field.Component == "global::Inventory").Path);
        uint[] words = [.. program.Fields.Select(field => field.Path == "Total" ? 27u : 0u), 1u];
        Assert.Equal(program.WordCount, words.Length);
        Assert.Null(GaInterpreter.Execute(program, words));
        Assert.Equal(27u, words[AggregateRegressionColumn(program, "Point.X")]);
    }

    [Fact]
    public void WholeComponentStoreWithReadonlyAncestorIsDiagnosedDuringLowering()
    {
        var result = Analyze("""
            public void Execute(ref ReadonlyRow row)
            {
                row.Value = default;
            }
            """, """
            public struct Cell { public int Count; }
            public struct Outer { public readonly Cell Nested; }
            public struct ReadonlyRow { public Outer Value; }
            """);
        Assert.Null(result.Program);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Id == "GA0100");
        Assert.Contains("read-only component field", diagnostic.GetMessage());
        Assert.Equal("row.Value", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void CopyFromAggregateResultEvaluatesSiblingFieldsInSourceOrder()
    {
        const string helper = """
            public struct Envelope { public Vector Nested; public int Sibling; }
            public static class Factory
            {
                public static Envelope MakeOuter(int nestedDivisor, int siblingDivisor) => new Envelope
                {
                    Nested = new Vector { X = 12 / nestedDivisor, Y = 5 },
                    Sibling = 18 / siblingDivisor
                };
            }
            """;
        var result = Analyze("""
            public void Execute(ref Position p, in Velocity v)
            {
                var nested = Factory.MakeOuter(v.X, v.Y).Nested;
                p.Point = nested;
            }
            """, helper);
        var program = Assert.IsType<GaProgram>(result.Program);
        uint[] Row(uint x, uint y) => program.Fields.Select(field => field.Path switch
        {
            "X" => x, "Y" => y, _ => 99u
        }).ToArray();

        var first = Assert.IsType<GaKernelFault>(GaInterpreter.Execute(program, Row(0, 0)));
        Assert.Equal((GaFaultKind.DivideByZero, 0ul), (first.Kind, first.EvaluationOrdinal));
        Assert.Equal("12 / nestedDivisor", helper.Substring(first.Source.Start, first.Source.Length));

        var row = Row(3, 0);
        var before = row.ToArray();
        var sibling = Assert.IsType<GaKernelFault>(GaInterpreter.Execute(program, row));
        Assert.Equal((GaFaultKind.DivideByZero, 1ul), (sibling.Kind, sibling.EvaluationOrdinal));
        Assert.Equal("18 / siblingDivisor", helper.Substring(sibling.Source.Start, sibling.Source.Length));
        Assert.Equal(before, row);

        row = Row(3, 2);
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(4u, row[AggregateRegressionColumn(program, "Point.X")]);
        Assert.Equal(5u, row[AggregateRegressionColumn(program, "Point.Y")]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscardedAggregateCallStillEvaluatesItsBody(bool throughHelper)
    {
        var program = Compile("""
            private static Vector Build(int divisor) => new Vector { X = 84 / divisor };
            private static int Evaluate(int divisor)
            {
                Build(divisor);
                return 77;
            }
            public void Execute(ref Position p, in Velocity v)
            {
                if (v.Flag)
                    p.Point.X = Evaluate(v.X);
                else
                    Build(v.X);
                p.Point.Y = 11;
            }
            """);
        uint[] Row(uint divisor) => program.Fields.Select(field => field.Path switch
        {
            "Flag" => throughHelper ? 1u : 0u, "X" => divisor, _ => 99u
        }).ToArray();

        var row = Row(0);
        var before = row.ToArray();
        var fault = Assert.IsType<GaKernelFault>(GaInterpreter.Execute(program, row));
        Assert.Equal((GaFaultKind.DivideByZero, 0ul), (fault.Kind, fault.EvaluationOrdinal));
        Assert.Equal("84 / divisor".Length, fault.Source.Length);
        Assert.Equal(before, row);

        row = Row(2);
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(throughHelper ? 77u : 99u, row[AggregateRegressionColumn(program, "Point.X")]);
        Assert.Equal(11u, row[AggregateRegressionColumn(program, "Point.Y")]);
    }

    [Fact]
    public void PartialStaticAggregateHelperUsesImplementationBody()
    {
        const string helper = """
            public static partial class Factory
            {
                public static partial Vector Make(int divisor);
            }
            public static partial class Factory
            {
                public static partial Vector Make(int divisor) => new Vector { X = 30 / divisor, Y = 22 };
            }
            """;
        var result = Analyze("""
            public void Execute(ref Position p, in Velocity v) => p.Point = Factory.Make(v.X);
            """, helper);
        var program = Assert.IsType<GaProgram>(result.Program);
        var fault = Assert.IsType<GaKernelFault>(GaInterpreter.Execute(program, new uint[program.WordCount]));
        Assert.Equal((GaFaultKind.DivideByZero, 0ul), (fault.Kind, fault.EvaluationOrdinal));
        Assert.Equal("Helper.cs", fault.Source.File);
        Assert.Equal("30 / divisor", helper.Substring(fault.Source.Start, fault.Source.Length));

        var row = program.Fields.Select(field => field.Path == "X" ? 3u : 0u).ToArray();
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(10u, row[AggregateRegressionColumn(program, "Point.X")]);
        Assert.Equal(22u, row[AggregateRegressionColumn(program, "Point.Y")]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnumSubtractionCompoundConvertsBackToTheEnum(bool useChecked)
    {
        var result = Analyze("""
            public void Execute(ref EnumRow row, in Velocity v)
            {
                if (v.Flag)
                    checked { row.Signed -= SignedStep.One; row.Unsigned -= UnsignedStep.One; }
                else
                    unchecked { row.Signed -= SignedStep.One; row.Unsigned -= UnsignedStep.One; }
            }
            """, """
            public enum SignedStep : int { One = 1 }
            public enum UnsignedStep : uint { One = 1 }
            public struct EnumRow { public SignedStep Signed; public UnsignedStep Unsigned; }
            """);
        var program = Assert.IsType<GaProgram>(result.Program);
        uint[] Row(uint signed, uint unsigned) => program.Fields.Select(field => field.Path switch
        {
            "Signed" => signed, "Unsigned" => unsigned, _ => useChecked ? 1u : 0u
        }).ToArray();

        var row = Row(5, 9);
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(4u, row[AggregateRegressionColumn(program, "Signed")]);
        Assert.Equal(8u, row[AggregateRegressionColumn(program, "Unsigned")]);

        row = Row(unchecked((uint)int.MinValue), 0);
        if (!useChecked)
        {
            Assert.Null(GaInterpreter.Execute(program, row));
            Assert.Equal((uint)int.MaxValue, row[AggregateRegressionColumn(program, "Signed")]);
            Assert.Equal(uint.MaxValue, row[AggregateRegressionColumn(program, "Unsigned")]);
            return;
        }

        var fault = Assert.IsType<GaKernelFault>(GaInterpreter.Execute(program, row));
        Assert.Equal((GaFaultKind.Overflow, 0ul), (fault.Kind, fault.EvaluationOrdinal));
        row = Row(5, 0);
        var before = row.ToArray();
        fault = Assert.IsType<GaKernelFault>(GaInterpreter.Execute(program, row));
        Assert.Equal((GaFaultKind.Overflow, 1ul), (fault.Kind, fault.EvaluationOrdinal));
        Assert.Equal(before, row);
    }

    private static int AggregateRegressionColumn(GaProgram program, string path) =>
        Array.FindIndex(program.Fields.ToArray(), field => field.Path == path);
}
