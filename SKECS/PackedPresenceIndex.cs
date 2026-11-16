using System;
using System.Collections.Generic;

namespace SKECS;

// Each entity with one packed component needs only one entry. Additional types allocate on demand.
internal sealed class PackedPresenceIndex
{
    private Entry[] _entries = Array.Empty<Entry>();
    private long _additionalBytes;

    internal long EstimatedBytes => (long)_entries.Length * (2 * IntPtr.Size) + _additionalBytes;

    internal void Clear()
    {
        _entries = Array.Empty<Entry>();
        _additionalBytes = 0;
    }

    internal int Count(int id) => (uint)id < (uint)_entries.Length ? _entries[id].Count : 0;

    internal PackedComponentPool Get(int id, int index) => index == 0
        ? _entries[id].First! : _entries[id].Additional![index - 1];

    internal long EstimateAddBytes(int id)
    {
        var capacity = PackedComponentPool.GrowCapacity(_entries.Length, checked(id + 1));
        var bytes = (long)(capacity - _entries.Length) * 2 * IntPtr.Size;
        if ((uint)id < (uint)_entries.Length && _entries[id].First != null)
        {
            var additional = _entries[id].Additional;
            if (additional == null) bytes += 96;
            bytes += (long)(PackedComponentPool.GrowCapacity(additional?.Capacity ?? 0,
                (additional?.Count ?? 0) + 1) - (additional?.Capacity ?? 0)) * IntPtr.Size;
        }
        return bytes;
    }

    internal void PrepareAdd(int id)
    {
        if (id >= _entries.Length)
            Array.Resize(ref _entries, PackedComponentPool.GrowCapacity(_entries.Length, checked(id + 1)));
        ref var entry = ref _entries[id];
        if (entry.First == null) return;

        var additional = entry.Additional;
        if (additional == null)
        {
            entry.Additional = new List<PackedComponentPool>(4);
            _additionalBytes += 96 + 4 * IntPtr.Size;
        }
        else if (additional.Count == additional.Capacity)
        {
            var old = additional.Capacity;
            additional.Capacity = PackedComponentPool.GrowCapacity(old, additional.Count + 1);
            _additionalBytes += (long)(additional.Capacity - old) * IntPtr.Size;
        }
    }

    internal void Add(int id, PackedComponentPool pool)
    {
        ref var entry = ref _entries[id];
        if (entry.First == null) entry.First = pool;
        else entry.Additional!.Add(pool);
    }

    internal void Remove(int id, PackedComponentPool pool)
    {
        ref var entry = ref _entries[id];
        var additional = entry.Additional;
        if (entry.First == pool)
        {
            entry.First = additional is { Count: > 0 } ? additional[additional.Count - 1] : null;
            if (entry.First != null) additional!.RemoveAt(additional.Count - 1);
        }
        else
        {
            additional!.Remove(pool);
        }

        if (additional is { Count: 0 })
        {
            _additionalBytes -= 96 + (long)additional.Capacity * IntPtr.Size;
            entry.Additional = null;
        }
    }

    private struct Entry
    {
        internal PackedComponentPool? First;
        internal List<PackedComponentPool>? Additional;
        internal int Count => First == null ? 0 : 1 + (Additional?.Count ?? 0);
    }
}
