using System;
using System.Collections.Generic;

namespace SKECS;

/// <summary>Application-owned storage permissions and the placement selected for one world.</summary>
public sealed class ComponentPlacementOptions
{
    private readonly HashSet<Type> _allowed;
    private readonly HashSet<Type> _requiredColumns;
    private readonly HashSet<Type> _packed;

    public ComponentPlacementOptions() : this(new(), new(), new()) { }

    private ComponentPlacementOptions(HashSet<Type> allowed, HashSet<Type> requiredColumns, HashSet<Type> packed)
    {
        _allowed = allowed;
        _requiredColumns = requiredColumns;
        _packed = packed;
    }

    /// <summary>Returns new permissions that allow profiles to consider this unmanaged type.</summary>
    public ComponentPlacementOptions AllowAdaptiveStorage<T>() where T : unmanaged
        => new(new HashSet<Type>(_allowed) { typeof(T) }, _requiredColumns, _packed);

    /// <summary>Pins a type used by a legacy or otherwise layout-dependent consumer.</summary>
    public ComponentPlacementOptions RequireArchetypeStorage<T>() where T : unmanaged
    {
        if (_packed.Contains(typeof(T)))
            throw new InvalidOperationException($"{typeof(T)} already has packed placement selected.");
        return new ComponentPlacementOptions(_allowed, new HashSet<Type>(_requiredColumns) { typeof(T) }, _packed);
    }

    public bool CanUsePackedPool(Type componentType) =>
        _allowed.Contains(componentType) && !_requiredColumns.Contains(componentType);

    /// <summary>Replaces the packed selection after validating every application-owned permission.</summary>
    public ComponentPlacementOptions WithPackedPools(params Type[] componentTypes)
    {
        if (componentTypes == null) throw new ArgumentNullException(nameof(componentTypes));
        var packed = new HashSet<Type>();
        foreach (var componentType in componentTypes)
        {
            if (!CanUsePackedPool(componentType))
                throw new ArgumentException($"{componentType} is ineligible for packed storage.", nameof(componentTypes));
            if (!packed.Add(componentType))
                throw new ArgumentException($"{componentType} is listed more than once.", nameof(componentTypes));
        }
        return new ComponentPlacementOptions(_allowed, _requiredColumns, packed);
    }

    internal bool HasPackedPools => _packed.Count != 0;
    internal bool IsPacked(Type componentType) => _packed.Contains(componentType);
}
