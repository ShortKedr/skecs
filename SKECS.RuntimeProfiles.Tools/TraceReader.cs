using System.Xml;

namespace SKECS.RuntimeProfiles.Tools;

internal sealed record TraceHeader(int SchemaVersion, string Scenario, string WorkloadId, string Runtime, string Os,
    string Architecture, long StopwatchFrequency, int RecordedEvents);

internal readonly record struct TraceSlot(int Archetype, int Chunk);
internal readonly record struct TraceComponent(string Assembly, string Type, int Size, int Alignment);
internal sealed record TraceLayout(int NativeBytes, TraceComponent[] Components);
internal readonly record struct MemoryPeak(int Frame, long OccupiedNative, long OccupiedPinned, long EntityRecords,
    long FreeIds, long Accessors)
{
    public long Total => checked(OccupiedNative + OccupiedPinned + EntityRecords + FreeIds + Accessors);
}

internal enum TraceAction { Occupy, Empty, Tables, Trim }
internal readonly record struct TraceChange(TraceAction Action, TraceSlot Slot, long Bytes, int Frame);

internal sealed record ProfilerTrace(TraceHeader Header, int ChunkCapacity, int Frames,
    IReadOnlyList<TraceLayout> Layouts, IReadOnlyList<TraceChange> Changes, MemoryPeak LowerBound,
    long PeakRetained, int EarlyEntityCapacity, long TableBytesPerEntity);

internal static class TraceReader
{
    internal static ProfilerTrace Read(Stream stream)
    {
        using var xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        try
        {
            Require(xml.MoveToContent() == XmlNodeType.Element && xml.Name == "skecs-profiler-trace" &&
                xml.NamespaceURI.Length == 0, "Expected a SKECS Profiler trace.");
            var version = Int(xml, "schemaVersion", 1);
            Require(version is 1 or 2, $"Unsupported Profiler trace version {version}.");
            var dropped = Number(xml, "droppedEvents");
            Require(dropped == 0, $"Trace lost {dropped} events.");
            var header = new TraceHeader(version, Required(xml, "scenario"), Required(xml, "workloadId"),
                Required(xml, "runtime"), Required(xml, "os"), Required(xml, "architecture"),
                Number(xml, "stopwatchFrequency", 1), Int(xml, "recordedEvents", 1));
            var trace = new Builder(header);
            Require(!xml.IsEmptyElement, "Trace has no events.");
            var count = 0;
            xml.ReadStartElement();
            while (xml.MoveToContent() == XmlNodeType.Element)
            {
                Require(xml.Name == "event" && xml.NamespaceURI.Length == 0 && xml.IsEmptyElement,
                    "Expected an empty Profiler event element.");
                Require(count++ < header.RecordedEvents, "Trace contains more events than declared.");
                trace.ReadEvent(xml, count);
                xml.Read();
            }
            xml.ReadEndElement();
            Require(xml.MoveToContent() == XmlNodeType.None && count == header.RecordedEvents,
                "Trace is truncated or contains unexpected content.");
            return trace.Finish();
        }
        catch (Exception error) when (error is XmlException or OverflowException or FormatException)
        {
            throw new InvalidDataException("Invalid SKECS Profiler trace.", error);
        }
    }

    private sealed class Builder(TraceHeader header)
    {
        private readonly Dictionary<int, Archetype> _archetypes = new();
        private readonly Dictionary<int, TraceComponent> _components = new();
        private readonly Dictionary<(string Assembly, string Type), int> _componentIds = new();
        private readonly Dictionary<TraceSlot, Chunk> _chunks = new();
        private readonly Dictionary<long, Chunk> _pool = new();
        private readonly HashSet<long> _backingIds = new();
        private readonly Dictionary<(string Table, int Id), (int Capacity, long Bytes)> _tables = new();
        private readonly List<TraceChange> _changes = new();
        private int _capacity;
        private int _frames;
        private bool _frameOpen;
        private bool _teardown;
        private bool _disposed;
        private long _native;
        private long _pinned;
        private long _entityRecords;
        private long _freeIds;
        private long _accessors;
        private long _retainedChunks;
        private long _peakRetained;
        private int _earlyEntityCapacity;
        private MemoryPeak _peak = new(-1, 0, 0, 0, 0, 0);

        private int Frame => _frameOpen ? _frames : _frames - 1;
        private long TableBytes => checked(_entityRecords + _freeIds + _accessors);

        internal void ReadEvent(XmlReader xml, int index)
        {
            var kind = Required(xml, "kind");
            Require(!_disposed && (index != 1 || kind == "worldStarted") &&
                (!_teardown || kind is "chunkReleased" or "pooledChunkReleased" or "worldDisposed"),
                $"Unexpected {kind} at event {index}.");
            switch (kind)
            {
                case "worldStarted":
                    Require(index == 1, "A trace must contain one world.");
                    _capacity = Int(xml, "chunkCapacity", 1);
                    break;
                case "archetypeDefined":
                    Require(_archetypes.TryAdd(Int(xml, "archetype"),
                        new Archetype(Int(xml, "componentCount"), Int(xml, "requestedNativeBytes", 1))),
                        "Duplicate archetype definition.");
                    break;
                case "archetypeComponent":
                    ReadComponent(xml);
                    break;
                case "chunkAllocated":
                    Allocate(xml);
                    break;
                case "chunkReleased":
                    Require(!_frameOpen, "A chunk released from a slot requires world teardown.");
                    if (!_chunks.Remove(Slot(xml), out var released)) throw new InvalidDataException("Undefined released chunk.");
                    Require(header.SchemaVersion == 1 || released.Rows != 0, "An empty controlled chunk must return to the pool.");
                    _retainedChunks -= released.Native + released.Pinned;
                    _teardown = true;
                    break;
                case "pooledChunkAllocated":
                    Require(header.SchemaVersion == 2 && _frames == 0 && !_frameOpen, "Unexpected startup backing.");
                    var reserved = NewBacking(xml);
                    _pool.Add(reserved.Id, reserved);
                    break;
                case "chunkPooled":
                    Require(header.SchemaVersion == 2, "Pooled slots require trace version 2.");
                    if (!_chunks.Remove(Slot(xml), out var pooled) || pooled.Rows != 0 || pooled.Id != Number(xml, "backing", 1))
                        throw new InvalidDataException("Invalid returned backing or occupied chunk.");
                    _pool.Add(pooled.Id, pooled);
                    break;
                case "chunkReused":
                    Require(header.SchemaVersion == 2, "Reused slots require trace version 2.");
                    var reused = Number(xml, "backing", 1);
                    if (!_pool.Remove(reused, out var backing)) throw new InvalidDataException("Reused backing was not pooled.");
                    Attach(Slot(xml), backing, reused: true);
                    break;
                case "pooledChunkReleased":
                    Require(header.SchemaVersion == 2, "Pool releases require trace version 2.");
                    if (!_pool.Remove(Number(xml, "backing", 1), out var cold)) throw new InvalidDataException("Released backing was not pooled.");
                    _retainedChunks -= cold.Native + cold.Pinned;
                    break;
                case "rowCreated":
                    _ = Int(xml, "entity");
                    if (Occupy(Slot(xml))) Maintenance();
                    break;
                case "rowDestroyed":
                    _ = Int(xml, "entity");
                    _ = Number(xml, "swapBytes");
                    if (Vacate(Slot(xml))) Maintenance();
                    break;
                case "rowMoved":
                    var source = Slot(xml);
                    var destination = new TraceSlot(Int(xml, "targetArchetype"), Int(xml, "targetChunk"));
                    Require(source.Archetype != destination.Archetype, "A move must change archetype.");
                    _ = Number(xml, "copiedBytes");
                    _ = Number(xml, "swapBytes");
                    var acquired = Occupy(destination);
                    var emptied = Vacate(source);
                    if (acquired || emptied) Maintenance();
                    break;
                case "tableResized":
                    Resize(xml);
                    break;
                case "frameStarted":
                    Require(!_frameOpen && Int(xml, "frame") == _frames, "Invalid frame start.");
                    _frameOpen = true;
                    break;
                case "frameCompleted":
                    Require(_frameOpen && Int(xml, "frame") == _frames, "Invalid frame completion.");
                    _ = Number(xml, "elapsedTicks");
                    Maintenance();
                    _frames++;
                    _frameOpen = false;
                    break;
                case "duration":
                    _ = Required(xml, "name");
                    _ = Number(xml, "elapsedTicks");
                    break;
                case "worldDisposed":
                    Require(!_frameOpen && _chunks.Count == 0 && _pool.Count == 0 && _retainedChunks == 0,
                        "World disposed with missing backing releases or an open frame.");
                    _disposed = true;
                    break;
                default:
                    throw new InvalidDataException($"Unsupported Profiler event {kind}.");
            }
        }

        private void ReadComponent(XmlReader xml)
        {
            var archetype = GetArchetype(Int(xml, "archetype"));
            var id = Int(xml, "componentId");
            var component = new TraceComponent(Required(xml, "assembly"), Required(xml, "type"),
                Int(xml, "size", 1), Int(xml, "alignment", 1));
            Require(component.Alignment == Math.Min(16, component.Size & -component.Size) &&
                archetype.Components.Count < archetype.ComponentCount && !archetype.Components.Contains(component),
                "Invalid component layout.");
            Require(!_components.TryGetValue(id, out var previous) || previous == component,
                "Inconsistent component ID in trace.");
            var key = (component.Assembly, component.Type);
            Require(!_componentIds.TryGetValue(key, out var previousId) || previousId == id,
                "Duplicate stable component identity in trace.");
            _components[id] = component;
            _componentIds[key] = id;
            archetype.Components.Add(component);
        }

        private void Allocate(XmlReader xml) => Attach(Slot(xml), NewBacking(xml), reused: false);

        private Chunk NewBacking(XmlReader xml)
        {
            var native = Int(xml, "nativeBytes", 1);
            var pinned = Number(xml, "pinnedBytes", 1);
            var id = header.SchemaVersion == 2 ? Number(xml, "backing", 1) : 0;
            Require((id > 0 ? _backingIds.Add(id) : xml.GetAttribute("backing") == null) && pinned == 8L * _capacity,
                "Invalid backing identity or pinned payload.");
            _retainedChunks = checked(_retainedChunks + native + pinned);
            Observe();
            return new Chunk(id, native, pinned);
        }

        private void Attach(TraceSlot slot, Chunk backing, bool reused)
        {
            var archetype = GetArchetype(slot.Archetype);
            Require(archetype.Components.Count == archetype.ComponentCount &&
                (reused ? backing.Native >= archetype.NativeBytes : backing.Native == archetype.NativeBytes) && _chunks.TryAdd(slot, backing),
                "Backing does not fit the archetype or its destination slot is in use.");
        }

        private bool Occupy(TraceSlot slot)
        {
            var chunk = GetChunk(slot);
            Require(chunk.Rows < _capacity, "Row added to a full chunk.");
            if (chunk.Rows++ != 0) return false;
            _native = checked(_native + chunk.Native);
            _pinned = checked(_pinned + chunk.Pinned);
            if (header.SchemaVersion == 1) _changes.Add(new(TraceAction.Occupy, slot, chunk.Native, Frame));
            Observe();
            return true;
        }

        private bool Vacate(TraceSlot slot)
        {
            var chunk = GetChunk(slot);
            Require(chunk.Rows > 0, "Row removed from an empty chunk.");
            if (--chunk.Rows != 0) return false;
            _native -= chunk.Native;
            _pinned -= chunk.Pinned;
            if (header.SchemaVersion == 1) _changes.Add(new(TraceAction.Empty, slot, chunk.Native, Frame));
            return true;
        }

        private void Maintenance()
        {
            if (header.SchemaVersion == 1) _changes.Add(new(TraceAction.Trim, default, 0, Frame));
        }

        private void Resize(XmlReader xml)
        {
            var table = Required(xml, "table");
            var id = Int(xml, "tableId");
            var capacity = Int(xml, "capacity", 1);
            var bytes = Number(xml, "payloadBytes", 1);
            Require(bytes % capacity == 0 && (table == "accessor" || id == 0), "Invalid table size or ID.");
            if (table == "accessor")
            {
                var key = (Required(xml, "assembly"), Required(xml, "type"));
                Require(!_componentIds.TryGetValue(key, out var componentId) || componentId == id,
                    "Inconsistent accessor component identity.");
            }
            _tables.TryGetValue((table, id), out var previous);
            Require(previous.Capacity < capacity && (previous.Capacity == 0 ||
                previous.Bytes / previous.Capacity == bytes / capacity), "Invalid table growth.");
            _tables[(table, id)] = (capacity, bytes);
            var growth = bytes - previous.Bytes;
            switch (table)
            {
                case "entityRecords":
                    if (Frame <= 0) _earlyEntityCapacity = capacity;
                    _entityRecords = checked(_entityRecords + growth);
                    break;
                case "freeEntityIds": _freeIds = checked(_freeIds + growth); break;
                case "accessor": _accessors = checked(_accessors + growth); break;
                default: throw new InvalidDataException($"Unrecognized table {table}.");
            }
            if (header.SchemaVersion == 1) _changes.Add(new(TraceAction.Tables, default, TableBytes, Frame));
            Observe();
        }

        private void Observe()
        {
            var memory = new MemoryPeak(Frame, _native, _pinned, _entityRecords, _freeIds, _accessors);
            if (memory.Total > _peak.Total) _peak = memory;
            _peakRetained = Math.Max(_peakRetained, checked(_retainedChunks + TableBytes));
        }

        internal ProfilerTrace Finish()
        {
            Require(_disposed && _frames > 0 && _archetypes.TryGetValue(0, out var root) && root.ComponentCount == 0,
                "Trace requires a complete world lifecycle, root archetype and completed frames.");
            foreach (var archetype in _archetypes.Values)
            {
                long offset = 0;
                foreach (var component in archetype.Components)
                    offset = checked(Align(offset, component.Alignment) + (long)component.Size * _capacity);
                Require(archetype.Components.Count == archetype.ComponentCount &&
                    archetype.NativeBytes == (offset == 0 ? 1 : Align(offset, 16) + 15), "Incomplete or inconsistent archetype layout.");
            }
            var layouts = _archetypes.Values.Select(archetype => new TraceLayout(archetype.NativeBytes,
                archetype.Components.OrderBy(component => component.Assembly, StringComparer.Ordinal)
                    .ThenBy(component => component.Type, StringComparer.Ordinal).ToArray())).ToArray();
            return new(header, _capacity, _frames, layouts, _changes, _peak, _peakRetained,
                _earlyEntityCapacity, _tables.Values.Sum(table => table.Bytes / table.Capacity));
        }

        private static long Align(long value, int alignment) => checked(value + alignment - 1) & -(long)alignment;
        private Archetype GetArchetype(int id) => _archetypes.TryGetValue(id, out var archetype) ? archetype :
            throw new InvalidDataException($"Undefined archetype {id}.");
        private Chunk GetChunk(TraceSlot slot) => _chunks.TryGetValue(slot, out var chunk) ? chunk :
            throw new InvalidDataException($"Undefined chunk {slot}.");

        private sealed record Archetype(int ComponentCount, int NativeBytes)
        {
            internal List<TraceComponent> Components { get; } = new();
        }

        private sealed class Chunk(long id, int native, long pinned)
        {
            internal long Id { get; } = id;
            internal int Native { get; } = native;
            internal long Pinned { get; } = pinned;
            internal int Rows { get; set; }
        }
    }

    private static TraceSlot Slot(XmlReader xml) => new(Int(xml, "archetype"), Int(xml, "chunk"));

    private static string Required(XmlReader xml, string name) => xml.GetAttribute(name) is { } value &&
        !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"Missing trace attribute {name}.");

    private static int Int(XmlReader xml, string name, int minimum = 0) => checked((int)Number(xml, name, minimum));

    private static long Number(XmlReader xml, string name, long minimum = 0)
    {
        var value = XmlConvert.ToInt64(Required(xml, name));
        Require(value >= minimum, $"Invalid trace attribute {name}.");
        return value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
