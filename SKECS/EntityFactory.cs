using System;

namespace SKECS;

/// <summary>A world-bound creation template which places entities directly in their final archetype.</summary>
public readonly struct EntityFactory<T1, T2> where T1 : unmanaged where T2 : unmanaged
{
    private readonly World _world;
    private readonly Archetype _archetype;
    // Column offsets in an archetype-only world, type IDs when placement is adaptive.
    private readonly int _component1;
    private readonly int _component2;

    internal EntityFactory(World world, Archetype archetype, int component1, int component2)
    {
        _world = world;
        _archetype = archetype;
        _component1 = component1;
        _component2 = component2;
    }

    public Entity Create(in T1 component1, in T2 component2)
        => _world.CreateEntityIn(_archetype, _component1, _component2, component1, component2);

    public void CreateBatch(Span<Entity> output, in T1 component1, in T2 component2)
    {
        _world.ThrowIfDisposed();
        for (var i = 0; i < output.Length; i++) output[i] = Create(component1, component2);
    }
}
