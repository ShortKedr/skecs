using System.Buffers.Binary;

namespace SKECS.Network;

internal enum FrameKind : byte
{
    Hello = 1,
    Full = 2,
    Delta = 3,
    Ack = 4,
    Resync = 5
}

internal readonly record struct ReplicationFrame(FrameKind Kind, Guid Session, ulong Sequence, ulong Tick, byte[] Bytes)
{
    internal const int HeaderBytes = 45;
    internal const int DigestBytes = 32;
    internal const int DeltaHeaderBytes = sizeof(ulong) + DigestBytes;
    internal const int MinimumFrameBytes = 256;
    internal const int MaximumFrameBytes = 64 * 1024 * 1024;

    private const uint Magic = 0x4E454B53; // SKEN
    private const ushort Major = 1;
    private const ushort Minor = 0;

    internal ReadOnlySpan<byte> Payload => Bytes.AsSpan(HeaderBytes);
    internal SnapshotNotice Notice => new((SnapshotEncoding)Kind, Sequence, Tick, Bytes.Length);

    internal static byte[] Create(FrameKind kind, Guid session, ulong sequence, ulong tick, int payloadLength)
    {
        var bytes = new byte[HeaderBytes + payloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), Major);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), Minor);
        bytes[8] = (byte)kind;
        session.TryWriteBytes(bytes.AsSpan(9, 16));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(25), sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(33), tick);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(41), payloadLength);
        return bytes;
    }

    internal static ReplicationFrame Parse(byte[] bytes, int maxBytes)
    {
        if (bytes.Length < HeaderBytes || bytes.Length > maxBytes)
            throw new InvalidDataException("Replication frame exceeds its size bounds.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)) != Major ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)) != Minor)
            throw new InvalidDataException("Unsupported replication protocol or version.");

        var kind = (FrameKind)bytes[8];
        if (kind is < FrameKind.Hello or > FrameKind.Resync ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(41)) != bytes.Length - HeaderBytes)
            throw new InvalidDataException("Invalid replication frame kind or length.");

        return new ReplicationFrame(kind, new Guid(bytes.AsSpan(9, 16)),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(25)),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(33)), bytes);
    }
}
