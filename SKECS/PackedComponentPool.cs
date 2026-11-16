using System;
using System.Collections.Generic;

namespace SKECS;

/// <summary>One typed column split across backings owned by the world's churn controller.</summary>
internal sealed unsafe class PackedComponentPool
{
    private readonly ChurnChunkPool _allocator;
    private readonly int _pageCapacity;
    private readonly long _maximumBackingBytes;
    private readonly List<ChurnChunkPool.Backing> _pages = new();
    private readonly LinkedList<ChurnChunkPool.Backing> _reserve = new();
    private int[] _slots = Array.Empty<int>();

    internal PackedComponentPool(ChurnChunkPool allocator, ComponentType type, int pageCapacity, int maxOversizePercent)
    {
        _allocator = allocator;
        Type = type;
        _pageCapacity = pageCapacity;
        RequestedNativeBytes = checked(RoundUp(checked(type.Size * pageCapacity)) + ChunkMemory.Alignment - 1);
        _maximumBackingBytes = RequestedNativeBytes + (long)RequestedNativeBytes * maxOversizePercent / 100
            + 2L * sizeof(int) * pageCapacity;
    }

    internal ComponentType Type { get; }
    internal int Count { get; private set; }
    internal int RequestedNativeBytes { get; }
    internal long IndexBytes => (long)_slots.Length * sizeof(int) + (long)_pages.Capacity * IntPtr.Size;
    internal long LiveBytes => (long)Count * (Type.Size + 2L * sizeof(int));

    private static int RoundUp(int size) =>
        checked(size + ChunkMemory.Alignment - 1) & ~(ChunkMemory.Alignment - 1);

    internal int Find(Entity entity)
    {
        if ((uint)entity.Id >= (uint)_slots.Length) return -1;
        var slot = _slots[entity.Id] - 1;
        return slot >= 0 && _pages[slot / _pageCapacity].Memory.Versions[slot % _pageCapacity] == entity.Version
            ? slot : -1;
    }

    internal Entity EntityAt(int slot, long worldId)
    {
        var page = _pages[slot / _pageCapacity];
        var row = slot % _pageCapacity;
        return new Entity(page.Memory.Entities[row], page.Memory.Versions[row], worldId);
    }

    internal byte* AddressAt(int slot) =>
        _pages[slot / _pageCapacity].Memory.Address + (slot % _pageCapacity) * Type.Size;

    internal ref T ValueAt<T>(int slot) where T : unmanaged => ref *(T*)AddressAt(slot);

    internal ref T Get<T>(Entity entity) where T : unmanaged
    {
        var slot = Find(entity);
        if (slot < 0) throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}.");
        return ref ValueAt<T>(slot);
    }

    internal int IndexCapacityFor(int entityId) => GrowCapacity(_slots.Length, checked(entityId + 1));

    internal static int GrowCapacity(int capacity, int required) => capacity >= required ? capacity
        : Math.Max(required, Math.Max(4, checked(capacity * 2)));

    internal long EstimateAddBytes(int entityId)
    {
        var bytes = (long)(IndexCapacityFor(entityId) - _slots.Length) * sizeof(int);
        if (Count % _pageCapacity == 0)
        {
            // Include backing/node overhead and the largest backing the allocator can select.
            bytes += _maximumBackingBytes + 512;
            bytes += (long)(GrowCapacity(_pages.Capacity, _pages.Count + 1) - _pages.Capacity) * IntPtr.Size;
        }
        return bytes;
    }

    internal void Add<T>(Entity entity, in T value, out PageChange pageChange) where T : unmanaged
    {
        if ((uint)entity.Id < (uint)_slots.Length && _slots[entity.Id] != 0)
            throw new InvalidOperationException($"Entity {entity} already has component {typeof(T).Name}.");

        var capacity = IndexCapacityFor(entity.Id);
        if (capacity != _slots.Length) Array.Resize(ref _slots, capacity);
        var pageIndex = Count / _pageCapacity;
        var row = Count % _pageCapacity;
        pageChange = default;
        if (row == 0)
        {
            _pages.Capacity = GrowCapacity(_pages.Capacity, checked(_pages.Count + 1));
            var backing = _allocator.Rent(RequestedNativeBytes, _reserve, out var reused);
            _pages.Add(backing);
            pageChange = new PageChange(pageIndex, backing, reused);
        }

        var page = _pages[pageIndex];
        ((T*)page.Memory.Address)[row] = value;
        page.Memory.Entities[row] = entity.Id;
        page.Memory.Versions[row] = entity.Version;
        _slots[entity.Id] = ++Count;
    }

    internal void RemoveAt(int slot, out PageChange pageChange)
    {
        var last = Count - 1;
        var target = _pages[slot / _pageCapacity];
        var targetRow = slot % _pageCapacity;
        var source = _pages[last / _pageCapacity];
        var sourceRow = last % _pageCapacity;
        _slots[target.Memory.Entities[targetRow]] = 0;
        if (slot != last)
        {
            Buffer.MemoryCopy(AddressAt(last), AddressAt(slot), Type.Size, Type.Size);
            var moved = source.Memory.Entities[sourceRow];
            target.Memory.Entities[targetRow] = moved;
            target.Memory.Versions[targetRow] = source.Memory.Versions[sourceRow];
            _slots[moved] = slot + 1;
        }

        Count = last;
        pageChange = default;
        if (sourceRow == 0)
        {
            _pages.RemoveAt(_pages.Count - 1);
            _allocator.Return(source, _reserve);
            pageChange = new PageChange(last / _pageCapacity, source, false);
        }
    }

    internal void Release(DiagnosticBuffer? diagnostics)
    {
        for (var index = 0; index < _pages.Count; index++)
        {
            var page = _pages[index];
            _allocator.Release(page);
            diagnostics?.ReleasePackedPage(Type, index, page.Id);
        }
        _pages.Clear();
        _pages.Capacity = 0;
        _slots = Array.Empty<int>();
        Count = 0;
    }

    internal readonly struct PageChange
    {
        internal PageChange(int index, ChurnChunkPool.Backing backing, bool reused)
        {
            Index = index;
            Backing = backing;
            Reused = reused;
        }

        internal int Index { get; }
        internal ChurnChunkPool.Backing? Backing { get; }
        internal bool Reused { get; }
    }
}
