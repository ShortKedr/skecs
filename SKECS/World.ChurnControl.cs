using System;
using System.Runtime.CompilerServices;

namespace SKECS;

public sealed partial class World
{
    private readonly ChurnChunkPool? _churnPool;
    internal ChurnChunkPool? ChurnPool => _churnPool;
    internal bool UsesChurnControl => _options.ChurnControl != null;

    /// <summary>Releases one bounded batch of cold, fully empty chunk backings above the soft target.</summary>
    /// <returns>The number of backings released. A world with churn control disabled returns zero.</returns>
    public int TrimChurnMemory()
    {
        ThrowIfDisposed();
        return MaintainChurnPool();
    }

    /// <summary>Returns estimated retained storage and cumulative backing activity for this world.</summary>
    public ChurnControlStatistics GetChurnControlStatistics()
    {
        ThrowIfDisposed();
        long live = 0;
        long occupied = 0;
        long reusable = 0;
        long allocations = 0;
        var pinned = 2L * sizeof(int) * ChunkCapacity;
        foreach (var archetype in _archetypeList)
        {
            if (archetype.Chunks.Count == 0) continue;
            long rowBytes = 2 * sizeof(int);
            foreach (var type in archetype.ComponentTypes) rowBytes += type.Size;
            var backingBytes = archetype.ChunkAllocationSize + pinned;
            foreach (var chunk in archetype.Chunks)
            {
                live += rowBytes * chunk.Count;
                if (_churnPool != null) continue;
                allocations++;
                if (chunk.Count == 0) reusable += backingBytes;
                else occupied += backingBytes;
            }
        }

        if (_adaptive != null)
            foreach (var packed in _adaptive.Pools.Values) live += packed?.LiveBytes ?? 0;
        if (_churnPool is { } pool)
            return new ChurnControlStatistics(true, _options.ChurnControl!.TargetRetainedBytes, live,
                pool.BackingBytes - pool.EmptyBytes, pool.EmptyBytes, GetChurnTableBytes(),
                pool.AllocationCount, pool.ReuseCount, pool.ReleaseCount);

        return new ChurnControlStatistics(false, 0, live, occupied, reusable, GetChurnTableBytes(),
            allocations, 0, 0);
    }

    private int MaintainChurnPool() => _churnPool is { EmptyBytes: > 0 } pool
        ? pool.Trim(GetChurnTableBytes()) : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CompleteChurnChange(Archetype.RemovalResult removed, bool acquiredBacking = false)
    {
        if (_churnPool == null) return;
        if (removed.BecameEmpty) removed.Source!.ReturnIfEmpty(removed.MovedChunkIndex);
        if (removed.BecameEmpty || acquiredBacking) MaintainChurnPool();
    }

    private unsafe long GetChurnTableBytes()
    {
#pragma warning disable CS8500
        var bytes = (long)_entityRecords.Capacity * sizeof(EntityRecord);
#pragma warning restore CS8500
        bytes += (long)_freeEntityIds.Capacity * sizeof(int);
        foreach (var accessor in _accessors) bytes += accessor.ManagedBufferBytes;
        return bytes + AdaptiveIndexBytes();
    }
}
