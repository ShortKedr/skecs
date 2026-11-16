using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SKECS;

internal sealed unsafe class AccessorStorage
{
    private Entry[] _entries = Array.Empty<Entry>();
    private readonly World _world;
    private readonly Type _componentType;
    public int TypeId { get; }
    public long ManagedBufferBytes => (long)_entries.Length * sizeof(Entry);

    public AccessorStorage(int typeId, World world, Type componentType)
    {
        TypeId = typeId;
        _world = world;
        _componentType = componentType;
    }

    internal void EnsureCapacity(int id)
    {
        if (id >= _entries.Length)
        {
            var capacity = Math.Max(id + 1, Math.Max(16, _entries.Length * 2));
            Array.Resize(ref _entries, capacity);
            _world.DiagnosticsBuffer?.ResizeTable(DiagnosticTable.Accessor, TypeId, capacity, ManagedBufferBytes, _componentType);
        }
    }

    public void Update(int id, World.EntityRecord record)
    {
        var address = record.Alive && record.Archetype!.HasComponent(TypeId)
            ? (nint)record.Archetype.GetComponentPointer(record.ChunkIndex, TypeId)
                + record.RowIndex * record.Archetype.ComponentSize(TypeId)
            : 0;
        _entries[id] = new Entry { Address = address, Version = record.Version };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(Entity entity)
    {
        _world.ThrowIfRunnerOwned();
        return entity.WorldId == _world.Id && (uint)entity.Id < (uint)_entries.Length
            && _entries[entity.Id].Address != 0 && _entries[entity.Id].Version == entity.Version;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(Entity entity) where T : unmanaged
    {
        _world.ThrowIfRunnerOwned();
        if (entity.WorldId != _world.Id || (uint)entity.Id >= (uint)_entries.Length) ThrowMissing();
        ref var entry = ref _entries[entity.Id];
        if (entry.Address == 0 || entry.Version != entity.Version) ThrowMissing();
        return ref *(T*)entry.Address;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMissing() => throw new InvalidOperationException("Entity is unavailable or does not contain this component.");

    public void Invalidate()
    {
        _entries = Array.Empty<Entry>();
    }

    // Keep generation next to the address so random lookups fetch one table entry, not two arrays.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Entry
    {
        public nint Address;
        public int Version;
    }
}
