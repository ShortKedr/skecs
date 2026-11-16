namespace SKECS.GA;

/// <summary>A staged event, reduction leaf or structural command emitted by a compiled GA invocation.</summary>
public readonly record struct GaOutputRecord
{
    public const int Words = 3;
    public const int DomainWordCount = 3;
    public const uint EmitInt32Kind = 1;
    public const uint DestroyEntityKind = 2;
    public const uint CreateEntityKind = 3;
    public const uint DestroyTicketKind = 4;
    public const uint RemoveEntityBase = 0x10000u;
    public const uint RemoveTicketBase = 0x20000u;
    public const uint AddEntityBase = 0x30000u;
    public const uint AddTicketBase = 0x40000u;
    public const uint EmitTypedBase = 0x50000u;
    public const uint ContributeBase = 0x60000u;
    public const int MaximumComponentCommands = 65536;
    public const int MaximumComponentRemovals = MaximumComponentCommands;
    public const int MaximumAdditionPayloadWords = 65536;
    public const int MaximumEventTypes = 65536;
    public const int MaximumEventPayloadWords = 65536;

    private readonly uint _kind;
    private readonly int _first;
    private readonly int _second;

    private GaOutputRecord(uint kind, int first, int second) =>
        (_kind, _first, _second) = (kind, first, second);

    public static GaOutputRecord EmitInt32(int value) => new(EmitInt32Kind, value, 0);

    public static GaOutputRecord EmitTyped(int eventTypeIndex) =>
        (uint)eventTypeIndex < MaximumEventTypes
            ? new GaOutputRecord(EmitTypedBase + (uint)eventTypeIndex, 0, 0)
            : throw new ArgumentOutOfRangeException(nameof(eventTypeIndex));

    public static GaOutputRecord Contribute(int eventTypeIndex) =>
        (uint)eventTypeIndex < MaximumEventTypes
            ? new GaOutputRecord(ContributeBase + (uint)eventTypeIndex, 0, 0)
            : throw new ArgumentOutOfRangeException(nameof(eventTypeIndex));

    public static GaOutputRecord DestroyEntity(int entityId, int generation) =>
        new(DestroyEntityKind, entityId, generation);

    public static GaOutputRecord CreateEntity() => new(CreateEntityKind, 0, 0);

    public static GaOutputRecord DestroyTicket(int createOrdinal) => new(DestroyTicketKind, createOrdinal, 0);

    public static GaOutputRecord RemoveEntity(int componentIndex, int entityId, int generation) =>
        new(RemoveEntityBase + ValidateComponentIndex(componentIndex), entityId, generation);

    public static GaOutputRecord RemoveTicket(int componentIndex, int createOrdinal) =>
        new(RemoveTicketBase + ValidateComponentIndex(componentIndex), createOrdinal, 0);

    public static GaOutputRecord AddEntity(int componentIndex, int entityId, int generation) =>
        new(AddEntityBase + ValidateComponentIndex(componentIndex), entityId, generation);

    public static GaOutputRecord AddTicket(int componentIndex, int createOrdinal) =>
        new(AddTicketBase + ValidateComponentIndex(componentIndex), createOrdinal, 0);

    private static uint ValidateComponentIndex(int index) =>
        (uint)index < MaximumComponentCommands ? (uint)index : throw new ArgumentOutOfRangeException(nameof(index));

    private uint CommandKind => _kind & 0xFFFF0000u;
    private int ComponentIndex => (int)(_kind & 0xFFFFu);
    private bool IsTypedOutput => CommandKind is EmitTypedBase or ContributeBase;
    private bool ReferencesTicket => _kind == DestroyTicketKind || CommandKind is RemoveTicketBase or AddTicketBase;

    /// <summary>Writes this record's header and clears its payload region.</summary>
    public void Write(Span<uint> words)
    {
        if (words.Length < Words)
            throw new ArgumentException("Output requires room for the record header.", nameof(words));
        words[0] = _kind;
        words[1] = unchecked((uint)_first);
        words[2] = unchecked((uint)_second);
        words[Words..].Clear();
    }

    /// <summary>Writes a domain key into the final three words of a domain-ordered record.</summary>
    public static void WriteDomainOrder(Span<uint> wholeRecord, bool hasKey, int primary, int secondary)
    {
        if (wholeRecord.Length < Words + DomainWordCount)
            throw new ArgumentException("Output requires room for its domain key.", nameof(wholeRecord));
        var key = wholeRecord[^DomainWordCount..];
        key[0] = hasKey ? 1u : 0u;
        key[1] = hasKey ? unchecked((uint)primary) : 0u;
        key[2] = hasKey ? unchecked((uint)secondary) : 0u;
    }

    /// <summary>Replays the outputs of one invocation into the runner's source stream.</summary>
    public static void ApplySource(DeterministicSource source, ReadOnlySpan<GaOutputRecord> records) =>
        ApplySource(source, records, []);

    /// <summary>Replays the outputs with the component removals declared by the compiled kernel.</summary>
    public static void ApplySource(DeterministicSource source, ReadOnlySpan<GaOutputRecord> records,
        ReadOnlySpan<GaComponentRemoval> removals)
    {
        for (int ordinal = 0; ordinal < records.Length; ordinal++)
        {
            var record = records[ordinal];
            if (!IsCanonical(record._kind, record._first, record._second))
                throw new InvalidDataException("Invalid GA output record.");
            record.ValidateBindings(removals, []);
            if (record.ReferencesTicket &&
                ((uint)record._first >= (uint)ordinal || records[record._first]._kind != CreateEntityKind))
                throw new InvalidDataException("A GA ticket must reference an earlier Create in the same source.");
        }

        var replay = new Replay(source, records.Length, removals, [], []);
        for (int ordinal = 0; ordinal < records.Length; ordinal++)
            replay.Apply(records[ordinal], ordinal);
    }

    /// <summary>Validates and replays one source's fixed-width packed output records.</summary>
    public static void ApplyPackedSource(DeterministicSource source, ReadOnlySpan<uint> records, int recordWords,
        ReadOnlySpan<GaComponentRemoval> removals, ReadOnlySpan<GaComponentAddition> additions)
        => ApplyPackedSource(source, records, recordWords, removals, additions, usesDomainOrder: false);

    /// <summary>Validates and replays packed output with the declared domain-key format.</summary>
    public static void ApplyPackedSource(DeterministicSource source, ReadOnlySpan<uint> records, int recordWords,
        ReadOnlySpan<GaComponentRemoval> removals, ReadOnlySpan<GaComponentAddition> additions, bool usesDomainOrder)
        => ApplyPackedSource(source, records, recordWords, removals, additions, usesDomainOrder, []);

    public static void ApplyPackedSource(DeterministicSource source, ReadOnlySpan<uint> records, int recordWords,
        ReadOnlySpan<GaComponentRemoval> removals, ReadOnlySpan<GaComponentAddition> additions, bool usesDomainOrder,
        ReadOnlySpan<GaTypedEvent> eventTypes)
    {
        int headerWords = Words + (usesDomainOrder ? DomainWordCount : 0);
        int maximumPayloadWords = Math.Max(MaximumAdditionPayloadWords, MaximumEventPayloadWords);
        if (recordWords < headerWords || recordWords > headerWords + maximumPayloadWords || records.Length % recordWords != 0)
            throw new ArgumentException("Packed GA output has an invalid record width.", nameof(recordWords));
        ValidatePackedSource(records, recordWords, removals, additions, usesDomainOrder, eventTypes);
        ReplayPackedSource(source, records, recordWords, removals, additions, usesDomainOrder, eventTypes);
    }

    internal static void ValidatePackedSource(ReadOnlySpan<uint> records, int recordWords,
        ReadOnlySpan<GaComponentRemoval> removals, ReadOnlySpan<GaComponentAddition> additions, bool usesDomainOrder,
        ReadOnlySpan<GaTypedEvent> eventTypes = default)
    {
        int count = records.Length / recordWords;
        for (int ordinal = 0; ordinal < count; ordinal++)
            Read(records.Slice(ordinal * recordWords, recordWords))
                .ValidatePackedRecord(records, ordinal, recordWords, removals, additions, usesDomainOrder, eventTypes);
    }

    internal static void ReplayPackedSource(DeterministicSource source, ReadOnlySpan<uint> records, int recordWords,
        ReadOnlySpan<GaComponentRemoval> removals, ReadOnlySpan<GaComponentAddition> additions, bool usesDomainOrder,
        ReadOnlySpan<GaTypedEvent> eventTypes = default)
    {
        int count = records.Length / recordWords;
        var replay = new Replay(source, count, removals, additions, eventTypes);
        int payloadWords = recordWords - Words - (usesDomainOrder ? DomainWordCount : 0);
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            var words = records.Slice(ordinal * recordWords, recordWords);
            replay.Apply(Read(words), ordinal, words.Slice(Words, payloadWords),
                usesDomainOrder ? ReadDomainKey(words) : null);
        }
    }

    public void Apply(DeterministicSource source)
    {
        switch (_kind)
        {
            case EmitInt32Kind: source.Emit(_first); break;
            case DestroyEntityKind: source.Destroy(_first, _second); break;
            case CreateEntityKind: source.Create(); break;
            case DestroyTicketKind: throw new InvalidOperationException("A ticket reference needs its source records.");
            default: throw new InvalidDataException("Unknown GA output record kind.");
        }
    }

    internal static GaOutputRecord Read(ReadOnlySpan<uint> words)
    {
        int first = unchecked((int)words[1]);
        int second = unchecked((int)words[2]);
        if (!IsCanonical(words[0], first, second))
            throw new InvalidDataException("Invalid GA output record.");
        return new GaOutputRecord(words[0], first, second);
    }

    private static bool IsCanonical(uint kind, int first, int second) =>
        kind switch
        {
            EmitInt32Kind or DestroyTicketKind => second == 0,
            DestroyEntityKind => true,
            CreateEntityKind => first == 0 && second == 0,
            >= RemoveEntityBase and < RemoveTicketBase or >= AddEntityBase and < AddTicketBase => true,
            >= RemoveTicketBase and < AddEntityBase or >= AddTicketBase and < AddTicketBase + MaximumComponentCommands => second == 0,
            >= EmitTypedBase and < ContributeBase + MaximumEventTypes => first == 0 && second == 0,
            _ => false
        };

    private void ValidatePackedRecord(ReadOnlySpan<uint> records, int ordinal, int recordWords,
        ReadOnlySpan<GaComponentRemoval> removals, ReadOnlySpan<GaComponentAddition> additions, bool usesDomainOrder,
        ReadOnlySpan<GaTypedEvent> eventTypes)
    {
        ValidateBindings(removals, additions, eventTypes);
        if (ReferencesTicket && ((uint)_first >= (uint)ordinal || records[_first * recordWords] != CreateEntityKind))
            throw new InvalidDataException("A GA ticket must reference an earlier Create in the same source.");
        var words = records.Slice(ordinal * recordWords, recordWords);
        if (usesDomainOrder)
        {
            var key = ReadDomainKey(words);
            if (ReferencesTicket && key.CompareTo(ReadDomainKey(records.Slice(_first * recordWords, recordWords))) < 0)
                throw new InvalidDataException("A GA ticket domain key cannot sort before its Create.");
        }
        var payload = words.Slice(Words, recordWords - Words - (usesDomainOrder ? DomainWordCount : 0));
        if (CommandKind is AddEntityBase or AddTicketBase)
            additions[ComponentIndex].Validate(payload);
        else if (IsTypedOutput)
            eventTypes[ComponentIndex].Validate(payload);
        else if (payload.IndexOfAnyExcept(0u) >= 0)
            throw new InvalidDataException("A GA output record has unexpected payload words.");
    }

    private static DeterministicDomainKey ReadDomainKey(ReadOnlySpan<uint> record)
    {
        var words = record[^DomainWordCount..];
        if (words[0] != 1u)
            throw new InvalidDataException("An ordered GA output must have a domain key.");
        return new DeterministicDomainKey(unchecked((int)words[1]), unchecked((int)words[2]));
    }

    private void ValidateBindings(ReadOnlySpan<GaComponentRemoval> removals, ReadOnlySpan<GaComponentAddition> additions,
        ReadOnlySpan<GaTypedEvent> eventTypes = default)
    {
        if (CommandKind is RemoveEntityBase or RemoveTicketBase &&
            (ComponentIndex >= removals.Length || !removals[ComponentIndex].IsBound))
            throw new InvalidDataException("A GA removal must reference a bound component type.");
        if (CommandKind is AddEntityBase or AddTicketBase &&
            (ComponentIndex >= additions.Length || !additions[ComponentIndex].IsBound))
            throw new InvalidDataException("A GA addition must reference a bound component type.");
        if (IsTypedOutput && (ComponentIndex >= eventTypes.Length || !eventTypes[ComponentIndex].IsBound ||
            eventTypes[ComponentIndex].IsContribution != (CommandKind == ContributeBase)))
            throw new InvalidDataException("A GA typed output must reference a bound payload type of the same kind.");
    }

    private ref struct Replay
    {
        private readonly DeterministicSource _source;
        private readonly int _count;
        private readonly ReadOnlySpan<GaComponentRemoval> _removals;
        private readonly ReadOnlySpan<GaComponentAddition> _additions;
        private readonly ReadOnlySpan<GaTypedEvent> _eventTypes;
        private CreateTicket[]? _tickets;

        internal Replay(DeterministicSource source, int count, ReadOnlySpan<GaComponentRemoval> removals,
            ReadOnlySpan<GaComponentAddition> additions, ReadOnlySpan<GaTypedEvent> eventTypes)
        {
            _source = source;
            _count = count;
            _removals = removals;
            _additions = additions;
            _eventTypes = eventTypes;
        }

        internal void Apply(GaOutputRecord record, int ordinal, ReadOnlySpan<uint> payload = default,
            DeterministicDomainKey? domain = null)
        {
            var source = domain is { } key ? _source.WithDomainOrder(key) : _source;
            switch (record._kind)
            {
                case CreateEntityKind:
                    (_tickets ??= new CreateTicket[_count])[ordinal] = source.Create();
                    break;
                case DestroyTicketKind:
                    source.Destroy(_tickets![record._first]);
                    break;
                default:
                    if (record.CommandKind == RemoveEntityBase)
                        _removals[record.ComponentIndex].Apply(source, record._first, record._second);
                    else if (record.CommandKind == RemoveTicketBase)
                        _removals[record.ComponentIndex].Apply(source, _tickets![record._first]);
                    else if (record.CommandKind == AddEntityBase)
                        _additions[record.ComponentIndex].Apply(source, record._first, record._second, payload);
                    else if (record.CommandKind == AddTicketBase)
                        _additions[record.ComponentIndex].Apply(source, _tickets![record._first], payload);
                    else if (record.IsTypedOutput)
                        _eventTypes[record.ComponentIndex].Apply(source, payload);
                    else
                        record.Apply(source);
                    break;
            }
        }
    }
}
