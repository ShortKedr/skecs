using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using SKECS.Network.Transport;
using SKECS.Serialization;

namespace SKECS.Network.Tests;

public sealed class ReplicationTests
{
    private const int TransportFrameBytes = 256 * 1024;
    private static readonly Guid AuthorityId = Guid.NewGuid();
    private static readonly Guid ReplicaId = Guid.NewGuid();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplicatesExactValuesAndRemapsEntityGenerations(bool tcp)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        var (authorityTransport, replicaTransport) = await CreateTransport(tcp, token);
        var (writer, reader) = await Connect(authorityTransport, replicaTransport, token);
        await using var authority = writer;
        await using var replica = reader;

        using var world = new World(new WorldOptions(chunkCapacity: 64));
        const int entityCount = 384;
        for (int i = 0; i < entityCount; i++) world.CreateEntity(new Position { X = i });
        var removing = world.CreateEntity(new Position { X = 100 });
        var victim = world.CreateEntity();
        var holder = world.CreateEntity(new Link { Target = victim });
        var exact = world.CreateEntity(new Position { X = 0f });

        var initial = await PublishAndApply(world, 0, authority, replica, token);
        Assert.Equal(SnapshotEncoding.Full, initial.Encoding);
        Assert.Equal(authority.SessionId, replica.SessionId);
        Assert.Equal(96, replica.Current!.World.ChunkCapacity);
        Assert.Equal(entityCount + 4, replica.Current.EntityCount);

        Assert.True(authority.TryGetNetworkEntityId(victim, out var victimId));
        Assert.True(authority.TryGetNetworkEntityId(holder, out var holderId));
        Assert.True(authority.TryGetNetworkEntityId(removing, out var removingId));
        Assert.True(authority.TryGetNetworkEntityId(exact, out var exactId));
        var first = replica.Current;
        Assert.True(first.TryResolve(holderId, out var firstHolder));
        Assert.True(first.TryResolve(victimId, out var firstVictim));
        Assert.Equal(firstVictim, first.World.Get<Link>(firstHolder).Target);
        Assert.NotEqual(victim, firstVictim);
        Assert.False(first.TryResolve(victimId with { SessionId = Guid.NewGuid() }, out _));
        Assert.Contains(victimId, first.GetEntityIds());

        world.Get<Position>(exact).X = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        world.Get<Position>(exact).Y = BitConverter.Int32BitsToSingle(0x7fc01234);
        var sent = await authority.PublishAsync(world, 1, token);
        Assert.Equal(SnapshotEncoding.Delta, sent.Snapshot.Encoding);
        Assert.True(sent.Snapshot.FrameBytes < initial.FrameBytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.PublishAsync(world, 2, token).AsTask());
        Assert.Equal(sent.Snapshot, await replica.ReceiveAsync(token));
        Assert.Same(first, replica.Current);
        await Assert.ThrowsAsync<InvalidOperationException>(() => replica.ConfirmAsync(token).AsTask());
        await Commit(authority, replica, SnapshotResult.Applied, token);

        var updated = replica.Current!;
        Assert.False(first.TryResolve(victimId, out _));
        Assert.True(updated.TryResolve(exactId, out var target));
        var value = updated.World.Get<Position>(target);
        Assert.Equal(unchecked((int)0x80000000), BitConverter.SingleToInt32Bits(value.X));
        Assert.Equal(0x7fc01234, BitConverter.SingleToInt32Bits(value.Y));

        world.Remove<Position>(removing);
        world.DestroyEntity(victim);
        var replacement = world.CreateEntity();
        Assert.Equal(victim.Id, replacement.Id);
        Assert.NotEqual(victim.Version, replacement.Version);
        // The link deliberately retains a stale reference and must never target the replacement.
        var removals = await PublishAndApply(world, 2, authority, replica, token);
        Assert.Equal(SnapshotEncoding.Delta, removals.Encoding);
        Assert.False(authority.TryGetNetworkEntityId(victim, out _));
        Assert.True(authority.TryGetNetworkEntityId(replacement, out var replacementId));
        Assert.NotEqual(victimId, replacementId);
        var last = replica.Current!;
        Assert.False(last.TryResolve(victimId, out _));
        Assert.True(last.TryResolve(replacementId, out var replacementLocal));
        Assert.True(last.TryResolve(holderId, out var lastHolder));
        Assert.True(last.TryResolve(removingId, out var lastRemoving));
        Assert.False(last.World.Has<Position>(lastRemoving));
        var stale = last.World.Get<Link>(lastHolder).Target;
        Assert.False(last.World.IsAlive(stale));
        Assert.NotEqual(replacementLocal, stale);
        Assert.Equal(victim.Version, stale.Version);
    }

    [Fact]
    public async Task MissingBaselineRequestsFullWithoutChangingCommittedWorld()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        var (a, b) = LoopbackTransport.CreatePair(TransportFrameBytes);
        var (writer, reader) = await Connect(a, b, token);
        await using var authority = writer;
        await using var replica = reader;
        using var world = new World();
        for (int i = 0; i < 32; i++) world.CreateEntity(new Position { X = i });
        var source = world.CreateEntity(new Position { X = 99 });

        await PublishAndApply(world, 0, authority, replica, token);
        var previous = replica.Current!;
        Assert.True(authority.TryGetNetworkEntityId(source, out var id));
        replica.ForgetBaseline();
        world.Get<Position>(source).X = 100;

        var sent = await authority.PublishAsync(world, 1, token);
        Assert.Equal(SnapshotEncoding.Delta, sent.Snapshot.Encoding);
        await replica.ReceiveAsync(token);
        await Commit(authority, replica, SnapshotResult.ResyncRequired, token);
        Assert.Same(previous, replica.Current);
        Assert.True(previous.TryResolve(id, out var unchanged));
        Assert.Equal(99f, previous.World.Get<Position>(unchanged).X);

        var recovered = await PublishAndApply(world, 1, authority, replica, token);
        Assert.Equal(SnapshotEncoding.Full, recovered.Encoding);
        Assert.Equal(3UL, recovered.Sequence);
        Assert.True(replica.Current!.TryResolve(id, out var changed));
        Assert.Equal(100f, replica.Current.World.Get<Position>(changed).X);
        Assert.Equal(SnapshotEncoding.Delta,
            (await PublishAndApply(world, 2, authority, replica, token)).Encoding);
    }

    [Fact]
    public async Task DeltaDisablesOnEitherPeerAndSlotsAreNegotiated()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        var (a, b) = LoopbackTransport.CreatePair(TransportFrameBytes);
        var (writer, reader) = await Connect(a, b, token, replicaOptions: ReplicaOptions with
        {
            EnableDelta = false,
            MaxFrameBytes = 4096,
            MaxSlots = 2
        });
        await using var authority = writer;
        await using var replica = reader;
        Assert.False(authority.DeltaEnabled);
        Assert.False(replica.DeltaEnabled);
        Assert.Equal(4096, authority.MaxFrameBytes);

        using var tooLarge = new World();
        for (int i = 0; i < 3; i++) tooLarge.CreateEntity();
        await Assert.ThrowsAsync<InvalidDataException>(() => authority.PublishAsync(tooLarge, 0, token).AsTask());
        using var world = new World();
        world.CreateEntity();
        Assert.Equal(SnapshotEncoding.Full, (await PublishAndApply(world, 0, authority, replica, token)).Encoding);
        Assert.Equal(SnapshotEncoding.Full, (await PublishAndApply(world, 1, authority, replica, token)).Encoding);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => authority.PublishAsync(world, 1, token).AsTask());
    }

    [Theory]
    [InlineData("protocol")]
    [InlineData("schema")]
    [InlineData("contract")]
    [InlineData("identity")]
    public async Task RejectsIncompatibleHello(string kind)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        var (a, b) = LoopbackTransport.CreatePair(TransportFrameBytes);
        var capture = new InspectTransport(b);
        if (kind == "protocol") capture.Transform = bytes => bytes[4]++;
        var options = kind switch
        {
            "identity" => ReplicaOptions with { LocalPeerId = Guid.NewGuid() },
            "contract" => ReplicaOptions with { ContractId = "movers.v2" },
            _ => ReplicaOptions
        };

        var writer = ReplicationSession.ConnectAsync(a, AuthorityOptions, Registry(), token).AsTask();
        var reader = ReplicationSession.ConnectAsync(capture, options,
            Registry(kind == "schema" ? 2u : 1u), token).AsTask();
        await Assert.ThrowsAsync<InvalidDataException>(() => writer);
        await Assert.ThrowsAsync<EndOfStreamException>(() => reader);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("delta")]
    [InlineData("sequence")]
    [InlineData("replay")]
    [InlineData("tick")]
    [InlineData("session")]
    [InlineData("kind")]
    public async Task InvalidOnWireSnapshotLeavesReplicaUntouched(string kind)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        var (a, b) = LoopbackTransport.CreatePair(TransportFrameBytes);
        var capture = new InspectTransport(a);
        var (writer, reader) = await Connect(capture, b, token);
        await using var authority = writer;
        await using var replica = reader;
        using var world = new World();
        world.CreateEntity(new Position { X = 8 });
        await PublishAndApply(world, 0, authority, replica, token);
        var old = replica.Current!;
        capture.Transform = bytes =>
        {
            const int kindOffset = 8, sessionOffset = 9, sequenceOffset = 25, tickOffset = 33, digestOffset = 45;
            if (kind == "delta")
            {
                const int deltaTargetLengthOffset = digestOffset + 32 + sizeof(ulong) + 32;
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(deltaTargetLengthOffset), int.MaxValue);
                return;
            }
            if (kind == "replay")
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(sequenceOffset), 1);
                return;
            }
            if (kind == "kind")
            {
                bytes[kindOffset] = byte.MaxValue;
                return;
            }
            var offset = kind switch
            {
                "session" => sessionOffset,
                "sequence" => sequenceOffset,
                "tick" => tickOffset,
                _ => digestOffset
            };
            bytes[offset] ^= 1;
        };
        var sent = await authority.PublishAsync(world, 1, token);
        Assert.Equal(SnapshotEncoding.Delta, sent.Snapshot.Encoding);
        if (kind is "digest" or "delta")
        {
            await replica.ReceiveAsync(token);
            await Assert.ThrowsAsync<InvalidDataException>(() => replica.ApplyAtBoundaryAsync(token).AsTask());
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => replica.ReceiveAsync(token).AsTask());
        }
        Assert.Same(old, replica.Current);
        await Assert.ThrowsAsync<EndOfStreamException>(() => authority.WaitForAcknowledgementAsync(token).AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignCheckpointSchemaCannotMigrateOrReplaceCommittedReplica(bool structural)
    {
        const int headerBytes = 45;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        using var world = new World();
        var entity = world.CreateEntity(new Position { X = 8 });

        // Capture a valid full payload from a session whose schema differs only by one component version.
        var (foreignA, foreignB) = LoopbackTransport.CreatePair(TransportFrameBytes);
        byte[]? foreignPayload = null;
        var foreignPath = new InspectTransport(foreignA);
        var (foreignWriter, foreignReader) = await Connect(foreignPath, foreignB, token);
        await using var foreignAuthority = foreignWriter;
        await using var foreignReplica = foreignReader;
        foreignPath.Transform = bytes => foreignPayload = bytes.AsSpan(headerBytes).ToArray();
        await foreignAuthority.PublishAsync(world, 0, token);
        Assert.NotNull(foreignPayload);

        var (a, b) = LoopbackTransport.CreatePair(TransportFrameBytes);
        var path = new InspectTransport(a);
        var destination = Registry(2);
        var calls = 0;
        if (structural)
            destination.MigrateCheckpoint(Registry().GetFingerprint(), destination.GetFingerprint(), _ => calls++);
        else
            destination.Migrate(1, 1, 2, _ => calls++);

        var (writer, reader) = await Connect(path, b, token,
            ReplicaOptions with { EnableDelta = false }, Registry(2), destination);
        await using var authority = writer;
        await using var replica = reader;
        await PublishAndApply(world, 0, authority, replica, token);
        var previous = replica.Current!;
        Assert.True(authority.TryGetNetworkEntityId(entity, out var id));

        world.Get<Position>(entity).X = 9;
        path.Transform = bytes =>
        {
            Assert.Equal(bytes.Length - headerBytes, foreignPayload.Length);
            foreignPayload.CopyTo(bytes, headerBytes);
        };
        await authority.PublishAsync(world, 1, token);
        await replica.ReceiveAsync(token);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => replica.ApplyAtBoundaryAsync(token).AsTask());
        Assert.Contains("does not match the codec registry", failure.Message);
        Assert.Equal(0, calls);
        Assert.Same(previous, replica.Current);
        Assert.True(previous.TryResolve(id, out var local));
        Assert.Equal(8f, previous.World.Get<Position>(local).X);
        await Assert.ThrowsAsync<EndOfStreamException>(() => authority.WaitForAcknowledgementAsync(token).AsTask());
    }

    [Fact]
    public async Task InvalidAcknowledgementCannotAdvanceAuthority()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        var (a, b) = LoopbackTransport.CreatePair(TransportFrameBytes);
        var capture = new InspectTransport(b);
        var (writer, reader) = await Connect(a, capture, token);
        await using var authority = writer;
        await using var replica = reader;
        using var world = new World();
        world.CreateEntity();
        await authority.PublishAsync(world, 0, token);
        await replica.ReceiveAsync(token);
        Assert.Equal(SnapshotResult.Applied, await replica.ApplyAtBoundaryAsync(token));
        capture.Transform = frame => frame[^1] ^= 1;
        await replica.ConfirmAsync(token);
        await Assert.ThrowsAsync<InvalidDataException>(() => authority.WaitForAcknowledgementAsync(token).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => authority.PublishAsync(world, 1, token).AsTask());
    }

    [Fact]
    public async Task CancellingAnActiveReadDisconnectsPeer()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var readCancellation = new CancellationTokenSource();
        var token = deadline.Token;
        var (a, b) = LoopbackTransport.CreatePair(TransportFrameBytes);
        var (writer, reader) = await Connect(a, b, token);
        await using var authority = writer;
        await using var replica = reader;
        var waiting = replica.ReceiveAsync(readCancellation.Token).AsTask();
        await Assert.ThrowsAsync<InvalidOperationException>(() => replica.ReceiveAsync(token).AsTask());
        readCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        using var world = new World();
        await Assert.ThrowsAsync<IOException>(() => authority.PublishAsync(world, 0, token).AsTask());
    }

    private static async Task<SnapshotNotice> PublishAndApply(World world, ulong tick, ReplicationSession authority,
        ReplicationSession replica, CancellationToken token)
    {
        var sending = authority.PublishAsync(world, tick, token).AsTask();
        var incoming = await replica.ReceiveAsync(token);
        Assert.Equal((await sending).Snapshot, incoming);
        await Commit(authority, replica, SnapshotResult.Applied, token);
        return incoming;
    }

    private static async Task Commit(ReplicationSession authority, ReplicationSession replica,
        SnapshotResult expect, CancellationToken token)
    {
        Assert.Equal(expect, await replica.ApplyAtBoundaryAsync(token));
        await replica.ConfirmAsync(token);
        Assert.Equal(expect, await authority.WaitForAcknowledgementAsync(token));
    }

    private static async Task<(ReplicationSession, ReplicationSession)> Connect(IReplicationTransport a,
        IReplicationTransport b, CancellationToken token, ReplicationOptions? replicaOptions = null,
        CodecRegistry? authorityRegistry = null, CodecRegistry? replicaRegistry = null)
    {
        var authority = ReplicationSession.ConnectAsync(a, AuthorityOptions, authorityRegistry ?? Registry(), token).AsTask();
        var replica = ReplicationSession.ConnectAsync(b, replicaOptions ?? ReplicaOptions, replicaRegistry ?? Registry(), token).AsTask();
        await Task.WhenAll(authority, replica);
        return (await authority, await replica);
    }

    private static ReplicationOptions AuthorityOptions => new(AuthorityId, ReplicaId, ReplicationRole.Authority, "movers.v1");
    private static ReplicationOptions ReplicaOptions => new(ReplicaId, AuthorityId, ReplicationRole.Replica, "movers.v1")
    {
        ReplicaWorldOptions = new WorldOptions(chunkCapacity: 96)
    };

    internal static async Task<(IReplicationTransport, IReplicationTransport)> CreateTransport(bool tcp, CancellationToken token)
    {
        if (!tcp) return LoopbackTransport.CreatePair(TransportFrameBytes);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, token);
        var server = await listener.AcceptTcpClientAsync(token);
        server.NoDelay = true;
        return (new TcpReplicationTransport(server, TransportFrameBytes), new TcpReplicationTransport(client, TransportFrameBytes));
    }

    private static CodecRegistry Registry(uint version = 1) => new CodecRegistry()
        .Register(new ComponentSchema(1, version,
            new FieldSchema(1, FieldKind.Float32), new FieldSchema(2, FieldKind.Float32)), new PositionCodec())
        .Register(new ComponentSchema(2, 1, new FieldSchema(1, FieldKind.Entity)), new LinkCodec());

    private struct Position { public float X, Y; }
    private struct Link { public Entity Target; }

    private sealed class PositionCodec : IComponentCodec<Position>
    {
        public void Write(in Position value, ComponentWriter fields)
        {
            fields.Set(1, FieldValue.From(value.X));
            fields.Set(2, FieldValue.From(value.Y));
        }

        public Position Read(ComponentReader fields) => new()
        {
            X = fields.Get(1).AsFloat32(),
            Y = fields.Get(2).AsFloat32()
        };
    }

    private sealed class LinkCodec : IComponentCodec<Link>
    {
        public void Write(in Link value, ComponentWriter fields) => fields.SetEntity(1, value.Target);
        public Link Read(ComponentReader fields) => new() { Target = fields.GetEntity(1) };
    }

    private sealed class InspectTransport(IReplicationTransport peer) : IReplicationTransport
    {
        public Action<byte[]>? Transform { get; set; }
        public TransportDelivery Delivery => peer.Delivery;
        public int MaxFrameBytes => peer.MaxFrameBytes;
        public ValueTask<byte[]?> ReceiveAsync(CancellationToken cancellationToken) => peer.ReceiveAsync(cancellationToken);

        public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
        {
            var bytes = frame.ToArray();
            Transform?.Invoke(bytes);
            return peer.SendAsync(bytes, cancellationToken);
        }

        public ValueTask DisposeAsync() => peer.DisposeAsync();
    }
}
