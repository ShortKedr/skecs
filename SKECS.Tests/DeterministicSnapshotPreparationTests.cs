using SKECS.Numerics;

namespace SKECS.Tests;

public sealed class DeterministicSnapshotPreparationTests
{
    private readonly record struct Position(int Value);
    private readonly record struct Optional(int Value);
    private readonly record struct Absent;

    [Fact]
    public void PreparationReadsAllSortedRowsAfterWritesAndBeforeGroupCommit()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 3));
        var entities = new Entity[130];
        for (int id = 0; id < entities.Length; id++)
        {
            entities[id] = world.CreateEntity(new Position(id));
            if (id % 2 == 0)
                world.Add(entities[id], new Optional(-id));
        }
        world.DestroyEntity(entities[7]);
        entities[7] = world.CreateEntity(new Position(7));
        int inGroup = 0;
        int afterGroup = 0;
        var step = Fixed.FromRatio(1, 3);
        var query = new DeterministicQuery().With<Position>().Read<Position>().Read<Optional>();
        var workload = new DeterministicWorkload<int>()
            .AddCommitGroup(SystemStage.Update, group =>
            {
                group.AddSystem(new DeterministicQuery().With<Position>().ReadWrite<Position>(), new WriteAndDestroy());
                group.AddSnapshotPreparation(query, source =>
                {
                    inGroup++;
                    Assert.Equal(130, source.Count);
                    Assert.Equal(19L, source.TickIndex);
                    Assert.Equal(SystemStage.Update, source.Stage);
                    Assert.Equal(0, source.SystemOrder);
                    Assert.Equal(1, source.KernelOrder);
                    Assert.Equal(33, source.Input);
                    Assert.Equal(step, source.Step);
                    for (int row = 0; row < source.Count; row++)
                    {
                        Assert.Equal(entities[row], source.GetEntity(row));
                        Assert.Equal(new Position(row + 33), source.Read<Position>(row));
                        Assert.Equal(row % 2 == 0, source.TryRead<Optional>(row, out var optional));
                        Assert.Equal(row % 2 == 0 ? new Optional(-row) : default, optional);
                    }
                    Assert.Throws<InvalidOperationException>(() => world.CreateEntity());
                    Assert.Throws<InvalidOperationException>(() => world.Get<Position>(entities[0]));
                });
            })
            .AddSnapshotPreparation(SystemStage.PostUpdate, query, source =>
            {
                afterGroup++;
                Assert.Equal(129, source.Count);
                Assert.Equal(SystemStage.PostUpdate, source.Stage);
                Assert.Equal(1, source.SystemOrder);
                Assert.Equal(0, source.KernelOrder);
                for (int row = 0; row < source.Count; row++)
                {
                    int id = row < 7 ? row : row + 1;
                    Assert.Equal(entities[id], source.GetEntity(row));
                    Assert.Equal(new Position(id + 33), source.Read<Position>(row));
                }
            });
        using var runner = new DeterministicRunner<int>(world, workload, workerCount: 2);
        int input = 33;
        runner.BeginTick(19, input, step);
        input = -1;
        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(1));
        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(1));
        Assert.Equal(0, inGroup);
        Assert.Equal(0, afterGroup);

        Assert.True(runner.Advance(1) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        Assert.Equal(1, inGroup);
        Assert.Equal(1, afterGroup);
        Assert.False(world.IsAlive(entities[7]));
    }

    [Fact]
    public void EmptyPreparationRunsExactlyOncePerTick()
    {
        using var world = new World();
        world.CreateEntity(new Position(5));
        var observed = new List<(long Tick, byte Input)>();
        var workload = new DeterministicWorkload<byte>().AddSnapshotPreparation(SystemStage.PreUpdate,
            new DeterministicQuery().With<Absent>().Read<Absent>(), source =>
            {
                Assert.Equal(0, source.Count);
                Assert.Equal(SystemStage.PreUpdate, source.Stage);
                Assert.Equal(0, source.SystemOrder);
                Assert.Equal(0, source.KernelOrder);
                Assert.Equal(Fixed.Zero, source.Step);
                observed.Add((source.TickIndex, source.Input));
            });
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(10, 5, Fixed.Zero);
        Assert.True(runner.Advance(1) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(1));
        runner.BeginTick(12, 9, Fixed.Zero);
        Assert.True(runner.Advance(1) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        Assert.Equal(new (long, byte)[] { (10, 5), (12, 9) }, observed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueryMembershipAndWriteAccessDoNotGrantPreparationReadAccess(bool optionalRead)
    {
        using var world = new World();
        world.CreateEntity(new Position(10));
        var workload = new DeterministicWorkload<byte>().AddSnapshotPreparation(SystemStage.Update,
            new DeterministicQuery().With<Position>().Write<Position>(), source =>
            {
                if (optionalRead)
                    source.TryRead<Position>(0, out _);
                else
                    _ = source.Read<Position>(0);
            });
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(0, 0, Fixed.One);

        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(1));
        Assert.Contains("Read access", Assert.IsType<InvalidOperationException>(runner.Fault).Message);
        Assert.Null(runner.LastCompleted);
    }

    [Fact]
    public void PreparationFaultStopsGroupCommitAndKeepsThePreviousTick()
    {
        using var world = new World();
        var original = world.CreateEntity(new Position(10));
        var workload = new DeterministicWorkload<byte>();
        var reduction = workload.AddReduction(0, static (sum, leaf) => leaf == -1
            ? throw new InvalidOperationException("Group outputs committed too early.")
            : sum + leaf);
        var query = new DeterministicQuery().With<Position>().Read<Position>();
        var failure = new InvalidOperationException("Snapshot preparation failed.");
        int downstream = 0;
        workload.AddCommitGroup(SystemStage.Update, group =>
        {
            group.AddJob(new DeterministicQuery().Write<Position>(), new QueueBeforePreparation(reduction));
            group.AddSnapshotPreparation(query, source =>
            {
                Assert.Equal(1 + source.Input, source.Count);
                if (source.Input != 0)
                    throw failure;
            });
        });
        workload.AddSnapshotPreparation(SystemStage.PostUpdate, query, _ => downstream++);
        using var runner = new DeterministicRunner<byte>(world, workload);
        runner.BeginTick(0, 0, Fixed.One);
        Assert.True(runner.Advance(1) == DeterministicTickStatus.Completed, runner.Fault?.ToString());
        var completed = runner.LastCompleted!;
        Assert.Equal(5, completed.GetReduction(reduction));
        Assert.Equal(20, Assert.Single(completed.Events).Get<int>());
        Assert.Equal(1, downstream);
        Assert.Equal(new Position(77), world.Get<Position>(world.BindEntity(1, 1)));

        runner.BeginTick(1, 1, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Faulted, runner.Advance(1));
        Assert.Same(failure, runner.Fault);
        Assert.Same(completed, runner.LastCompleted);
        Assert.Equal(20, Assert.Single(completed.Events).Get<int>());
        Assert.Equal(5, completed.GetReduction(reduction));
        Assert.Equal(1, downstream);
        Assert.Throws<InvalidOperationException>(() => world.Get<Position>(original));
    }

    private readonly struct WriteAndDestroy : IDeterministicJobProcessor<int>
    {
        public void Execute(DeterministicJob<int> job)
        {
            for (int row = 0; row < job.Count; row++)
            {
                job.Write(row, new Position(job.Read<Position>(row).Value + job.Input));
                var entity = job.GetEntity(row);
                if (entity.Id == 7)
                    job.ForEntity(row).Destroy(entity);
            }
        }
    }

    private readonly struct QueueBeforePreparation(DeterministicReduction<int> reduction) : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            var source = job.ForJob();
            var ticket = source.Create();
            source.Add(ticket, new Position(77));
            source.Emit(job.Input == 0 ? 20 : 22);
            source.Contribute(reduction, job.Input == 0 ? 5 : -1);
        }
    }
}
