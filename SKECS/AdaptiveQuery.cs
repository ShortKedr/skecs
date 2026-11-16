using System;

namespace SKECS;

/// <summary>Traverses matching entities without promising contiguous columns.</summary>
/// <remarks>Do not depend on visitation order. Archetype-driven plans use the current chunk and row
/// order. Pool-driven plans use packed slot order of the smallest required pool at the start of each
/// traversal, with ties broken by world component registration order. Structural changes can change
/// either order. Refs may be used during their callback only. Structural changes during traversal throw.</remarks>
public readonly struct AdaptiveQuery<T1> where T1 : unmanaged
{
    private readonly AdaptiveQueryPlan _plan;
    private readonly int _type1;
    private readonly PackedComponentPool? _pool1;

    internal AdaptiveQuery(AdaptiveQueryPlan plan, int type1)
    {
        _plan = plan;
        _type1 = type1;
        _pool1 = plan.FindPool(type1);
    }

    public void ForEach<TProcessor>(ref TProcessor processor) where TProcessor : struct, IQueryProcessor<T1>
    {
        var world = _plan.World;
        world.BeginAdaptiveQuery();
        try
        {
            if (_plan.Dense is { } dense)
            {
                new Query<T1>(world, dense).ForEach(ref processor);
                return;
            }
            var cursor = _plan.Iterate();
            while (cursor.MoveNext())
                processor.Execute(cursor.Entity, ref cursor.Get<T1>(_type1, _pool1));
        }
        finally { world.EndAdaptiveQuery(); }
    }
}

/// <inheritdoc cref="AdaptiveQuery{T1}"/>
public readonly struct AdaptiveQuery<T1, T2> where T1 : unmanaged where T2 : unmanaged
{
    private readonly AdaptiveQueryPlan _plan;
    private readonly int _type1;
    private readonly int _type2;
    private readonly PackedComponentPool? _pool1;
    private readonly PackedComponentPool? _pool2;

    internal AdaptiveQuery(AdaptiveQueryPlan plan, int type1, int type2)
    {
        _plan = plan;
        _type1 = type1;
        _type2 = type2;
        _pool1 = plan.FindPool(type1);
        _pool2 = plan.FindPool(type2);
    }

    public void ForEach<TProcessor>(ref TProcessor processor) where TProcessor : struct, IQueryProcessor<T1, T2>
    {
        var world = _plan.World;
        world.BeginAdaptiveQuery();
        try
        {
            if (_plan.Dense is { } dense)
            {
                new Query<T1, T2>(world, dense).ForEach(ref processor);
                return;
            }
            var cursor = _plan.Iterate();
            while (cursor.MoveNext())
                processor.Execute(cursor.Entity, ref cursor.Get<T1>(_type1, _pool1), ref cursor.Get<T2>(_type2, _pool2));
        }
        finally { world.EndAdaptiveQuery(); }
    }
}

/// <inheritdoc cref="AdaptiveQuery{T1}"/>
public readonly struct AdaptiveQuery<T1, T2, T3> where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
{
    private readonly AdaptiveQueryPlan _plan;
    private readonly int _type1;
    private readonly int _type2;
    private readonly int _type3;
    private readonly PackedComponentPool? _pool1;
    private readonly PackedComponentPool? _pool2;
    private readonly PackedComponentPool? _pool3;

    internal AdaptiveQuery(AdaptiveQueryPlan plan, int type1, int type2, int type3)
    {
        _plan = plan;
        _type1 = type1;
        _type2 = type2;
        _type3 = type3;
        _pool1 = plan.FindPool(type1);
        _pool2 = plan.FindPool(type2);
        _pool3 = plan.FindPool(type3);
    }

    public void ForEach<TProcessor>(ref TProcessor processor) where TProcessor : struct, IQueryProcessor<T1, T2, T3>
    {
        var world = _plan.World;
        world.BeginAdaptiveQuery();
        try
        {
            if (_plan.Dense is { } dense)
            {
                new Query<T1, T2, T3>(world, dense).ForEach(ref processor);
                return;
            }
            var cursor = _plan.Iterate();
            while (cursor.MoveNext())
                processor.Execute(cursor.Entity, ref cursor.Get<T1>(_type1, _pool1), ref cursor.Get<T2>(_type2, _pool2),
                    ref cursor.Get<T3>(_type3, _pool3));
        }
        finally { world.EndAdaptiveQuery(); }
    }
}

public sealed class AdaptiveQueryBuilder
{
    private readonly World _world;
    private ComponentSetKey _include = ComponentSetKey.Empty;
    private ComponentSetKey _exclude = ComponentSetKey.Empty;

    internal AdaptiveQueryBuilder(World world) => _world = world;

    public AdaptiveQueryBuilder With<T>() where T : unmanaged
    {
        _include = _include.Add(_world.GetAdaptiveComponentType<T>().Id);
        return this;
    }

    public AdaptiveQueryBuilder Without<T>() where T : unmanaged
    {
        _exclude = _exclude.Add(_world.GetAdaptiveComponentType<T>().Id);
        return this;
    }

    public AdaptiveQueryDescription Build() => new(_world.BuildAdaptivePlan(_include, _exclude));
}

public readonly struct AdaptiveQueryDescription
{
    private readonly AdaptiveQueryPlan _plan;

    internal AdaptiveQueryDescription(AdaptiveQueryPlan plan) => _plan = plan;

    /// <summary>Number of rows in the current search space before packed membership filters.</summary>
    public int CandidateCount => _plan.CandidateCount();

    /// <summary>Exact current number of matching entities.</summary>
    public int EntityCount
    {
        get
        {
            if (_plan.Dense != null) return CandidateCount;
            _plan.World.ThrowIfDisposed();
            var count = 0;
            var cursor = _plan.Iterate();
            while (cursor.MoveNext()) count++;
            return count;
        }
    }

    /// <inheritdoc cref="AdaptiveQuery{T1}"/>
    public void ForEach(Action<Entity> action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        var world = _plan.World;
        world.BeginAdaptiveQuery();
        try
        {
            var cursor = _plan.Iterate();
            while (cursor.MoveNext()) action(cursor.Entity);
        }
        finally { world.EndAdaptiveQuery(); }
    }
}
