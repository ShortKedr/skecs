using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SKECS.GA.Tests;

public sealed class CompilerTests
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
            private static int Add(int a, int b) => a + b;
            """);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "GA0100");
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
        Assert.Equal(5, GaProgram.SubsetVersion);
        Assert.Equal(5, GaProgram.IrVersion);
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

    private static GaProgram Compile(string body)
    {
        var result = Analyze(body);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        return result.Program!;
    }

    private static GaCompilationResult Analyze(string body)
    {
        var syntax = CSharpSyntaxTree.ParseText("""
            using SKECS.GA;
            public struct Vector { public int X; public int Y; }
            public struct Position { public Vector Point; }
            public struct Velocity { public int X; public int Y; public bool Flag; }
            [SecsSystem(SystemId = "fixtures.movement")]
            public partial struct Movement
            {
            """ + "\n" + body + "\n}", path: "Movement.cs");
        var compilation = CSharpCompilation.Create("Fixtures", [syntax],
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(AssemblyPath("System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(SecsSystemAttribute).Assembly.Location)
            }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var entry = compilation.GetTypeByMetadataName("Movement")!.GetMembers("Execute").OfType<IMethodSymbol>().Single();
        return GaCompiler.Compile(compilation, entry);
    }

    private static string AssemblyPath(string name) => Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, name);
}
