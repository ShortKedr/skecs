using System;
using System.Collections.Generic;

namespace SKECS;

internal sealed class AdaptiveQueryPlan
{
    private readonly QueryCacheEntry _archetypes;
    private readonly PackedComponentPool[] _include;
    private readonly PackedComponentPool[] _exclude;
    private readonly bool _useSmallestPool;
    private readonly bool _empty;

    internal AdaptiveQueryPlan(QueryCacheEntry archetypes, PackedComponentPool[] include,
        PackedComponentPool[] exclude, bool useSmallestPool, bool empty)
    {
        _archetypes = archetypes;
        _include = include;
        _exclude = exclude;
        _useSmallestPool = useSmallestPool;
        _empty = empty;
    }

    internal World World => _archetypes.World;
    internal QueryCacheEntry? Dense => _include.Length == 0 && _exclude.Length == 0 && !_empty ? _archetypes : null;

    internal PackedComponentPool? FindPool(int typeId)
    {
        foreach (var pool in _include)
            if (pool.Type.Id == typeId) return pool;
        return null;
    }

    private PackedComponentPool? Driver()
    {
        if (!_useSmallestPool) return null;
        var smallest = _include[0];
        for (var i = 1; i < _include.Length; i++)
            if (_include[i].Count < smallest.Count) smallest = _include[i];
        return smallest;
    }

    internal int CandidateCount()
    {
        World.ThrowIfDisposed();
        if (_empty) return 0;
        if (Driver() is { } driver) return driver.Count;
        var count = 0;
        foreach (var archetype in _archetypes.Matches)
            foreach (var chunk in archetype.Chunks) count = checked(count + chunk.Count);
        return count;
    }

    internal Cursor Iterate() => new(this, Driver());

    internal struct Cursor
    {
        private readonly AdaptiveQueryPlan _plan;
        private readonly PackedComponentPool? _driver;
        private int _archetype;
        private int _chunk;
        private int _row;

        internal Cursor(AdaptiveQueryPlan plan, PackedComponentPool? driver)
        {
            _plan = plan;
            _driver = driver;
            _archetype = 0;
            _chunk = 0;
            _row = -1;
            Entity = default;
            Chunk = null;
        }

        internal Entity Entity { get; private set; }
        internal Archetype.Chunk? Chunk { get; private set; }
        internal int Row => _row;

        internal bool MoveNext()
        {
            if (_plan._empty) return false;
            while (NextCandidate())
                if (Matches()) return true;
            return false;
        }

        private bool NextCandidate()
        {
            if (_driver != null)
            {
                if (++_row >= _driver.Count) return false;
                Entity = _driver.EntityAt(_row, _plan.World.Id);
                return true;
            }

            var matches = _plan._archetypes.Matches;
            while (_archetype < matches.Count)
            {
                var chunks = matches[_archetype].Chunks;
                while (_chunk < chunks.Count)
                {
                    var chunk = chunks[_chunk];
                    if (++_row < chunk.Count)
                    {
                        Chunk = chunk;
                        Entity = new Entity(chunk.Entities[_row], chunk.Versions[_row], _plan.World.Id);
                        return true;
                    }
                    _chunk++;
                    _row = -1;
                }
                _archetype++;
                _chunk = 0;
            }
            return false;
        }

        private bool Matches()
        {
            foreach (var pool in _plan._include)
                if (pool != _driver && pool.Find(Entity) < 0) return false;
            foreach (var pool in _plan._exclude)
                if (pool.Find(Entity) >= 0) return false;
            return true;
        }

        internal unsafe ref T Get<T>(int typeId, PackedComponentPool? pool) where T : unmanaged
        {
            if (pool != null)
                return ref *(T*)pool.AddressAt(pool == _driver ? _row : pool.Find(Entity));
            return ref ((T*)Chunk!.GetComponentPointer(typeId))[_row];
        }
    }
}

public sealed partial class World
{
    public AdaptiveQueryBuilder AdaptiveQueryBuilder()
    {
        ThrowIfDisposed();
        return new AdaptiveQueryBuilder(this);
    }

    public AdaptiveQuery<T1> QueryAdaptive<T1>() where T1 : unmanaged
    {
        var type1 = GetAdaptiveComponentType<T1>().Id;
        return new AdaptiveQuery<T1>(BuildAdaptivePlan(ComponentSetKey.Empty.Add(type1), ComponentSetKey.Empty), type1);
    }

    public AdaptiveQuery<T1, T2> QueryAdaptive<T1, T2>() where T1 : unmanaged where T2 : unmanaged
    {
        var type1 = GetAdaptiveComponentType<T1>().Id;
        var type2 = GetAdaptiveComponentType<T2>().Id;
        return new AdaptiveQuery<T1, T2>(BuildAdaptivePlan(ComponentSetKey.Empty.Add(type1).Add(type2),
            ComponentSetKey.Empty), type1, type2);
    }

    public AdaptiveQuery<T1, T2, T3> QueryAdaptive<T1, T2, T3>()
        where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
    {
        var type1 = GetAdaptiveComponentType<T1>().Id;
        var type2 = GetAdaptiveComponentType<T2>().Id;
        var type3 = GetAdaptiveComponentType<T3>().Id;
        return new AdaptiveQuery<T1, T2, T3>(BuildAdaptivePlan(ComponentSetKey.Empty.Add(type1).Add(type2).Add(type3),
            ComponentSetKey.Empty), type1, type2, type3);
    }

    internal AdaptiveQueryPlan BuildAdaptivePlan(ComponentSetKey include, ComponentSetKey exclude)
    {
        // A purely archetypal plan reuses the legacy cache and its direct typed traversal.
        if (_adaptive == null)
            return new AdaptiveQueryPlan(GetOrCreateQueryCache(new QueryCacheKey(include, exclude)),
                Array.Empty<PackedComponentPool>(), Array.Empty<PackedComponentPool>(), false, false);
        var packedInclude = Split(ref include);
        var packedExclude = Split(ref exclude);
        var empty = Array.Exists(packedInclude, pool => Array.IndexOf(packedExclude, pool) >= 0);
        var useSmallest = packedInclude.Length > 0 && include.TypeIds.Length == 0 && exclude.TypeIds.Length == 0;
        var candidates = GetOrCreateQueryCache(new QueryCacheKey(include, exclude));
        return new AdaptiveQueryPlan(candidates, packedInclude, packedExclude, useSmallest, empty);
    }

    private PackedComponentPool[] Split(ref ComponentSetKey key)
    {
        List<PackedComponentPool>? packed = null;
        foreach (var id in key.TypeIds)
        {
            if (GetPackedPool(id) is not { } pool) continue;
            (packed ??= new List<PackedComponentPool>()).Add(pool);
            key = key.Remove(id);
        }
        return packed?.ToArray() ?? Array.Empty<PackedComponentPool>();
    }
}
