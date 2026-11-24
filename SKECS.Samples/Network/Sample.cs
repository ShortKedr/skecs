using SKECS;
using SKECS.Network;
using SKECS.Network.Transport;
using SKECS.Serialization;

namespace Skecs.Samples.Network;

internal static class Sample
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 0)
            throw new ArgumentException("The network sample takes no arguments.");

        const int frameLimit = 64 * 1024;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        var (authorityPath, replicaPath) = LoopbackTransport.CreatePair(frameLimit);
        await using var authorityTransport = authorityPath;
        await using var replicaTransport = replicaPath;

        var authorityId = Guid.NewGuid();
        var replicaId = Guid.NewGuid();
        var authorityOptions = new ReplicationOptions(authorityId, replicaId, ReplicationRole.Authority, "sample-movement-v1")
        {
            MaxFrameBytes = frameLimit,
            MaxSlots = 1,
        };
        var replicaOptions = authorityOptions with
        {
            LocalPeerId = replicaId,
            ExpectedPeerId = authorityId,
            Role = ReplicationRole.Replica,
        };

        // Both ends participate in the handshake.
        var authorityConnection = ReplicationSession.ConnectAsync(authorityTransport, authorityOptions, CreateRegistry(), token);
        var replicaConnection = ReplicationSession.ConnectAsync(replicaTransport, replicaOptions, CreateRegistry(), token);
        await using var authority = await authorityConnection;
        await using var replica = await replicaConnection;

        using var world = new World();
        var entity = world.CreateEntity(new Position(0, 0));
        var networkId = await ReplicateAsync(0, SnapshotEncoding.Full, new Position(0, 0));

        world.Get<Position>(entity) = new Position(3, 1);
        if (await ReplicateAsync(1, SnapshotEncoding.Delta, new Position(3, 1)) != networkId)
            throw new InvalidOperationException("The moving entity changed its network identity.");

        async Task<NetworkEntityId> ReplicateAsync(ulong tick, SnapshotEncoding encoding, Position expected)
        {
            var sent = await authority.PublishAsync(world, tick, token);
            var received = await replica.ReceiveAsync(token);
            if (received != sent.Snapshot || received.Encoding != encoding)
                throw new InvalidOperationException($"Expected a {encoding} snapshot for tick {tick}.");

            if (await replica.ApplyAtBoundaryAsync(token) != SnapshotResult.Applied)
                throw new InvalidOperationException($"The replica could not apply tick {tick}.");

            await replica.ConfirmAsync(token);
            if (await authority.WaitForAcknowledgementAsync(token) != SnapshotResult.Applied)
                throw new InvalidOperationException($"The authority could not acknowledge tick {tick}.");

            // Resolve the same network ID against each newly applied replica world.
            var state = replica.Current;
            if (!authority.TryGetNetworkEntityId(entity, out var id) ||
                state is null || state.Tick != tick || state.EntityCount != 1 ||
                !state.TryResolve(id, out var local) || state.World.Get<Position>(local) != expected)
                throw new InvalidOperationException($"The replica has unexpected entity state at tick {tick}.");

            Console.WriteLine($"Tick {tick}: {received.Encoding} snapshot, replica position ({expected.X}, {expected.Y}).");
            return id;
        }
    }

    private static CodecRegistry CreateRegistry() => new CodecRegistry().Register(
        new ComponentSchema(1, 1, new FieldSchema(1, FieldKind.Int32), new FieldSchema(2, FieldKind.Int32)),
        new PositionCodec());

    private readonly record struct Position(int X, int Y);

    private sealed class PositionCodec : IComponentCodec<Position>
    {
        public void Write(in Position value, ComponentWriter fields)
        {
            fields.Set(1, FieldValue.From(value.X));
            fields.Set(2, FieldValue.From(value.Y));
        }

        public Position Read(ComponentReader fields) =>
            new(fields.Get(1).AsInt32(), fields.Get(2).AsInt32());
    }
}
