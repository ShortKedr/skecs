using System.Runtime.InteropServices;

namespace SKECS.Serialization;

/// <summary>A logical field value with its exact numeric bits.</summary>
public readonly struct FieldValue
{
    private readonly bool _bound;

    private FieldValue(FieldKind kind, ulong bits, byte[]? blob = null, bool bound = false)
    {
        Kind = kind;
        Bits = bits;
        Blob = blob;
        _bound = bound;
    }

    public FieldKind Kind { get; }
    internal ulong Bits { get; }
    internal byte[]? Blob { get; }

    public static FieldValue From(bool value) => new(FieldKind.Boolean, value ? 1u : 0u);
    public static FieldValue From(sbyte value) => new(FieldKind.Int8, unchecked((byte)value));
    public static FieldValue From(byte value) => new(FieldKind.UInt8, value);
    public static FieldValue From(short value) => new(FieldKind.Int16, unchecked((ushort)value));
    public static FieldValue From(ushort value) => new(FieldKind.UInt16, value);
    public static FieldValue From(int value) => new(FieldKind.Int32, unchecked((uint)value));
    public static FieldValue From(uint value) => new(FieldKind.UInt32, value);
    public static FieldValue From(long value) => new(FieldKind.Int64, unchecked((ulong)value));
    public static FieldValue From(ulong value) => new(FieldKind.UInt64, value);
    public static FieldValue From(float value) => new(FieldKind.Float32, new SingleBits { Value = value }.Bits);
    public static FieldValue From(double value) => new(FieldKind.Float64, new DoubleBits { Value = value }.Bits);
    public static FieldValue FromBytes(byte[] value) => new(FieldKind.Bytes, 0, value ?? throw new ArgumentNullException(nameof(value)));
    public static FieldValue FromEntity(int id, int version, bool bound) =>
        new(FieldKind.Entity, ((ulong)unchecked((uint)version) << 32) | unchecked((uint)id), bound: bound);

    internal static FieldValue FromBits(FieldKind kind, ulong bits) => new(kind, bits);

    public bool AsBoolean() => GetBits(FieldKind.Boolean) != 0;
    public sbyte AsInt8() => unchecked((sbyte)GetBits(FieldKind.Int8));
    public byte AsUInt8() => (byte)GetBits(FieldKind.UInt8);
    public short AsInt16() => unchecked((short)GetBits(FieldKind.Int16));
    public ushort AsUInt16() => (ushort)GetBits(FieldKind.UInt16);
    public int AsInt32() => unchecked((int)GetBits(FieldKind.Int32));
    public uint AsUInt32() => (uint)GetBits(FieldKind.UInt32);
    public long AsInt64() => unchecked((long)GetBits(FieldKind.Int64));
    public ulong AsUInt64() => GetBits(FieldKind.UInt64);
    public float AsFloat32() => new SingleBits { Bits = (uint)GetBits(FieldKind.Float32) }.Value;
    public double AsFloat64() => new DoubleBits { Bits = GetBits(FieldKind.Float64) }.Value;
    public byte[] AsBytes()
    {
        GetBits(FieldKind.Bytes);
        return Blob!;
    }

    public void AsEntity(out int id, out int version, out bool bound)
    {
        var bits = GetBits(FieldKind.Entity);
        id = unchecked((int)bits);
        version = unchecked((int)(bits >> 32));
        bound = _bound;
    }

    private ulong GetBits(FieldKind kind)
    {
        if (Kind != kind) throw new InvalidOperationException($"Expected {kind}, got {Kind}.");
        return Bits;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct SingleBits
    {
        [FieldOffset(0)] public float Value;
        [FieldOffset(0)] public uint Bits;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DoubleBits
    {
        [FieldOffset(0)] public double Value;
        [FieldOffset(0)] public ulong Bits;
    }
}
