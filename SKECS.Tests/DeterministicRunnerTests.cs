using SKECS.Numerics;

namespace SKECS.Tests;

public sealed class DeterministicRunnerTests
{
    private readonly record struct Position(int Value);
    private readonly record struct Split;
    private readonly record struct Newborn;
    private readonly record struct Empty;
    private readonly record struct Trace(int Phase, int Job, int Value);
    private record struct TickInput(int Offset);

    [Fact]
    public void FrameBudgetsAndPhysicalChunksPreserveStateIdentityAndExactTrace()
    {
        var reference = Run(capacity: 256, int.MaxValue);
        foreach (var capacity in new[] { 1, 63, 256 })
        foreach (var budget in new[] { 1, 3, int.MaxValue })
        {
            var actual = Run(capacity, budget);
            Assert.Equal(reference.State, actual.State);
            Assert.Equal(reference.Events, actual.Events);
            Assert.Equal(reference.Reductions, actual.Reductions);
            Assert.Equal(2 * ((10L + budget - 1) / budget), actual.Frames);
        }

        Assert.Equal(Enumerable.Range(0, 130).Select(id => id == 0
            ? (0, 3, 111, true)
            : (id, 1, id + 19, false)), reference.State);
        Assert.All(reference.Reductions, value => Assert.Equal(ExpectedReduction(), value));
        Assert.Equal(ExpectedTrace(), reference.Events);
    }

    private static ((int Id, int Version, int Value, bool IsNew)[] State,
        (long Tick, SystemStage Stage, int Order, DeterministicSourceKind Kind, int Id, int Version, int Ordinal, Trace Payload)[] Events,
        ulong[] Reductions, int Frames) Run(int capacity, int budget)
    {
        using var world = new World(new WorldOptions(chunkCapacity: capacity));
        for (var i = 0; i < 130; i++)
        {
            var entity = world.CreateEntity(new Position(i));
            if (i % 2 == 0) world.Add(entity, new Split());
        }

        var workload = new DeterministicWorkload<TickInput>();
        var reduction = workload.AddReduction(0UL, static (accumulator, leaf) => unchecked(accumulator * 131 + leaf));
        var movementQuery = new DeterministicQuery().With<Position>().ReadWrite<Position>();
        workload
            .AddSystem(SystemStage.PostUpdate, new DeterministicQuery().With<Position>().Read<Position>(), new Observe(3))
            .AddSystem(SystemStage.Update, movementQuery, new Move())
            .AddSystem(SystemStage.PreUpdate, new DeterministicQuery().With<Position>().Write<Position>().Write<Newborn>(),
                new Replace(reduction))
            .AddSystem(SystemStage.PreUpdate, new DeterministicQuery().With<Empty>(), new MustNotRun())
            .AddSystem(SystemStage.Update, new DeterministicQuery().With<Position>().With<Newborn>().Read<Position>(), new Observe(2))
            .AddSystem(SystemStage.PostUpdate, new DeterministicQuery().With<Empty>(), new MustNotRun());
        // Registration snapshots the query builder.
        movementQuery.With<Empty>();
        using var runner = new DeterministicRunner<TickInput>(world, workload);
        var events = new List<(long, SystemStage, int, DeterministicSourceKind, int, int, int, Trace)>();
        var reductions = new List<ulong>();
        var frames = 0;
        for (var tick = 10; tick <= 11; tick++)
        {
            var input = new TickInput(7 + tick - 10);
            runner.BeginTick(tick, input, Fixed.FromInt32(2));
            input.Offset = 1000;
            DeterministicTickStatus status;
            do
            {
                status = runner.Advance(budget);
                frames++;
                if (status == DeterministicTickStatus.Pending)
                {
                    Assert.Equal(tick == 10 ? (long?)null : tick - 1, runner.LastCompleted?.TickIndex);
                    if (budget == 1) Thread.Yield();
                }
            } while (status == DeterministicTickStatus.Pending);

            Assert.True(status == DeterministicTickStatus.Completed, runner.Fault?.ToString());
            var completed = runner.LastCompleted!;
            Assert.Equal(tick, completed.TickIndex);
            reductions.Add(completed.GetReduction(reduction));
            foreach (var entry in completed.Events)
            {
                var key = entry.Key;
                events.Add((key.TickIndex, key.Stage, key.SystemOrder, key.SourceKind, key.SourceId,
                    key.SourceGeneration, key.LocalOrdinal, entry.Get<Trace>()));
            }
        }

        var state = new (int, int, int, bool)[130];
        for (var i = 0; i < state.Length; i++)
        {
            var entity = world.BindEntity(i, i == 0 ? 3 : 1);
            state[i] = (i, entity.Version, world.Get<Position>(entity).Value, world.Has<Newborn>(entity));
        }

        return (state, events.ToArray(), reductions.ToArray(), frames);
    }

    private static (long, SystemStage, int, DeterministicSourceKind, int, int, int, Trace)[] ExpectedTrace()
    {
        var trace = new List<(long, SystemStage, int, DeterministicSourceKind, int, int, int, Trace)>();
        for (var tick = 10; tick <= 11; tick++)
        {
            var generation = tick - 9;
            for (var id = 0; id < 130; id++)
                trace.Add((tick, SystemStage.PreUpdate, 2, DeterministicSourceKind.Entity, id, id == 0 ? generation : 1,
                    id == 0 ? 11 : 1, new Trace(0, id / 64, id)));
            for (var job = 0; job < 3; job++)
                trace.Add((tick, SystemStage.PreUpdate, 2, DeterministicSourceKind.Job, job, 0, 0, new Trace(0, job, -1)));
            for (var job = 0; job < 3; job++)
                trace.Add((tick, SystemStage.Update, 1, DeterministicSourceKind.Job, job, 0, 0,
                    new Trace(1, job, job == 2 ? 2 : 64)));
            var newborn = 100 + tick - 10 + (tick == 10 ? 9 : 10);
            trace.Add((tick, SystemStage.Update, 4, DeterministicSourceKind.Entity, 0, generation + 1, 0,
                new Trace(2, 0, newborn)));
            for (var id = 0; id < 130; id++)
                trace.Add((tick, SystemStage.PostUpdate, 0, DeterministicSourceKind.Entity, id, id == 0 ? generation + 1 : 1,
                    0, new Trace(3, id / 64, id == 0 ? newborn : id + (tick == 10 ? 9 : 19))));
        }

        return trace.ToArray();
    }

    private static ulong ExpectedReduction()
    {
        var value = 0UL;
        for (var id = 0; id < 130; id++) value = unchecked(value * 131 + (ulong)id);
        for (var job = 0; job < 3; job++) value = unchecked(value * 131 + (ulong)(200 + job));
        return value;
    }

    private struct Replace(DeterministicReduction<ulong> reduction) : IDeterministicJobProcessor<TickInput>
    {
        public void Execute(DeterministicJob<TickInput> job)
        {
            var commandSource = job.ForJob();
            commandSource.Emit(new Trace(0, job.JobId, -1));
            commandSource.Contribute(reduction, (ulong)(200 + job.JobId));
            // Reverse each job's emission order to exercise canonical merging independently of traversal.
            for (var index = job.Count - 1; index >= 0; index--)
            {
                var entity = job.GetEntity(index);
                var source = job.ForEntity(index);
                source.Contribute(reduction, (ulong)entity.Id);
                if (entity.Id == 0)
                {
                    source.Destroy(entity);
                    source.Destroy(entity);
                    var replacement = source.Create();
                    source.Set(entity, new Position(-1)); // The same ID is alive with a new generation by this point.
                    source.Remove<Position>(replacement);
                    source.Add(replacement, new Position(100 + (int)job.TickIndex - 10));
                    source.Add(replacement, new Position(-2));
                    source.Add(replacement, new Newborn());
                    var temporary = source.Create();
                    source.Destroy(temporary);
                }

                source.Emit(new Trace(0, job.JobId, entity.Id));
            }
        }
    }

    private struct Move : IDeterministicJobProcessor<TickInput>
    {
        private int _calls;

        public void Execute(DeterministicJob<TickInput> job)
        {
            Assert.Equal(1, ++_calls); // State is copied for every logical job.
            for (var i = 0; i < job.Count; i++)
                job.Write(i, new Position(job.Read<Position>(i).Value + job.Input.Offset + (int)job.Step));
            job.ForJob().Emit(new Trace(1, job.JobId, job.Count));
        }
    }

    private readonly struct Observe(int phase) : IDeterministicJobProcessor<TickInput>
    {
        public void Execute(DeterministicJob<TickInput> job)
        {
            for (var i = 0; i < job.Count; i++)
                job.ForEntity(i).Emit(new Trace(phase, job.JobId, job.Read<Position>(i).Value));
        }
    }

    private struct MustNotRun : IDeterministicJobProcessor<TickInput>
    {
        public void Execute(DeterministicJob<TickInput> job) => throw new InvalidOperationException("Empty query dispatched.");
    }

    [Fact]
    public void LeaseBlocksLegacyAndBorrowedManagedPathsThenReleasesAtCompletion()
    {
        using var world = new World();
        using var other = new World();
        var entity = world.CreateEntity(new Position(5));
        var accessor = world.GetAccessor<Position>();
        var factory = world.PrepareEntity<Position, Split>();
        var query = world.Query<Position>();
        var chunks = query.Chunks().GetEnumerator();
        Assert.True(chunks.MoveNext());
        var description = world.QueryBuilder().With<Position>().Build();
        var builder = world.QueryBuilder();
        var workload = new DeterministicWorkload<TickInput>()
            .AddJob(SystemStage.PreUpdate, new DeterministicQuery(), new NoOp())
            .AddJob(SystemStage.Update, new DeterministicQuery(), new NoOp());
        using var runner = new DeterministicRunner<TickInput>(world, workload);
        using var competitor = new DeterministicRunner<TickInput>(world, workload);
        runner.BeginTick(0, default, Fixed.Zero);
        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(1));

        Assert.Throws<InvalidOperationException>(() => world.Get<Position>(entity));
        Assert.Throws<InvalidOperationException>(() => world.TryGet<Position>(entity, out _));
        Assert.Throws<InvalidOperationException>(() => world.Set(entity, new Position(9)));
        Assert.Throws<InvalidOperationException>(() => world.Remove<Position>(entity));
        Assert.Throws<InvalidOperationException>(() => world.DestroyEntity(entity));
        Assert.Throws<InvalidOperationException>(() => world.CreateEntity());
        Assert.Throws<InvalidOperationException>(() => world.BindEntity(entity.Id, entity.Version));
        Assert.Throws<InvalidOperationException>(() => accessor.TryGet(entity, out _));
        Assert.Throws<InvalidOperationException>(() => accessor.Get(entity));
        Assert.Throws<InvalidOperationException>(() => factory.Create(new Position(99), new Split()));
        Assert.Throws<InvalidOperationException>(() => query.Chunks());
        Assert.Throws<InvalidOperationException>(() => chunks.MoveNext());
        Assert.Throws<InvalidOperationException>(() => chunks.Current);
        Assert.Throws<InvalidOperationException>(() => description.MatchCount);
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(() => world.RunAll());
        Assert.Throws<InvalidOperationException>(() => world.GetStorageStatistics());
        Assert.Throws<InvalidOperationException>(() => world.OpenCheckpoint());
        Assert.Throws<InvalidOperationException>(() => world.Dispose());
        Assert.Throws<InvalidOperationException>(() => competitor.BeginTick(0, default, Fixed.Zero));
        Assert.Throws<InvalidOperationException>(() => runner.BeginTick(1, default, Fixed.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => runner.Advance(0));
        Assert.True(other.IsAlive(other.CreateEntity()));

        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(1));
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => runner.BeginTick(0, default, Fixed.Zero));
        runner.Dispose();
        Assert.Equal(5, accessor.Get(entity).Value);
        Assert.Equal(5, world.Get<Position>(entity).Value);
    }

    [Fact]
    public void AbortAndDisposeKeepPartialWorldUnavailable()
    {
        using var world = new World();
        var entity = world.CreateEntity(new Position(5));
        var accessor = world.GetAccessor<Position>();
        var workload = new DeterministicWorkload<TickInput>()
            .AddSystem(SystemStage.PreUpdate, new DeterministicQuery().With<Position>().ReadWrite<Position>(), new Move())
            .AddJob(SystemStage.Update, new DeterministicQuery(), new NoOp());
        using var runner = new DeterministicRunner<TickInput>(world, workload);
        runner.BeginTick(0, new TickInput(9), Fixed.Zero);
        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(1));
        runner.Abort();
        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(1));
        Assert.IsType<OperationCanceledException>(runner.Fault);
        Assert.Null(runner.LastCompleted);
        Assert.Throws<InvalidOperationException>(() => runner.BeginTick(1, default, Fixed.Zero));
        Assert.Throws<InvalidOperationException>(() => world.Get<Position>(entity));
        Assert.Throws<InvalidOperationException>(() => world.Dispose());
        runner.DiscardWorld();
        Assert.Throws<ObjectDisposedException>(() => world.IsAlive(entity));
        Assert.False(accessor.Has(entity)); // Existing post-disposal accessor behavior.
        Assert.Throws<ObjectDisposedException>(() => runner.Advance(1));
    }

    [Fact]
    public void ABarrierFaultKeepsLastCompletedAndRejectsReentry()
    {
        using var world = new World();
        var workload = new DeterministicWorkload<TickInput>();
        DeterministicRunner<TickInput>? runnerRef = null;
        var reduction = workload.AddReduction(0, (previous, value) =>
        {
            Assert.Throws<InvalidOperationException>(() => world.CreateEntity());
            Assert.Throws<InvalidOperationException>(() => runnerRef!.Advance(1));
            Assert.Throws<InvalidOperationException>(() => runnerRef!.BeginTick(99, default, Fixed.Zero));
            Assert.Throws<InvalidOperationException>(() => runnerRef!.Abort());
            Assert.Throws<InvalidOperationException>(() => runnerRef!.Dispose());
            return checked(previous + value);
        });
        workload.AddJob(SystemStage.Update, new DeterministicQuery(), new Contribute(reduction));
        using var runner = new DeterministicRunner<TickInput>(world, workload);
        runnerRef = runner;
        runner.BeginTick(0, default, Fixed.Zero);
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(1));
        var completed = runner.LastCompleted!;
        Assert.Equal(1, completed.GetReduction(reduction));
        runner.BeginTick(1, new TickInput(int.MaxValue), Fixed.Zero);
        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(1));
        Assert.IsType<OverflowException>(runner.Fault);
        Assert.Same(completed, runner.LastCompleted);
        Assert.Throws<InvalidOperationException>(() => world.OpenCheckpoint());
        runner.Dispose();
        Assert.Throws<ObjectDisposedException>(() => world.CreateEntity());
    }

    [Fact]
    public void LegacyCallbackCannotStartAReservedTick()
    {
        using var world = new World();
        using var runner = new DeterministicRunner<TickInput>(world, new DeterministicWorkload<TickInput>());
        world.AddSystem(SystemStage.Update, _ => Assert.Throws<InvalidOperationException>(
            () => runner.BeginTick(0, default, Fixed.Zero)));
        world.RunAll();

        var entity = world.CreateEntity(new Position(5));
        var query = world.Query<Position>();
        var tryBegin = new TryBegin(runner);
        query.ForEach(ref tryBegin);
        Assert.Equal(DeterministicTickStatus.Idle, runner.Status);
        Assert.True(world.IsAlive(entity));
        runner.BeginTick(0, default, Fixed.Zero);
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(1));
    }

    [Fact]
    public void MigrationChangesFilterMembershipAndSupportsOptionalReads()
    {
        using var world = new World();
        var first = world.CreateEntity(3, 6L);
        var second = world.CreateEntity(7);
        var workload = new DeterministicWorkload<TickInput>()
            .AddSystem(SystemStage.Update, new DeterministicQuery().With<int>().Write<long>(), new Migrate())
            .AddSystem(SystemStage.Update, new DeterministicQuery().With<int>().Without<long>().Read<long>(), new ObserveMissing());
        using var runner = new DeterministicRunner<TickInput>(world, workload);
        runner.BeginTick(0, default, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(1));
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(1));
        Assert.False(world.Has<long>(first));
        Assert.Equal(14L, world.Get<long>(second));
        Assert.Equal(first.Id, Assert.Single(runner.LastCompleted!.Events).Get<int>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RangeAccessAndCrossSourceTicketsFaultTheTick(int operation)
    {
        using var world = new World();
        world.CreateEntity(1);
        world.CreateEntity(2);
        var workload = new DeterministicWorkload<TickInput>()
            .AddSystem(SystemStage.Update, new DeterministicQuery().With<int>().Read<int>().Write<long>(), new InvalidJob(operation));
        using var runner = new DeterministicRunner<TickInput>(world, workload);
        runner.BeginTick(0, default, Fixed.Zero);
        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(1));
        if (operation == 1)
            Assert.IsType<IndexOutOfRangeException>(runner.Fault);
        else
            Assert.IsType<InvalidOperationException>(runner.Fault);
        Assert.Null(runner.LastCompleted);
        Assert.Throws<InvalidOperationException>(() => world.CreateEntity());
    }

    private struct NoOp : IDeterministicJobProcessor<TickInput>
    {
        public void Execute(DeterministicJob<TickInput> job)
        {
            Assert.Equal(0, job.Count);
            Assert.Equal(0, job.JobId);
        }
    }

    private struct Migrate : IDeterministicJobProcessor<TickInput>
    {
        public void Execute(DeterministicJob<TickInput> job)
        {
            job.ForEntity(0).Remove<long>(job.GetEntity(0));
            job.ForEntity(1).Set(job.GetEntity(1), 14L);
        }
    }

    private struct ObserveMissing : IDeterministicJobProcessor<TickInput>
    {
        public void Execute(DeterministicJob<TickInput> job)
        {
            for (var i = 0; i < job.Count; i++)
            {
                Assert.False(job.TryRead<long>(i, out var missing));
                Assert.Equal(0L, missing);
                job.ForEntity(i).Emit(job.GetEntity(i).Id);
            }
        }
    }

    private readonly struct Contribute(DeterministicReduction<int> reduction) : IDeterministicJobProcessor<TickInput>
    {
        public void Execute(DeterministicJob<TickInput> job)
        {
            var source = job.ForJob();
            source.Emit(job.TickIndex);
            source.Contribute(reduction, job.Input.Offset);
            source.Contribute(reduction, 1);
        }
    }

    private readonly struct TryBegin(DeterministicRunner<TickInput> runner) : IQueryProcessor<Position>
    {
        public void Execute(Entity entity, ref Position position)
        {
            var activeRunner = runner;
            Assert.Throws<InvalidOperationException>(() => activeRunner.BeginTick(0, default, Fixed.Zero));
        }
    }

    private readonly struct InvalidJob(int operation) : IDeterministicJobProcessor<TickInput>
    {
        public void Execute(DeterministicJob<TickInput> job)
        {
            switch (operation)
            {
                case 0:
                    job.Write(0, 99); // Read-only.
                    break;
                case 1:
                    job.Read<int>(job.Count);
                    break;
                case 2:
                    job.Read<long>(0); // Write-only.
                    break;
                case 3:
                    job.ForJob().Destroy(job.ForEntity(0).Create()); // Source ID 0 collides across kinds.
                    break;
                case 4:
                    job.ForEntity(1).Set(job.ForEntity(0).Create(), 7L);
                    break;
                case 5:
                    job.ForEntity(0).Destroy(default(CreateTicket));
                    break;
            }
        }
    }
}
