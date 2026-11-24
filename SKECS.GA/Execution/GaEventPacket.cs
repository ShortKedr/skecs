namespace SKECS.GA;

public delegate TEvent GaEventDecoder<TEvent>(ReadOnlySpan<uint> words) where TEvent : unmanaged;

/// <summary>Bounded output readback with one ordered slice per logical source.</summary>
/// <remarks>Words are version, source count, capacity per source and event width, followed by
/// one count word and capacity fixed-width event slots for each source. The enclosing batch assigns
/// the source order. The backing words must remain unchanged while this view is in use.</remarks>
public readonly ref struct GaEventPacket
{
    public const uint Version = 1;
    public const int HeaderWords = 4;
    public const int MaximumCapacityPerSource = 65536;

    private readonly ReadOnlySpan<uint> _slots;
    private readonly int _sourceWords;

    public GaEventPacket(ReadOnlySpan<uint> words)
    {
        if (words.Length < HeaderWords || words[0] != Version ||
            words[1] > int.MaxValue || words[2] > MaximumCapacityPerSource || words[3] is 0 or > int.MaxValue)
            throw new InvalidDataException("The GA event packet header is invalid.");
        SourceCount = (int)words[1];
        CapacityPerSource = (int)words[2];
        EventWords = (int)words[3];
        long sourceWords = 1L + (long)CapacityPerSource * EventWords;
        if (sourceWords > int.MaxValue || HeaderWords + SourceCount * sourceWords != words.Length)
            throw new InvalidDataException("The GA event packet dimensions do not match its length.");

        _sourceWords = (int)sourceWords;
        _slots = words[HeaderWords..];
        for (int source = 0; source < SourceCount; source++)
            if (_slots[source * _sourceWords] > CapacityPerSource)
                throw new InvalidDataException($"GA event capacity exceeded for source {source}.");
    }

    public int SourceCount { get; }
    public int CapacityPerSource { get; }
    public int EventWords { get; }

    public void ApplyEntityRows<TInput, TEvent>(DeterministicJob<TInput> job, int firstSource, int successfulRows,
        GaEventDecoder<TEvent> decode)
        where TInput : unmanaged where TEvent : unmanaged
    {
        if (decode is null)
            throw new ArgumentNullException(nameof(decode));
        if ((uint)successfulRows > job.Count)
            throw new ArgumentOutOfRangeException(nameof(successfulRows));
        if ((uint)firstSource > SourceCount || successfulRows > SourceCount - firstSource)
            throw new ArgumentOutOfRangeException(nameof(firstSource));
        for (int row = 0; row < successfulRows; row++)
            Apply(firstSource + row, job.ForEntity(row), decode);
    }

    public void ApplyLogicalJob<TInput, TEvent>(DeterministicJob<TInput> job, int source, GaEventDecoder<TEvent> decode)
        where TInput : unmanaged where TEvent : unmanaged
    {
        if (decode is null)
            throw new ArgumentNullException(nameof(decode));
        if ((uint)source >= SourceCount)
            throw new ArgumentOutOfRangeException(nameof(source));
        Apply(source, job.ForJob(), decode);
    }

    internal void ValidateMixedRecords(int successfulSources, ReadOnlySpan<GaComponentRemoval> removals,
        ReadOnlySpan<GaComponentAddition> additions, bool usesDomainOrder, ReadOnlySpan<GaTypedEvent> eventTypes)
    {
        for (int source = 0; source < successfulSources; source++)
            GaOutputRecord.ValidatePackedSource(Records(source), EventWords, removals, additions, usesDomainOrder, eventTypes);
    }

    internal void ApplyMixedEntityRows<TInput>(DeterministicJob<TInput> job, int firstSource, int successfulRows,
        ReadOnlySpan<GaComponentRemoval> removals, ReadOnlySpan<GaComponentAddition> additions, bool usesDomainOrder,
        ReadOnlySpan<GaTypedEvent> eventTypes)
        where TInput : unmanaged
    {
        for (int row = 0; row < successfulRows; row++)
            GaOutputRecord.ReplayPackedSource(job.ForEntity(row), Records(firstSource + row), EventWords, removals, additions,
                usesDomainOrder, eventTypes);
    }

    private ReadOnlySpan<uint> Records(int source)
    {
        int offset = source * _sourceWords;
        return _slots.Slice(offset + 1, (int)_slots[offset] * EventWords);
    }

    private void Apply<TEvent>(int source, DeterministicSource target, GaEventDecoder<TEvent> decode)
        where TEvent : unmanaged
    {
        int offset = source * _sourceWords;
        int count = (int)_slots[offset++];
        for (int item = 0; item < count; item++, offset += EventWords)
            target.Emit(decode(_slots.Slice(offset, EventWords)));
    }
}
