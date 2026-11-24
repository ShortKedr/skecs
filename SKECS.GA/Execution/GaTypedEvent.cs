namespace SKECS.GA;

/// <summary>Binds a generated decoder for one typed event or reduction leaf.</summary>
public readonly struct GaTypedEvent
{
    private delegate void EmitEvent(DeterministicSource source, ReadOnlySpan<uint> payload);

    private readonly EmitEvent _emit;
    private readonly int[] _booleans;

    private GaTypedEvent(EmitEvent emit, int payloadWords, int[] booleans, bool isContribution)
    {
        _emit = emit;
        PayloadWords = payloadWords;
        _booleans = booleans;
        IsContribution = isContribution;
    }

    public int PayloadWords { get; }
    internal bool IsContribution { get; }
    internal bool IsBound => _emit is not null;

    public static GaTypedEvent For<T>(GaEventDecoder<T> decode, int payloadWords,
        ReadOnlySpan<int> booleanOffsets) where T : unmanaged
    {
        if (decode is null)
            throw new ArgumentNullException(nameof(decode));
        return Bind((source, payload) => source.Emit(decode(payload)), payloadWords, booleanOffsets, false);
    }

    /// <summary>Binds this payload to a declared reduction of the runner's workload.</summary>
    public static GaTypedEvent ForContribution<T>(DeterministicReduction<T> reduction, GaEventDecoder<T> decode,
        int payloadWords, ReadOnlySpan<int> booleanOffsets) where T : unmanaged
    {
        if (decode is null)
            throw new ArgumentNullException(nameof(decode));
        return Bind((source, payload) => source.Contribute(reduction, decode(payload)),
            payloadWords, booleanOffsets, true);
    }

    private static GaTypedEvent Bind(EmitEvent emit, int payloadWords, ReadOnlySpan<int> booleanOffsets, bool isContribution)
    {
        if ((uint)payloadWords > GaOutputRecord.MaximumEventPayloadWords)
            throw new ArgumentOutOfRangeException(nameof(payloadWords));
        foreach (int offset in booleanOffsets)
            if ((uint)offset >= (uint)payloadWords)
                throw new ArgumentOutOfRangeException(nameof(booleanOffsets));
        return new GaTypedEvent(emit, payloadWords, booleanOffsets.ToArray(), isContribution);
    }

    internal void Validate(ReadOnlySpan<uint> payload)
    {
        if (payload.Length < PayloadWords || GaPackedWords.HasNonZeroWord(payload[PayloadWords..]))
            throw new InvalidDataException("A GA typed output has invalid payload length or padding.");
        foreach (int offset in _booleans)
            if (payload[offset] > 1u)
                throw new InvalidDataException("A GA typed output has a non-canonical Boolean field.");
    }

    internal void Apply(DeterministicSource source, ReadOnlySpan<uint> payload) =>
        _emit(source, payload[..PayloadWords]);
}
