using SKECS.Numerics;

namespace SKECS.Tests;

public sealed class DeterministicSourceOrderTests
{
    private const int RowCount = 130;
    private readonly record struct Participant;
    private readonly record struct LayoutMarker;
    private readonly record struct Excluded;
    private readonly record struct Rank(int Token, int Primary, int Secondary);
    private readonly record struct RowTrace(int Id, int Generation, int Job, long Ordinal,
        bool HasKey, int Primary, int Secondary);

    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(17, true, 1)]
    [InlineData(192, true, 2)]
    public void SignedSourceOrderDeterminesJobRowsBeforeDispatch(int chunkCapacity, bool useBatch, int budget) =>
        VerifyRows(chunkCapacity, useBatch, budget, customOrder: true);

    [Fact]
    public void DefaultSourceOrderUsesEntityOrderAndDoesNotAttachKeys() =>
        VerifyRows(17, useBatch: true, budget: 2, customOrder: false);

    [Fact]
    public void ASelectedRowWithoutTheSortComponentFaultsBeforeBatchSubmission()
    {
        using var world = new World();
        world.CreateEntity(new Participant(), new Rank(0, -1, 0));
        world.CreateEntity(new Participant());
        var query = new DeterministicQuery().With<Participant>()
            .OrderBy(DeterministicSourceOrder.By<Rank>("target.order", 1, Key));
        var batch = new InspectBatch();
        var workload = new DeterministicWorkload<byte>().AddBatchSystem(SystemStage.Update, query, batch);
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(9, 0, Fixed.One);

        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(2));
        Assert.Contains(nameof(Rank), Assert.IsType<InvalidOperationException>(runner.Fault).Message);
        Assert.Equal(0, batch.Begun);
        Assert.Null(runner.LastCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SortingDoesNotGrantProcessorAccessToTheSortComponent(bool tryRead)
    {
        using var world = new World();
        world.CreateEntity(new Participant(), new Rank(0, 0, 0));
        var query = new DeterministicQuery().With<Participant>()
            .OrderBy(DeterministicSourceOrder.By<Rank>("target.order", 1, Key));
        var workload = new DeterministicWorkload<byte>().AddSystem(SystemStage.Update, query, new ReadRank(tryRead));
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(9, 0, Fixed.One);

        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(1));
        Assert.Contains("Read access", Assert.IsType<InvalidOperationException>(runner.Fault).Message);
        Assert.Null(runner.LastCompleted);
    }

    [Fact]
    public void SourceOrderRequiresAVersionedIdentityAndAnEntityKernel()
    {
        Assert.Throws<ArgumentException>(() => DeterministicSourceOrder.By<Rank>(" ", 1, Key));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeterministicSourceOrder.By<Rank>("target.order", 0, Key));
        Assert.Throws<ArgumentNullException>(() => DeterministicSourceOrder.By<Rank>("target.order", 1, null!));
        Assert.Throws<ArgumentNullException>(() => new DeterministicQuery().OrderBy(null!));
        var query = new DeterministicQuery().OrderBy(DeterministicSourceOrder.By<Rank>("target.order", 1, Key));
        var workload = new DeterministicWorkload<byte>();
        Assert.Throws<ArgumentException>(() => workload.AddJob(SystemStage.Update, query, new EmitRows()));
        Assert.Throws<ArgumentException>(() => workload.AddSnapshotPreparation(SystemStage.Update, query, _ => { }));
        Assert.Throws<ArgumentException>(() => workload.AddCommitGroup(SystemStage.Update,
            group => group.AddJob(query, new EmitRows())));
        Assert.Throws<ArgumentException>(() => workload.AddCommitGroup(SystemStage.Update,
            group => group.AddSnapshotPreparation(query, _ => { })));
    }

    private static void VerifyRows(int chunkCapacity, bool useBatch, int budget, bool customOrder)
    {
        using var world = new World(new WorldOptions(chunkCapacity: chunkCapacity));
        (Entity Entity, Rank Rank)[] rows = new (Entity, Rank)[RowCount];
        for (int id = 0; id < RowCount; id++)
        {
            var rank = id switch
            {
                _ when id % 23 == 0 => new Rank(id, int.MinValue, int.MaxValue),
                _ when id % 11 == 0 => new Rank(id, 4, int.MinValue),
                _ when id % 17 == 0 => new Rank(id, 4, int.MaxValue),
                _ when id % 31 == 0 => new Rank(id, int.MaxValue, int.MinValue),
                _ => new Rank(id, 4, 0)
            };
            var entity = world.CreateEntity(new Participant(), rank);
            if (id % 5 == 0)
                world.Add(entity, new LayoutMarker());
            rows[id] = (entity, rank);
        }
        world.DestroyEntity(rows[10].Entity);
        rows[10].Entity = world.CreateEntity(new Participant(), rows[10].Rank);
        Assert.Equal(10, rows[10].Entity.Id);
        Assert.Equal(2, rows[10].Entity.Version);
        var excluded = world.CreateEntity(new Participant(), new Rank(RowCount, -1, 0));
        world.Add(excluded, new Excluded());
        world.CreateEntity();

        var visits = new int[RowCount + 1];
        var query = new DeterministicQuery().With<Participant>().Without<Excluded>();
        if (customOrder)
        {
            var order = DeterministicSourceOrder.By<Rank>("target.order", 7, (in Rank rank) =>
            {
                visits[rank.Token]++;
                return Key(in rank);
            });
            Assert.Equal("target.order", order.ContractId);
            Assert.Equal(7, order.Version);
            query.OrderBy(order);
        }

        var workload = new DeterministicWorkload<byte>();
        var batch = new InspectBatch();
        if (useBatch)
            workload.AddBatchSystem(SystemStage.Update, query, batch);
        else
            workload.AddSystem(SystemStage.Update, query, new EmitRows());
        using var runner = new DeterministicRunner<byte>(world, workload, workerCount: useBatch ? 1 : 2);
        runner.BeginTick(9, 0, Fixed.One);
        DeterministicTickStatus status = DeterministicTickStatus.Pending;
        for (int attempt = 0; attempt < 4 && status == DeterministicTickStatus.Pending; attempt++)
            status = runner.Advance(budget);
        Assert.True(status == DeterministicTickStatus.Completed, runner.Fault?.ToString());

        var sorted = customOrder ? rows.OrderBy(row => row.Rank.Primary).ThenBy(row => row.Rank.Secondary)
            .ThenBy(row => row.Entity.Id).ThenBy(row => row.Entity.Version).ToArray() : rows;
        var expected = sorted.Select((row, ordinal) => new RowTrace(row.Entity.Id, row.Entity.Version, ordinal / 64,
            ordinal, customOrder, customOrder ? row.Rank.Primary : 0, customOrder ? row.Rank.Secondary : 0)).ToArray();
        Assert.Equal(0, expected[63].Job);
        Assert.Equal(1, expected[64].Job);
        Assert.Equal((expected[63].Primary, expected[63].Secondary), (expected[64].Primary, expected[64].Secondary));
        Assert.True(expected[63].Id < expected[64].Id);
        if (useBatch)
            Assert.Equal(expected, batch.Submitted);
        Assert.All(visits.Take(RowCount), count => Assert.Equal(customOrder ? 1 : 0, count));
        Assert.Equal(0, visits[RowCount]);

        var events = runner.LastCompleted!.Events;
        Assert.Equal(expected.OrderBy(row => row.Id), events.Select(entry => entry.Get<RowTrace>()));
        Assert.Equal(rows.Select(row => row.Entity.Id), events.Select(entry => entry.Key.SourceId));
        Assert.Equal(rows.Select(row => row.Entity.Version), events.Select(entry => entry.Key.SourceGeneration));
        Assert.All(events, entry =>
        {
            Assert.Equal(0, entry.Key.LocalOrdinal);
            Assert.Null(entry.Key.DomainKey);
        });
    }

    private static DeterministicDomainKey Key(in Rank rank) => new(rank.Primary, rank.Secondary);

    private static RowTrace Trace(Entity entity, int job, long ordinal, DeterministicDomainKey? key) =>
        new(entity.Id, entity.Version, job, ordinal, key.HasValue, key?.Primary ?? 0, key?.Secondary ?? 0);

    private readonly struct EmitRows : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            for (int row = 0; row < job.Count; row++)
            {
                var ordinal = job.GetSourceOrderOrdinal(row);
                Assert.Equal((long)job.JobId * 64 + row, ordinal);
                job.ForEntity(row).Emit(Trace(job.GetEntity(row), job.JobId, ordinal, job.GetSourceOrderKey(row)));
            }
        }
    }

    private readonly struct ReadRank(bool tryRead) : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            if (tryRead)
                job.TryRead<Rank>(0, out _);
            else
                _ = job.Read<Rank>(0);
        }
    }

    private sealed class InspectBatch : IDeterministicBatchProcessor<byte>, IDeterministicBatchSubmission<byte>
    {
        public bool IsCompleted => true;
        public int Begun { get; private set; }
        public List<RowTrace> Submitted { get; } = new();

        public IDeterministicBatchSubmission<byte> Begin(DeterministicBatch<byte> batch)
        {
            Begun++;
            for (int id = batch.FirstJobId; id < batch.FirstJobId + batch.JobCount; id++)
            {
                var job = batch.GetReadJob(id);
                for (int row = 0; row < job.Count; row++)
                    Submitted.Add(Trace(job.GetEntity(row), job.JobId, job.GetSourceOrderOrdinal(row), job.GetSourceOrderKey(row)));
            }
            return this;
        }

        public void Apply(DeterministicBatch<byte> batch)
        {
            var processor = new EmitRows();
            for (int id = batch.FirstJobId; id < batch.FirstJobId + batch.JobCount; id++)
                processor.Execute(batch.GetJob(id));
        }

        public void Discard() { }
        public void Dispose() { }
    }
}
