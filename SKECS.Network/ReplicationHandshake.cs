using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SKECS.Serialization;
using static SKECS.Network.ReplicationFrame;

namespace SKECS.Network;

internal readonly record struct ReplicationHandshake(Guid Session, int FrameBytes, int Slots, bool Delta)
{
    private const uint FullCapability = 1;
    private const uint DeltaCapability = 2;
    private const int HelloBytes = 93;

    internal static async ValueTask<ReplicationHandshake> ExchangeAsync(IReplicationTransport transport,
        ReplicationOptions options, CodecRegistry registry, CancellationToken cancellationToken)
    {
        var delivery = TransportDelivery.Reliable | TransportDelivery.Ordered;
        if ((transport.Delivery & delivery) != delivery)
            throw new ArgumentException("Replication requires reliable, ordered delivery.", nameof(transport));
        if (options.MaxFrameBytes is < MinimumFrameBytes or > MaximumFrameBytes ||
            transport.MaxFrameBytes < MinimumFrameBytes || options.MaxSlots <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid replication size limits.");
        if (options.LocalPeerId == Guid.Empty || options.ExpectedPeerId == Guid.Empty ||
            options.LocalPeerId == options.ExpectedPeerId || options.Role is < ReplicationRole.Authority or > ReplicationRole.Replica ||
            string.IsNullOrWhiteSpace(options.ContractId) || options.ContractId.Length > 1024)
            throw new ArgumentException("Invalid peer identity, role or application contract.", nameof(options));

        var maxBytes = Math.Min(options.MaxFrameBytes, transport.MaxFrameBytes);
        var schema = CheckpointSnapshot.SchemaFingerprint(registry);
        var contract = SHA256.HashData(new UTF8Encoding(false, true).GetBytes(options.ContractId));
        var authority = options.Role == ReplicationRole.Authority;
        var session = authority ? Guid.NewGuid() : Guid.Empty;
        var hello = BuildHello(options, session, maxBytes, schema, contract);

        if (!authority) await transport.SendAsync(hello, cancellationToken).ConfigureAwait(false);
        var incoming = await transport.ReceiveAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new EndOfStreamException("Peer disconnected during the replication handshake.");
        var peer = Parse(incoming, maxBytes);
        var (peerMax, peerSlots, peerDelta) = ValidateHello(peer, options, schema, contract);
        if (authority) await transport.SendAsync(hello, cancellationToken).ConfigureAwait(false);

        return new ReplicationHandshake(authority ? session : peer.Session, Math.Min(maxBytes, peerMax),
            Math.Min(options.MaxSlots, peerSlots), options.EnableDelta && peerDelta);
    }

    private static byte[] BuildHello(ReplicationOptions options, Guid session, int maxBytes,
        byte[] schema, byte[] contract)
    {
        var hello = Create(FrameKind.Hello, session, 0, 0, HelloBytes);
        var payload = hello.AsSpan(HeaderBytes);
        options.LocalPeerId.TryWriteBytes(payload[..16]);
        payload[16] = (byte)options.Role;
        BinaryPrimitives.WriteUInt32LittleEndian(payload[17..],
            FullCapability | (options.EnableDelta ? DeltaCapability : 0));
        BinaryPrimitives.WriteInt32LittleEndian(payload[21..], maxBytes);
        BinaryPrimitives.WriteInt32LittleEndian(payload[25..], options.MaxSlots);
        schema.CopyTo(payload[29..]);
        contract.CopyTo(payload[61..]);
        return hello;
    }

    private static (int MaxBytes, int Slots, bool Delta) ValidateHello(ReplicationFrame hello,
        ReplicationOptions options, byte[] schema, byte[] contract)
    {
        if (hello.Kind != FrameKind.Hello || hello.Sequence != 0 || hello.Tick != 0 ||
            hello.Payload.Length != HelloBytes)
            throw new InvalidDataException("Expected a replication hello.");
        var payload = hello.Payload;
        var authority = options.Role == ReplicationRole.Authority;
        var expectedRole = authority ? ReplicationRole.Replica : ReplicationRole.Authority;
        if (new Guid(payload[..16]) != options.ExpectedPeerId || payload[16] != (byte)expectedRole ||
            (hello.Session == Guid.Empty) != authority)
            throw new InvalidDataException("Unexpected replication peer, role or session identity.");
        if (!payload.Slice(29, DigestBytes).SequenceEqual(schema) || !payload[61..].SequenceEqual(contract))
            throw new InvalidDataException("The replication schema or application contract differs.");

        var capabilities = BinaryPrimitives.ReadUInt32LittleEndian(payload[17..]);
        var maxBytes = BinaryPrimitives.ReadInt32LittleEndian(payload[21..]);
        var slots = BinaryPrimitives.ReadInt32LittleEndian(payload[25..]);
        if ((capabilities & FullCapability) == 0 || maxBytes is < MinimumFrameBytes or > MaximumFrameBytes || slots <= 0)
            throw new InvalidDataException("Peer has incompatible replication capabilities or limits.");
        return (maxBytes, slots, (capabilities & DeltaCapability) != 0);
    }
}
