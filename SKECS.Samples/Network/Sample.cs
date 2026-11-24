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
        {
            throw new ArgumentException("The network sample takes no arguments.");
        }

        const int frameLimit = 64 * 1024;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = timeout.Token;
        var (authorityEndpoint, replicaEndpoint) = LoopbackTransport.CreatePair(frameLimit);
        await using var authorityTransport = authorityEndpoint;
        await using var replicaTransport = replicaEndpoint;

        var authorityId = Guid.NewGuid();
        var replicaId = Guid.NewGuid();
        var authorityOptions = new ReplicationOptions(
            authorityId,
            replicaId,
            ReplicationRole.Authority,
            "sample-movement-v1")
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

        // Start both sides of the handshake before awaiting either session.
        var authorityConnection = ReplicationSession.ConnectAsync(
            authorityTransport, authorityOptions, CreateRegistry(), cancellationToken);
        var replicaConnection = ReplicationSession.ConnectAsync(
            replicaTransport, replicaOptions, CreateRegistry(), cancellationToken);
        await using var authority = await authorityConnection;
        await using var replica = await replicaConnection;

        using var sourceWorld = new World();
        var initialPosition = new Position(0, 0);
        var entity = sourceWorld.CreateEntity(initialPosition);
        var originalNetworkId = await ReplicateAsync(0, SnapshotEncoding.Full, initialPosition);

        var movedPosition = new Position(3, 1);
        sourceWorld.Get<Position>(entity) = movedPosition;
        var updatedNetworkId = await ReplicateAsync(1, SnapshotEncoding.Delta, movedPosition);
        if (updatedNetworkId != originalNetworkId)
        {
            throw new InvalidOperationException("The moving entity changed its network identity.");
        }

        async Task<NetworkEntityId> ReplicateAsync(ulong tick, SnapshotEncoding encoding, Position expected)
        {
            var sent = await authority.PublishAsync(sourceWorld, tick, cancellationToken);
            var received = await replica.ReceiveAsync(cancellationToken);
            if (received != sent.Snapshot || received.Encoding != encoding)
            {
                throw new InvalidOperationException($"Expected a {encoding} snapshot for tick {tick}.");
            }

            // The replica commits the staged snapshot at its simulation boundary.
            if (await replica.ApplyAtBoundaryAsync(cancellationToken) != SnapshotResult.Applied)
            {
                throw new InvalidOperationException($"The replica could not apply tick {tick}.");
            }

            await replica.ConfirmAsync(cancellationToken);
            if (await authority.WaitForAcknowledgementAsync(cancellationToken) != SnapshotResult.Applied)
            {
                throw new InvalidOperationException($"The authority could not acknowledge tick {tick}.");
            }

            // Resolve the authority's network ID to a local entity in the replica.
            var replicaState = replica.Current;
            if (replicaState is null || replicaState.Tick != tick || replicaState.EntityCount != 1 ||
                !authority.TryGetNetworkEntityId(entity, out var networkId) ||
                !replicaState.TryResolve(networkId, out var replicaEntity) ||
                replicaState.World.Get<Position>(replicaEntity) != expected)
            {
                throw new InvalidOperationException($"The replica has unexpected entity state at tick {tick}.");
            }

            Console.WriteLine(
                $"Tick {tick}: {received.Encoding} snapshot, replica position ({expected.X}, {expected.Y}).");
            return networkId;
        }
    }

    private static CodecRegistry CreateRegistry()
    {
        var positionSchema = new ComponentSchema(
            1,
            1,
            new FieldSchema(1, FieldKind.Int32),
            new FieldSchema(2, FieldKind.Int32));

        return new CodecRegistry().Register(positionSchema, new PositionCodec());
    }

    private readonly record struct Position(int X, int Y);

    private sealed class PositionCodec : IComponentCodec<Position>
    {
        public void Write(in Position value, ComponentWriter fields)
        {
            fields.Set(1, FieldValue.From(value.X));
            fields.Set(2, FieldValue.From(value.Y));
        }

        public Position Read(ComponentReader fields)
        {
            var x = fields.Get(1).AsInt32();
            var y = fields.Get(2).AsInt32();
            return new Position(x, y);
        }
    }
}
