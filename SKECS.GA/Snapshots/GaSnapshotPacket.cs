using System.Buffers.Binary;
using SKECS.Numerics;

namespace SKECS.GA;

/// <summary>A world-local key. Validate world ownership before encoding a bound entity as a key.</summary>
public readonly record struct GaSnapshotKey(int Id, int Generation);

/// <summary>The ordered scalar fields in each snapshot payload.</summary>
public sealed class GaSnapshotLayout
{
    private readonly GaScalar[] _fields;
    private readonly int[] _offsets;
    private readonly int[] _booleanOffsets;

    public GaSnapshotLayout(ReadOnlySpan<GaScalar> fields)
    {
        _fields = fields.ToArray();
        _offsets = new int[fields.Length];
        var schema = new char[fields.Length];
        var booleans = new List<int>();
        int offset = 0;
        for (int index = 0; index < _fields.Length; index++)
        {
            var field = _fields[index];
            _offsets[index] = offset;
            schema[index] = field switch
            {
                GaScalar.Bool => 'b',
                GaScalar.Int32 => 'i',
                GaScalar.UInt32 => 'u',
                GaScalar.Fixed => 'f',
                _ => throw new ArgumentException("A snapshot field must be a supported GA scalar.", nameof(fields))
            };
            switch (field)
            {
                case GaScalar.Bool:
                    booleans.Add(offset);
                    goto case GaScalar.Int32;
                case GaScalar.Int32:
                case GaScalar.UInt32:
                    offset = checked(offset + 1);
                    break;
                case GaScalar.Fixed:
                    offset = checked(offset + 2);
                    break;
            }
        }
        Schema = new string(schema);
        _booleanOffsets = booleans.ToArray();
        ValueWordCount = offset;
        RowWordCount = checked(offset + GaSnapshotPacket.KeyWordCount);
    }

    public ReadOnlySpan<GaScalar> Fields => _fields;
    /// <summary>The ordered field codes b, i, u and f used by GA artifact contracts.</summary>
    public string Schema { get; }
    public int ValueWordCount { get; }
    public int RowWordCount { get; }

    public int GetWordOffset(int field)
    {
        if ((uint)field >= (uint)_offsets.Length)
            throw new ArgumentOutOfRangeException(nameof(field));
        return _offsets[field];
    }

    public static bool IsValidSchema(string schema)
    {
        if (string.IsNullOrEmpty(schema))
            return false;
        foreach (char code in schema)
            if (code is not ('b' or 'i' or 'u' or 'f'))
                return false;
        return true;
    }

    internal void ValidateBooleans(ReadOnlySpan<uint> words)
    {
        foreach (int offset in _booleanOffsets)
            if (words[offset] > 1)
                throw new ArgumentException("Snapshot Boolean fields must be encoded as 0 or 1.");
    }
}

/// <summary>Packs a copied component according to its registered snapshot layout.</summary>
public delegate void GaSnapshotPack<T>(in T component, Span<uint> values) where T : unmanaged;

/// <summary>An immutable component snapshot for one world and one capture boundary.</summary>
/// <remarks>
/// The word header is [version, row count, value word count]. Rows are sorted by ID, with
/// [ID, generation, values] in each row. Fixed raw bits are packed low word first.
/// World ownership and boundary lifetime are host concerns; no live world is retained.
/// </remarks>
public sealed class GaSnapshotPacket
{
    public const uint FormatVersion = 1;
    public const int HeaderWordCount = 3;
    public const int KeyWordCount = 2;

    private readonly uint[] _words;

    private GaSnapshotPacket(GaSnapshotLayout layout, uint[] words)
    {
        Layout = layout;
        _words = words;
    }

    public GaSnapshotLayout Layout { get; }
    public int RowCount => (int)_words[1];
    public ReadOnlySpan<uint> Words => _words;

    /// <summary>Copies packed rows and sorts them into canonical order.</summary>
    /// <remarks>Row payloads follow the input key order. Each ID can occur once per packet.</remarks>
    public static GaSnapshotPacket FromRows(GaSnapshotLayout layout, ReadOnlySpan<GaSnapshotKey> keys,
        ReadOnlySpan<uint> packedRows)
    {
        if (layout is null)
            throw new ArgumentNullException(nameof(layout));
        if ((long)keys.Length * layout.ValueWordCount != packedRows.Length)
            throw new ArgumentException("Snapshot payload length does not match the layout and keys.", nameof(packedRows));
        int length = GetPacketLength(layout, keys.Length);
        var order = new (GaSnapshotKey Key, int Source)[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            if (!IsValidKey(key))
                throw new ArgumentException("Snapshot IDs must be nonnegative and generations must be positive.", nameof(keys));
            order[i] = (key, i);
        }
        Array.Sort(order, static (left, right) => left.Key.Id.CompareTo(right.Key.Id));

        var words = new uint[length];
        words[0] = FormatVersion;
        words[1] = (uint)keys.Length;
        words[2] = (uint)layout.ValueWordCount;
        for (int i = 0; i < order.Length; i++)
        {
            var (key, source) = order[i];
            if (i != 0 && order[i - 1].Key.Id == key.Id)
                throw new ArgumentException($"Duplicate snapshot ID {key.Id}.", nameof(keys));
            var values = packedRows.Slice(source * layout.ValueWordCount, layout.ValueWordCount);
            layout.ValidateBooleans(values);
            int row = HeaderWordCount + i * layout.RowWordCount;
            words[row] = (uint)key.Id;
            words[row + 1] = (uint)key.Generation;
            values.CopyTo(words.AsSpan(row + KeyWordCount));
        }
        return new GaSnapshotPacket(layout, words);
    }

    /// <summary>Captures currently live members of a component at a completed world boundary.</summary>
    /// <remarks>Use a pure codec and exclude concurrent world mutation during capture.</remarks>
    public static GaSnapshotPacket Capture<T>(World world, GaSnapshotLayout layout, GaSnapshotPack<T> pack)
        where T : unmanaged
    {
        if (world is null)
            throw new ArgumentNullException(nameof(world));
        if (layout is null)
            throw new ArgumentNullException(nameof(layout));
        if (pack is null)
            throw new ArgumentNullException(nameof(pack));
        var checkpoint = world.OpenCheckpoint();
        var words = new List<uint> { FormatVersion, 0, (uint)layout.ValueWordCount };
        var values = new uint[layout.ValueWordCount];
        int rowCount = 0;
        for (int id = 0; id < checkpoint.SlotCount; id++)
        {
            var slot = checkpoint.GetSlot(id);
            if (!slot.Alive || !checkpoint.TryGet<T>(world.BindEntity(id, slot.Version), out var component))
                continue;
            GetPacketLength(layout, rowCount + 1);
            values.AsSpan().Clear();
            pack(in component, values);
            layout.ValidateBooleans(values);
            words.Add((uint)id);
            words.Add((uint)slot.Version);
            words.AddRange(values);
            rowCount++;
        }
        words[1] = (uint)rowCount;
        return new GaSnapshotPacket(layout, words.ToArray());
    }

    /// <summary>Returns a payload only when both the ID and the captured generation match.</summary>
    public bool TryGet(GaSnapshotKey key, out ReadOnlySpan<uint> values)
    {
        values = default;
        if (!IsValidKey(key))
            return false;

        int first = 0;
        int last = RowCount;
        while (first < last)
        {
            int middle = first + (last - first) / 2;
            int row = HeaderWordCount + middle * Layout.RowWordCount;
            uint id = _words[row];
            if (id < (uint)key.Id)
                first = middle + 1;
            else if (id > (uint)key.Id)
                last = middle;
            else
            {
                if (_words[row + 1] != (uint)key.Generation)
                    return false;
                values = _words.AsSpan(row + KeyWordCount, Layout.ValueWordCount);
                return true;
            }
        }
        return false;
    }

    public int ReadInt32(int id, int generation, int fallback)
    {
        RequireSchema(null);
        return TryGet(new GaSnapshotKey(id, generation), out var values)
            ? unchecked((int)values[0]) : fallback;
    }

    public bool ReadBool(int id, int generation, int field, bool fallback) =>
        ReadFieldBits(id, generation, field, GaScalar.Bool, fallback ? 1u : 0u) != 0;

    public int ReadInt32(int id, int generation, int field, int fallback) =>
        unchecked((int)ReadFieldBits(id, generation, field, GaScalar.Int32, unchecked((uint)fallback)));

    public uint ReadUInt32(int id, int generation, int field, uint fallback) =>
        (uint)ReadFieldBits(id, generation, field, GaScalar.UInt32, fallback);

    public Fixed ReadFixed(int id, int generation, int field, Fixed fallback) =>
        Fixed.FromRaw(unchecked((long)ReadFieldBits(id, generation, field, GaScalar.Fixed, unchecked((ulong)fallback.Raw))));

    internal ulong ReadFieldBits(int id, int generation, int field, GaScalar type, ulong fallback)
    {
        int offset = Layout.GetWordOffset(field);
        if (Layout.Fields[field] != type)
            throw new ArgumentException("The snapshot field type does not match the lookup type.", nameof(field));
        if (!TryGet(new GaSnapshotKey(id, generation), out var values))
            return fallback;
        ulong bits = values[offset];
        if (type == GaScalar.Fixed)
            bits |= (ulong)values[offset + 1] << 32;
        return bits;
    }

    internal void RequireSchema(string? expected)
    {
        expected ??= "i";
        if (Layout.Schema != expected)
            throw new ArgumentException($"The GA snapshot layout '{Layout.Schema}' does not match '{expected}'.");
    }

    public void WriteLittleEndian(Span<byte> destination)
    {
        if (destination.Length != (long)_words.Length * sizeof(uint))
            throw new ArgumentException("Destination must have exactly four bytes per packet word.", nameof(destination));
        for (int i = 0; i < _words.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(i * sizeof(uint)), _words[i]);
    }

    private static bool IsValidKey(GaSnapshotKey key) => key.Id >= 0 && key.Generation > 0;

    private static int GetPacketLength(GaSnapshotLayout layout, int rowCount)
    {
        long length = HeaderWordCount + (long)rowCount * layout.RowWordCount;
        if (length > GaPackedWords.MaximumLength)
            throw new ArgumentException("Snapshot dimensions exceed the maximum managed packet length.");
        return (int)length;
    }
}
