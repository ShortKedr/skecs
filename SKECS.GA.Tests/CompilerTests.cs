using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SKECS.GA.Tests;

public sealed partial class CompilerTests
{
    [Fact]
    public void MovementMatchesIndependentCSharpAcrossTicks()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                for (int i = 0; i < 4; i++)
                {
                    p.Point.X += v.X;
                    if (p.Point.X < 0 && v.Y != 0)
                        p.Point.Y -= p.Point.X % v.Y;
                    else
                        p.Point.Y += (p.Point.X >>> v.Y);
                }
            }
            """);

        Assert.Equal(new[] { "Point.X", "X", "Y", "Point.Y" }, program.Fields.Select(f => f.Path));
        uint[] words = [unchecked((uint)-8), 1, 3, 42];
        int x = -8;
        int y = 42;
        for (int tick = 0; tick < 10; tick++)
        {
            for (int i = 0; i < 4; i++)
            {
                x += 1;
                y += x < 0 ? -(x % 3) : x >>> 3;
            }
            Assert.Null(GaInterpreter.Execute(program, words));
            Assert.Equal(unchecked((uint)x), words[0]);
            Assert.Equal(1u, words[1]);
            Assert.Equal(3u, words[2]);
            Assert.Equal(unchecked((uint)y), words[3]);
        }
        var shader = GaSlangEmitter.EmitVulkan(program);
        Assert.Equal(shader.Source, GaSlangEmitter.EmitVulkan(program).Source);
        Assert.Contains("gaFaults", shader.Source);
        Assert.Contains(" % ", shader.Source);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(127)]
    [InlineData(128)]
    public void WorkgroupWidthChangesOnlyPhysicalShaderDispatch(int workgroupX)
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v) => p.Point.X = checked(p.Point.X + v.X);
            """);
        var fixedHeader = $"[numthreads({GaSlangModule.ThreadsPerGroup}, 1, 1)]";
        var variantHeader = $"[numthreads({workgroupX}, 1, 1)]";
        foreach (var (baseline, variant) in new[]
        {
            (GaSlangEmitter.EmitVulkan(program), GaSlangEmitter.EmitVulkan(program, workgroupX)),
            (GaSlangEmitter.EmitD3D12(program), GaSlangEmitter.EmitD3D12(program, workgroupX))
        })
        {
            Assert.Contains(fixedHeader, baseline.Source);
            Assert.Equal(baseline.Source.Replace(fixedHeader, variantHeader, StringComparison.Ordinal), variant.Source);
            Assert.Equal(baseline.FaultSources.ToArray(), variant.FaultSources.ToArray());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveWorkgroupWidthIsRejected(int workgroupX)
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v) => p.Point.X = v.X;
            """);
        Assert.Throws<ArgumentOutOfRangeException>(nameof(workgroupX), () => GaSlangEmitter.EmitVulkan(program, workgroupX));
        Assert.Throws<ArgumentOutOfRangeException>(nameof(workgroupX), () => GaSlangEmitter.EmitD3D12(program, workgroupX));
    }

    [Fact]
    public void D3D12WorkgroupWidthRespectsTheTargetLimit()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v) => p.Point.X = v.X;
            """);
        Assert.Contains("[numthreads(1024, 1, 1)]", GaSlangEmitter.EmitD3D12(program, 1024).Source);
        Assert.Throws<ArgumentOutOfRangeException>("workgroupX", () => GaSlangEmitter.EmitD3D12(program, 1025));
    }

    [Fact]
    public void FaultRetainsRowAndSelectsFirstDynamicallyEvaluatedOperation()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                p.Point.X = 1;
                if (v.Flag && 1 / v.Y == 0)
                    p.Point.X = 99;
                for (int i = 0; i < 4; i++)
                    p.Point.Y = 100 / (2 - i);
                p.Point.X = checked(v.X * v.X);
            }
            """);

        uint[] words = [7, 0, 0, 8, 50000];
        var fault = GaInterpreter.Execute(program, words);
        Assert.NotNull(fault);
        Assert.Equal(GaFaultKind.DivideByZero, fault.Value.Kind);
        Assert.Equal(2ul, fault.Value.EvaluationOrdinal);
        Assert.Equal(new uint[] { 7, 0, 0, 8, 50000 }, words);

        words[1] = 1;
        fault = GaInterpreter.Execute(program, words);
        Assert.Equal(0ul, fault!.Value.EvaluationOrdinal);
        Assert.Throws<ArgumentException>(() => GaInterpreter.Execute(program, [7, 2, 0, 8, 50000]));
    }

    [Fact]
    public void SignedMinRemainderFaultIsContextIndependent()
    {
        foreach (string context in new[] { "checked", "unchecked" })
        {
            var program = Compile($$"""
                public void Execute(ref Position p, in Velocity v)
                {
                    p.Point.X = {{context}}(v.X % v.Y);
                }
                """);
            var fault = GaInterpreter.Execute(program, [0, 0x80000000u, 0xffffffffu]);
            Assert.Equal(GaFaultKind.Overflow, fault!.Value.Kind);
            uint[] words = [0, 0x80000000u, 3];
            Assert.Null(GaInterpreter.Execute(program, words));
            Assert.Equal(unchecked((uint)-2), words[0]);
        }
    }

    [Fact]
    public void RejectsUnsupportedLoweringInsteadOfSilentlySelectingCpu()
    {
        var result = Analyze("""
            public void Execute(ref Position p, in Velocity v)
            {
                p.Point.X = Add(v.X, v.Y);
            }
            private static int Add(int a, int b)
            {
                return a + ++b;
            }
            """);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "GA0100");
    }

    [Fact]
    public void ScalarHelpersBindNamedArgumentsAndSpecializeNestedGenerics()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                if (v.Flag)
                    p.Point.X = Relay(Pick(second: 12 / v.Y, first: 24 / v.X));
                p.Point.Y = 100 / v.X;
            }
            private static int Pick(int first, int second)
            {
                int doubled = unchecked(first + first);
                return doubled;
            }
            private static T Relay<T>(T value) where T : unmanaged
            {
                T local = value;
                return Identity(local);
            }
            private static T Identity<T>(T value) where T : unmanaged => value;
            """);

        uint[] Row(int x, int y, bool flag) => program.Fields.Select(field => field.Path switch
        {
            "X" => (uint)x, "Y" => (uint)y, "Flag" => flag ? 1u : 0u, _ => 0u
        }).ToArray();

        var row = Row(3, 2, true);
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(16u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.X")]);

        var first = GaInterpreter.Execute(program, Row(0, 0, true))!.Value;
        var second = GaInterpreter.Execute(program, Row(0, 2, true))!.Value;
        var skipped = GaInterpreter.Execute(program, Row(0, 0, false))!.Value;
        Assert.Equal(0ul, first.EvaluationOrdinal);
        Assert.Equal(1ul, second.EvaluationOrdinal);
        Assert.Equal(0ul, skipped.EvaluationOrdinal);
        Assert.True(first.Source.Start < second.Source.Start && second.Source.Start < skipped.Source.Start);
    }

    [Fact]
    public void ExternalHelperChangesSourceIdentityAndKeepsItsFaultSpan()
    {
        const string execute = "public void Execute(ref Position p, in Velocity v) => p.Point.X = MathHelper.Divide(v.X);";
        const string helper = "static class MathHelper { internal static int Divide(int value) => 17 / value; }";
        var original = Assert.IsType<GaProgram>(Analyze(execute, helper).Program);
        var changed = Assert.IsType<GaProgram>(Analyze(execute, helper.Replace("17", "19", StringComparison.Ordinal)).Program);
        Assert.NotEqual(original.SourceDigest, changed.SourceDigest);
        var fault = GaInterpreter.Execute(original, [0, 0])!.Value;
        Assert.Equal("Helper.cs", fault.Source.File);
        Assert.Equal("17 / value", helper.Substring(fault.Source.Start, fault.Source.Length));
    }

    [Fact]
    public void RejectsHelperWithStaticInitialization()
    {
        var result = Analyze("public void Execute(ref Position p, in Velocity v) => p.Point.X = Utility.Identity(v.X);",
            "static class Utility { static Utility() { } internal static int Identity(int x) => x; }");
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "GA0100" &&
            diagnostic.GetMessage().Contains("static initialization", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadonlyGetterOperatorsAndConversionReadTheNestedValueByCopy()
    {
        const string execute = """
            public void Execute(ref Position p, in MemberEnvelope envelope)
            {
                p.Point.X = Knobs.Offset + envelope.Nested.Ratio + (int)envelope.Nested + -envelope.Nested;
                p.Point.Y = checked(envelope.Nested + 3);
            }
            """;
        const string helpers = """
            public struct Counts
            {
                public int Value;
                public int Divisor;
                public readonly int Ratio => 100 / Divisor;
                public static explicit operator int(Counts count) => count.Value;
                public static int operator -(Counts count) => unchecked(-count.Value);
                public static int operator +(Counts count, int amount) => unchecked(count.Value + amount);
                public static int operator checked +(Counts count, int amount) => checked(count.Value + amount);
            }
            public struct MemberEnvelope { public Counts Nested; }
            public static class Knobs { public static int Offset => unchecked(1 + 1); }
            """;
        var compile = Analyze(execute, helpers);
        Assert.True(compile.Program != null, string.Join("\n", compile.Diagnostics));
        var program = compile.Program!;
        var revised = Analyze(execute, helpers.Replace("1 + 1", "1 + 2", StringComparison.Ordinal));
        Assert.NotEqual(program.SourceDigest, Assert.IsType<GaProgram>(revised.Program).SourceDigest);

        uint[] Row(int value, int divisor) => program.Fields.Select(field => field.Path switch
        {
            "Nested.Value" => (uint)value, "Nested.Divisor" => (uint)divisor, _ => 0u
        }).ToArray();
        int Column(string path) => Array.FindIndex(program.Fields.ToArray(), field => field.Path == path);

        var row = Row(7, 4);
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(27u, row[Column("Point.X")]);
        Assert.Equal(10u, row[Column("Point.Y")]);

        row = Row(int.MaxValue, 4);
        var before = row.ToArray();
        var overflow = GaInterpreter.Execute(program, row)!.Value;
        Assert.Equal(GaFaultKind.Overflow, overflow.Kind);
        Assert.Equal(1ul, overflow.EvaluationOrdinal);
        Assert.Equal("Helper.cs", overflow.Source.File);
        Assert.Equal("count.Value + amount", helpers.Substring(overflow.Source.Start, overflow.Source.Length));
        Assert.Equal(helpers.LastIndexOf("count.Value + amount", StringComparison.Ordinal), overflow.Source.Start);
        Assert.Equal(before, row);

        var zero = GaInterpreter.Execute(program, Row(int.MaxValue, 0))!.Value;
        Assert.Equal(GaFaultKind.DivideByZero, zero.Kind);
        Assert.Equal(0ul, zero.EvaluationOrdinal);
        Assert.Equal("100 / Divisor", helpers.Substring(zero.Source.Start, zero.Source.Length));
    }

    [Fact]
    public void ExplicitValueCopyKeepsItsFieldsAfterTheSourceChanges()
    {
        const string execute = """
            public void Execute(ref Position p, ref MemberEnvelope envelope)
            {
                var copy = envelope.Nested;
                envelope.Nested.Value = 99;
                p.Point.X = copy.Value + (int)copy;
                p.Point.Y = (int)envelope.Nested;
            }
            """;
        const string helpers = """
            public struct Counts
            {
                public int Value;
                public int Offset;
                public static explicit operator int(Counts count) => count.Value + count.Offset;
            }
            public struct MemberEnvelope { public Counts Nested; }
            """;
        var result = Analyze(execute, helpers);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        var program = result.Program!;
        Assert.Equal(new[] { "Nested.Offset", "Nested.Value", "Point.X", "Point.Y" }, program.Fields.Select(field => field.Path));
        uint[] row = [2, 7, 0, 0];
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(new uint[] { 2, 99, 16, 101 }, row);
    }

    [Fact]
    public void HelperReturnsSkipOnlyTheirOwnBodyAndPreserveLocalAssignment()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                p.Point.X = Choose(v.X, v.Y, v.Flag);
                p.Point.Y = checked(p.Point.Y + 11);
            }
            private static int Choose(int value, int divisor, bool alternate)
            {
                int selected;
                if (alternate)
                {
                    if (divisor == 0)
                        return 5;
                    selected = 24 / divisor;
                }
                else
                {
                    value++;
                    selected = value;
                }
                selected = checked(selected + 1);
                return selected;
            }
            """);

        uint[] Row(int value, int divisor, bool alternate, int output) => program.Fields.Select(field => field.Path switch
        {
            "X" => (uint)value, "Y" => (uint)divisor, "Flag" => alternate ? 1u : 0u,
            "Point.Y" => (uint)output, _ => 0u
        }).ToArray();
        int Column(string path) => Array.FindIndex(program.Fields.ToArray(), field => field.Path == path);

        var early = Row(7, 0, true, 0);
        Assert.Null(GaInterpreter.Execute(program, early));
        Assert.Equal(5u, early[Column("Point.X")]);
        Assert.Equal(11u, early[Column("Point.Y")]);

        var then = Row(7, 3, true, 0);
        Assert.Null(GaInterpreter.Execute(program, then));
        Assert.Equal(9u, then[Column("Point.X")]);

        var otherwise = Row(7, 0, false, 0);
        Assert.Null(GaInterpreter.Execute(program, otherwise));
        Assert.Equal(9u, otherwise[Column("Point.X")]);
        Assert.Equal(7u, otherwise[Column("X")]);

        var earlyFault = GaInterpreter.Execute(program, Row(7, 0, true, int.MaxValue))!.Value;
        var fallthroughFault = GaInterpreter.Execute(program, Row(7, 3, true, int.MaxValue))!.Value;
        Assert.Equal(GaFaultKind.Overflow, earlyFault.Kind);
        Assert.Equal(0ul, earlyFault.EvaluationOrdinal);
        Assert.Equal(2ul, fallthroughFault.EvaluationOrdinal);
        Assert.Equal(earlyFault.Source, fallthroughFault.Source);
    }

    [Fact]
    public void HelperCanReturnDirectlyFromBothArms()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v) => p.Point.X = Choose(v.X, v.Flag);
            private static int Choose(int value, bool useInput)
            {
                if (useInput)
                    return value;
                else
                    return 23;
            }
            """);

        Assert.Equal(new[] { "Point.X", "X", "Flag" }, program.Fields.Select(field => field.Path));
        uint[] row = [0, 7, 0];
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(23u, row[0]);
        row[2] = 1;
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(7u, row[0]);
    }

    [Fact]
    public void EarlyReturnFromAHelperLoopSkipsItsCheckedStepAndKeepsCallerEvaluation()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                p.Point.X = Pick(v.X);
                p.Point.Y = 100 / v.Y;
            }
            private static int Pick(int stop)
            {
                checked
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        if (i == stop) return unchecked(i + 10);
                    }
                }
                return 50;
            }
            """);

        uint[] Row(uint stop, uint divisor) => program.Fields.Select(field => field.Path switch
        {
            "X" => stop, "Y" => divisor, _ => 0u
        }).ToArray();
        int Index(string path) => Array.FindIndex(program.Fields.ToArray(), field => field.Path == path);

        var completed = Row(2, 4);
        Assert.Null(GaInterpreter.Execute(program, completed));
        Assert.Equal(12u, completed[Index("Point.X")]);
        Assert.Equal(25u, completed[Index("Point.Y")]);

        var earlyFault = GaInterpreter.Execute(program, Row(0, 0))!.Value;
        var laterFault = GaInterpreter.Execute(program, Row(2, 0))!.Value;
        var fullFault = GaInterpreter.Execute(program, Row(4, 0))!.Value;
        Assert.Equal(GaFaultKind.DivideByZero, earlyFault.Kind);
        Assert.Equal(0ul, earlyFault.EvaluationOrdinal);
        Assert.Equal(2ul, laterFault.EvaluationOrdinal);
        Assert.Equal(4ul, fullFault.EvaluationOrdinal);
        Assert.Equal(earlyFault.Source, laterFault.Source);
        Assert.Equal(earlyFault.Source, fullFault.Source);
    }

    [Fact]
    public void HelperSwitchReturnsOnlyFromTheSelectedArm()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v) => p.Point.X = Pick(v.X);
            private static int Pick(int value)
            {
                switch (value)
                {
                    case 0: return 12;
                    case 1: value += 6; break;
                    default: return 98;
                }
                return value;
            }
            """);

        Assert.Equal(new[] { "Point.X", "X" }, program.Fields.Select(field => field.Path));
        foreach (var (input, output) in new[] { (0u, 12u), (1u, 7u), (2u, 98u) })
        {
            uint[] row = [0, input];
            Assert.Null(GaInterpreter.Execute(program, row));
            Assert.Equal(output, row[0]);
        }
    }

    [Theory]
    [InlineData(255, true)]
    [InlineData(256, false)]
    public void LoopBudgetIncludesLoopsInsideCalledHelpers(int helperIterations, bool accepted)
    {
        var result = Analyze($$"""
            public void Execute(ref Position p, in Velocity v)
            {
                for (int i = 0; i < 256; ++i)
                    p.Point.X += Count(i);
            }
            private static int Count(int value)
            {
                for (int j = 0; j < {{helperIterations}}; ++j)
                    ++value;
                return value;
            }
            """);

        if (accepted)
            Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        else
        {
            Assert.Null(result.Program);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "GA0100" &&
                diagnostic.GetMessage().Contains("65536 total", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ReadonlyIndexersUseSourceOrderForNamedArguments()
    {
        const string execute = """
            public void Execute(ref Position p, in Selector selector, in Velocity v)
            {
                p.Point.X = selector[second: 12 / v.Y, first: 24 / v.X];
                p.Point.Y = selector[3];
            }
            """;
        const string helper = """
            public struct Selector
            {
                public int Weight;
                public readonly int this[int first, int second] => unchecked(Weight + first * 3 + second);
                public readonly int this[int value]
                {
                    get { return unchecked(Weight - value); }
                }
            }
            """;
        var result = Analyze(execute, helper);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        var program = result.Program!;

        uint[] Row(uint x, uint y) => program.Fields.Select(field => field.Path switch
        {
            "X" => x, "Y" => y, "Weight" => 4u, _ => 0u
        }).ToArray();
        int Index(string path) => Array.FindIndex(program.Fields.ToArray(), field => field.Path == path);

        var row = Row(3, 2);
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(34u, row[Index("Point.X")]);
        Assert.Equal(1u, row[Index("Point.Y")]);

        var first = GaInterpreter.Execute(program, Row(0, 0))!.Value;
        var second = GaInterpreter.Execute(program, Row(0, 2))!.Value;
        Assert.Equal(0ul, first.EvaluationOrdinal);
        Assert.Equal(1ul, second.EvaluationOrdinal);
        Assert.Equal("12 / v.Y".Length, first.Source.Length);
        Assert.Equal("24 / v.X".Length, second.Source.Length);
        Assert.True(first.Source.Start < second.Source.Start);
    }

    [Fact]
    public void CheckedUserCompoundEvaluatesInlineArrayTargetExactlyOnce()
    {
        const string execute = """
            public void Execute(ref Accumulator p, in Delta delta, in Velocity v)
            {
                if (v.Flag)
                    checked { p.Items[100 / v.X] += delta; }
                else
                    unchecked { p.Items[100 / v.X] += delta; }
            }
            """;
        const string helper = """
            using System.Runtime.CompilerServices;
            [InlineArray(4)] public struct IntSlots { private int element; }
            public struct Accumulator { public IntSlots Items; }
            public struct Delta
            {
                public int Amount;
                public static int operator +(int left, Delta right) => unchecked(left + right.Amount);
                public static int operator checked +(int left, Delta right) => checked(left + right.Amount);
            }
            """;
        var result = Analyze(execute, helper);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        var program = result.Program!;
        uint[] Row(uint x, bool useChecked) => program.Fields.Select(field => field.Path switch
        {
            "X" => x, "Flag" => useChecked ? 1u : 0u, "Items[2]" => (uint)int.MaxValue, "Amount" => 1u, _ => 0u
        }).ToArray();
        int selected = Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Items[2]");

        var division = GaInterpreter.Execute(program, Row(0, true))!.Value;
        var invalidIndex = GaInterpreter.Execute(program, Row(10, true))!.Value;
        var overflow = GaInterpreter.Execute(program, Row(50, true))!.Value;
        Assert.Equal((GaFaultKind.DivideByZero, 0ul), (division.Kind, division.EvaluationOrdinal));
        Assert.Equal((GaFaultKind.IndexOutOfRange, 1ul), (invalidIndex.Kind, invalidIndex.EvaluationOrdinal));
        Assert.Equal((GaFaultKind.Overflow, 2ul), (overflow.Kind, overflow.EvaluationOrdinal));
        Assert.Equal("Helper.cs", overflow.Source.File);
        Assert.Equal(helper.LastIndexOf("left + right.Amount", StringComparison.Ordinal), overflow.Source.Start);

        var wrapped = Row(50, false);
        Assert.Null(GaInterpreter.Execute(program, wrapped));
        Assert.Equal(unchecked((uint)int.MinValue), wrapped[selected]);
    }

    [Fact]
    public void HelperScalarLocalCanUseAUserCompoundOperator()
    {
        const string execute = "public void Execute(ref Position p, in Holder input) => p.Point.X = input.Sum;";
        const string helper = """
            public struct Delta
            {
                public int Amount;
                public static int operator +(int left, Delta right) => unchecked(left + right.Amount);
            }
            public struct Holder
            {
                public Delta Addition;
                public readonly int Sum
                {
                    get
                    {
                        int local = 4;
                        local += Addition;
                        return local;
                    }
                }
            }
            """;
        var result = Analyze(execute, helper);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        var program = result.Program!;
        Assert.Equal(new[] { "Point.X", "Addition.Amount" }, program.Fields.Select(field => field.Path));
        uint[] row = [0, 6];
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(10u, row[0]);
    }

    [Fact]
    public void EnumSwitchAndDescendingLoopUseExactControlFlow()
    {
        var program = Compile("""
            private enum Choice : int { First = -1, Second = 2, Third = 3 }
            public void Execute(ref Position p, in Velocity v)
            {
                int value;
                switch ((Choice)v.X)
                {
                    case Choice.Second:
                    case Choice.Third:
                        value = 10;
                        break;
                    case Choice.First:
                    {
                        value = 20;
                        break;
                    }
                    default:
                        value = 30;
                        break;
                }
                for (int i = 3; i >= 1; --i)
                    value += i;
                p.Point.X = value;
            }
            """);

        foreach (var (choice, result) in new[] { (-1, 26), (2, 16), (3, 16), (100, 36) })
        {
            uint[] words = [unchecked((uint)choice), 0];
            Assert.Null(GaInterpreter.Execute(program, words));
            Assert.Equal((uint)result, words[1]);
        }
    }

    [Fact]
    public void LoopBranchesPreserveSwitchTargetsAndCheckedIncrement()
    {
        var program = Compile("""
            public void Execute(in SystemContext context, ref Position p, in Velocity v)
            {
                checked
                {
                    for (int i = 0; i < 5; ++i)
                    {
                        if (i == 0)
                            continue;
                        int value = unchecked(i * 10);
                        switch (i)
                        {
                            case 1:
                                break;
                            case 2:
                                continue;
                            default:
                                value = unchecked(value + 1);
                                break;
                        }
                        if (i == 4)
                            break;
                        context.Emit(value);
                    }
                }
                p.Point.X = 100 / v.X;
            }
            """);

        Assert.Equal(5, program.EventCapacity);
        uint[] row = [0, 2];
        int[] events = new int[program.EventCapacity];
        Assert.Null(GaInterpreter.Execute(program, row, events, out int count));
        Assert.Equal(2, count);
        Assert.Equal(new[] { 10, 31 }, events[..count]);
        Assert.Equal(50u, row[0]);

        row = [0, 0];
        var fault = GaInterpreter.Execute(program, row, events, out count)!.Value;
        Assert.Equal(GaFaultKind.DivideByZero, fault.Kind);
        Assert.Equal(4ul, fault.EvaluationOrdinal);
        Assert.Equal(0, count);
    }

    [Fact]
    public void ContinueKeepsDefiniteAssignmentOnTheRemainingPaths()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                int outside;
                for (int i = 0; i < 4; ++i)
                {
                    if (i == v.X)
                        continue;
                    else
                        outside = i;
                    int inside;
                    if (i == 3)
                        break;
                    else
                        inside = 10 * outside;
                    p.Point.X += inside;
                }
            }
            """);

        uint[] row = program.Fields.Select(field => field.Path == "X" ? 1u : 0u).ToArray();
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(20u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.X")]);
    }

    [Fact]
    public void BoundedWhilePreservesLeadingAndTrailingStepOrder()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                p.Point.X = 0;
                checked
                {
                    int i = 0;
                    while (i < 4)
                    {
                        if (i == v.X) break;
                        p.Point.X = unchecked(p.Point.X * 10 + i);
                        ++i;
                    }
                    int j = 4;
                    while (j > 0)
                    {
                        j -= 1;
                        if (j == v.Y) continue;
                        p.Point.X = unchecked(p.Point.X * 10 + j);
                    }
                }
                p.Point.Y = 100 / v.Y;
            }
            """);

        Assert.Equal(new[] { "Point.X", "X", "Y", "Point.Y" }, program.Fields.Select(field => field.Path));
        uint[] row = [0, 2, 1, 0];
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(new uint[] { 1320, 2, 1, 100 }, row);

        row = [12, 2, 0, 34];
        var fault = GaInterpreter.Execute(program, row)!.Value;
        Assert.Equal(GaFaultKind.DivideByZero, fault.Kind);
        Assert.Equal(6ul, fault.EvaluationOrdinal);
        Assert.Equal(new uint[] { 12, 2, 0, 34 }, row);
    }

    [Fact]
    public void BoundedWhileAllowsZeroTripsAndAnUnbracedStep()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                int i = int.MaxValue;
                while (i < int.MaxValue) ++i;
                p.Point.X = i;
                int j = 0;
                while (j < 4) j += 1;
                p.Point.Y = j;
            }
            """);

        uint[] row = [0, 0];
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(new uint[] { int.MaxValue, 4 }, row);
    }

    [Theory]
    [InlineData("int i = 0; while (i < 65537) ++i;", "GA0100")]
    [InlineData("int i = int.MaxValue; while (i <= int.MaxValue) ++i;", "GA0004")]
    [InlineData("int i = 0; while (i < 4) { if (v.X == 0) continue; ++i; }", "GA0004")]
    [InlineData("int i = 0; while (i < 4) { ++i; i = v.X; }", "GA0004")]
    [InlineData("int i = 0; while (i < v.X) ++i;", "GA0100")]
    [InlineData("int i = 0; p.Point.X = i; while (i < 4) ++i;", "GA0100")]
    public void WhileRequiresAProvenPortableBound(string body, string diagnosticId)
    {
        var result = Analyze($$"""
            public void Execute(ref Position p, in Velocity v) { {{body}} }
            """);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public void IntLoopsUseTheCapturedRangeAndRetainRuntimeStartAndEnd()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                int upper = v.X & 7;
                p.Point.X = 1;
                for (int i = v.Y & 3; i <= upper; i += 1)
                    p.Point.X = unchecked(p.Point.X + i);

                int lower = v.Flag ? v.X % 5 : 3 & v.X;
                int j = v.Y & 7;
                while (j >= lower)
                {
                    --j;
                    p.Point.Y = unchecked(p.Point.Y + j);
                }
                upper = 123;
                p.Point.X = unchecked(p.Point.X + upper);
            }
            """);

        uint[] row = program.Fields.Select(field => field.Path switch
        {
            "X" => unchecked((uint)-2), "Y" => 5u, "Flag" => 1u, _ => 0u
        }).ToArray();
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(145u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.X")]);
        Assert.Equal(4u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.Y")]);
    }

    [Fact]
    public void ProvesTheInclusiveMaximumOf65536Trips()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                int limit = v.X & 65535;
                for (int i = 0; i <= limit; ++i)
                    ++p.Point.X;
            }
            """);

        Assert.Equal(new[] { "X", "Point.X" }, program.Fields.Select(field => field.Path));
        uint[] row = [uint.MaxValue, 0];
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(65536u, row[1]);
    }

    [Theory]
    [InlineData("int limit = v.X; for (int i = 0; i < limit; ++i) ++p.Point.X;", "GA0100")]
    [InlineData("int limit = v.X & 7; if (v.Flag) limit = v.Y; for (int i = 0; i < limit; ++i) ++p.Point.X;", "GA0100")]
    [InlineData("int limit = v.X & 65536; for (int i = 0; i <= limit; ++i) ++p.Point.X;", "GA0100")]
    [InlineData("int limit = v.X & int.MaxValue; for (int i = 0; i <= limit; ++i) ++p.Point.X;", "GA0004")]
    [InlineData("int limit = v.X & int.MinValue; for (int i = 0; i < limit; ++i) ++p.Point.X;", "GA0100")]
    [InlineData("int limit = v.Flag ? (v.X & 7) : v.Y; for (int i = 0; i < limit; ++i) ++p.Point.X;", "GA0100")]
    public void RejectsLoopBoundsThatCannotBeProvenFrozenAndWithinTheTripCap(string body, string id)
    {
        var result = Analyze($$"""
            public void Execute(ref Position p, in Velocity v) { {{body}} }
            """);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == id);
    }

    [Fact]
    public void NestedLoopAndSwitchBranchesPreserveTheirOwnTargets()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                for (int i = 0; i < 4; ++i)
                {
                    if (i == 3) break;
                    int limit = i & 7;
                    int j = 0;
                    while (j < limit)
                    {
                        ++j;
                        switch (j)
                        {
                            case 1: continue;
                            case 2: break;
                        }
                        if (v.X == j) break;
                        p.Point.X += i * 10 + j;
                    }
                    if (i == 1) continue;
                    ++p.Point.Y;
                }
            }
            """);

        Assert.Equal(new[] { "X", "Point.X", "Point.Y" }, program.Fields.Select(field => field.Path));
        uint[] row = [99, 0, 0];
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(new uint[] { 99, 22, 2 }, row);
        row = [2, 0, 0];
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(new uint[] { 2, 0, 2 }, row);
    }

    [Fact]
    public void NestedLoopsCanReadTheCurrentOuterCounterAsStartOrBound()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                for (int i = 0; i < 5; ++i)
                {
                    for (int j = 0; j < i; ++j) ++p.Point.X;
                    for (int j = i; j > 0; --j) ++p.Point.X;
                    for (int j = 0; j <= i; ++j) ++p.Point.Y;
                }
                int level = 0;
                while (level < 5)
                {
                    ++level;
                    for (int j = 0; j < level; ++j) ++p.Point.Y;
                    for (int j = 6; j >= level; --j) ++p.Point.Y;
                }
            }
            """);

        uint[] row = [0, 0];
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(new uint[] { 20, 50 }, row);
    }

    [Fact]
    public void RejectsAnInclusiveNestedBoundThatCanOverflowItsCounter()
    {
        var result = Analyze("""
            public void Execute(ref Position p, in Velocity v)
            {
                int i = int.MaxValue - 1;
                while (i < int.MaxValue)
                {
                    ++i;
                    for (int j = int.MaxValue - 1; j <= i; ++j) ++p.Point.X;
                }
            }
            """);

        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "GA0100" &&
            diagnostic.GetMessage().Contains("iteration range", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedBudgetUsesTheMaximumOfExclusiveBranches()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                for (int i = 0; i < 256; ++i)
                {
                    if (v.Flag)
                    {
                        for (int j = 0; j < 255; ++j) ++p.Point.X;
                    }
                    else
                    {
                        for (int j = 0; j < 255; ++j) --p.Point.X;
                    }
                }
            }
            """);

        uint[] row = [1, 0];
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(65280u, row[1]);
    }

    [Fact]
    public void RejectsExcessNestedWorkEvenWhenEachLoopFitsIndividually()
    {
        var result = Analyze("""
            public void Execute(ref Position p, in Velocity v)
            {
                for (int i = 0; i < 256; ++i)
                    for (int j = 0; j < 256; ++j)
                        ++p.Point.X;
            }
            """);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "GA0100" &&
            diagnostic.GetMessage().Contains("65536 total", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedFrozenBoundCanBeReinitializedOnEveryOuterIteration()
    {
        var program = Compile("""
            public void Execute(ref Position p, in Velocity v)
            {
                for (int i = 0; i < 4; ++i)
                {
                    int limit = i & 7;
                    for (int j = 0; j <= limit; ++j) ++p.Point.X;
                    limit = v.X;
                    p.Point.Y += limit;
                }
            }
            """);
        uint[] row = program.Fields.Select(field => field.Path == "X" ? 10u : 0u).ToArray();
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(10u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.X")]);
        Assert.Equal(40u, row[Array.FindIndex(program.Fields.ToArray(), field => field.Path == "Point.Y")]);

        var rejected = Analyze("""
            public void Execute(ref Position p, in Velocity v)
            {
                int limit = v.X & 7;
                for (int i = 0; i < 4; ++i)
                {
                    for (int j = 0; j < limit; ++j) ++p.Point.X;
                    limit = v.Y;
                }
            }
            """);
        Assert.Null(rejected.Program);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Id == "GA0100");
    }

    [Fact]
    public void NestedLoopEventCapacityMultipliesThePerIterationBound()
    {
        var program = Compile("""
            public void Execute(in SystemContext context, ref Position p, in Velocity v)
            {
                for (int i = 0; i < 2; ++i)
                    for (int j = 0; j < 3; ++j)
                        context.Emit(i * 10 + j);
            }
            """);

        Assert.Equal(6, program.EventCapacity);
        int[] events = new int[6];
        Assert.Null(GaInterpreter.Execute(program, [], events, out int count));
        Assert.Equal(6, count);
        Assert.Equal(new[] { 0, 1, 2, 10, 11, 12 }, events);
    }

    [Fact]
    public void IntEventsAreBoundedAndPublishOnlyForSuccessfulRows()
    {
        var program = Compile("""
            public void Execute(in SystemContext context, ref Position p, in Velocity v)
            {
                context.Emit(p.Point.X);
                if (v.Flag)
                    for (int i = 0; i < 3; ++i)
                        context.Emit(i);
                p.Point.X /= v.Y;
                context.Emit(p.Point.X);
            }
            """);
        Assert.Equal(5, program.EventCapacity);

        uint[] row = [12, 1, 3];
        int[] events = new int[program.EventCapacity];
        Assert.Null(GaInterpreter.Execute(program, row, events, out int count));
        Assert.Equal(5, count);
        Assert.Equal(new[] { 12, 0, 1, 2, 4 }, events);
        Assert.Equal(4u, row[0]);

        row = [12, 1, 0];
        Array.Fill(events, -1);
        Assert.Equal(GaFaultKind.DivideByZero, GaInterpreter.Execute(program, row, events, out count)!.Value.Kind);
        Assert.Equal(0, count);
        Assert.Equal(12u, row[0]);
        Assert.All(events, value => Assert.Equal(-1, value));
    }

    [Fact]
    public void EventCapacityOverflowProducesACompilerDiagnostic()
    {
        var result = Analyze("""
            public void Execute(in SystemContext context, ref Position p, in Velocity v)
            {
                for (int i = 0; i < 65536; ++i)
                {
                    context.Emit(i);
                    context.Emit(v.X);
                }
                p.Point.X = v.X;
            }
            """);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DestroySharesTheEventOrderAndStagesOnlySuccessfulInvocations()
    {
        var program = Compile("""
            public void Execute(in SystemContext context, ref Position p, in Velocity v)
            {
                p.Point.X = 42;
                context.Emit(-11);
                context.Destroy(entityId: 120 / p.Point.Y, generation: checked(v.X + 1));
                context.Emit(p.Point.X);
            }
            """);
        Assert.True(program.UsesCommands);
        Assert.Equal(3, program.EventCapacity);
        Assert.Equal(new[] { "Point.X", "Point.Y", "X" }, program.Fields.Select(field => field.Path));

        uint[] row = [7, 4, 9];
        var outputs = new List<GaOutputRecord> { GaOutputRecord.EmitInt32(1) };
        Assert.Throws<InvalidOperationException>(() => GaInterpreter.Execute(program, row));
        Assert.Throws<InvalidOperationException>(() => GaInterpreter.Execute(program, row, new int[3], out _));
        Assert.Null(GaInterpreter.Execute(program, row, outputs));
        Assert.Equal(42u, row[0]);
        Assert.Equal(new[]
        {
            GaOutputRecord.EmitInt32(1),
            GaOutputRecord.EmitInt32(-11),
            GaOutputRecord.DestroyEntity(30, 10),
            GaOutputRecord.EmitInt32(42)
        }, outputs);

        row = [7, 0, int.MaxValue];
        var fault = GaInterpreter.Execute(program, row, outputs);
        Assert.True(fault.HasValue);
        Assert.Equal(GaFaultKind.DivideByZero, fault.Value.Kind);
        Assert.Equal(0ul, fault.Value.EvaluationOrdinal);
        Assert.Equal(7u, row[0]);
        Assert.Equal(4, outputs.Count);

        row = [7, 4, int.MaxValue];
        fault = GaInterpreter.Execute(program, row, outputs);
        Assert.True(fault.HasValue);
        Assert.Equal(GaFaultKind.Overflow, fault.Value.Kind);
        Assert.Equal(1ul, fault.Value.EvaluationOrdinal);
        Assert.Equal(7u, row[0]);
        Assert.Equal(4, outputs.Count);
    }

    [Fact]
    public void TicketsUseTheCreatingOutputOrdinalAcrossBranchesAndIterations()
    {
        var program = Compile("""
            public void Execute(in SystemContext context, ref Position p, in Velocity v)
            {
                context.Emit(-1);
                context.Create();
                var outer = context.Create();
                if (v.X > 0)
                {
                    var branch = context.Create();
                    context.Destroy(outer);
                    context.Destroy(branch);
                }
                for (int i = 0; i < 2; ++i)
                {
                    var local = context.Create();
                    context.Emit(i);
                    context.Destroy(local);
                }
                p.Point.X = 100 / v.Y;
                context.Destroy(outer);
            }
            """);
        Assert.True(program.UsesCommands);
        Assert.Equal(3, program.TicketLocalCount);
        Assert.Equal(13, program.EventCapacity);
        Assert.Equal(new[] { "X", "Point.X", "Y" }, program.Fields.Select(field => field.Path));

        uint[] row = [1, 7, 5];
        var outputs = new List<GaOutputRecord> { GaOutputRecord.EmitInt32(9) };
        Assert.Null(GaInterpreter.Execute(program, row, outputs));
        Assert.Equal(new[] { 1u, 20u, 5u }, row);
        Assert.Equal(new[]
        {
            GaOutputRecord.EmitInt32(9),
            GaOutputRecord.EmitInt32(-1), GaOutputRecord.CreateEntity(), GaOutputRecord.CreateEntity(),
            GaOutputRecord.CreateEntity(), GaOutputRecord.DestroyTicket(2), GaOutputRecord.DestroyTicket(3),
            GaOutputRecord.CreateEntity(), GaOutputRecord.EmitInt32(0), GaOutputRecord.DestroyTicket(6),
            GaOutputRecord.CreateEntity(), GaOutputRecord.EmitInt32(1), GaOutputRecord.DestroyTicket(9),
            GaOutputRecord.DestroyTicket(2)
        }, outputs);

        outputs.Clear();
        row = [0, 7, 5];
        Assert.Null(GaInterpreter.Execute(program, row, outputs));
        Assert.Equal(new[]
        {
            GaOutputRecord.EmitInt32(-1), GaOutputRecord.CreateEntity(), GaOutputRecord.CreateEntity(),
            GaOutputRecord.CreateEntity(), GaOutputRecord.EmitInt32(0), GaOutputRecord.DestroyTicket(3),
            GaOutputRecord.CreateEntity(), GaOutputRecord.EmitInt32(1), GaOutputRecord.DestroyTicket(6),
            GaOutputRecord.DestroyTicket(2)
        }, outputs);

        row = [1, 7, 0];
        var fault = GaInterpreter.Execute(program, row, outputs);
        Assert.True(fault.HasValue);
        Assert.Equal(GaFaultKind.DivideByZero, fault.Value.Kind);
        Assert.Equal(new[] { 1u, 7u, 0u }, row);
        Assert.Equal(10, outputs.Count);
    }

    [Theory]
    [InlineData("context.Destroy(default(GaEntityTicket));")]
    [InlineData("var ticket = new GaEntityTicket(); context.Destroy(ticket);")]
    [InlineData("var first = context.Create(); var second = first; context.Destroy(second);")]
    [InlineData("var ticket = context.Create(); ticket = context.Create();")]
    [InlineData("context.Destroy(context.Create());")]
    [InlineData("var alias = context; var ticket = alias.Create(); context.Destroy(ticket);")]
    public void RejectsTicketsWithoutTheirOriginalCreateLocal(string statements)
    {
        var result = Analyze($$"""
            public void Execute(in SystemContext context, ref Position p)
            {
                {{statements}}
            }
            """);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "GA0006");
    }

    [Fact]
    public void RemovesKeepStableComponentAndSourceOrderWithoutChangingQueryMembership()
    {
        var program = Compile("""
            public void Execute(in SystemContext context, ref Position p, in Velocity v)
            {
                p.Point.X = 37;
                context.Emit(4);
                var ticket = context.Create();
                context.Remove<RemovalGroup<int>.Marker>(ticket: ticket);
                context.Remove<Velocity>(entityId: 60 / v.X, generation: checked(v.Y + 1));
                context.Remove<RemovalGroup<int>.Marker>(ticket);
            }
            """);
        Assert.True(program.UsesCommands);
        Assert.Equal(5, program.EventCapacity);
        Assert.Equal(new[] { "global::RemovalGroup<int>.Marker", "global::Velocity" }, program.RemovalComponents);
        Assert.Equal(new[] { "global::Position", "global::Velocity" }, program.Components.Select(component => component.Id));
        Assert.Equal(new[] { "Point.X", "X", "Y" }, program.Fields.Select(field => field.Path));

        uint[] row = [7, 5, 8];
        var outputs = new List<GaOutputRecord> { GaOutputRecord.EmitInt32(1) };
        Assert.Null(GaInterpreter.Execute(program, row, outputs));
        Assert.Equal(37u, row[0]);
        Assert.Equal(new[]
        {
            GaOutputRecord.EmitInt32(1), GaOutputRecord.EmitInt32(4), GaOutputRecord.CreateEntity(),
            GaOutputRecord.RemoveTicket(0, 1), GaOutputRecord.RemoveEntity(1, 12, 9), GaOutputRecord.RemoveTicket(0, 1)
        }, outputs);

        row = [7, 0, int.MaxValue];
        var fault = GaInterpreter.Execute(program, row, outputs);
        Assert.True(fault.HasValue);
        Assert.Equal(GaFaultKind.DivideByZero, fault.Value.Kind);
        Assert.Equal(0ul, fault.Value.EvaluationOrdinal);
        Assert.Equal(7u, row[0]);
        Assert.Equal(6, outputs.Count);

        row = [7, 5, int.MaxValue];
        fault = GaInterpreter.Execute(program, row, outputs);
        Assert.True(fault.HasValue);
        Assert.Equal(GaFaultKind.Overflow, fault.Value.Kind);
        Assert.Equal(1ul, fault.Value.EvaluationOrdinal);
        Assert.Equal(7u, row[0]);
        Assert.Equal(6, outputs.Count);
    }

    [Fact]
    public void RemovalComponentIdentityChangesSchemaAndIrDigests()
    {
        var left = Compile("""
            public void Execute(in SystemContext context, ref Position p)
            {
                context.Remove<RemovalGroup<int>.Marker>(3, 2);
            }
            """);
        var right = Compile("""
            public void Execute(in SystemContext context, ref Position p)
            {
                context.Remove<RemovalGroup<uint>.Marker>(3, 2);
            }
            """);
        Assert.Equal(left.Components, right.Components);
        Assert.Equal(left.Fields, right.Fields);
        string leftCodec = GaRowCodecEmitter.Emit(left);
        string rightCodec = GaRowCodecEmitter.Emit(right);
        Assert.NotEqual(DigestLine(leftCodec, "ComponentSchemaDigest"), DigestLine(rightCodec, "ComponentSchemaDigest"));
        Assert.NotEqual(DigestLine(leftCodec, "IrDigest"), DigestLine(rightCodec, "IrDigest"));
    }

    [Theory]
    [InlineData("context.Remove<Velocity>(default(GaEntityTicket));")]
    [InlineData("var ticket = context.Create(); var alias = ticket; context.Remove<Velocity>(alias);")]
    [InlineData("context.Remove<Velocity>(context.Create());")]
    [InlineData("context.Remove<Velocity>(generation: 2, entityId: 3);")]
    [InlineData("var alias = context; alias.Remove<Velocity>(3, 2);")]
    [InlineData("context.Remove<int>(3, 2);")]
    public void RejectsInvalidRemovalTargetsAndComponentTypes(string statements)
    {
        var result = Analyze($$"""
            public void Execute(in SystemContext context, ref Position p) { {{statements}} }
            """);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "GA0006");
    }

    [Fact]
    public void AddPackPreservesFieldOrderPaddingAndFaultAtomicity()
    {
        var program = Compile("""
            public void Execute(in SystemContext context, ref Position p, in Velocity v)
            {
                p.Point.X = 40;
                var ticket = context.Create();
                context.Add(11, 12, new AdditionPayload
                {
                    Marker = v.Flag,
                    Raw = Fixed.FromRaw(-4294967294L),
                    Data = new AdditionData { Number = checked(v.X + 1), Code = AdditionCode.Selected },
                    Count = 120u / (uint)v.Y
                });
                context.Add(ticket, default(AdditionPayload));
                context.Add<Velocity>(ticket, v);
            }
            """);
        Assert.Equal(4, program.EventCapacity);
        Assert.Equal(6, program.AdditionPayloadWords);
        Assert.Equal(new[] { "global::Position", "global::Velocity" }, program.Components.Select(component => component.Id));
        Assert.Equal(new[] { "global::AdditionPayload", "global::Velocity" }, program.AdditionComponents.Select(component => component.Id));
        Assert.Equal(new[] { "Count", "Data.Code", "Data.Number", "Marker", "Raw" },
            program.AdditionComponents[0].Fields.Select(field => field.Path));
        Assert.Equal("global::AdditionCode", program.AdditionComponents[0].Fields[1].ClrEnumType);
        Assert.Equal(new[] { "Point.X", "Flag", "X", "Y" }, program.Fields.Select(field => field.Path));

        uint[] row = [7, 1, 5, 6];
        uint[] records = Enumerable.Repeat(uint.MaxValue, 36).ToArray();
        Assert.Throws<InvalidOperationException>(() => GaInterpreter.Execute(program, row, new List<GaOutputRecord>()));
        Assert.Null(GaInterpreter.ExecutePackedOutputs(program, row, records, out int count));
        Assert.Equal(4, count);
        Assert.Equal(40u, row[0]);
        Assert.Equal(new uint[]
        {
            GaOutputRecord.CreateEntityKind, 0, 0, 0, 0, 0, 0, 0, 0,
            GaOutputRecord.AddEntityBase, 11, 12, 20, 8, 6, 1, 2, uint.MaxValue,
            GaOutputRecord.AddTicketBase, 0, 0, 0, 0, 0, 0, 0, 0,
            GaOutputRecord.AddTicketBase + 1, 0, 0, 1, 5, 6, 0, 0, 0
        }, records);

        Array.Fill(records, uint.MaxValue);
        row = [7, 1, int.MaxValue, 0];
        var fault = GaInterpreter.ExecutePackedOutputs(program, row, records, out count);
        Assert.True(fault.HasValue);
        Assert.Equal(GaFaultKind.Overflow, fault.Value.Kind);
        Assert.Equal(0ul, fault.Value.EvaluationOrdinal);
        Assert.Equal(0, count);
        Assert.Equal(7u, row[0]);
        Assert.All(records, word => Assert.Equal(uint.MaxValue, word));
    }

    [Fact]
    public void AddInitializerEvaluationOrderChangesIrButNotSchema()
    {
        var left = Compile("""
            public void Execute(in SystemContext context)
            {
                context.Add(3, 2, new AdditionPayload { Count = 1, Marker = true });
            }
            """);
        var right = Compile("""
            public void Execute(in SystemContext context)
            {
                context.Add(3, 2, new AdditionPayload { Marker = true, Count = 1 });
            }
            """);
        string leftCodec = GaRowCodecEmitter.Emit(left);
        string rightCodec = GaRowCodecEmitter.Emit(right);
        Assert.Equal(DigestLine(leftCodec, "ComponentSchemaDigest"), DigestLine(rightCodec, "ComponentSchemaDigest"));
        Assert.NotEqual(DigestLine(leftCodec, "IrDigest"), DigestLine(rightCodec, "IrDigest"));
    }

    [Fact]
    public void AddMarkerUsesAnOrderedRecordWithNoPayloadWords()
    {
        var program = Compile("""
            public void Execute(in SystemContext context)
            {
                context.Add(3, 2, new RemovalGroup<int>.Marker());
            }
            """);
        Assert.Equal(0, program.AdditionPayloadWords);
        Assert.Equal(1, program.EventCapacity);
        Assert.Empty(Assert.Single(program.AdditionComponents).Fields);
        uint[] records = new uint[3];
        Assert.Null(GaInterpreter.ExecutePackedOutputs(program, [], records, out int count));
        Assert.Equal(1, count);
        Assert.Equal(new uint[] { GaOutputRecord.AddEntityBase, 3, 2 }, records);
    }

    [Theory]
    [InlineData("context.Add(3, 2, new AdditionCtor());")]
    [InlineData("context.Add(3, 2, new AdditionProperty { Count = 1 });")]
    [InlineData("context.Add(3, 2, default(AdditionReadonly));")]
    [InlineData("context.Add<AdditionPayload>(default(GaEntityTicket), default);")]
    [InlineData("context.Add<AdditionPayload>(generation: 2, entityId: 3, value: default);")]
    [InlineData("var alias = context; alias.Add<AdditionPayload>(3, 2, default);")]
    public void RejectsUnportableAddCalls(string statements)
    {
        var result = Analyze($$"""
            public void Execute(in SystemContext context) { {{statements}} }
            """);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "GA0006");
    }

    [Fact]
    public void DomainOrderPersistsAcrossOutputsAndResetsPerInvocation()
    {
        var program = Compile("""
            public void Execute(in SystemContext context, in Velocity v)
            {
                context.Emit(v.X);
                context.SetDomainOrder(primary: 0, secondary: 0);
                context.Emit(20);
                if (v.Y != 0)
                    context.SetDomainOrder(-2, 4);
                context.Emit(30);
            }
            """);
        Assert.True(GaProgram.SubsetVersion >= 8);
        Assert.True(GaProgram.IrVersion >= 8);
        Assert.True(program.UsesDomainOrder);
        Assert.False(program.UsesCommands);
        Assert.Equal(3, program.EventCapacity);
        uint[] row = [11, 1];
        uint[] records = Enumerable.Repeat(uint.MaxValue, 18).ToArray();
        Assert.Throws<InvalidOperationException>(() => GaInterpreter.Execute(program, row, new int[3], out _));
        Assert.Throws<InvalidOperationException>(() => GaInterpreter.Execute(program, row, new List<GaOutputRecord>()));
        Assert.Null(GaInterpreter.ExecutePackedOutputs(program, row, records, out int count));
        Assert.Equal(3, count);
        Assert.Equal(new uint[]
        {
            GaOutputRecord.EmitInt32Kind, 11, 0, 0, 0, 0,
            GaOutputRecord.EmitInt32Kind, 20, 0, 1, 0, 0,
            GaOutputRecord.EmitInt32Kind, 30, 0, 1, unchecked((uint)-2), 4
        }, records);

        row = [12, 0];
        Assert.Null(GaInterpreter.ExecutePackedOutputs(program, row, records, out count));
        Assert.Equal(3, count);
        Assert.Equal(new uint[]
        {
            GaOutputRecord.EmitInt32Kind, 12, 0, 0, 0, 0,
            GaOutputRecord.EmitInt32Kind, 20, 0, 1, 0, 0,
            GaOutputRecord.EmitInt32Kind, 30, 0, 1, 0, 0
        }, records);
    }

    [Fact]
    public void DomainOrderFollowsTypedPayloadInPackedRecords()
    {
        var program = Compile("""
            public void Execute(in SystemContext context)
            {
                context.SetDomainOrder(-3, 4);
                var ticket = context.Create();
                context.Add(ticket, new AdditionPayload { Count = 2, Data = new AdditionData { Number = 7 } });
                context.SetDomainOrder(-2, -5);
                context.Destroy(ticket);
            }
            """);
        Assert.True(program.UsesDomainOrder);
        Assert.True(program.UsesCommands);
        Assert.Equal(3, program.EventCapacity);
        Assert.Equal(6, program.AdditionPayloadWords);
        uint[] records = new uint[36];
        Assert.Null(GaInterpreter.ExecutePackedOutputs(program, [], records, out int count));
        Assert.Equal(3, count);
        Assert.Equal(new uint[]
        {
            GaOutputRecord.CreateEntityKind, 0, 0, 0, 0, 0, 0, 0, 0, 1, unchecked((uint)-3), 4,
            GaOutputRecord.AddTicketBase, 0, 0, 2, 0, 7, 0, 0, 0, 1, unchecked((uint)-3), 4,
            GaOutputRecord.DestroyTicketKind, 0, 0, 0, 0, 0, 0, 0, 0, 1, unchecked((uint)-2), unchecked((uint)-5)
        }, records);
    }

    [Fact]
    public void DomainOrderArgumentsPreserveFirstFaultAndDoNotPublish()
    {
        var program = Compile("""
            public void Execute(in SystemContext context, ref Position p, in Velocity v)
            {
                context.SetDomainOrder(-1, 2);
                p.Point.X = 5;
                context.Emit(17);
                context.SetDomainOrder(checked(v.X + 1), 120 / v.Y);
                context.Emit(18);
            }
            """);
        Assert.Equal(2, program.EventCapacity);
        uint[] records = Enumerable.Repeat(uint.MaxValue, 12).ToArray();
        uint[] row = [9, int.MaxValue, 0];
        var first = GaInterpreter.ExecutePackedOutputs(program, row, records, out int count);
        Assert.True(first.HasValue);
        Assert.Equal(GaFaultKind.Overflow, first.Value.Kind);
        Assert.Equal(0ul, first.Value.EvaluationOrdinal);
        Assert.Equal(0, count);
        Assert.Equal(9u, row[0]);
        Assert.All(records, word => Assert.Equal(uint.MaxValue, word));

        row = [9, 2, 0];
        var second = GaInterpreter.ExecutePackedOutputs(program, row, records, out count);
        Assert.True(second.HasValue);
        Assert.Equal(GaFaultKind.DivideByZero, second.Value.Kind);
        Assert.Equal(1ul, second.Value.EvaluationOrdinal);
        Assert.Equal(0, count);
        Assert.Equal(9u, row[0]);
        Assert.All(records, word => Assert.Equal(uint.MaxValue, word));
    }

    [Fact]
    public void DomainOrderChangesArtifactSchemaAndArgumentsChangeIr()
    {
        var unkeyed = Compile("public void Execute(in SystemContext context) { context.Emit(2); }");
        var keyed = Compile("public void Execute(in SystemContext context) { context.SetDomainOrder(0, 1); context.Emit(2); }");
        var different = Compile("public void Execute(in SystemContext context) { context.SetDomainOrder(1, 0); context.Emit(2); }");
        string unkeyedCodec = GaRowCodecEmitter.Emit(unkeyed);
        string keyedCodec = GaRowCodecEmitter.Emit(keyed);
        string differentCodec = GaRowCodecEmitter.Emit(different);
        Assert.False(unkeyed.UsesDomainOrder);
        Assert.NotEqual(DigestLine(unkeyedCodec, "ComponentSchemaDigest"), DigestLine(keyedCodec, "ComponentSchemaDigest"));
        Assert.Equal(DigestLine(keyedCodec, "ComponentSchemaDigest"), DigestLine(differentCodec, "ComponentSchemaDigest"));
        Assert.NotEqual(DigestLine(keyedCodec, "IrDigest"), DigestLine(differentCodec, "IrDigest"));
    }

    [Theory]
    [InlineData("context.SetDomainOrder(secondary: 2, primary: 1);")]
    [InlineData("var alias = context; alias.SetDomainOrder(1, 2);")]
    public void RejectsDomainOrderOnAliasesAndReorderedNamedArguments(string statements)
    {
        var result = Analyze($$"""
            public void Execute(in SystemContext context) { {{statements}} }
            """);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "GA0006");
    }

    private static string DigestLine(string codec, string member) => codec.Split('\n')
        .Single(line => line.TrimStart().StartsWith($"public const string {member} = ", StringComparison.Ordinal));

    private static GaProgram Compile(string body)
    {
        var result = Analyze(body);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        return result.Program!;
    }

    private static GaCompilationResult Analyze(string body, string? helper = null)
    {
        var syntax = CSharpSyntaxTree.ParseText("""
            using SKECS.GA;
            using SKECS.Numerics;
            public struct Vector { public int X; public int Y; }
            public struct Position { public Vector Point; }
            public struct Velocity { public int X; public int Y; public bool Flag; }
            public struct RemovalGroup<T> where T : unmanaged { public struct Marker { } }
            public enum AdditionCode : uint { Selected = 8 }
            public struct AdditionData { public int Number; public AdditionCode Code; }
            public struct AdditionPayload { public uint Count; public AdditionData Data; public bool Marker; public Fixed Raw; }
            public struct AdditionCtor { public int Count; public AdditionCtor() { Count = 1; } }
            public struct AdditionProperty { public int Count { get => 0; set { } } }
            public struct AdditionReadonly { public readonly int Count; }
            [SecsSystem(SystemId = "fixtures.movement")]
            public partial struct Movement
            {
            """ + "\n" + body + "\n}", path: "Movement.cs");
        var trees = helper == null ? new[] { syntax } : new[] { syntax, CSharpSyntaxTree.ParseText(helper, path: "Helper.cs") };
        var compilation = CSharpCompilation.Create("Fixtures", trees,
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(AssemblyPath("System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(SecsSystemAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(SKECS.Numerics.Fixed).Assembly.Location)
            }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var entry = compilation.GetTypeByMetadataName("Movement")!.GetMembers("Execute").OfType<IMethodSymbol>().Single();
        return GaCompiler.Compile(compilation, entry);
    }

    private static string AssemblyPath(string name) => Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, name);
}
