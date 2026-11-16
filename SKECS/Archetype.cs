using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SKECS;

internal sealed unsafe class Archetype : IDisposable
{
    private readonly World _world;
    private readonly int[] _columnByType;
    private readonly int[] _offsetByType;
    private readonly int[] _offsets;
    private readonly int _chunkCapacity;
    private readonly int _allocationSize;
    private readonly Dictionary<int, Transition> _addEdges = new();
    private readonly Dictionary<int, Transition> _removeEdges = new();
    private readonly List<Chunk> _chunks = new();
    private readonly List<Chunk> _chunkSlots;
    private readonly List<int> _availableChunks = new();
    private readonly LinkedList<ChurnChunkPool.Backing>? _localReserve;
    private int _vacantSlot = -1;

    public Archetype(World world, ComponentSetKey key, ComponentType[] componentTypes)
    {
        _world = world;
        _chunkCapacity = world.ChunkCapacity;
        if (world.UsesChurnControl)
        {
            _chunkSlots = new List<Chunk>();
            _localReserve = new LinkedList<ChurnChunkPool.Backing>();
        }
        else
        {
            _chunkSlots = _chunks;
        }
        Key = key;
        ComponentTypes = componentTypes;
        var maxTypeId = -1;
        foreach (var type in componentTypes) maxTypeId = Math.Max(maxTypeId, type.Id);
        _columnByType = new int[maxTypeId + 1];
        _offsetByType = new int[maxTypeId + 1];
        for (var i = 0; i < _columnByType.Length; i++)
        {
            _columnByType[i] = -1;
            _offsetByType[i] = -1;
        }
        _offsets = new int[componentTypes.Length];

        var offset = 0;
        for (var i = 0; i < componentTypes.Length; i++)
        {
            _columnByType[componentTypes[i].Id] = i;
            offset = Align(offset, componentTypes[i].Alignment);
            _offsets[i] = offset;
            _offsetByType[componentTypes[i].Id] = offset;
            offset = checked(offset + componentTypes[i].Size * world.ChunkCapacity);
        }

        _allocationSize = offset == 0 ? 1 : checked(Align(offset, ChunkMemory.Alignment) + (ChunkMemory.Alignment - 1));
    }

    public ComponentSetKey Key { get; }

    public ComponentType[] ComponentTypes { get; }

    internal int DiagnosticId { get; set; }

    internal long DiagnosticRowBytes { get; set; }

    internal int ChunkAllocationSize => _allocationSize;

    internal long NextCheckpointChunkBytes => _availableChunks.Count != 0
        ? 0 : _allocationSize + 8L * _world.ChunkCapacity + (_localReserve == null ? 128 : 512);

    public List<Chunk> Chunks => _chunks;

    public bool HasComponent(int componentTypeId) => (uint)componentTypeId < (uint)_columnByType.Length && _columnByType[componentTypeId] >= 0;

    internal int ComponentSize(int typeId) => ComponentTypes[_columnByType[typeId]].Size;

    public Transition GetAddTransition(int componentTypeId)
    {
        if (!_addEdges.TryGetValue(componentTypeId, out var transition))
        {
            transition = new Transition(this, _world.GetArchetype(Key.Add(componentTypeId)));
            _addEdges.Add(componentTypeId, transition);
        }

        return transition;
    }

    public Transition GetRemoveTransition(int componentTypeId)
    {
        if (!_removeEdges.TryGetValue(componentTypeId, out var transition))
        {
            transition = new Transition(this, _world.GetArchetype(Key.Remove(componentTypeId)));
            _removeEdges.Add(componentTypeId, transition);
        }

        return transition;
    }

    public World.EntitySlot AddEntity(Entity entity)
    {
        var slot = ReserveEntity(out _);
        PublishEntity(slot, entity);
        return slot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal World.EntitySlot ReserveEntity(out bool acquiredBacking)
    {
        acquiredBacking = _availableChunks.Count == 0;
        if (acquiredBacking) AcquireBacking();

        var chunkIndex = _availableChunks[_availableChunks.Count - 1];
        var chunk = _chunkSlots[chunkIndex];
        return new World.EntitySlot(chunk, chunk.Count);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AcquireBacking()
    {
        // Reserve capacity for this slot's future full-to-partial transition before attaching memory.
        EnsureRoom(_availableChunks, _chunkSlots.Count + (_vacantSlot < 0 ? 1 : 0));
        if (_localReserve != null) AttachBacking(_world.ChurnPool!, _localReserve);
        else AllocateLegacyChunk();
    }

    private void AllocateLegacyChunk()
    {
        var index = _chunks.Count;
        EnsureRoom(_chunks, index + 1);
        var chunk = new Chunk(index, _offsetByType);
        chunk.Allocate(_allocationSize, _world.ChunkCapacity);
        _chunks.Add(chunk);
        AddAvailable(chunk);
        _world.DiagnosticsBuffer?.AllocateChunk(DiagnosticId, index, _allocationSize,
            (long)_world.ChunkCapacity * 2 * sizeof(int));
    }

    private void AttachBacking(ChurnChunkPool pool, LinkedList<ChurnChunkPool.Backing> localReserve)
    {
        EnsureRoom(_chunks, _chunks.Count + 1);
        var vacant = _vacantSlot;
        Chunk chunk;
        if (vacant < 0)
        {
            EnsureRoom(_chunkSlots, _chunkSlots.Count + 1);
            chunk = new Chunk(_chunkSlots.Count, _offsetByType);
        }
        else
        {
            chunk = _chunkSlots[vacant];
        }

        var backing = pool.Rent(_allocationSize, localReserve, out var reused);
        if (vacant < 0) _chunkSlots.Add(chunk);
        else _vacantSlot = chunk.NextVacantSlot;
        chunk.Attach(backing, _chunks.Count);
        _chunks.Add(chunk);
        AddAvailable(chunk);
        if (_world.DiagnosticsBuffer is { } diagnostics)
        {
            if (reused) diagnostics.ReuseChunk(DiagnosticId, chunk.SlotIndex, backing.Id);
            else diagnostics.AllocateChunk(DiagnosticId, chunk.SlotIndex, backing.NativeBytes,
                (long)_world.ChunkCapacity * 2 * sizeof(int), backing.Id);
        }
    }

    private static void EnsureRoom<T>(List<T> list, int required)
    {
        if (list.Capacity < required) list.Capacity = Math.Max(required, Math.Max(4, list.Capacity * 2));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PublishEntity(World.EntitySlot slot, Entity entity)
    {
        var chunk = slot.Chunk;
        chunk.Entities[slot.RowIndex] = entity.Id;
        chunk.Versions[slot.RowIndex] = entity.Version;
        if (++chunk.Count == _chunkCapacity) _availableChunks.RemoveAt(_availableChunks.Count - 1);
    }

    internal bool ReturnIfEmpty(int chunkIndex)
    {
        if (_localReserve == null) return false;
        var chunk = _chunkSlots[chunkIndex];
        if (chunk.Count != 0) return false;
        RemoveAvailable(chunk);

        // Only the enumeration index of the last active chunk changes. Its entity records use SlotIndex.
        var last = _chunks.Count - 1;
        var moved = _chunks[last];
        _chunks[chunk.ActiveIndex] = moved;
        moved.ActiveIndex = chunk.ActiveIndex;
        _chunks.RemoveAt(last);

        var backing = chunk.Detach();
        chunk.NextVacantSlot = _vacantSlot;
        _vacantSlot = chunkIndex;
        _world.ChurnPool!.Return(backing, _localReserve);
        _world.DiagnosticsBuffer?.PoolChunk(DiagnosticId, chunkIndex, backing.Id);
        return true;
    }

    private void AddAvailable(Chunk chunk)
    {
        chunk.AvailableIndex = _availableChunks.Count;
        _availableChunks.Add(chunk.SlotIndex);
    }

    private void RemoveAvailable(Chunk chunk)
    {
        var last = _availableChunks.Count - 1;
        var availableIndex = chunk.AvailableIndex;
        if (availableIndex != last)
        {
            var moved = _availableChunks[last];
            _availableChunks[availableIndex] = moved;
            _chunkSlots[moved].AvailableIndex = availableIndex;
        }
        _availableChunks.RemoveAt(last);
    }

    public RemovalResult RemoveAt(int chunkIndex, int rowIndex)
    {
        var chunk = _chunkSlots[chunkIndex];
        if (chunk.Count == _chunkCapacity) AddAvailable(chunk);
        var lastRow = chunk.Count - 1;

        if (rowIndex != lastRow)
        {
            chunk.Entities[rowIndex] = chunk.Entities[lastRow];
            chunk.Versions[rowIndex] = chunk.Versions[lastRow];

            for (var i = 0; i < ComponentTypes.Length; i++)
            {
                var size = ComponentTypes[i].Size;
                CopyComponent(chunk.Memory + _offsets[i] + lastRow * size,
                    chunk.Memory + _offsets[i] + rowIndex * size, size);
            }
        }

        var movedEntityId = rowIndex == lastRow ? -1 : chunk.Entities[rowIndex];
        chunk.Count = lastRow;

        return new RemovalResult(this, movedEntityId, chunkIndex, rowIndex, lastRow == 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponentRef<T>(int chunkIndex, int rowIndex, int componentTypeId)
        where T : unmanaged
    {
        var pointer = (T*)GetComponentPointer(chunkIndex, componentTypeId);
        return ref pointer[rowIndex];
    }

    public T ReadComponent<T>(int chunkIndex, int rowIndex, int componentTypeId)
        where T : unmanaged
    {
        return ((T*)GetComponentPointer(chunkIndex, componentTypeId))[rowIndex];
    }

    public void WriteComponent<T>(int chunkIndex, int rowIndex, int componentTypeId, in T component)
        where T : unmanaged
    {
        ((T*)GetComponentPointer(chunkIndex, componentTypeId))[rowIndex] = component;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryWriteComponent<T>(int chunkIndex, int rowIndex, int componentTypeId, in T component)
        where T : unmanaged
    {
        if ((uint)componentTypeId >= (uint)_offsetByType.Length) return false;
        var offset = _offsetByType[componentTypeId];
        if (offset < 0) return false;
        ((T*)(_chunkSlots[chunkIndex].Memory + offset))[rowIndex] = component;
        return true;
    }

    public void Dispose()
    {
        for (var i = 0; i < _chunks.Count; i++)
        {
            var chunk = _chunks[i];
            if (_localReserve != null) _world.ChurnPool!.Release(chunk.Detach());
            else chunk.Dispose();
            _world.DiagnosticsBuffer?.ReleaseChunk(DiagnosticId, chunk.SlotIndex);
        }
    }

    public long NativeBytes => (long)_allocationSize * _chunks.Count;

    internal sealed class Transition
    {
        private readonly Archetype _source;
        private readonly CopyColumn[] _columns;
        public Archetype Destination { get; }
        internal long DiagnosticCopiedBytes { get; }

        public Transition(Archetype source, Archetype destination)
        {
            _source = source;
            Destination = destination;
            var columns = new List<CopyColumn>(source.ComponentTypes.Length);
            for (var i = 0; i < source.ComponentTypes.Length; i++)
            {
                var type = source.ComponentTypes[i];
                if (destination.HasComponent(type.Id))
                    columns.Add(new CopyColumn(source._offsets[i], destination._offsets[destination._columnByType[type.Id]], type.Size));
            }
            _columns = columns.ToArray();
            if (source._world.DiagnosticsBuffer != null)
            {
                foreach (var column in _columns) DiagnosticCopiedBytes += column.Size;
            }
        }

        public void Copy(int sourceChunkIndex, int sourceRowIndex, World.EntitySlot destinationSlot)
        {
            var source = _source._chunkSlots[sourceChunkIndex].Memory;
            var target = destinationSlot.Chunk.Memory;
            foreach (var column in _columns)
            {
                CopyComponent(source + column.SourceOffset + sourceRowIndex * column.Size,
                    target + column.DestinationOffset + destinationSlot.RowIndex * column.Size, column.Size);
            }
        }

        private readonly struct CopyColumn
        {
            public CopyColumn(int sourceOffset, int destinationOffset, int size)
            { SourceOffset = sourceOffset; DestinationOffset = destinationOffset; Size = size; }
            public readonly int SourceOffset;
            public readonly int DestinationOffset;
            public readonly int Size;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyComponent(byte* source, byte* destination, int size)
    {
#if NET8_0_OR_GREATER
        // Small columns are common in transitions; avoid a general-purpose memmove per field.
        switch (size)
        {
            case 4: Unsafe.WriteUnaligned(destination, Unsafe.ReadUnaligned<int>(source)); break;
            case 8: Unsafe.WriteUnaligned(destination, Unsafe.ReadUnaligned<long>(source)); break;
            case 12:
                Unsafe.WriteUnaligned(destination, Unsafe.ReadUnaligned<long>(source));
                Unsafe.WriteUnaligned(destination + 8, Unsafe.ReadUnaligned<int>(source + 8));
                break;
            default: Buffer.MemoryCopy(source, destination, size, size); break;
        }
#else
        Buffer.MemoryCopy(source, destination, size, size);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int GetComponentOffset(int componentTypeId)
    {
        if ((uint)componentTypeId >= (uint)_offsetByType.Length || _offsetByType[componentTypeId] < 0)
            throw MissingComponent(componentTypeId);
        return _offsetByType[componentTypeId];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static InvalidOperationException MissingComponent(int componentTypeId) =>
        new InvalidOperationException($"Component {componentTypeId} is not in archetype.");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void* GetComponentPointer(int chunkIndex, int componentTypeId)
    {
        var offset = GetComponentOffset(componentTypeId);
        return _chunkSlots[chunkIndex].Memory + offset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align(int value, int alignment)
    {
        var mask = alignment - 1;
        return checked(value + mask) & ~mask;
    }

    internal readonly struct RemovalResult
    {
        public RemovalResult(Archetype source, int movedEntityId, int movedChunkIndex, int movedRowIndex, bool becameEmpty)
        {
            Source = source;
            MovedEntityId = movedEntityId;
            MovedChunkIndex = movedChunkIndex;
            MovedRowIndex = movedRowIndex;
            BecameEmpty = becameEmpty;
        }

        public Archetype? Source { get; }

        public int MovedEntityId { get; }

        public int MovedChunkIndex { get; }

        public int MovedRowIndex { get; }

        public bool BecameEmpty { get; }
    }

    internal sealed unsafe class Chunk : IDisposable
    {
        private readonly int[] _offsetByType;
        private ChunkMemory _ownedMemory;
        private int* _pinnedEntities;
        private int* _pinnedVersions;
        private ChurnChunkPool.Backing? _backing;

        public Chunk(int slotIndex, int[] offsetByType)
        {
            SlotIndex = slotIndex;
            _offsetByType = offsetByType;
        }

        internal void Allocate(int bytes, int capacity)
        {
            _ownedMemory.Allocate(bytes, capacity);
            BindMemory(in _ownedMemory);
        }

        internal void Attach(ChurnChunkPool.Backing backing, int activeIndex)
        {
            _backing = backing;
            ActiveIndex = activeIndex;
            BindMemory(in backing.Memory);
        }

        private void BindMemory(in ChunkMemory memory)
        {
            Memory = memory.Address;
            Entities = memory.Entities;
            Versions = memory.Versions;
            _pinnedEntities = memory.PinnedEntities;
            _pinnedVersions = memory.PinnedVersions;
        }

        internal ChurnChunkPool.Backing Detach()
        {
            var backing = _backing!;
            _backing = null;
            Memory = null;
            Entities = null!;
            Versions = null!;
            _pinnedEntities = null;
            _pinnedVersions = null;
            return backing;
        }

        public byte* Memory { get; private set; }

        public int Count { get; set; }

        internal int SlotIndex { get; }
        internal int ActiveIndex { get; set; }
        internal int AvailableIndex { get; set; }
        internal int NextVacantSlot { get; set; }
        public int[] Entities { get; private set; } = null!;
        public int[] Versions { get; private set; } = null!;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void* GetComponentPointer(int componentTypeId)
        {
            if ((uint)componentTypeId >= (uint)_offsetByType.Length || _offsetByType[componentTypeId] < 0)
                throw new InvalidOperationException($"Component {componentTypeId} is not in chunk.");
            return Memory + _offsetByType[componentTypeId];
        }

        public RawChunkView AsRawView()
        {
            return new RawChunkView(Count, _pinnedEntities, _pinnedVersions);
        }

        public QueryChunkView<T1> AsView<T1>(int component1Id)
            where T1 : unmanaged
        {
            return new QueryChunkView<T1>(
                Count,
                _pinnedEntities,
                _pinnedVersions,
                (T1*)GetComponentPointer(component1Id));
        }

        public QueryChunkView<T1, T2> AsView<T1, T2>(int component1Id, int component2Id)
            where T1 : unmanaged
            where T2 : unmanaged
        {
            return new QueryChunkView<T1, T2>(
                Count,
                _pinnedEntities,
                _pinnedVersions,
                (T1*)GetComponentPointer(component1Id),
                (T2*)GetComponentPointer(component2Id));
        }

        public QueryChunkView<T1, T2, T3> AsView<T1, T2, T3>(int component1Id, int component2Id, int component3Id)
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
        {
            return new QueryChunkView<T1, T2, T3>(
                Count,
                _pinnedEntities,
                _pinnedVersions,
                (T1*)GetComponentPointer(component1Id),
                (T2*)GetComponentPointer(component2Id),
                (T3*)GetComponentPointer(component3Id));
        }

        public void Dispose() => _ownedMemory.Release();
    }
}
