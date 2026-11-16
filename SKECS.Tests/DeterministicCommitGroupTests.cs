using SKECS.Numerics;

namespace SKECS.Tests;

public sealed class DeterministicCommitGroupTests
{
    private readonly record struct Position(int Value);
    private readonly record struct Marked;

    [Fact]
    public void ProducersShareDirectWritesAndCommitStructureBeforeDownstreamQueries()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 1));
        var first = world.CreateEntity(new Position(10));
        var second = world.CreateEntity(new Position(20));
        var unmarked = new DeterministicQuery().With<Position>().Without<Marked>()
            .ReadWrite<Position>().Write<Marked>();
        var batch = new Batch(
            apply: batch =>
            {
                var job = batch.GetJob(0);
                Assert.Equal(SystemStage.Update, job.Stage);
                Assert.Equal(1, job.SystemOrder);
                Assert.Equal(1, job.KernelOrder);
                for (int row = job.Count - 1; row >= 0; row--)
                {
                    int value = job.Read<Position>(row).Value + 100;
                    job.Write(row, new Position(value));
                    job.ForEntity(row).Emit(value);
                }
                job.ForJob().Emit(-101);
            },
            inspect: batch =>
            {
                Assert.Equal(SystemStage.Update, batch.Stage);
                Assert.Equal(1, batch.SystemOrder);
                Assert.Equal(1, batch.KernelOrder);
                Assert.Equal(1, batch.JobCount);
                var rows = batch.GetReadJob(0);
                Assert.Equal(2, rows.Count);
                Assert.Equal(first, rows.GetEntity(0));
                Assert.Equal(second, rows.GetEntity(1));
                Assert.Equal(110, rows.Read<Position>(0).Value);
                Assert.Equal(120, rows.Read<Position>(1).Value);
            })
        { IsCompleted = false };
        var workload = new DeterministicWorkload<byte>()
            .AddJob(SystemStage.PreUpdate, new DeterministicQuery(), new EmitJob(-1))
            .AddCommitGroup(SystemStage.Update, group =>
            {
                group.AddSystem(unmarked, new WriteAndQueue());
                group.AddBatchSystem(unmarked, batch);
                group.AddJob(new DeterministicQuery(), new EmitJob(-102));
            })
            .AddSystem(SystemStage.Update, unmarked, new UnexpectedRows())
            .AddSystem(SystemStage.Update,
                new DeterministicQuery().With<Position>().With<Marked>().Read<Position>(), new ObservePositions());
        using var runner = new DeterministicRunner<byte>(world, workload, workerCount: 2);
        runner.BeginTick(10, default, Fixed.One);

        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(3));
        Assert.Equal(1, batch.Begun);
        Assert.Null(runner.LastCompleted);
        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(1));
        Assert.Equal(0, batch.Applied);
        batch.IsCompleted = true;
        Assert.True(runner.Advance(2) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        Assert.Equal(1, batch.Applied);
        Assert.Equal(1, batch.Disposed);
        Assert.Equal(0, batch.Discarded);

        var completed = runner.LastCompleted!;
        Assert.Equal(10, completed.TickIndex);
        Assert.All(completed.Events, entry => Assert.Equal(10, entry.Key.TickIndex));
        const DeterministicSourceKind Entity = DeterministicSourceKind.Entity;
        const DeterministicSourceKind Job = DeterministicSourceKind.Job;
        const SystemStage Update = SystemStage.Update;
        Assert.Equal(new[]
        {
            (SystemStage.PreUpdate, 0, 0, Job, 0, 0, 0, -1),
            (Update, 1, 0, Entity, first.Id, first.Version, 0, 110),
            (Update, 1, 0, Entity, second.Id, second.Version, 0, 120),
            (Update, 1, 0, Job, 0, 0, 0, -100),
            (Update, 1, 1, Entity, first.Id, first.Version, 0, 210),
            (Update, 1, 1, Entity, second.Id, second.Version, 0, 220),
            (Update, 1, 1, Job, 0, 0, 0, -101),
            (Update, 1, 2, Job, 0, 0, 0, -102),
            (Update, 3, 0, Entity, first.Id, first.Version, 0, 210),
            (Update, 3, 0, Entity, second.Id, second.Version, 0, 220),
            (Update, 3, 0, Entity, 2, 1, 0, 900),
        }, completed.Events.Select(entry => (entry.Key.Stage, entry.Key.SystemOrder, entry.Key.ProducerOrder,
            entry.Key.SourceKind, entry.Key.SourceId, entry.Key.SourceGeneration, entry.Key.LocalOrdinal, entry.Get<int>())));
        Assert.Equal(completed.Events.Count, completed.Events.Select(entry => entry.Key).Distinct().Count());
        var created = world.BindEntity(2, 1);
        Assert.True(world.Has<Marked>(created));
        Assert.Equal(new Position(900), world.Get<Position>(created));
    }

    [Fact]
    public void TicketFromAnotherProducerIsRejectedEvenWhenTheSourceIdAndGenerationMatch()
    {
        using var world = new World();
        world.CreateEntity(new Position(10));
        var query = new DeterministicQuery().With<Position>();
        CreateTicket ticket = default;
        var create = new Batch(batch => ticket = batch.GetJob(0).ForEntity(0).Create());
        var use = new Batch(batch =>
        {
            var source = batch.GetJob(0).ForEntity(0);
            source.Emit(0);
            source.Destroy(ticket);
        });
        var workload = new DeterministicWorkload<byte>().AddCommitGroup(SystemStage.Update, group =>
        {
            group.AddBatchSystem(query, create);
            group.AddBatchSystem(query, use);
        });
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(0, default, Fixed.One);

        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(2));
        Assert.Contains("ticket", Assert.IsType<InvalidOperationException>(runner.Fault).Message);
        Assert.Null(runner.LastCompleted);
    }

    [Fact]
    public void FailedProducerDiscardsPendingOutputsAndPreservesTheCompletedTick()
    {
        using var world = new World();
        world.CreateEntity(new Position(10));
        var workload = new DeterministicWorkload<byte>();
        var reduction = workload.AddReduction(0, static (sum, leaf) => leaf == 99
            ? throw new InvalidOperationException("The group applied a reduction before its final producer succeeded.")
            : sum + leaf);
        var batch = new Batch(batch =>
        {
            batch.GetJob(0).ForEntity(0).Emit(21);
            if (batch.Input != 0)
                throw new InvalidOperationException("Producer fault.");
        });
        workload.AddCommitGroup(SystemStage.Update, group =>
        {
            group.AddJob(new DeterministicQuery(), new QueueBeforeFailure(reduction));
            group.AddBatchSystem(new DeterministicQuery().With<Position>(), batch);
        });
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(0, 0, Fixed.One);
        Assert.True(runner.Advance(2) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        var completed = runner.LastCompleted!;
        Assert.Equal(new[] { 20, 21 }, completed.Events.Select(entry => entry.Get<int>()));
        Assert.Equal(5, completed.GetReduction(reduction));

        batch.IsCompleted = false;
        runner.BeginTick(1, 1, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(2));
        Assert.Same(completed, runner.LastCompleted);
        batch.IsCompleted = true;
        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(1));
        Assert.Equal("Producer fault.", Assert.IsType<InvalidOperationException>(runner.Fault).Message);
        Assert.Same(completed, runner.LastCompleted);
        Assert.Equal(new[] { 20, 21 }, completed.Events.Select(entry => entry.Get<int>()));
        Assert.Equal(5, completed.GetReduction(reduction));
        Assert.Equal(1, batch.Discarded);
        Assert.Equal(2, batch.Disposed);
    }

    [Fact]
    public void ThrowingGroupConfigurationLeavesNoSystemsRegistered()
    {
        using var world = new World();
        var workload = new DeterministicWorkload<byte>();
        var failure = new InvalidOperationException();
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            workload.AddCommitGroup(SystemStage.Update, group =>
            {
                group.AddJob(new DeterministicQuery(), new EmitJob(-1));
                throw failure;
            })));
        workload.AddJob(SystemStage.Update, new DeterministicQuery(), new EmitJob(1));
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(0, default, Fixed.One);

        Assert.True(runner.Advance(1) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        var entry = Assert.Single(runner.LastCompleted!.Events);
        Assert.Equal(1, entry.Get<int>());
        Assert.Equal(0, entry.Key.SystemOrder);
        Assert.Equal(0, entry.Key.ProducerOrder);
    }

    private readonly struct WriteAndQueue : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            Assert.Equal(SystemStage.Update, job.Stage);
            Assert.Equal(1, job.SystemOrder);
            Assert.Equal(0, job.KernelOrder);
            job.ForJob().Emit(-100);
            for (int row = job.Count - 1; row >= 0; row--)
            {
                int value = job.Read<Position>(row).Value + 100;
                job.Write(row, new Position(value));
                var source = job.ForEntity(row);
                source.Emit(value);
                source.Add(job.GetEntity(row), new Marked());
                if (row == 0)
                {
                    var ticket = source.Create();
                    source.Add(ticket, new Position(900));
                    source.Add(ticket, new Marked());
                }
            }
        }
    }

    private readonly struct EmitJob(int value) : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job) => job.ForJob().Emit(value);
    }

    private readonly struct UnexpectedRows : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job) => throw new InvalidOperationException("The committed marker was missing.");
    }

    private readonly struct ObservePositions : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            for (int row = 0; row < job.Count; row++)
                job.ForEntity(row).Emit(job.Read<Position>(row).Value);
        }
    }

    private readonly struct QueueBeforeFailure(DeterministicReduction<int> reduction) : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            var source = job.ForJob();
            source.Create();
            source.Emit(job.Input == 0 ? 20 : 22);
            source.Contribute(reduction, job.Input == 0 ? 5 : 99);
        }
    }

    private delegate void BatchOperation(DeterministicBatch<byte> batch);

    private sealed class Batch(BatchOperation apply, BatchOperation? inspect = null) :
        IDeterministicBatchProcessor<byte>, IDeterministicBatchSubmission<byte>
    {
        public bool IsCompleted { get; set; } = true;
        public int Begun { get; private set; }
        public int Applied { get; private set; }
        public int Discarded { get; private set; }
        public int Disposed { get; private set; }

        public IDeterministicBatchSubmission<byte> Begin(DeterministicBatch<byte> batch)
        {
            Begun++;
            inspect?.Invoke(batch);
            return this;
        }

        public void Apply(DeterministicBatch<byte> batch)
        {
            Applied++;
            apply(batch);
        }

        public void Discard() => Discarded++;
        public void Dispose() => Disposed++;
    }
}
