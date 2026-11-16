using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SKECS.Numerics;

namespace SKECS.GA.Tests;

public sealed class CpuRunnerTests
{
    private const int Rows = 155;
    private const int Ticks = 4;

    private const string Fixture = """
        using SKECS;
        using SKECS.GA;
        using SKECS.Numerics;
        using System;

        namespace AdapterFixture;

        public struct Point { public int X; public int Y; }
        public enum Mode : uint { Idle }
        public struct Position { public Point @event; public Mode Mode; }
        public struct Velocity { public int X; public int Divisor; public bool Flag; }
        public struct RequiredTag { }
        public struct AlternateArchetype { }

        [SecsSystem(SystemId = "fixtures.movement.cpu")]
        public partial struct Movement
        {
            BODY
        }

        public static class RunnerFixture
        {
            private static World Create(int capacity, int count, bool faults, out Entity[] entities)
            {
                var world = new World(new WorldOptions(chunkCapacity: capacity));
                entities = new Entity[count];
                for (int i = 0; i < entities.Length; i++)
                {
                    var position = new Position { @event = new Point { X = i - 70, Y = i * 3 }, Mode = (Mode)(100u + (uint)i) };
                    var velocity = new Velocity { X = i % 9 - 4, Divisor = i % 5 + 4, Flag = i % 2 == 0 };
                    if (faults && i == 66) { velocity.Divisor = 2; velocity.Flag = false; }
                    if (faults && i == 120) velocity.X = int.MaxValue;
                    var entity = world.CreateEntity(position, velocity);
                    entities[i] = entity;
                    // The required, unused component is part of the query membership.
                    if (i != 0 && (faults || i % 7 != 0)) world.Add(entity, new RequiredTag());
                    if ((i & 1) == 0) world.Add(entity, new AlternateArchetype());
                }
                return world;
            }

            public static int[] RunMovement(int capacity, int budget, int count, int ticks)
            {
                using var world = Create(capacity, count, false, out var entities);
                var workload = new DeterministicWorkload<byte>();
                GaCpuAdapter.Register(workload, SystemStage.Update);
                using var runner = new DeterministicRunner<byte>(world, workload);
                var results = new int[1 + count * 3 * ticks];
                var next = 1;
                for (int tick = 0; tick < ticks; tick++)
                {
                    if (tick != 0) world.Get<Position>(entities[3]).@event.X += 7;
                    runner.BeginTick(tick, (byte)0, Fixed.One);
                    var status = runner.Advance(budget);
                    results[0]++;
                    while (status == DeterministicTickStatus.Pending)
                    {
                        AssertWorldOwned(world, entities[3]);
                        if (runner.LastCompleted?.TickIndex != (tick == 0 ? (long?)null : tick - 1))
                            throw new InvalidOperationException("A pending tick changed the last completion.");
                        status = runner.Advance(budget);
                        results[0]++;
                    }
                    if (status != DeterministicTickStatus.Completed || runner.LastCompleted?.TickIndex != tick)
                        throw new InvalidOperationException("Movement did not complete at the expected barrier.", runner.Fault);
                    foreach (var entity in entities)
                    {
                        var position = world.Get<Position>(entity);
                        results[next++] = position.@event.X;
                        results[next++] = position.@event.Y;
                        results[next++] = unchecked((int)position.Mode);
                    }
                }
                return results;
            }

            public static GaExecutionException RunFault(int capacity, int budget, int count)
            {
                using var world = Create(capacity, count, true, out var entities);
                var workload = new DeterministicWorkload<byte>();
                GaCpuAdapter.Register(workload, SystemStage.Update);
                using var runner = new DeterministicRunner<byte>(world, workload);
                runner.BeginTick(42, (byte)0, Fixed.One);
                DeterministicTickStatus status;
                do { status = runner.Advance(budget); } while (status == DeterministicTickStatus.Pending);
                if (status != DeterministicTickStatus.Faulted || runner.LastCompleted != null)
                    throw new InvalidOperationException("A failed entity kernel published a completed tick.");
                AssertWorldOwned(world, entities[66]);
                return (GaExecutionException)runner.Fault!;
            }

            public static GaExecutionException RunArithmeticFault(int left, int right)
            {
                using var world = new World();
                var entity = world.CreateEntity(default(Position), new Velocity { X = left, Divisor = right });
                world.Add(entity, new RequiredTag());
                var workload = new DeterministicWorkload<byte>();
                GaCpuAdapter.Register(workload, SystemStage.Update);
                using var runner = new DeterministicRunner<byte>(world, workload);
                runner.BeginTick(1, (byte)0, Fixed.One);
                if (runner.Advance(1) != DeterministicTickStatus.Faulted)
                    throw new InvalidOperationException("Expected an arithmetic fault.");
                return (GaExecutionException)runner.Fault!;
            }

            private static void AssertWorldOwned(World world, Entity entity)
            {
                try { world.Set(entity, default(Position)); }
                catch (InvalidOperationException) { return; }
                throw new InvalidOperationException("An external write passed through runner ownership.");
            }
        }
        """;

    private const string Movement = """
        public void Execute(ref Position position, in Velocity velocity, in RequiredTag tag)
        {
            var dx = checked(velocity.X + 1);
            for (int i = 0; i < 4; i++)
            {
                position.@event.X += dx;
                position.@event.Y += velocity.Flag && position.@event.X < 0
                    ? position.@event.X % velocity.Divisor
                    : position.@event.X >>> velocity.Divisor;
            }
            position.Mode = (Mode)unchecked((uint)(position.@event.Y + (int)position.Mode));
        }
        """;

    private const string FaultingMovement = """
        public void Execute(ref Position position, in Velocity velocity, in RequiredTag tag)
        {
            position.@event.X = 99;
            if (velocity.Flag && 1 / velocity.Divisor == -999)
                position.@event.X = 100;
            position.@event.Y = checked(velocity.X + 1);
            for (int i = 0; i < 4; i++)
                position.@event.X = 100 / (velocity.Divisor - i);
        }
        """;

    private const string FixedFixture = """
        using SKECS;
        using SKECS.GA;
        using SKECS.Numerics;
        using System;

        namespace AdapterFixture;

        public struct Position { public Fixed X; public Fixed Y; public int Whole; public bool Flag; }
        public struct Velocity { public Fixed X; public Fixed Divisor; public int Offset; }

        [SecsSystem(SystemId = "fixtures.movement.fixed.cpu")]
        public partial struct Movement
        {
            BODY
        }

        public static class RunnerFixture
        {
            public static long[] RunFixed(int count, int ticks)
            {
                using var world = new World(new WorldOptions(chunkCapacity: 9));
                var entities = new Entity[count];
                for (int i = 0; i < count; i++)
                {
                    var position = new Position { X = Fixed.FromRaw(i % 2 == 0 ? 3 : -3), Y = Fixed.FromInt32(i % 7 - 3) };
                    var velocity = new Velocity { X = Fixed.FromRaw(1L << 31), Divisor = Fixed.FromInt32(3), Offset = i < 2 ? 0 : i % 3 - 1 };
                    entities[i] = world.CreateEntity(position, velocity);
                }
                var workload = new DeterministicWorkload<byte>();
                GaCpuAdapter.Register(workload, SystemStage.Update);
                using var runner = new DeterministicRunner<byte>(world, workload);
                var result = new long[count * ticks * 4];
                int next = 0;
                for (int tick = 0; tick < ticks; tick++)
                {
                    runner.BeginTick(tick, (byte)0, Fixed.One);
                    while (runner.Advance(1) == DeterministicTickStatus.Pending) { }
                    if (runner.Status != DeterministicTickStatus.Completed)
                        throw new InvalidOperationException("The Fixed kernel did not complete.", runner.Fault);
                    foreach (var entity in entities)
                    {
                        var position = world.Get<Position>(entity);
                        result[next++] = position.X.Raw;
                        result[next++] = position.Y.Raw;
                        result[next++] = position.Whole;
                        result[next++] = position.Flag ? 1 : 0;
                    }
                }
                return result;
            }

            public static GaExecutionException RunFixedFault(bool overflow)
            {
                using var world = new World();
                world.CreateEntity(new Position { X = overflow ? Fixed.MaxValue : Fixed.Zero }, new Velocity());
                var workload = new DeterministicWorkload<byte>();
                GaCpuAdapter.Register(workload, SystemStage.Update);
                using var runner = new DeterministicRunner<byte>(world, workload);
                runner.BeginTick(12, (byte)0, Fixed.One);
                if (runner.Advance(1) != DeterministicTickStatus.Faulted)
                    throw new InvalidOperationException("Expected a Fixed arithmetic fault.");
                return (GaExecutionException)runner.Fault!;
            }
        }
        """;

    [Theory]
    [InlineData(1, 1)]
    [InlineData(17, 2)]
    [InlineData(256, 999)]
    public void GeneratedMovementMatchesEachTickAndRequiresUnusedComponent(int chunkCapacity, int budget)
    {
        var (program, fixture, _) = Compile(Movement);
        Assert.Contains(program.Components, component => component.Id.EndsWith(".RequiredTag", StringComparison.Ordinal));
        Assert.DoesNotContain(program.Fields, field => field.Component.EndsWith(".RequiredTag", StringComparison.Ordinal));
        var result = ((Func<int, int, int, int, int[]>)fixture.GetMethod("RunMovement")!
            .CreateDelegate(typeof(Func<int, int, int, int, int[]>)))(chunkCapacity, budget, Rows, Ticks);

        int jobs = ((Rows - (Rows + 6) / 7) + DeterministicRunner<byte>.LogicalJobSize - 1) /
            DeterministicRunner<byte>.LogicalJobSize;
        Assert.Equal(Ticks * ((jobs + budget - 1) / budget), result[0]);
        Assert.Equal(OracleMovement(), result.Skip(1));
    }

    [Theory]
    [InlineData(7, 1)]
    [InlineData(256, 9)]
    public void GeneratedFaultMatchesOracleAndUsesCanonicalEntityJobOrdinal(int chunkCapacity, int budget)
    {
        var (program, fixture, source) = Compile(FaultingMovement);
        var result = ((Func<int, int, int, GaExecutionException>)fixture.GetMethod("RunFault")!
            .CreateDelegate(typeof(Func<int, int, int, GaExecutionException>)))(chunkCapacity, budget, Rows);

        Assert.Equal("fixtures.movement.cpu", result.SystemId);
        Assert.Equal(42L, result.TickIndex);
        Assert.Equal(65L, result.SourceOrderOrdinal);
        Assert.Equal(66, result.SourceId);
        Assert.Equal(1, result.SourceGeneration);
        Assert.Equal(GaFaultKind.DivideByZero, result.KernelFault.Kind);
        Assert.Equal(3ul, result.KernelFault.EvaluationOrdinal);
        var span = result.KernelFault.Source;
        Assert.Equal("100 / (velocity.Divisor - i)", source.Substring(span.Start, span.Length));

        uint[] words = program.Fields.Select(field => field.Path switch
        {
            "Divisor" => 2u,
            "X" => unchecked((uint)(66 % 9 - 4)),
            _ => 0u
        }).ToArray();
        Assert.Equal(GaInterpreter.Execute(program, words), result.KernelFault);
    }

    [Theory]
    [InlineData("unchecked(velocity.X % velocity.Divisor)", int.MinValue, -1, 0ul)]
    [InlineData("checked(-velocity.X)", int.MinValue, 2, 0ul)]
    [InlineData("checked((int)unchecked((uint)velocity.X))", -1, 0, 0ul)]
    [InlineData("(int)checked((uint)velocity.X * (uint)velocity.Divisor)", 65536, 65536, 2ul)]
    [InlineData("(int)checked((uint)velocity.X - (uint)velocity.Divisor)", 2, 3, 2ul)]
    public void GeneratedArithmeticUsesCheckedAndSignedMinRules(string operation, int left, int right, ulong ordinal)
    {
        var (program, fixture, _) = Compile($$"""
            public void Execute(ref Position position, in Velocity velocity, in RequiredTag tag)
            {
                position.@event.X = {{operation}};
            }
            """);
        var result = ((Func<int, int, GaExecutionException>)fixture.GetMethod("RunArithmeticFault")!
            .CreateDelegate(typeof(Func<int, int, GaExecutionException>)))(left, right);

        Assert.Equal(GaFaultKind.Overflow, result.KernelFault.Kind);
        Assert.Equal(ordinal, result.KernelFault.EvaluationOrdinal);
        uint[] words = program.Fields.Select(field => field.Path switch
        {
            "X" => unchecked((uint)left),
            "Divisor" => unchecked((uint)right),
            _ => 0u
        }).ToArray();
        Assert.Equal(GaInterpreter.Execute(program, words), result.KernelFault);
    }

    [Fact]
    public void GeneratedFixedMovementMatchesIndependentCSharpUsingTheSharedNumericContract()
    {
        var (program, fixture, _) = Compile("""
            public void Execute(ref Position position, in Velocity velocity)
            {
                var offset = Fixed.FromInt32(velocity.Offset);
                position.X = (position.X + offset) * velocity.X;
                position.Y = (position.Y - position.X) / velocity.Divisor;
                position.Whole = (int)(-position.Y);
                position.Flag = position.X >= position.Y;
            }
            """, FixedFixture);
        Assert.True(program.UsesFixed);
        const int count = 70;
        var actual = ((Func<int, int, long[]>)fixture.GetMethod("RunFixed")!
            .CreateDelegate(typeof(Func<int, int, long[]>)))(count, Ticks);

        var x = Enumerable.Range(0, count).Select(i => Fixed.FromRaw(i % 2 == 0 ? 3 : -3)).ToArray();
        var y = Enumerable.Range(0, count).Select(i => Fixed.FromInt32(i % 7 - 3)).ToArray();
        var expected = new List<long>();
        for (int tick = 0; tick < Ticks; tick++)
        {
            for (int i = 0; i < count; i++)
            {
                x[i] = (x[i] + Fixed.FromInt32(i < 2 ? 0 : i % 3 - 1)) * Fixed.FromRatio(1, 2);
                y[i] = (y[i] - x[i]) / Fixed.FromInt32(3);
                expected.AddRange([x[i].Raw, y[i].Raw, (int)(-y[i]), x[i] >= y[i] ? 1L : 0L]);
            }
        }
        Assert.Equal(2L, actual[0]);
        Assert.Equal(-2L, actual[4]);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false, GaFaultKind.DivideByZero, 1ul)]
    [InlineData(true, GaFaultKind.Overflow, 0ul)]
    public void GeneratedFixedFaultMatchesTheOracleAcrossCheckedContexts(bool overflow, GaFaultKind kind, ulong ordinal)
    {
        var (program, fixture, _) = Compile("""
            public void Execute(ref Position position, in Velocity velocity)
            {
                position.X = checked(position.X + Fixed.One);
                position.Y = unchecked(position.X / velocity.Divisor);
            }
            """, FixedFixture);
        var fault = ((Func<bool, GaExecutionException>)fixture.GetMethod("RunFixedFault")!
            .CreateDelegate(typeof(Func<bool, GaExecutionException>)))(overflow);
        Assert.Equal(12L, fault.TickIndex);
        Assert.Equal(kind, fault.KernelFault.Kind);
        Assert.Equal(ordinal, fault.KernelFault.EvaluationOrdinal);

        uint[] words = new uint[program.WordCount];
        if (overflow)
        {
            Assert.Equal("X", program.Fields[0].Path);
            words[0] = 0xffffffffu;
            words[1] = 0x7fffffffu;
        }
        Assert.Equal(GaInterpreter.Execute(program, words), fault.KernelFault);
    }

    private static int[] OracleMovement()
    {
        var x = Enumerable.Range(0, Rows).Select(index => index - 70).ToArray();
        var y = Enumerable.Range(0, Rows).Select(index => index * 3).ToArray();
        var mode = Enumerable.Range(100, Rows).ToArray();
        var output = new List<int>();
        for (int tick = 0; tick < Ticks; tick++)
        {
            if (tick != 0) x[3] += 7;
            for (int index = 0; index < Rows; index++)
            {
                if (index % 7 != 0)
                {
                    int divisor = index % 5 + 4;
                    for (int iteration = 0; iteration < 4; iteration++)
                    {
                        x[index] += index % 9 - 3;
                        y[index] += index % 2 == 0 && x[index] < 0
                            ? x[index] % divisor
                            : (int)((uint)x[index] >> divisor);
                    }
                    mode[index] += y[index];
                }
                output.AddRange([x[index], y[index], mode[index]]);
            }
        }
        return output.ToArray();
    }

    private static (GaProgram Program, Type Fixture, string Source) Compile(string body, string fixture = Fixture)
    {
        string source = fixture.Replace("BODY", body, StringComparison.Ordinal);
        string libraries = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            typeof(object).Assembly.Location,
            Path.Combine(libraries, "System.Runtime.dll"),
            typeof(World).Assembly.Location,
            typeof(SecsSystemAttribute).Assembly.Location
        }.Select(path => MetadataReference.CreateFromFile(path));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: true);
        var compilation = CSharpCompilation.Create("CpuRunnerFixture" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, path: "Movement.cs")], references, options);
        // The generated source is also compiled in a project with overflow checking enabled.
        string kernelOnly = source[..source.IndexOf("public static class RunnerFixture", StringComparison.Ordinal)];
        var kernel = compilation.WithOptions(options.WithOverflowChecks(false))
            .RemoveAllSyntaxTrees().AddSyntaxTrees(CSharpSyntaxTree.ParseText(kernelOnly, path: "Movement.cs"));
        var entry = kernel.GetTypeByMetadataName("AdapterFixture.Movement")!.GetMembers("Execute").OfType<IMethodSymbol>().Single();
        var result = GaCompiler.Compile(kernel, entry);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));

        string generated = GaCpuAdapterEmitter.Emit(result.Program!);
        Assert.Equal(generated, GaCpuAdapterEmitter.Emit(result.Program!));
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, path: "Movement.g.cs"));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        return (result.Program!, AssemblyLoadContext.Default.LoadFromStream(stream).GetType("AdapterFixture.RunnerFixture")!, source);
    }
}
