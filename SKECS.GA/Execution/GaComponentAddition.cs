namespace SKECS.GA;

public delegate T GaAdditionDecoder<T>(ReadOnlySpan<uint> words) where T : unmanaged;

/// <summary>Binds a generated component decoder and its packed structural payload.</summary>
public readonly struct GaComponentAddition
{
    private delegate void EntityAddition(DeterministicSource source, int id, int generation, ReadOnlySpan<uint> payload);
    private delegate void TicketAddition(DeterministicSource source, CreateTicket ticket, ReadOnlySpan<uint> payload);

    private readonly EntityAddition _entity;
    private readonly TicketAddition _ticket;
    private readonly int[] _booleans;

    private GaComponentAddition(EntityAddition entity, TicketAddition ticket, int payloadWords, int[] booleans)
    {
        _entity = entity;
        _ticket = ticket;
        PayloadWords = payloadWords;
        _booleans = booleans;
    }

    public int PayloadWords { get; }

    internal bool IsBound => _entity is not null;

    public static GaComponentAddition For<T>(GaAdditionDecoder<T> decode, int payloadWords,
        ReadOnlySpan<int> booleanOffsets) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(decode);
        if ((uint)payloadWords > GaOutputRecord.MaximumAdditionPayloadWords)
            throw new ArgumentOutOfRangeException(nameof(payloadWords));
        foreach (int offset in booleanOffsets)
            if ((uint)offset >= (uint)payloadWords)
                throw new ArgumentOutOfRangeException(nameof(booleanOffsets));
        return new GaComponentAddition(
            (source, id, generation, payload) => source.Add(id, generation, decode(payload)),
            (source, ticket, payload) => source.Add(ticket, decode(payload)),
            payloadWords, booleanOffsets.ToArray());
    }

    internal void Validate(ReadOnlySpan<uint> payload)
    {
        if (payload.Length < PayloadWords || payload[PayloadWords..].IndexOfAnyExcept(0u) >= 0)
            throw new InvalidDataException("A GA addition has invalid payload length or padding.");
        foreach (int offset in _booleans)
            if (payload[offset] > 1u)
                throw new InvalidDataException("A GA addition has a non-canonical Boolean field.");
    }

    internal void Apply(DeterministicSource source, int id, int generation, ReadOnlySpan<uint> payload) =>
        _entity(source, id, generation, payload[..PayloadWords]);

    internal void Apply(DeterministicSource source, CreateTicket ticket, ReadOnlySpan<uint> payload) =>
        _ticket(source, ticket, payload[..PayloadWords]);
}
