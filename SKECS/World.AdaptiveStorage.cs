using System;
using System.Collections.Generic;

namespace SKECS;

public sealed partial class World
{
    private readonly AdaptiveStorage? _adaptive;
    private int _adaptiveQueryDepth;

    private bool IsPackedType(int typeId) => GetPackedPool(typeId) != null;

    private PackedComponentPool? GetPackedPool(int typeId) =>
        _adaptive is { } storage ? ResolvePackedPool(storage, typeId) : null;

    private PackedComponentPool? ResolvePackedPool(AdaptiveStorage storage, int typeId)
    {
        if (storage.Pools.TryGetValue(typeId, out var pool)) return pool;
        var type = _componentTypes.Resolve(typeId);
        if (storage.Options.IsPacked(type.RuntimeType))
        {
            pool = new PackedComponentPool(_churnPool!, type, ChunkCapacity, _options.ChurnControl!.MaxOversizePercent);
            _diagnostics?.DefinePackedComponent(type, pool.RequestedNativeBytes);
        }
        storage.Pools.Add(typeId, pool);
        return pool;
    }

    internal ComponentType GetAdaptiveComponentType<T>() where T : unmanaged
    {
        ThrowIfDisposed();
        return _componentTypes.GetOrCreate<T>();
    }

    private void EnsureArchetypeType(int typeId)
    {
        if (IsPackedType(typeId))
            throw new InvalidOperationException($"{_componentTypes.Resolve(typeId).RuntimeType} uses packed storage. Use an adaptive query.");
    }

    internal void BeginAdaptiveQuery()
    {
        BeginQuery();
        _adaptiveQueryDepth++;
    }

    internal void EndAdaptiveQuery()
    {
        _adaptiveQueryDepth--;
        EndQuery();
    }

    private void EnsureAdaptiveTraversalAllowsStructuralChange()
    {
        if (_adaptiveQueryDepth != 0)
            throw new InvalidOperationException("An adaptive query is borrowing component storage.");
    }

    private void AddPacked<T>(PackedComponentPool pool, Entity entity, in T value) where T : unmanaged
    {
        EnsureAdaptiveTraversalAllowsStructuralChange();
        if (pool.Find(entity) >= 0)
            throw new InvalidOperationException($"Entity {entity} already has component {typeof(T).Name}.");
        var presence = _adaptive!.Presence;
        presence.PrepareAdd(entity.Id);
        pool.Add(entity, value, out var change);
        presence.Add(entity.Id, pool);
        RecordPackedPage(pool, change, added: true);
        _diagnostics?.ChangePackedRows(pool.Type, pool.Count, entity.Id, added: true);
        _diagnostics?.ResizePackedIndexes(pool.Type, pool.IndexBytes, presence.EstimatedBytes);
    }

    private bool RemovePacked(PackedComponentPool pool, Entity entity)
    {
        var slot = pool.Find(entity);
        if (slot < 0) return false;
        EnsureAdaptiveTraversalAllowsStructuralChange();
        var swappedBytes = slot == pool.Count - 1 ? 0 : pool.Type.Size + 2L * sizeof(int);
        pool.RemoveAt(slot, out var change);
        var presence = _adaptive!.Presence;
        presence.Remove(entity.Id, pool);
        _diagnostics?.ChangePackedRows(pool.Type, pool.Count, entity.Id, added: false, swappedBytes);
        RecordPackedPage(pool, change, added: false);
        _diagnostics?.ResizePackedIndexes(pool.Type, pool.IndexBytes, presence.EstimatedBytes);
        return true;
    }

    private void RecordPackedPage(PackedComponentPool pool, PackedComponentPool.PageChange change, bool added)
    {
        if (_diagnostics is { } diagnostics && change.Backing is { } backing)
            diagnostics.ChangePackedPage(pool.Type, change.Index, backing.Id, backing.NativeBytes,
                2L * sizeof(int) * ChunkCapacity, added, change.Reused);
    }

    private void RemovePackedComponents(Entity entity)
    {
        var presence = _adaptive!.Presence;
        while (presence.Count(entity.Id) != 0)
            RemovePacked(presence.Get(entity.Id, presence.Count(entity.Id) - 1), entity);
    }

    private long AdaptiveIndexBytes()
    {
        if (_adaptive == null) return 0;
        var bytes = _adaptive.Presence.EstimatedBytes;
        foreach (var pool in _adaptive.Pools.Values) bytes += pool?.IndexBytes ?? 0;
        return bytes;
    }

    private void ReleasePackedPools()
    {
        if (_adaptive == null) return;
        foreach (var pool in _adaptive.Pools.Values) pool?.Release(_diagnostics);
        _adaptive.Pools.Clear();
        _adaptive.Accessors.Clear();
        _adaptive.Presence.Clear();
    }

    // ID and generation lookup is already the packed accessor's address cache. Swap-delete
    // changes that mapping in one place, so an accessor never keeps an old address.
    private ComponentAccessor<T> GetPackedAccessor<T>(PackedComponentPool pool) where T : unmanaged
    {
        var accessors = _adaptive!.Accessors;
        if (!accessors.TryGetValue(pool.Type.Id, out var storage))
        {
            storage = new AccessorStorage(pool.Type.Id, this, typeof(T), pool);
            accessors.Add(pool.Type.Id, storage);
        }
        return new ComponentAccessor<T>(storage);
    }

    private Archetype GetAdaptiveCreationArchetype(int type1, int type2, int type3)
    {
        var key = ComponentSetKey.Empty;
        if (!IsPackedType(type1)) key = key.Add(type1);
        if (type2 >= 0 && !IsPackedType(type2)) key = key.Add(type2);
        if (type3 >= 0 && !IsPackedType(type3)) key = key.Add(type3);
        return GetArchetype(key);
    }

    private void WriteCreatedComponent<T>(Archetype archetype, EntitySlot slot, Entity entity, int typeId, in T value)
        where T : unmanaged
    {
        if (GetPackedPool(typeId) is { } pool) AddPacked(pool, entity, value);
        else archetype.WriteComponent(slot.ChunkIndex, slot.RowIndex, typeId, value);
    }

    private sealed class AdaptiveStorage
    {
        internal AdaptiveStorage(ComponentPlacementOptions options) => Options = options;

        internal ComponentPlacementOptions Options { get; }
        internal Dictionary<int, PackedComponentPool?> Pools { get; } = new();
        internal Dictionary<int, AccessorStorage> Accessors { get; } = new();
        internal PackedPresenceIndex Presence { get; } = new();
    }
}
