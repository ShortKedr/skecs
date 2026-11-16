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
            new DeterministicWorkload<byte>().AddBatchSystem(SystemStage.Update, query, processor));

        snapshot.Bind(1, Packet(-7));
        Assert.Throws<InvalidOperationException>(() => snapshot.Bind(1, Packet(500)));
        runner.BeginTick(1, 0, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Pending, runner.Advance(1));
        snapshot.Bind(2, Packet(19));
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(1));
        Assert.All(entities, entity => Assert.Equal(-7, world.Get<Position>(entity).X));

        runner.BeginTick(2, 0, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(2));
        Assert.All(entities, entity => Assert.Equal(19, world.Get<Position>(entity).X));
    }

    private static GaSnapshotPacket Packet(int value) =>
        GaSnapshotPacket.FromRows(new GaSnapshotLayout([GaScalar.Int32]), [new GaSnapshotKey(4, 2)], [unchecked((uint)value)]);

    private readonly record struct Position(int X);
}
