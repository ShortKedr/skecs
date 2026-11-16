namespace SKECS.GA;

/// <summary>A staged event or structural command emitted by a compiled GA invocation.</summary>
public readonly record struct GaOutputRecord
{
    public const int Words = 3;
    public const uint EmitInt32Kind = 1;
    public const uint DestroyEntityKind = 2;
    public const uint CreateEntityKind = 3;
    public const uint DestroyTicketKind = 4;

    private readonly uint _kind;
    private readonly int _first;
    private readonly int _second;

    private GaOutputRecord(uint kind, int first, int second) =>
        (_kind, _first, _second) = (kind, first, second);

    public static GaOutputRecord EmitInt32(int value) => new(EmitInt32Kind, value, 0);

    public static GaOutputRecord DestroyEntity(int entityId, int generation) =>
        new(DestroyEntityKind, entityId, generation);

    public static GaOutputRecord CreateEntity() => new(CreateEntityKind, 0, 0);

    public static GaOutputRecord DestroyTicket(int createOrdinal) => new(DestroyTicketKind, createOrdinal, 0);

    /// <summary>Replays the outputs of one invocation into the runner's source stream.</summary>
    public static void ApplySource(DeterministicSource source, ReadOnlySpan<GaOutputRecord> records)
    {
        for (int ordinal = 0; ordinal < records.Length; ordinal++)
        {
            var record = records[ordinal];
            if (!IsCanonical(record._kind, record._first, record._second))
                throw new InvalidDataException("Invalid GA output record.");
            if (record._kind == DestroyTicketKind &&
                ((uint)record._first >= (uint)ordinal || records[record._first]._kind != CreateEntityKind))
                throw new InvalidDataException("A GA ticket must reference an earlier Create in the same source.");
        }

        var replay = new Replay(source, records.Length);
        for (int ordinal = 0; ordinal < records.Length; ordinal++)
            replay.Apply(records[ordinal], ordinal);
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
            _ => false
        };

    internal void ValidateTicket(ReadOnlySpan<uint> records, int ordinal)
    {
        if (_kind == DestroyTicketKind && ((uint)_first >= (uint)ordinal || records[_first * Words] != CreateEntityKind))
            throw new InvalidDataException("A GA ticket must reference an earlier Create in the same source.");
    }

    internal ref struct Replay
    {
        private readonly DeterministicSource _source;
        private readonly int _count;
        private CreateTicket[]? _tickets;

        internal Replay(DeterministicSource source, int count)
        {
            _source = source;
            _count = count;
        }

        internal void Apply(GaOutputRecord record, int ordinal)
        {
            switch (record._kind)
            {
                case CreateEntityKind:
                    (_tickets ??= new CreateTicket[_count])[ordinal] = _source.Create();
                    break;
                case DestroyTicketKind:
                    _source.Destroy(_tickets![record._first]);
                    break;
                default:
                    record.Apply(_source);
                    break;
            }
        }
    }
}
