using SKECS.Numerics;

namespace SKECS.Tests;

public sealed class DeterministicDomainOrderTests
{
    private readonly record struct Position(int Value);

    [Fact]
    public void DomainCoordinatesSortAcrossProducersBeforeTheCanonicalSourceTies()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 1));
        var first = world.CreateEntity(new Position(10));
        var second = world.CreateEntity(new Position(20));
        var domain = new DeterministicDomainOrder("shots", 2);
        var query = new DeterministicQuery().With<Position>();
        var workload = new DeterministicWorkload<byte>()
            .AddJob(SystemStage.Update, new DeterministicQuery(), new EmitJob(-1, keyed: false))
            .AddCommitGroup(SystemStage.Update, domain, group =>
            {
                Assert.Same(domain, group.DomainOrder);
                group.AddSystem(query, new EmitEntities());
                group.AddBatchSystem(query, new BatchProducer(domain));
            });
        using var runner = new DeterministicRunner<byte>(world, workload, workerCount: 2);
        runner.BeginTick(0, 0, Fixed.One);
        Assert.True(runner.Advance(7) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        var events = runner.LastCompleted!.Events;
        var standalone = events[0];
        Assert.Equal(-1, standalone.Get<int>());
        Assert.Null(standalone.Key.DomainKey);
        Assert.Equal(28658942, standalone.Key.GetHashCode());

        const DeterministicSourceKind Entity = DeterministicSourceKind.Entity;
        const DeterministicSourceKind Job = DeterministicSourceKind.Job;
        Assert.Equal(new[]
        {
            (int.MinValue, int.MaxValue, 0, Entity, first.Id, first.Version, 2, 10),
            (4, -1, 1, Job, 0, 0, 0, 400),
            (4, 0, 0, Entity, first.Id, first.Version, 0, 100),
            (4, 0, 0, Entity, first.Id, first.Version, 1, 200),
            (4, 0, 0, Entity, second.Id, second.Version, 0, 101),
            (4, 0, 0, Entity, second.Id, second.Version, 1, 201),
            (4, 0, 0, Job, 0, 0, 0, 300),
            (4, 0, 1, Entity, first.Id, first.Version, 0, 500),
            (4, int.MaxValue, 1, Entity, first.Id, first.Version, 1, 501)
        }, events.Skip(1).Select(entry => (entry.Key.DomainKey!.Value.Primary, entry.Key.DomainKey.Value.Secondary,
            entry.Key.ProducerOrder, entry.Key.SourceKind, entry.Key.SourceId, entry.Key.SourceGeneration,
            entry.Key.LocalOrdinal, entry.Get<int>())));
        Assert.Equal(events.Count, events.Select(entry => entry.Key).Distinct().Count());
        for (int i = 1; i < events.Count; i++)
            Assert.True(events[i - 1].Key.CompareTo(events[i].Key) < 0);
    }

    [Fact]
    public void DomainContractRequiresAnIdentifierAndPositiveVersion()
    {
        Assert.Throws<ArgumentException>(() => new DeterministicDomainOrder("", 1));
        Assert.Throws<ArgumentException>(() => new DeterministicDomainOrder(" ", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeterministicDomainOrder("shots", 0));
    }

    [Theory]
    [InlineData(0, "requires a domain key")]
    [InlineData(1, "does not declare domain ordering")]
    [InlineData(2, "does not declare domain ordering")]
    public void EnqueueRejectsMissingAndUnscheduledKeys(int route, string message)
    {
        using var world = new World();
        var workload = new DeterministicWorkload<byte>();
        var processor = new EmitJob(42, keyed: route != 0);
        switch (route)
        {
            case 0:
                workload.AddCommitGroup(SystemStage.Update, new DeterministicDomainOrder("shots", 1),
                    group => group.AddJob(processor));
                break;
            case 1:
                workload.AddCommitGroup(SystemStage.Update, group => group.AddJob(processor));
                break;
            default:
                workload.AddJob(SystemStage.Update, new DeterministicQuery(), processor);
                break;
        }

        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(0, 0, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(1));
        Assert.Contains(message, Assert.IsType<InvalidOperationException>(runner.Fault).Message);
        Assert.Null(runner.LastCompleted);
    }

    [Fact]
    public void TicketCanUseLaterDomainKeyAndRejectsAReferenceThatWouldSortBeforeItsCreation()
    {
        using var world = new World();
        var workload = new DeterministicWorkload<byte>();
        var reduction = workload.AddReduction(0, static (sum, leaf) => leaf < 0
            ? throw new InvalidOperationException("A rejected group reached its commit barrier.")
            : sum + leaf);
        workload.AddCommitGroup(SystemStage.Update, new DeterministicDomainOrder("tickets", 1),
            group => group.AddJob(new DeterministicQuery().Write<Position>(), new UseDomainTicket(reduction)));
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(0, 0, Fixed.One);
        Assert.True(runner.Advance(1) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        var completed = runner.LastCompleted!;
        Assert.Equal(1, completed.GetReduction(reduction));
        var entry = Assert.Single(completed.Events);
        Assert.Equal(3, entry.Key.LocalOrdinal);
        Assert.Equal(new Position(42), world.Get<Position>(world.BindEntity(0, 1)));

        runner.BeginTick(1, 1, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(1));
        Assert.Contains("sorts before its ticket", Assert.IsType<InvalidOperationException>(runner.Fault).Message);
        Assert.Same(completed, runner.LastCompleted);
        Assert.Equal(1, completed.GetReduction(reduction));
        Assert.Equal(44, Assert.Single(completed.Events).Get<int>());
    }

    private readonly struct EmitJob(int value, bool keyed) : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            var source = job.ForJob();
            if (keyed) source = source.WithDomainOrder(new DeterministicDomainKey(0, 0));
            source.Emit(value);
        }
    }

    private readonly struct EmitEntities : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            Assert.Equal("shots", job.DomainOrder!.ContractId);
            Assert.Equal(2, job.DomainOrder.Version);
            var tie = new DeterministicDomainKey(4, 0);
            job.ForJob().WithDomainOrder(tie).Emit(300);
            for (int row = job.Count - 1; row >= 0; row--)
            {
                var source = job.ForEntity(row).WithDomainOrder(tie);
                source.Emit(100 + row);
                source.Emit(200 + row);
                if (row == 0)
                    source.WithDomainOrder(new DeterministicDomainKey(int.MinValue, int.MaxValue)).Emit(10);
            }
        }
    }

    private sealed class BatchProducer(DeterministicDomainOrder domain) :
        IDeterministicBatchProcessor<byte>, IDeterministicBatchSubmission<byte>
    {
        public bool IsCompleted => true;

        public IDeterministicBatchSubmission<byte> Begin(DeterministicBatch<byte> batch)
        {
            Assert.Same(domain, batch.DomainOrder);
            Assert.Same(domain, batch.GetReadJob(0).DomainOrder);
            return this;
        }

        public void Apply(DeterministicBatch<byte> batch)
        {
            Assert.Same(domain, batch.DomainOrder);
            var job = batch.GetJob(0);
            job.ForJob().WithDomainOrder(new DeterministicDomainKey(4, -1)).Emit(400);
            var entity = job.ForEntity(0);
            entity.WithDomainOrder(new DeterministicDomainKey(4, 0)).Emit(500);
            entity.WithDomainOrder(new DeterministicDomainKey(4, int.MaxValue)).Emit(501);
        }

        public void Discard() { }
        public void Dispose() { }
    }

    private readonly struct UseDomainTicket(DeterministicReduction<int> reduction) : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            var source = job.ForJob();
            source.WithDomainOrder(new DeterministicDomainKey(1, 0)).Contribute(reduction, job.Input == 0 ? 1 : -1);
            var ticket = source.WithDomainOrder(new DeterministicDomainKey(2, 4)).Create();
            source.WithDomainOrder(new DeterministicDomainKey(2, job.Input == 0 ? 5 : 3)).Add(ticket, new Position(42));
            source.WithDomainOrder(new DeterministicDomainKey(2, 6)).Emit(44);
        }
    }
}
