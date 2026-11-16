using System;

namespace SKECS;

/// <summary>Opt-in storage for an ordered prefix of one world's diagnostic events.</summary>
/// <remarks>Use the buffer and its world on one thread at a time. The producer
/// never invokes a consumer while recording. The buffer does not retain the world.</remarks>
public sealed class DiagnosticBuffer
{
    private readonly DiagnosticEvent[] _events;
    private bool _attached;

    public DiagnosticBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _events = new DiagnosticEvent[capacity];
    }

    public int Capacity => _events.Length;
    public int Count { get; private set; }
    public long DroppedEvents { get; private set; }
    public bool ChurnControlEnabled { get; private set; }

    public ref readonly DiagnosticEvent this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return ref _events[index];
        }
    }

    /// <summary>Records the start of a host frame.</summary>
    public void RecordFrameStart(int frame) => Record(new DiagnosticEvent(DiagnosticEventKind.FrameStarted, frame));

    /// <summary>Records the elapsed stopwatch ticks of a host frame.</summary>
    public void RecordFrameEnd(int frame, long elapsedTicks) =>
        Record(new DiagnosticEvent(DiagnosticEventKind.FrameCompleted, frame, value: elapsedTicks));

    /// <summary>Records elapsed stopwatch ticks for a named host operation.</summary>
    public void RecordDuration(string name, long elapsedTicks) =>
        Record(new DiagnosticEvent(DiagnosticEventKind.Duration, value: elapsedTicks, metadata: name));

    internal void Attach(int chunkCapacity, bool churnControlEnabled = false)
    {
        if (_attached) throw new InvalidOperationException("A diagnostic buffer can only be attached to one world.");
        _attached = true;
        ChurnControlEnabled = churnControlEnabled;
        Record(new DiagnosticEvent(DiagnosticEventKind.WorldStarted, chunkCapacity));
    }

    internal void DefineArchetype(int archetype, int count, int requestedNativeBytes) =>
        Record(new DiagnosticEvent(DiagnosticEventKind.ArchetypeDefined, archetype, count, value: requestedNativeBytes));

    internal void DefineColumn(int archetype, ComponentType type) =>
        Record(new DiagnosticEvent(DiagnosticEventKind.ArchetypeComponent, archetype, type.Size, type.Alignment,
            type.Id, metadata: type.RuntimeType));

    internal void AllocateChunk(int archetype, int chunk, long nativeBytes, long pinnedBytes, long backingId = 0) =>
        RecordBacking(DiagnosticEventKind.ChunkAllocated, archetype, chunk, backingId, nativeBytes, pinnedBytes);

    internal void ReleaseChunk(int archetype, int chunk) =>
        Record(new DiagnosticEvent(DiagnosticEventKind.ChunkReleased, archetype, chunk));

    internal void PoolChunk(int archetype, int chunk, long backingId) =>
        RecordBacking(DiagnosticEventKind.ChunkPooled, archetype, chunk, backingId);

    internal void ReuseChunk(int archetype, int chunk, long backingId) =>
        RecordBacking(DiagnosticEventKind.ChunkReused, archetype, chunk, backingId);

    internal void AllocatePooledChunk(long backingId, long nativeBytes, long pinnedBytes) =>
        RecordBacking(DiagnosticEventKind.PooledChunkAllocated, 0, 0, backingId, nativeBytes, pinnedBytes);

    internal void ReleasePooledChunk(long backingId) =>
        RecordBacking(DiagnosticEventKind.PooledChunkReleased, 0, 0, backingId);

    private void RecordBacking(DiagnosticEventKind kind, int archetype, int chunk,
        long backingId, long nativeBytes = 0, long pinnedBytes = 0) =>
        Record(new DiagnosticEvent(kind, archetype, chunk, unchecked((int)backingId), (int)(backingId >> 32),
            nativeBytes, pinnedBytes));

    internal void CreateRow(int archetype, int chunk, int entity) =>
        Record(new DiagnosticEvent(DiagnosticEventKind.RowCreated, archetype, chunk, entity));

    internal void DestroyRow(int archetype, int chunk, int entity, long swapBytes) =>
        Record(new DiagnosticEvent(DiagnosticEventKind.RowDestroyed, archetype, chunk, entity, value: swapBytes));

    internal void MoveRow(int archetype, int chunk, int targetArchetype, int targetChunk, long copiedBytes, long swapBytes) =>
        Record(new DiagnosticEvent(DiagnosticEventKind.RowMoved, archetype, chunk, targetArchetype, targetChunk,
            copiedBytes, swapBytes));

    internal void ResizeTable(DiagnosticTable table, int tableId, int capacity, long bytes, Type? componentType = null) =>
        Record(new DiagnosticEvent(DiagnosticEventKind.TableResized, (int)table, tableId, capacity,
            value: bytes, metadata: componentType));

    internal void DisposeWorld() => Record(new DiagnosticEvent(DiagnosticEventKind.WorldDisposed));

    private void Record(in DiagnosticEvent entry)
    {
        if (Count < _events.Length)
            _events[Count++] = entry;
        else if (DroppedEvents != long.MaxValue)
            DroppedEvents++;
    }
}

public enum DiagnosticEventKind
{
    WorldStarted,
    ArchetypeDefined,
    ArchetypeComponent,
    ChunkAllocated,
    ChunkReleased,
    RowCreated,
    RowDestroyed,
    RowMoved,
    TableResized,
    WorldDisposed,
    FrameStarted,
    FrameCompleted,
    Duration,
    ChunkPooled,
    ChunkReused,
    PooledChunkAllocated,
    PooledChunkReleased
}

public enum DiagnosticTable
{
    EntityRecords,
    FreeEntityIds,
    Accessor
}

/// <summary>A trace record. Fields are interpreted in the context of its kind.</summary>
public readonly struct DiagnosticEvent
{
    internal DiagnosticEvent(DiagnosticEventKind kind, int index = 0, int detail = 0, int relatedIndex = 0, int relatedDetail = 0,
        long value = 0, long secondary = 0, object? metadata = null)
    {
        Kind = kind;
        Index = index;
        Detail = detail;
        RelatedIndex = relatedIndex;
        RelatedDetail = relatedDetail;
        Value = value;
        Secondary = secondary;
        Metadata = metadata;
    }

    public DiagnosticEventKind Kind { get; }
    private int Index { get; }
    private int Detail { get; }
    private int RelatedIndex { get; }
    private int RelatedDetail { get; }
    private long Value { get; }
    private long Secondary { get; }
    private object? Metadata { get; }

    // Each property is meaningful only for the corresponding event kinds.
    public int ChunkCapacity => Index;
    public int ArchetypeId => Index;
    public int ComponentCount => Detail;
    public long RequestedNativeBytes => Value;
    public Type? ComponentType => Metadata as Type;
    public int ComponentId => RelatedDetail;
    public int ComponentSize => Detail;
    public int ComponentAlignment => RelatedIndex;
    public int ChunkIndex => Detail;
    public long BackingId => ((long)RelatedDetail << 32) | (uint)RelatedIndex;
    public long NativeBytes => Value;
    public long PinnedBytes => Secondary;
    public int EntityId => RelatedIndex;
    public int TargetArchetypeId => RelatedIndex;
    public int TargetChunkIndex => RelatedDetail;
    public long CopiedBytes => Value;
    public long SwapBytes => Kind == DiagnosticEventKind.RowMoved ? Secondary : Value;
    public DiagnosticTable Table => (DiagnosticTable)Index;
    public int TableId => Detail;
    public int TableCapacity => RelatedIndex;
    public long TablePayloadBytes => Value;
    public int Frame => Index;
    public long ElapsedTicks => Value;
    public string? MeasurementName => Metadata as string;
}
