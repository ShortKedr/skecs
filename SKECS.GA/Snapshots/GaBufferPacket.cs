namespace SKECS.GA;

/// <summary>An owned, immutable flat buffer. Its packed form begins with the row count.</summary>
public sealed class GaBufferPacket
{
    private readonly uint[] _words;

    internal GaBufferPacket(string schema, int rowWords, uint[] words)
    {
        if (schema is null)
            throw new ArgumentNullException(nameof(schema));
        if (string.IsNullOrWhiteSpace(schema))
            throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", nameof(schema));
        Schema = schema;
        RowWords = rowWords;
        _words = words;
    }

    public string Schema { get; }
    public int RowWords { get; }
    public int Count => (int)_words[0];
    public ReadOnlySpan<uint> Words => _words;

    /// <summary>Copies already-packed rows without the row-count header.</summary>
    public static GaBufferPacket FromWords(string schema, int rowWords, int count, ReadOnlySpan<uint> words)
    {
        if (rowWords < 0)
            throw new ArgumentOutOfRangeException(nameof(rowWords), rowWords, "Value must be non-negative.");
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Value must be non-negative.");
        if (words.Length != (long)rowWords * count)
            throw new ArgumentException("The packed words must match the row count and buffer width.", nameof(words));
        var packet = Allocate(rowWords, count);
        words.CopyTo(packet.AsSpan(1));
        return new GaBufferPacket(schema, rowWords, packet);
    }

    public uint ReadWord(int index, int wordOffset) => _words[GetOffset(index, wordOffset, 1)];

    public ulong ReadWide(int index, int wordOffset)
    {
        int offset = GetOffset(index, wordOffset, 2);
        return _words[offset] | ((ulong)_words[offset + 1] << 32);
    }

    internal void RequireSchema(string schema, int rowWords)
    {
        if (Schema != schema || RowWords != rowWords)
            throw new ArgumentException("The packed read buffer does not match the declared schema and width.");
    }

    internal static uint[] Allocate(int rowWords, int count)
    {
        if (rowWords < 0)
            throw new ArgumentOutOfRangeException(nameof(rowWords), rowWords, "Value must be non-negative.");
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Value must be non-negative.");
        long length = 1L + (long)rowWords * count;
        if (length > GaPackedWords.MaximumLength)
            throw new ArgumentException("The packed read buffer exceeds the maximum array length.", nameof(count));
        var words = new uint[(int)length];
        words[0] = (uint)count;
        return words;
    }

    private int GetOffset(int index, int wordOffset, int width)
    {
        if ((uint)index >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (wordOffset < 0 || wordOffset > RowWords - width)
            throw new ArgumentOutOfRangeException(nameof(wordOffset));
        return 1 + index * RowWords + wordOffset;
    }
}

public delegate void GaPackBufferRow<T>(in T row, Span<uint> words) where T : unmanaged;

/// <summary>The typed projection of source rows used by a portable kernel's read buffer.</summary>
public readonly record struct GaBufferCodec<T>(string Schema, int RowWords, GaPackBufferRow<T> Pack)
    where T : unmanaged
{
    /// <summary>Packs data into owned words. The source span is never retained.</summary>
    public GaBufferPacket Capture(ReadOnlySpan<T> rows)
    {
        if (Schema is null)
            throw new ArgumentNullException(nameof(Schema));
        if (string.IsNullOrWhiteSpace(Schema))
            throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", nameof(Schema));
        if (Pack is null)
            throw new ArgumentNullException(nameof(Pack));
        var words = GaBufferPacket.Allocate(RowWords, rows.Length);
        if (RowWords != 0)
            for (int row = 0; row < rows.Length; row++)
                Pack(in rows[row], words.AsSpan(1 + row * RowWords, RowWords));
        return new GaBufferPacket(Schema, RowWords, words);
    }
}
