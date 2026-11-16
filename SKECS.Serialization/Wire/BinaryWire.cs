using System;
using System.Buffers.Binary;
using System.IO;

namespace SKECS.Serialization;

internal sealed class BinaryWireWriter : WireWriter
{
    internal static readonly byte[] Magic = { (byte)'S', (byte)'K', (byte)'E', (byte)'C', (byte)'S', (byte)'C', (byte)'P', 0 };

    private readonly Stream _stream;
    private readonly int _maxFrameBytes;
    private readonly int _maxValueBytes;
    private readonly long _maxDocumentBytes;
    private readonly byte[] _header = new byte[5];
    private byte[] _frame = Array.Empty<byte>();
    private int _length;
    private int _columnStart;
    private long _written;

    internal BinaryWireWriter(Stream stream, CheckpointLimits limits)
    {
        _stream = stream;
        _maxFrameBytes = limits.MaxFrameBytes;
        _maxValueBytes = limits.MaxValueBytes;
        _maxDocumentBytes = limits.MaxDocumentBytes;
    }

    internal override void BeginFile() => Write(Magic, Magic.Length);

    internal override void BeginFrame(byte kind)
    {
        _header[0] = kind;
        _length = 0;
    }

    internal override void EndFrame()
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_header.AsSpan(1), (uint)_length);
        Write(_header, _header.Length);
        Write(_frame, _length);
    }

    internal override void BeginColumn()
    {
        _columnStart = _length;
        Reserve(sizeof(uint));
    }

    internal override void EndColumn() => BinaryPrimitives.WriteUInt32LittleEndian(
        _frame.AsSpan(_columnStart), (uint)(_length - _columnStart - sizeof(uint)));

    internal override void EndFile() { }

    internal override void WriteUInt32(uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(Reserve(sizeof(uint)), value);

    internal override void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));
    internal override void WriteBoolean(bool value) => Reserve(1)[0] = value ? (byte)1 : (byte)0;

    internal override void WriteBytes(byte[] value)
    {
        if (value.Length > _maxValueBytes)
            throw new InvalidDataException("Checkpoint byte value exceeds its limit.");

        WriteUInt32((uint)value.Length);
        value.AsSpan().CopyTo(Reserve(value.Length));
    }

    internal override void WriteField(FieldValue value)
    {
        switch (value.Kind)
        {
            case FieldKind.Boolean:
                WriteBoolean(value.AsBoolean());
                break;
            case FieldKind.Int8:
            case FieldKind.UInt8:
                Reserve(1)[0] = unchecked((byte)value.Bits);
                break;
            case FieldKind.Int16:
            case FieldKind.UInt16:
                BinaryPrimitives.WriteUInt16LittleEndian(Reserve(sizeof(ushort)), unchecked((ushort)value.Bits));
                break;
            case FieldKind.Int32:
            case FieldKind.UInt32:
            case FieldKind.Float32:
                WriteUInt32(unchecked((uint)value.Bits));
                break;
            case FieldKind.Int64:
            case FieldKind.UInt64:
            case FieldKind.Float64:
                BinaryPrimitives.WriteUInt64LittleEndian(Reserve(sizeof(ulong)), value.Bits);
                break;
            case FieldKind.Entity:
                value.AsEntity(out var id, out var version, out var bound);
                WriteInt32(id);
                WriteInt32(version);
                WriteBoolean(bound);
                break;
            case FieldKind.Bytes:
                WriteBytes(value.AsBytes());
                break;
            default:
                throw new InvalidDataException($"Unknown checkpoint field kind {value.Kind}.");
        }
    }

    private Span<byte> Reserve(int size)
    {
        if (size > _maxFrameBytes - _length)
            throw new InvalidDataException("Binary checkpoint frame exceeds its limit.");

        var start = _length;
        _length += size;
        if (_length > _frame.Length)
        {
            var suggested = Math.Max(_length, Math.Max(256L, _frame.Length * 2L));
            Array.Resize(ref _frame, (int)Math.Min(_maxFrameBytes, suggested));
        }

        return _frame.AsSpan(start, size);
    }

    private void Write(byte[] bytes, int count)
    {
        if (count > _maxDocumentBytes - _written)
            throw new InvalidDataException("Binary checkpoint exceeds the document limit.");

        _stream.Write(bytes, 0, count);
        _written += count;
    }

    public override void Dispose() { }
}

internal sealed class BinaryWireReader : WireReader
{
    private readonly Stream _stream;
    private readonly int _maxFrameBytes;
    private readonly int _maxValueBytes;
    private readonly long _maxDocumentBytes;
    private readonly byte[] _scratch = new byte[8];
    private byte[] _frame = Array.Empty<byte>();
    private long _consumed;
    private int _offset;
    private int _length;
    private int? _columnEnd;

    internal BinaryWireReader(Stream stream, CheckpointLimits limits)
    {
        _stream = stream;
        _maxFrameBytes = limits.MaxFrameBytes;
        _maxValueBytes = limits.MaxValueBytes;
        _maxDocumentBytes = limits.MaxDocumentBytes;
    }

    internal override void BeginFile()
    {
        ReadExactly(_scratch, BinaryWireWriter.Magic.Length);
        if (!_scratch.AsSpan(0, BinaryWireWriter.Magic.Length).SequenceEqual(BinaryWireWriter.Magic))
            throw new InvalidDataException("Unrecognized binary checkpoint signature.");
    }

    internal override byte BeginFrame()
    {
        ReadExactly(_scratch, 5);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(_scratch.AsSpan(1));
        if (size > _maxFrameBytes || size > _maxDocumentBytes - _consumed)
            throw new InvalidDataException("Binary checkpoint frame exceeds its limit.");

        _offset = 0;
        _length = (int)size;
        if (_length > _frame.Length)
        {
            var suggested = Math.Max(_length, Math.Max(256L, _frame.Length * 2L));
            Array.Resize(ref _frame, (int)Math.Min(_maxFrameBytes, suggested));
        }

        ReadExactly(_frame, _length);
        return _scratch[0];
    }

    internal override void EndFrame()
    {
        if (_offset != _length)
            throw new InvalidDataException("Unexpected bytes in the checkpoint frame.");
    }

    internal override void BeginColumn()
    {
        var size = ReadUInt32();
        if (size > _length - _offset)
            throw new InvalidDataException("Binary checkpoint column exceeds the enclosing frame.");

        _columnEnd = _offset + (int)size;
    }

    internal override void EndColumn()
    {
        if (_offset != _columnEnd)
            throw new InvalidDataException("Unexpected bytes in the checkpoint column.");

        _columnEnd = null;
    }

    internal override void EndFile()
    {
        if (_stream.ReadByte() != -1)
            throw new InvalidDataException("Unexpected bytes after the binary checkpoint.");
    }

    internal override uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadScalar(sizeof(uint)));
    internal override int ReadInt32() => unchecked((int)ReadUInt32());

    internal override bool ReadBoolean() => ReadScalar(1)[0] switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException("Invalid Boolean in the binary checkpoint.")
    };

    internal override byte[] ReadBytes()
    {
        var length = ReadUInt32();
        if (length > _maxValueBytes)
            throw new InvalidDataException("Checkpoint byte value exceeds its limit.");

        if (length == 0)
            return Array.Empty<byte>();

        return ReadScalar((int)length).ToArray();
    }

    internal override FieldValue ReadField(FieldKind kind) => kind switch
    {
        FieldKind.Boolean => FieldValue.From(ReadBoolean()),
        FieldKind.Int8 or FieldKind.UInt8 => FieldValue.FromBits(kind, ReadScalar(1)[0]),
        FieldKind.Int16 or FieldKind.UInt16 => FieldValue.FromBits(kind,
            BinaryPrimitives.ReadUInt16LittleEndian(ReadScalar(sizeof(ushort)))),
        FieldKind.Int32 or FieldKind.UInt32 or FieldKind.Float32 => FieldValue.FromBits(kind, ReadUInt32()),
        FieldKind.Int64 or FieldKind.UInt64 or FieldKind.Float64 => FieldValue.FromBits(kind,
            BinaryPrimitives.ReadUInt64LittleEndian(ReadScalar(sizeof(ulong)))),
        FieldKind.Entity => FieldValue.FromEntity(ReadInt32(), ReadInt32(), ReadBoolean()),
        FieldKind.Bytes => FieldValue.FromBytes(ReadBytes()),
        _ => throw new InvalidDataException($"Unknown checkpoint field kind {kind}.")
    };

    private ReadOnlySpan<byte> ReadScalar(int count)
    {
        if (count > (_columnEnd ?? _length) - _offset)
            throw new InvalidDataException("A binary checkpoint value extends beyond its declared payload.");

        var bytes = _frame.AsSpan(_offset, count);
        _offset += count;
        return bytes;
    }

    private void ReadExactly(byte[] buffer, int count)
    {
        if (count > _maxDocumentBytes - _consumed)
            throw new InvalidDataException("Binary checkpoint exceeds the document limit.");

        var offset = 0;
        while (offset < count)
        {
            var read = _stream.Read(buffer, offset, count - offset);
            if (read == 0)
                throw new InvalidDataException("Unexpected end of the binary checkpoint.");

            offset += read;
            _consumed += read;
        }
    }

    public override void Dispose() { }
}
