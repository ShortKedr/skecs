using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SKECS.Serialization;

internal static class CheckpointFormat
{
    internal const byte End = 0, Header = 1, Schema = 2, Slots = 3, Free = 4, Component = 5, Extension = 6;
    internal const uint Major = 1, Minor = 0, Capabilities = 0, Allocator = 1;
    internal const int RowsPerBlock = 256;
    internal const int EstimatedValueBytes = 32;

    internal static byte[] Fingerprint(IEnumerable<ComponentSchema> schemas)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var data = new byte[12];
        foreach (var schema in schemas)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data, schema.Id);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), schema.Version);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)schema.Fields.Count);
            hash.AppendData(data);
            foreach (var field in schema.Fields)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data, field.Tag);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)field.Kind);
                data[8] = field.Optional ? (byte)1 : (byte)0;
                data[9] = field.Retired ? (byte)1 : (byte)0;
                hash.AppendData(data, 0, 10);
            }
        }
        return hash.GetHashAndReset();
    }

    internal static void CheckLimit(long value, long maximum, string item)
    {
        if (value < 0 || value > maximum)
            throw new InvalidDataException($"Checkpoint {item} exceeds its limit ({maximum}).");
    }

    internal static int Count(WireReader reader, int maximum, string item)
    {
        var value = reader.ReadUInt32();
        CheckLimit(value, maximum, item);
        return (int)value;
    }

    internal static int NonEmptyCount(WireReader reader, int maximum, string item)
    {
        var count = Count(reader, maximum, item);
        if (count == 0) throw new InvalidDataException($"Empty checkpoint {item} block.");
        return count;
    }

    internal static void Begin(WireReader reader, byte expected)
    {
        var actual = reader.BeginFrame();
        if (actual != expected) throw new InvalidDataException($"Expected checkpoint frame {expected}, got {actual}.");
    }

    internal static FieldKind ReadKind(WireReader reader)
    {
        var kind = reader.ReadUInt32();
        if (kind < (uint)FieldKind.Boolean || kind > (uint)FieldKind.Bytes)
            throw new InvalidDataException($"Unknown field wire type {kind}.");
        return (FieldKind)kind;
    }
}
