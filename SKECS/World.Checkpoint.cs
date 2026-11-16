using System;

namespace SKECS;

/// <summary>The generation and liveness of an issued entity slot.</summary>
public readonly struct CheckpointSlot
{
    public CheckpointSlot(int version, bool alive)
    {
        Version = version;
        Alive = alive;
    }

    public int Version { get; }
    public bool Alive { get; }
}

public sealed partial class World
{
    /// <summary>Borrows read-only access to the world at a completed simulation boundary.</summary>
    /// <remarks>Any mutation ends the borrow, including a write through an existing reference.
    /// The caller must exclude mutations and concurrent operations for the entire borrow.
    /// Access after disposal throws ObjectDisposedException.</remarks>
    public CheckpointView OpenCheckpoint()
    {
        ThrowIfDisposed();
        if (_queryDepth != 0)
            throw new InvalidOperationException("Open a checkpoint outside Query.ForEach.");
        return new CheckpointView(this);
    }

    /// <summary>Creates a world with the given entity slots and free IDs in next-allocation order.</summary>
    /// <remarks>Live entities start empty so the caller can install components after all handles are bound.</remarks>
    public static World RestoreCheckpoint(
        ReadOnlySpan<CheckpointSlot> slots, ReadOnlySpan<int> freeIds, WorldOptions options = default)
    {
        ValidateCheckpointAllocator(slots, freeIds);
        var world = new World(options);
        try
        {
            world._entityRecords.Capacity = Math.Max(world._entityRecords.Capacity, slots.Length);
            for (var id = 0; id < slots.Length; id++)
            {
                var slot = slots[id];
                var record = new EntityRecord { Version = slot.Version, Alive = slot.Alive, ChunkIndex = -1, RowIndex = -1 };
                if (slot.Alive)
                {
                    var location = world._rootArchetype.AddEntity(new Entity(id, slot.Version, world.Id));
                    record.Archetype = world._rootArchetype;
                    record.ChunkIndex = location.ChunkIndex;
                    record.RowIndex = location.RowIndex;
                }
                world._entityRecords.Add(record);
            }

            for (var i = freeIds.Length - 1; i >= 0; i--)
                world._freeEntityIds.Add(freeIds[i]);
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    /// <summary>Validates checkpoint allocator metadata without creating a world.</summary>
    public static void ValidateCheckpointAllocator(ReadOnlySpan<CheckpointSlot> slots, ReadOnlySpan<int> freeIds)
    {
        var isFree = new bool[slots.Length];
        foreach (var id in freeIds)
        {
            if ((uint)id >= (uint)slots.Length || isFree[id] || slots[id].Alive)
                throw new ArgumentException("Invalid free entity ID.", nameof(freeIds));
            isFree[id] = true;
        }

        for (var id = 0; id < slots.Length; id++)
        {
            var slot = slots[id];
            if (slot.Version < 1 || (!slot.Alive &&
                (slot.Version == 1 || (slot.Version != int.MaxValue && !isFree[id]))))
                throw new ArgumentException($"Invalid allocator metadata for entity slot {id}.", nameof(slots));
        }
    }

    /// <summary>Adds a checkpoint component if its estimated retained storage fits the remaining budget.</summary>
    /// <remarks>Use on the unpublished world returned by RestoreCheckpoint. Discard that world on failure.</remarks>
    public bool TryAddCheckpointComponent<T>(Entity entity, in T component, ref long remainingBytes) where T : unmanaged
    {
        ValidateEntity(entity);
        var typeId = _componentTypes.GetId<T>();
        if (GetPackedPool(typeId) is { } packed)
        {
            if (packed.Find(entity) >= 0)
                throw new InvalidOperationException($"Duplicate checkpoint component {typeof(T)} on {entity}.");
            var packedCost = packed.EstimateAddBytes(entity.Id) + _adaptive!.Presence.EstimateAddBytes(entity.Id);
            if (packedCost > remainingBytes) return false;
            AddPacked(packed, entity, component);
            MaintainChurnPool();
            remainingBytes -= packedCost;
            return true;
        }
        var source = _entityRecords[entity.Id].Archetype!;
        if (source.HasComponent(typeId))
            throw new InvalidOperationException($"Duplicate checkpoint component {typeof(T)} on {entity}.");
        var archetypesBefore = _archetypeList.Count;
        var destination = source.GetAddTransition(typeId).Destination;
        var cost = destination.NextCheckpointChunkBytes;
        if (_archetypeList.Count > archetypesBefore)
        {
            // CLR type-index tables and transition metadata are managed allocations.
            var types = destination.ComponentTypes;
            cost += 512L + 64L * types.Length + 8L * (types[types.Length - 1].Id + 1L);
        }
        if (cost > remainingBytes) return false;
        Add(entity, component);
        remainingBytes -= cost;
        return true;
    }

    /// <summary>A synchronous read-only borrow. Values are copied and no component pointers escape.</summary>
    public readonly ref struct CheckpointView
    {
        private readonly World _world;

        internal CheckpointView(World world) => _world = world;

        public int SlotCount
        {
            get { _world.ThrowIfDisposed(); return _world._entityRecords.Count; }
        }

        public int FreeCount
        {
            get { _world.ThrowIfDisposed(); return _world._freeEntityIds.Count; }
        }

        public CheckpointSlot GetSlot(int id)
        {
            _world.ThrowIfDisposed();
            var record = _world._entityRecords[id];
            return new CheckpointSlot(record.Version, record.Alive);
        }

        public void CopyFreeIds(Span<int> destination)
        {
            if (destination.Length != FreeCount)
                throw new ArgumentException("Destination must have exactly FreeCount elements.", nameof(destination));
            for (var i = 0; i < destination.Length; i++)
                destination[i] = _world._freeEntityIds[destination.Length - 1 - i];
        }

        public int GetComponentCount(Entity entity)
        {
            var archetype = GetArchetype(entity);
            return archetype.ComponentTypes.Length + (_world._adaptive?.Presence.Count(entity.Id) ?? 0);
        }

        public Type GetComponentType(Entity entity, int index)
        {
            var types = GetArchetype(entity).ComponentTypes;
            if (index < types.Length || _world._adaptive == null) return types[index].RuntimeType;
            return _world._adaptive.Presence.Get(entity.Id, index - types.Length).Type.RuntimeType;
        }

        public bool Owns(Entity entity)
        {
            _world.ThrowIfDisposed();
            return entity.WorldId == _world.Id;
        }

        private Archetype GetArchetype(Entity entity)
        {
            _world.ValidateEntity(entity);
            return _world._entityRecords[entity.Id].Archetype!;
        }

        public bool TryGet<T>(Entity entity, out T value) where T : unmanaged
        {
            _world.ThrowIfDisposed();
            if (_world.IsAliveCore(entity))
            {
                if (_world._adaptive != null)
                {
                    var presence = _world._adaptive.Presence;
                    for (var i = 0; i < presence.Count(entity.Id); i++)
                    {
                        var pool = presence.Get(entity.Id, i);
                        if (pool.Type.RuntimeType != typeof(T)) continue;
                        value = pool.Get<T>(entity);
                        return true;
                    }
                }
                var record = _world._entityRecords[entity.Id];
                foreach (var type in record.Archetype!.ComponentTypes)
                {
                    if (type.RuntimeType != typeof(T)) continue;
                    value = record.Archetype.ReadComponent<T>(record.ChunkIndex, record.RowIndex, type.Id);
                    return true;
                }
            }
            value = default;
            return false;
        }
    }
}
