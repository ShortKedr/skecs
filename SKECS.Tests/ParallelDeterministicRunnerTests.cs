using System.Collections.Concurrent;
using SKECS.Numerics;

namespace SKECS.Tests;

public sealed class ParallelDeterministicRunnerTests
{
    private const int RowCount = 327;
    private readonly record struct Value(int Number);
    private readonly record struct Bias(int Number);
    private readonly record struct Marker(int Number);
    private readonly record struct Migrated;
    private record struct Input(uint Seed, int Shift, int SlowJob);
    private readonly record struct Row(int Id, int Generation, int? Value, int? Bias, int? Marker, bool Migrated);
    private readonly record struct Emission(int Job, int Value);
    private sealed record Tick(Row[] State, (DeterministicOrderKey Key, Emission Payload)[] Events, ulong Reduction, int Slices);

    [Theory]
    [InlineData(1u)]
    [InlineData(0xfedcba98u)]
    public void EveryTickMatchesTheSequentialRunner(uint seed)
    {
        var (initial, expected) = Run(seed, capacity: 256, budget: int.MaxValue, workers: 1, slowJob: -1);
        foreach (var workers in new[] { 1, 2, 4, Environment.ProcessorCount }.Distinct())
        foreach (var capacity in new[] { 1, 63, 256 })
        foreach (var budget in new[] { 1, 3, 64 })
        {
            var (actualInitial, actual) = Run(seed, capacity, budget, workers, slowJob: capacity % 2);
            Assert.Equal(initial, actualInitial);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i].State, actual[i].State);
                Assert.Equal(expected[i].Events, actual[i].Events);
                Assert.Equal(expected[i].Reduction, actual[i].Reduction);
                // Two six-job row systems and two singleton systems.
                Assert.Equal((14 + budget - 1) / budget, actual[i].Slices);
            }
        }

        // Validate a conflict across job boundaries and multiple generations of recreated IDs.
        Assert.Equal(RowCount, expected[^1].State[0].Marker);
        Assert.Equal(4, expected[^1].State[1].Generation);
        Assert.Equal(RowCount + 1, expected[^1].State.Length);
    }

    private static (Row[] Initial, Tick[] Ticks) Run(uint seed, int capacity, int budget, int workers, int slowJob)
    {
        using var world = new World(new WorldOptions(chunkCapacity: capacity));
        var anchor = world.CreateEntity(new Marker(-1));
        for (var id = 1; id <= RowCount; id++)
        {
            var entity = world.CreateEntity(new Value(id));
            if (id % 2 == 0) world.Add(entity, new Bias(id % 19));
        }

        var workload = new DeterministicWorkload<Input>();
        var reduction = workload.AddReduction(0UL, static (previous, leaf) => unchecked(previous * 131 + leaf));
        workload.AddJob(SystemStage.PreUpdate, new DeterministicQuery(), new Boundary());
        workload.AddSystem(SystemStage.Update,
            new DeterministicQuery().With<Value>().Read<Value>().Write<Value>().Read<Bias>().Write<Bias>()
                .Write<Migrated>().Write<Marker>(), new Update(reduction, anchor));
        workload.AddSystem(SystemStage.PostUpdate,
            new DeterministicQuery().With<Value>().Read<Value>().Read<Bias>(), new Observe());
        workload.AddJob(SystemStage.PostUpdate, new DeterministicQuery(), new Boundary());

        var ticks = new Tick[3];
        using var runner = new DeterministicRunner<Input>(world, workload, workers);
        var initial = ReadState(world);
        for (var tick = 0; tick < ticks.Length; tick++)
        {
            var input = new Input(seed, Shift: 17 + tick, slowJob);
            runner.BeginTick(tick, input, Fixed.One);
            input.Shift = -10000;
            var slices = 1;
            while (runner.Advance(budget) == DeterministicTickStatus.Pending)
            {
                Assert.Equal(tick == 0 ? (long?)null : tick - 1, runner.LastCompleted?.TickIndex);
                slices++;
            }

            Assert.True(runner.Status == DeterministicTickStatus.Completed, runner.Fault?.ToString());
            var completed = runner.LastCompleted!;
            Assert.Equal(tick, completed.TickIndex);
            var events = completed.Events.Select(static entry => (entry.Key, entry.Get<Emission>())).ToArray();
            ticks[tick] = new Tick(ReadState(world), events, completed.GetReduction(reduction), slices);
        }

        runner.Dispose();
        Assert.True(world.IsAlive(anchor));
        return (initial, ticks);
    }

    private static unsafe Row[] ReadState(World world)
    {
        var rows = new List<Row>();
        foreach (var chunk in world.QueryBuilder().Build().Chunks())
        for (var row = 0; row < chunk.Count; row++)
        {
            var id = chunk.Entities[row];
            var generation = chunk.Versions[row];
            var entity = world.BindEntity(id, generation);
            rows.Add(new Row(id, generation,
                world.TryGet<Value>(entity, out var value) ? value.Number : null,
                world.TryGet<Bias>(entity, out var bias) ? bias.Number : null,
                world.TryGet<Marker>(entity, out var marker) ? marker.Number : null,
                world.Has<Migrated>(entity)));
        }

        return rows.OrderBy(static row => row.Id).ToArray();
    }

    private struct Update(DeterministicReduction<ulong> reduction, Entity anchor) : IDeterministicJobProcessor<Input>
    {
        private int _calls;

        public void Execute(DeterministicJob<Input> job)
        {
            Assert.Equal(1, ++_calls);
            if (job.JobId == job.Input.SlowJob) Thread.Sleep(1);

            var jobSource = job.ForJob();
            jobSource.Emit(new Emission(job.JobId, job.Count));
            jobSource.Contribute(reduction, (ulong)job.Count);
            var jobTicket = jobSource.Create();
            jobSource.Destroy(jobTicket);

            for (var row = job.Count - 1; row >= 0; row--)
            {
                var entity = job.GetEntity(row);
                var previous = job.Read<Value>(row).Number;
                var hasBias = job.TryRead<Bias>(row, out var bias);
                var value = previous + bias.Number + job.Input.Shift + (int)job.Step + DrawV1(job.Input.Seed, job.TickIndex, entity.Id);
                job.Write(row, new Value(value));
                var source = job.ForEntity(row);
                source.Set(anchor, new Marker(entity.Id));
                if (entity.Id % 64 == 1)
                {
                    source.Destroy(entity);
                    var ticket = source.Create();
                    source.Set(entity, new Value(-10000));
                    source.Set(ticket, new Value(value));
                    source.Destroy(entity);
                    if (hasBias) source.Set(ticket, bias);
                }
                else if (job.TickIndex % 2 == 0)
                {
                    source.Add(entity, new Migrated());
                }
                else
                {
                    source.Remove<Migrated>(entity);
                }

                source.Contribute(reduction, (ulong)value);
                source.Emit(new Emission(job.JobId, value));
            }
        }

        // Version 1 uses one xorshift32 draw per entity in this system.
        private static int DrawV1(uint seed, long tick, int id)
        {
            var state = unchecked(seed ^ (uint)tick * 0x9e3779b9u ^ (uint)id * 0x85ebca6bu);
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (int)(state & 7);
        }
    }

    private readonly struct Observe : IDeterministicJobProcessor<Input>
    {
        public void Execute(DeterministicJob<Input> job)
        {
            for (var row = 0; row < job.Count; row++)
            {
                var value = job.Read<Value>(row).Number;
                job.TryRead<Bias>(row, out var bias);
                job.ForEntity(row).Emit(new Emission(job.JobId, value + bias.Number));
            }
        }
    }

    private readonly struct Boundary : IDeterministicJobProcessor<Input>
    {
        public void Execute(DeterministicJob<Input> job) => job.ForJob().Emit(new Emission(job.JobId, job.Input.Shift));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReversedCompletionDrainsWorkersAndSelectsTheFirstLogicalFault(bool abortAfterBatch)
    {
        using var world = new World();
        var entity = world.CreateEntity(new Value(0));
        for (var i = 1; i < 128; i++) world.CreateEntity(new Value(i));
        var accessor = world.GetAccessor<Value>();
        var workload = new DeterministicWorkload<Input>()
            .AddSystem(SystemStage.Update, new DeterministicQuery().With<Value>().ReadWrite<Value>(), new Reorder())
            .AddJob(SystemStage.PostUpdate, new DeterministicQuery(), new Boundary());
        using var runner = new DeterministicRunner<Input>(world, workload, 2);
        runner.BeginTick(0, default, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(3));
        var previous = runner.LastCompleted;

        using var fasterJob = new ManualResetEventSlim();
        var probe = new JobProbe(fasterJob, world);
        Volatile.Write(ref _probe, probe);
        try
        {
            runner.BeginTick(1, new Input(0, abortAfterBatch ? 0 : 1, -1), Fixed.One);
            var status = runner.Advance(2);
            Assert.Equal(new[] { 1, 0 }, probe.Finishes.ToArray());
            if (abortAfterBatch)
            {
                Assert.Equal(DeterministicTickStatus.Pending, status);
                runner.Abort();
                Assert.IsType<OperationCanceledException>(runner.Fault);
            }
            else
            {
                Assert.Equal(DeterministicTickStatus.Faulted, status);
                Assert.Equal(0, Assert.IsType<JobFault>(runner.Fault).JobId);
            }

            Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(2));
            Assert.Same(previous, runner.LastCompleted);
            Assert.Throws<InvalidOperationException>(() => accessor.Get(entity));
            Assert.Throws<InvalidOperationException>(world.Dispose);
            runner.DiscardWorld();
            Assert.Throws<ObjectDisposedException>(() => world.IsAlive(entity));
            Assert.False(accessor.Has(entity));
        }
        finally
        {
            Volatile.Write(ref _probe, null);
        }
    }

    private static JobProbe? _probe;
    private sealed record JobProbe(ManualResetEventSlim FasterJob, World World)
    {
        public ConcurrentQueue<int> Finishes { get; } = new();
    }

    private sealed class JobFault(int jobId) : Exception
    {
        public int JobId { get; } = jobId;
    }

    private readonly struct Reorder : IDeterministicJobProcessor<Input>
    {
        public void Execute(DeterministicJob<Input> job)
        {
            if (Volatile.Read(ref _probe) is not { } probe) return;
            var entity = job.GetEntity(0);
            Assert.Throws<InvalidOperationException>(() => probe.World.Get<Value>(entity));
            if (job.JobId == 0 && !probe.FasterJob.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The higher-ID job did not run concurrently.");
            job.Write(0, new Value(job.JobId));
            job.ForJob().Emit(new Emission(job.JobId, job.JobId));
            try
            {
                if (job.Input.Shift != 0) throw new JobFault(job.JobId);
            }
            finally
            {
                probe.Finishes.Enqueue(job.JobId);
                if (job.JobId == 1) probe.FasterJob.Set();
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WorkersCannotAccessOtherJobsRowsOrUndeclaredComponents(int violation)
    {
        using var world = new World();
        var entity = world.CreateEntity(new Value(0));
        for (var i = 1; i < 65; i++) world.CreateEntity(new Value(i));
        var workload = new DeterministicWorkload<int>().AddSystem(SystemStage.Update,
            new DeterministicQuery().With<Value>().ReadWrite<Value>(), new ViolateAccess());
        using var runner = new DeterministicRunner<int>(world, workload, 2);
        runner.BeginTick(0, violation, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(2));
        if (violation == 2)
            Assert.IsType<InvalidOperationException>(runner.Fault);
        else
            Assert.IsType<IndexOutOfRangeException>(runner.Fault);
        Assert.Null(runner.LastCompleted);
        Assert.Throws<InvalidOperationException>(() => world.IsAlive(entity));
    }

    private readonly struct ViolateAccess : IDeterministicJobProcessor<int>
    {
        public void Execute(DeterministicJob<int> job)
        {
            if (job.JobId != 0) return;
            switch (job.Input)
            {
                case 0: job.Read<Value>(job.Count); break;
                case 1: job.Write(job.Count, new Value(5)); break;
                case 2: job.Write(0, new Marker(5)); break;
            }
        }
    }
}
