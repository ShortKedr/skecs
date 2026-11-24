namespace SKECS.GA.Tests;

public sealed partial class CompilerTests
{
    [Fact]
    public void ClosedGenericValueHelpersKeepSeparateSpecializations()
    {
        const string helper = """
            public struct Box<T> where T : unmanaged
            {
                public T Item;
                public readonly T Value => Item;
                public static Box<T> Wrap(T item) => new() { Item = item };
            }
            public static class GenericValues
            {
                public static T Copy<T>(T value) where T : unmanaged => value;
                public static Box<T> Build<T>(T value) where T : unmanaged => Box<T>.Wrap(Copy(value));
            }
            """;
        var result = Analyze("""
            public void Execute(ref Position p, in Velocity v)
            {
                var first = GenericValues.Build(new Vector { X = v.X, Y = v.Y });
                var second = GenericValues.Build(v.X);
                p.Point = GenericValues.Copy(first.Value);
                p.Point.X += second.Value;
            }
            """, helper);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        var program = result.Program!;
        var row = program.Fields.Select(field => field.Path switch { "X" => 3u, "Y" => 4u, _ => 0u }).ToArray();
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(6u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.X")]);
        Assert.Equal(4u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.Y")]);
    }

    [Fact]
    public void AggregateInitializerUsesSourceOrderAndCopiesBeforeWholeAssignment()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                Vector first = new() { Y = 12 / v.Y, X = 24 / v.X };
                var copy = first;
                first.X = 99;
                p.Point = copy;
            }
            """);
        uint[] Row(uint x, uint y) => program.Fields.Select(field => field.Component.EndsWith("Velocity")
            ? field.Path == "X" ? x : field.Path == "Y" ? y : 0u : 0u).ToArray();
        var first = GaInterpreter.Execute(program, Row(0, 0))!.Value;
        var second = GaInterpreter.Execute(program, Row(0, 2))!.Value;
        Assert.Equal(0ul, first.EvaluationOrdinal);
        Assert.Equal(1ul, second.EvaluationOrdinal);
        Assert.Equal("12 / v.Y".Length, first.Source.Length);
        Assert.Equal("24 / v.X".Length, second.Source.Length);
        var row = Row(3, 2);
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(8u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.X")]);
        Assert.Equal(6u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.Y")]);
    }

    [Fact]
    public void AggregateHelperCanReturnEarlyFromABoundedLoop()
    {
        var program = Compile("""
            private static Vector Build(int selected, int denominator)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (i == selected)
                        return new Vector { X = 12 / denominator, Y = i };
                }
                return default;
            }
            public void Execute(ref Position p, in Velocity v)
            {
                var copy = Build(v.X, v.Y);
                p.Point.X = copy.X;
                p.Point.Y = copy.Y;
            }
            """);
        foreach (int selected in new[] { -1, 0, 1, 2, 3 })
        {
            var row = program.Fields.Select(field => field.Path switch
            {
                "X" => unchecked((uint)selected), "Y" => 2u, _ => 0u
            }).ToArray();
            Assert.Null(GaInterpreter.Execute(program, row));
            Assert.Equal(selected is >= 0 and < 3 ? 6u : 0u,
                row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.X")]);
        }
    }

    [Fact]
    public void AggregateConditionalAndTemporaryProjectionEvaluateTheWholeSelectedValue()
    {
        var program = Compile("""
            private static Vector Build(int denominator) => new Vector { X = 5, Y = 12 / denominator };
            public void Execute(ref Position p, in Velocity v)
            {
                var value = v.Flag ? Build(v.X) : new Vector { Y = 7 };
                p.Point.X = value.X;
                p.Point.Y = new Vector { X = 24 / v.Y, Y = 8 }.Y;
            }
            """);
        var row = program.Fields.Select(field => field.Path switch
        {
            "Flag" => 0u, "X" => 0u, "Y" => 0u, _ => 99u
        }).ToArray();
        var fault = GaInterpreter.Execute(program, row)!.Value;
        Assert.Equal(GaFaultKind.DivideByZero, fault.Kind);
        Assert.Equal(0ul, fault.EvaluationOrdinal);
        Assert.Equal("24 / v.Y".Length, fault.Source.Length);
    }

    [Fact]
    public void AggregateCompoundUsesSelectedInputAndOutputConversions()
    {
        const string helper = """
            public struct Balance
            {
                public int Amount;
                public static implicit operator Wide(Balance value) => new Wide { Amount = value.Amount };
                public static implicit operator Balance(Wide value) => new Balance { Amount = checked(value.Amount + 1) };
            }
            public struct Wide
            {
                public int Amount;
            }
            public struct Delta
            {
                public int Amount;
                public static Wide operator +(Wide left, Delta right) => new Wide { Amount = unchecked(left.Amount + right.Amount) };
                public static Wide operator checked +(Wide left, Delta right) => new Wide { Amount = checked(left.Amount + right.Amount) };
            }
            public struct Account { public Balance Value; }
            """;
        var result = Analyze("""
            public void Execute(ref Account account, in Delta delta)
            {
                checked { account.Value += delta; }
            }
            """, helper);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        var program = result.Program!;
        var row = program.Fields.Select(field => field.Path == "Value.Amount" ? 10u : 2u).ToArray();
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(13u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Value.Amount")]);
        row = program.Fields.Select(field => field.Path == "Value.Amount" ? (uint)int.MaxValue - 2 : 2u).ToArray();
        var fault = GaInterpreter.Execute(program, row)!.Value;
        Assert.Equal(GaFaultKind.Overflow, fault.Kind);
        Assert.Equal("checked(value.Amount + 1)".Length - "checked()".Length, fault.Source.Length);
        Assert.Equal("Helper.cs", fault.Source.File);
    }

    [Fact]
    public void AggregateHelperParametersAreIndependentCopies()
    {
        var program = Compile("""
            private static int Change(Vector value)
            {
                value.X = 77;
                return value.X;
            }
            public void Execute(ref Position p, in Velocity v)
            {
                var copy = new Vector { X = v.X };
                p.Point.X = Change(copy);
                p.Point.Y = copy.X;
            }
            """);
        var row = program.Fields.Select(field => field.Path == "X" ? 3u : 0u).ToArray();
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(77u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.X")]);
        Assert.Equal(3u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.Y")]);
    }
}
