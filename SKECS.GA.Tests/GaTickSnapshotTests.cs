using SKECS.Numerics;

namespace SKECS.GA.Tests;

public sealed class GaTickSnapshotTests
{
    [Fact]
    public void LaterBindingDoesNotReplaceSnapshotBetweenBatches()
    {
        using var world = new World();
        var entities = Enumerable.Range(0, 65).Select(_ => world.CreateEntity(new Position(0))).ToArray();
        var snapshot = new GaTickSnapshot();
        var processor = new GaSnapshotCpuBatch<byte>(snapshot, static (job, packet) =>
        {
            for (int row = 0; row < job.Count; row++)
                job.Write(row, new Position(packet.ReadInt32(4, 2, 999)));
        });
        var query = new DeterministicQuery().With<Position>().ReadWrite<Position>();
        using var runner = new DeterministicRunner<byte>(world,
            new DeterministicWorkload<byte>()
                .AddBatchSystem(SystemStage.Update, query, processor)
                .AddBatchSystem(SystemStage.PostUpdate, query, processor));

        snapshot.Bind(1, Packet(-7));
        Assert.Throws<InvalidOperationException>(() => snapshot.Bind(1, Packet(500)));
        runner.BeginTick(1, 0, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(1));
        snapshot.Bind(2, Packet(19));
        Assert.Equal(DeterministicTickStatus.Completed, Drain(runner));
        Assert.All(entities, entity => Assert.Equal(-7, world.Get<Position>(entity).X));

        runner.BeginTick(2, 0, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Completed, Drain(runner));
        Assert.All(entities, entity => Assert.Equal(19, world.Get<Position>(entity).X));
    }

    [Fact]
    public void PreparationRefreshesBetweenGroupKernelsAndCapturesEmptyMembershipAfterCommit()
    {
        using var world = new World();
        var target = world.CreateEntity(new SourceMarker(), new Signal(-7));
        var missing = world.CreateEntity(new SourceMarker());
        world.CreateEntity(new Signal(300));
        var observers = Enumerable.Range(0, 65).Select(_ => world.CreateEntity(new Position(0))).ToArray();
        var snapshot = new GaTickSnapshot();
        var prepare = snapshot.PrepareInt32<byte, Signal>(static (in Signal signal, Span<uint> words) =>
            words[0] = unchecked((uint)signal.Value));
        var sourceQuery = new DeterministicQuery().With<SourceMarker>().Read<Signal>();
        var writeQuery = new DeterministicQuery().With<SourceMarker>().With<Signal>().ReadWrite<Signal>();
        var observeQuery = new DeterministicQuery().With<Position>();
        var observer = new GaSnapshotCpuBatch<byte>(snapshot, (job, packet) =>
        {
            var observation = new Observation(packet.RowCount,
                packet.ReadInt32(target.Id, target.Version, 999),
                packet.ReadInt32(missing.Id, missing.Version, 999),
                packet.ReadInt32(target.Id, target.Version + 1, 999));
            for (int row = 0; row < job.Count; row++)
                job.ForEntity(row).Emit(observation);
        });
        var workload = new DeterministicWorkload<byte>()
            .AddCommitGroup(SystemStage.Update, group => group
                .AddSnapshotPreparation(sourceQuery, prepare)
                .AddBatchSystem(observeQuery, observer)
                .AddSystem(writeQuery, new UpdateSignal())
                .AddSnapshotPreparation(sourceQuery, prepare)
                .AddBatchSystem(observeQuery, observer))
            .AddSnapshotPreparation(SystemStage.PostUpdate, sourceQuery, prepare)
            .AddBatchSystem(SystemStage.PostUpdate, observeQuery, observer);
        using var runner = new DeterministicRunner<byte>(world, workload);

        runner.BeginTick(1, 0, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(1));
        snapshot.Bind(2, Packet(19));
        Assert.Equal(DeterministicTickStatus.Completed, Drain(runner));
        Assert.False(world.Has<Signal>(target));

        var events = runner.LastCompleted!.Events;
        Assert.Equal(observers.Length * 3, events.Count);
        VerifyEpoch(0, SystemStage.Update, 1, new Observation(1, -7, 999, 999));
        VerifyEpoch(1, SystemStage.Update, 4, new Observation(1, 12, 999, 999));
        VerifyEpoch(2, SystemStage.PostUpdate, 0, new Observation(0, 999, 999, 999));

        void VerifyEpoch(int epoch, SystemStage stage, int producer, Observation expected)
        {
            for (int row = 0; row < observers.Length; row++)
            {
                var item = events[epoch * observers.Length + row];
                Assert.Equal(stage, item.Key.Stage);
                Assert.Equal(producer, item.Key.ProducerOrder);
                Assert.Equal(observers[row].Id, item.Key.SourceId);
                Assert.Equal(expected, item.Get<Observation>());
            }
        }
    }

    [Fact]
    public void ConsumerBeforePreparationFaultsWithNoSnapshot()
    {
        using var world = new World();
        world.CreateEntity(new Position(0));
        var snapshot = new GaTickSnapshot();
        var query = new DeterministicQuery().With<Position>().Read<Position>();
        var observer = new GaSnapshotCpuBatch<byte>(snapshot, static (_, _) => { });
        var prepare = snapshot.PrepareInt32<byte, Position>(static (in Position value, Span<uint> words) =>
            words[0] = unchecked((uint)value.X));
        var workload = new DeterministicWorkload<byte>().AddCommitGroup(SystemStage.Update, group => group
            .AddBatchSystem(query, observer)
            .AddSnapshotPreparation(query, prepare));
        using var runner = new DeterministicRunner<byte>(world, workload);

        runner.BeginTick(1, 0, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Faulted, Drain(runner));
        Assert.Contains("no capture before tick 1", Assert.IsType<InvalidOperationException>(runner.Fault).Message);
        Assert.Null(runner.LastCompleted);
    }

    private static DeterministicTickStatus Drain(DeterministicRunner<byte> runner)
    {
        DeterministicTickStatus status;
        do { status = runner.Advance(1); } while (status == DeterministicTickStatus.Pending);
        return status;
    }

    private static GaSnapshotPacket Packet(int value) =>
        GaSnapshotPacket.FromRows(new GaSnapshotLayout([GaScalar.Int32]), [new GaSnapshotKey(4, 2)], [unchecked((uint)value)]);

    private readonly record struct Position(int X);
    private readonly record struct Signal(int Value);
    private readonly record struct Observation(int Count, int Value, int Missing, int Stale);
    private readonly struct SourceMarker { }

    private readonly struct UpdateSignal : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            for (int row = 0; row < job.Count; row++)
            {
                job.Write(row, new Signal(12));
                job.ForEntity(row).Remove<Signal>(job.GetEntity(row));
            }
        }
    }
}
