using System;
using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace SKECS.Serialization;

internal static class JsonFrame
{
    internal static readonly string[] Names = { "end", "header", "schema", "slots", "free", "component", "extension" };
}

internal sealed class JsonWireWriter : WireWriter
{
    private readonly Stream _stream;
    private readonly FrameBuffer _buffer;
    private readonly Utf8JsonWriter _json;
    private readonly int _maxFrameBytes;
    private readonly int _maxValueBytes;
    private readonly long _maxDocumentBytes;
    private readonly int _maxDepth;
    private long _frameStart = -1;

    internal JsonWireWriter(Stream stream, CheckpointLimits limits)
    {
        _stream = stream;
        _buffer = new FrameBuffer(limits.MaxFrameBytes);
        _json = new Utf8JsonWriter(_buffer, new JsonWriterOptions { MaxDepth = limits.MaxDepth });
        _maxFrameBytes = limits.MaxFrameBytes;
        _maxValueBytes = limits.MaxValueBytes;
        _maxDocumentBytes = limits.MaxDocumentBytes;
        _maxDepth = limits.MaxDepth;
    }

    private long Position => _json.BytesCommitted + _json.BytesPending;

    internal override void BeginFile() => StartArray();

    internal override void BeginFrame(byte kind)
    {
        StartArray();
        _frameStart = Position - 1;
        _json.WriteStringValue(JsonFrame.Names[kind]);
        CheckSize();
    }

    internal override void EndFrame()
    {
        _json.WriteEndArray();
        CheckSize();
        _frameStart = -1;
        WritePendingBytes();
    }

    internal override void BeginColumn() => StartArray();

    internal override void EndColumn()
    {
        _json.WriteEndArray();
        CheckSize();
    }

    internal override void EndFile()
    {
        _json.WriteEndArray();
        CheckSize();
        WritePendingBytes();
    }

    internal override void WriteUInt32(uint value)
    {
        _json.WriteNumberValue(value);
        CheckSize();
    }

    internal override void WriteInt32(int value)
    {
        _json.WriteNumberValue(value);
        CheckSize();
    }

    internal override void WriteBoolean(bool value)
    {
        _json.WriteBooleanValue(value);
        CheckSize();
    }

    internal override void WriteBytes(byte[] value)
    {
        if (value.Length > _maxValueBytes)
            throw new InvalidDataException("Checkpoint byte value exceeds its limit.");

        var encodedSize = ((value.Length + 2L) / 3 * 4) + 2;
        CheckSize(encodedSize);
        _json.WriteBase64StringValue(value);
        CheckSize();
    }

    internal override void WriteField(FieldValue value)
    {
        switch (value.Kind)
        {
            case FieldKind.Boolean:
                _json.WriteBooleanValue(value.AsBoolean());
                break;
            case FieldKind.Int8:
                _json.WriteNumberValue(value.AsInt8());
                break;
            case FieldKind.UInt8:
                _json.WriteNumberValue(value.AsUInt8());
                break;
            case FieldKind.Int16:
                _json.WriteNumberValue(value.AsInt16());
                break;
            case FieldKind.UInt16:
                _json.WriteNumberValue(value.AsUInt16());
                break;
            case FieldKind.Int32:
                _json.WriteNumberValue(value.AsInt32());
                break;
            case FieldKind.UInt32:
                _json.WriteNumberValue(value.AsUInt32());
                break;
            case FieldKind.Int64:
                _json.WriteStringValue(value.AsInt64().ToString(CultureInfo.InvariantCulture));
                break;
            case FieldKind.UInt64:
                _json.WriteStringValue(value.AsUInt64().ToString(CultureInfo.InvariantCulture));
                break;
            case FieldKind.Float32:
                if (IsFloat32Special((uint)value.Bits))
                    _json.WriteStringValue("0x" + value.Bits.ToString("x8", CultureInfo.InvariantCulture));
                else
                    _json.WriteNumberValue(value.AsFloat32());
                break;
            case FieldKind.Float64:
                if (IsFloat64Special(value.Bits))
                    _json.WriteStringValue("0x" + value.Bits.ToString("x16", CultureInfo.InvariantCulture));
                else
                    _json.WriteNumberValue(value.AsFloat64());
                break;
            case FieldKind.Entity:
                value.AsEntity(out var id, out var version, out var bound);
                StartArray();
                _json.WriteNumberValue(id);
                _json.WriteNumberValue(version);
                _json.WriteBooleanValue(bound);
                _json.WriteEndArray();
                break;
            case FieldKind.Bytes:
                WriteBytes(value.AsBytes());
                return;
            default:
                throw new InvalidDataException($"Unknown checkpoint field kind {value.Kind}.");
        }

        CheckSize();
    }

    internal static bool IsFloat32Special(uint bits) => (bits & 0x7f800000U) == 0x7f800000U || bits == 0x80000000U;
    internal static bool IsFloat64Special(ulong bits) =>
        (bits & 0x7ff0000000000000UL) == 0x7ff0000000000000UL || bits == 0x8000000000000000UL;

    private void CheckSize(long additionalBytes = 0)
    {
        var position = Position;
        if (additionalBytes > _maxDocumentBytes - position)
            throw new InvalidDataException("JSON checkpoint exceeds the document limit.");
        if (_frameStart >= 0 && additionalBytes > _maxFrameBytes - (position - _frameStart))
            throw new InvalidDataException("JSON checkpoint frame exceeds its limit.");
    }

    private void StartArray()
    {
        if (_json.CurrentDepth >= _maxDepth)
            throw new InvalidDataException("JSON checkpoint exceeds the depth limit.");

        _json.WriteStartArray();
    }

    private void WritePendingBytes()
    {
        _json.Flush();
        _buffer.WriteTo(_stream);
    }

    public override void Dispose() => _json.Dispose();

    private sealed class FrameBuffer : IBufferWriter<byte>
    {
        private readonly long _maxCapacity;
        private byte[] _bytes = Array.Empty<byte>();
        private int _written;

        internal FrameBuffer(int maxFrameBytes)
        {
            // Utf8JsonWriter requests room beyond the current token when it grows.
            _maxCapacity = Math.Min((long)maxFrameBytes + 4096, int.MaxValue);
        }

        public void Advance(int count) => _written += count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Reserve(sizeHint);
            return _bytes.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Reserve(sizeHint);
            return _bytes.AsSpan(_written);
        }

        private void Reserve(int sizeHint)
        {
            var needed = checked(_written + Math.Max(1, sizeHint));
            if (needed > _bytes.Length)
            {
                var capacity = Math.Max(needed, Math.Min(_maxCapacity, _bytes.Length * 2L));
                Array.Resize(ref _bytes, (int)capacity);
            }
        }

        internal void WriteTo(Stream stream)
        {
            stream.Write(_bytes, 0, _written);
            _written = 0;
        }
    }
}

internal sealed class JsonWireReader : WireReader
{
    private readonly Stream _stream;
    private readonly int _maxFrameBytes;
    private readonly int _maxValueBytes;
    private readonly long _maxDocumentBytes;
    private readonly int _maxBufferBytes;
    private JsonReaderState _state;
    private byte[] _buffer;
    private int _start;
    private int _end;
    private long _position;
    private long _loaded;
    private long _tokenStart;
    private long _frameStart = -1;
    private bool _eof;

    internal JsonWireReader(Stream stream, CheckpointLimits limits)
    {
        _stream = stream;
        _state = new JsonReaderState(new JsonReaderOptions { MaxDepth = limits.MaxDepth });
        _maxFrameBytes = limits.MaxFrameBytes;
        _maxValueBytes = limits.MaxValueBytes;
        _maxDocumentBytes = limits.MaxDocumentBytes;
        _maxBufferBytes = (int)Math.Min(_maxDocumentBytes, Math.Min(int.MaxValue, _maxFrameBytes + 1L));
        _buffer = new byte[Math.Min(4096, _maxBufferBytes)];
    }

    internal override void BeginFile() => Expect(JsonTokenType.StartArray);

    internal override byte BeginFrame()
    {
        Expect(JsonTokenType.StartArray);
        _frameStart = _tokenStart;
        var token = Expect(JsonTokenType.String);
        for (byte kind = 0; kind < JsonFrame.Names.Length; kind++)
        {
            if (token.ValueTextEquals(JsonFrame.Names[kind]))
                return kind;
        }

        throw new InvalidDataException("Unrecognized JSON checkpoint frame.");
    }

    internal override void EndFrame()
    {
        Expect(JsonTokenType.EndArray);
        _frameStart = -1;
    }

    internal override void BeginColumn() => Expect(JsonTokenType.StartArray);
    internal override void EndColumn() => Expect(JsonTokenType.EndArray);

    internal override void EndFile()
    {
        Expect(JsonTokenType.EndArray);
        if (TryNextToken(out _))
            throw new InvalidDataException("Unexpected tokens after the JSON checkpoint.");
    }

    internal override uint ReadUInt32() => Expect(JsonTokenType.Number).TryGetUInt32(out var value)
        ? value : throw InvalidValue(nameof(FieldKind.UInt32));

    internal override int ReadInt32() => Expect(JsonTokenType.Number).TryGetInt32(out var value)
        ? value : throw InvalidValue(nameof(FieldKind.Int32));

    internal override bool ReadBoolean() => NextToken().TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        _ => throw InvalidValue(nameof(FieldKind.Boolean))
    };

    internal override byte[] ReadBytes()
    {
        var token = Expect(JsonTokenType.String);
        if (token.ValueSpan.Length > ((_maxValueBytes + 2L) / 3 * 4))
            throw new InvalidDataException("Checkpoint byte value exceeds its limit.");

        if (!token.TryGetBytesFromBase64(out var value) || value is null)
            throw InvalidValue(nameof(FieldKind.Bytes));
        if (value.Length > _maxValueBytes)
            throw new InvalidDataException("Checkpoint byte value exceeds its limit.");

        return value;
    }

    internal override FieldValue ReadField(FieldKind kind) => kind switch
    {
        FieldKind.Boolean => FieldValue.From(ReadBoolean()),
        FieldKind.Int8 => Expect(JsonTokenType.Number).TryGetSByte(out var i8)
            ? FieldValue.From(i8) : throw InvalidValue(nameof(FieldKind.Int8)),
        FieldKind.UInt8 => Expect(JsonTokenType.Number).TryGetByte(out var u8)
            ? FieldValue.From(u8) : throw InvalidValue(nameof(FieldKind.UInt8)),
        FieldKind.Int16 => Expect(JsonTokenType.Number).TryGetInt16(out var i16)
            ? FieldValue.From(i16) : throw InvalidValue(nameof(FieldKind.Int16)),
        FieldKind.UInt16 => Expect(JsonTokenType.Number).TryGetUInt16(out var u16)
            ? FieldValue.From(u16) : throw InvalidValue(nameof(FieldKind.UInt16)),
        FieldKind.Int32 => FieldValue.From(ReadInt32()),
        FieldKind.UInt32 => FieldValue.From(ReadUInt32()),
        FieldKind.Int64 => ReadInt64Field(),
        FieldKind.UInt64 => ReadUInt64Field(),
        FieldKind.Float32 => ReadFloat32(),
        FieldKind.Float64 => ReadFloat64(),
        FieldKind.Entity => ReadEntity(),
        FieldKind.Bytes => FieldValue.FromBytes(ReadBytes()),
        _ => throw new InvalidDataException($"Unknown checkpoint field kind {kind}.")
    };

    private FieldValue ReadInt64Field()
    {
        var text = Expect(JsonTokenType.String).ValueSpan;
        return Utf8Parser.TryParse(text, out long value, out var consumed) && consumed == text.Length
            ? FieldValue.From(value) : throw InvalidValue(nameof(FieldKind.Int64));
    }

    private FieldValue ReadUInt64Field()
    {
        var text = Expect(JsonTokenType.String).ValueSpan;
        return Utf8Parser.TryParse(text, out ulong value, out var consumed) && consumed == text.Length
            ? FieldValue.From(value) : throw InvalidValue(nameof(FieldKind.UInt64));
    }

    private FieldValue ReadFloat32()
    {
        var token = NextToken();
        if (token.TokenType == JsonTokenType.String &&
            TryReadHex(token.ValueSpan, 8, out var bits) && JsonWireWriter.IsFloat32Special((uint)bits))
            return FieldValue.FromBits(FieldKind.Float32, bits);

        if (token.TokenType == JsonTokenType.Number && token.TryGetSingle(out var value) && !float.IsInfinity(value))
            return FieldValue.From(value);

        throw InvalidValue(nameof(FieldKind.Float32));
    }

    private FieldValue ReadFloat64()
    {
        var token = NextToken();
        if (token.TokenType == JsonTokenType.String &&
            TryReadHex(token.ValueSpan, 16, out var bits) && JsonWireWriter.IsFloat64Special(bits))
            return FieldValue.FromBits(FieldKind.Float64, bits);

        if (token.TokenType == JsonTokenType.Number && token.TryGetDouble(out var value) && !double.IsInfinity(value))
            return FieldValue.From(value);

        throw InvalidValue(nameof(FieldKind.Float64));
    }

    private static bool TryReadHex(ReadOnlySpan<byte> text, int digits, out ulong value)
    {
        value = 0;
        if (text.Length != digits + 2 || text[0] != '0' || text[1] != 'x')
            return false;

        foreach (var character in text.Slice(2))
        {
            var digit = character - '0';
            if ((uint)digit > 9)
            {
                digit = (character | 32) - 'a';
                if ((uint)digit > 5)
                    return false;
                digit += 10;
            }

            value = (value << 4) | (uint)digit;
        }

        return true;
    }

    private FieldValue ReadEntity()
    {
        Expect(JsonTokenType.StartArray);
        var value = FieldValue.FromEntity(ReadInt32(), ReadInt32(), ReadBoolean());
        Expect(JsonTokenType.EndArray);
        return value;
    }

    private Utf8JsonReader Expect(JsonTokenType type)
    {
        var token = NextToken();
        return token.TokenType == type
            ? token : throw new InvalidDataException($"Expected JSON {type} in the checkpoint.");
    }

    private Utf8JsonReader NextToken() => TryNextToken(out var token)
        ? token : throw new InvalidDataException("Unexpected end of the JSON checkpoint.");

    private bool TryNextToken(out Utf8JsonReader token)
    {
        while (true)
        {
            token = new Utf8JsonReader(_buffer.AsSpan(_start, _end - _start), _eof, _state);
            bool found;
            try
            {
                found = token.Read();
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Invalid JSON checkpoint.", exception);
            }

            _tokenStart = _position + token.TokenStartIndex;
            _start += (int)token.BytesConsumed;
            _position += token.BytesConsumed;
            _state = token.CurrentState;
            if (_frameStart >= 0 && _position - _frameStart > _maxFrameBytes)
                throw new InvalidDataException("JSON checkpoint frame exceeds its limit.");

            if (found || _eof)
                return found;

            FillBuffer();
        }
    }

    private void FillBuffer()
    {
        if (_loaded == _maxDocumentBytes)
        {
            if (_stream.ReadByte() != -1)
                throw new InvalidDataException("JSON checkpoint exceeds the document limit.");
            _eof = true;
            return;
        }

        if (_start != 0)
        {
            Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
            _end -= _start;
            _start = 0;
        }

        if (_end == _buffer.Length)
        {
            if (_end == _maxBufferBytes)
                throw new InvalidDataException("JSON checkpoint token exceeds the frame limit.");

            Array.Resize(ref _buffer, (int)Math.Min(_maxBufferBytes, 2L * _buffer.Length));
        }

        // Complete this fill even when the stream fragments reads. Re-parsing an
        // incomplete token after each fragment would make large values quadratic.
        var count = (int)Math.Min(_buffer.Length - _end, _maxDocumentBytes - _loaded);
        while (count > 0)
        {
            var read = _stream.Read(_buffer, _end, count);
            if (read == 0)
            {
                _eof = true;
                break;
            }

            _end += read;
            _loaded += read;
            count -= read;
        }
    }

    private static InvalidDataException InvalidValue(string kind) =>
        new InvalidDataException($"Invalid {kind} value in the JSON checkpoint.");

    public override void Dispose() { }
}
