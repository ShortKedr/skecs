using System;
using System.Collections.Generic;

namespace SKECS;

public enum DeterministicSourceKind
{
    Entity = 0,
    Job = 1,
}

/// <summary>The canonical, lexicographically ordered key of a command, event or reduction leaf.</summary>
public readonly struct DeterministicOrderKey : IComparable<DeterministicOrderKey>, IEquatable<DeterministicOrderKey>
{
    internal DeterministicOrderKey(long tickIndex, SystemStage stage, int systemOrder,
        DeterministicSourceKind sourceKind, int sourceId, int sourceGeneration, int localOrdinal)
    {
        TickIndex = tickIndex;
        Stage = stage;
        SystemOrder = systemOrder;
        SourceKind = sourceKind;
        SourceId = sourceId;
        SourceGeneration = sourceGeneration;
        LocalOrdinal = localOrdinal;
    }

    public long TickIndex { get; }
    public SystemStage Stage { get; }
    public int SystemOrder { get; }
    public DeterministicSourceKind SourceKind { get; }
    public int SourceId { get; }
    public int SourceGeneration { get; }
    public int LocalOrdinal { get; }

    internal bool SameSource(DeterministicOrderKey other) =>
        TickIndex == other.TickIndex && Stage == other.Stage && SystemOrder == other.SystemOrder
        && SourceKind == other.SourceKind && SourceId == other.SourceId && SourceGeneration == other.SourceGeneration;

    public int CompareTo(DeterministicOrderKey other)
    {
        var order = TickIndex.CompareTo(other.TickIndex);
        if (order != 0) return order;
        order = Stage.CompareTo(other.Stage);
        if (order != 0) return order;
        order = SystemOrder.CompareTo(other.SystemOrder);
        if (order != 0) return order;
        order = SourceKind.CompareTo(other.SourceKind);
        if (order != 0) return order;
        order = SourceId.CompareTo(other.SourceId);
        if (order != 0) return order;
        order = SourceGeneration.CompareTo(other.SourceGeneration);
        return order != 0 ? order : LocalOrdinal.CompareTo(other.LocalOrdinal);
    }

    public bool Equals(DeterministicOrderKey other) => SameSource(other) && LocalOrdinal == other.LocalOrdinal;

    public override bool Equals(object? obj) => obj is DeterministicOrderKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = TickIndex.GetHashCode();
            hash = hash * 31 + (int)Stage;
            hash = hash * 31 + SystemOrder;
            hash = hash * 31 + (int)SourceKind;
            hash = hash * 31 + SourceId;
            hash = hash * 31 + SourceGeneration;
            return hash * 31 + LocalOrdinal;
        }
    }
}

/// <summary>Identifies a Create command before its system's barrier assigns an entity ID.</summary>
public readonly struct CreateTicket
{
    internal CreateTicket(object scope, DeterministicOrderKey key)
    {
        Scope = scope;
        Key = key;
    }

    internal object? Scope { get; }

    public DeterministicOrderKey Key { get; }
}

/// <summary>An event published at a completed tick boundary.</summary>
public readonly struct DeterministicEvent
{
    private readonly object _payload;

    internal DeterministicEvent(DeterministicOrderKey key, object payload)
    {
        Key = key;
        _payload = payload;
    }

    public DeterministicOrderKey Key { get; }

    public Type PayloadType => _payload.GetType();

    public T Get<T>() where T : unmanaged => (T)_payload;
}

/// <summary>A tick-local fold. Each tick starts at Seed and folds individual leaves in key order.</summary>
/// <remarks>Reuse the same channel for contributions and reads. The combiner must be deterministic
/// and depend only on the accumulator and leaf. An exception faults the tick.</remarks>
public readonly struct DeterministicReduction<T> where T : unmanaged
{
    internal DeterministicReduction(long workloadId, int index, T seed)
    {
        WorkloadId = workloadId;
        Index = index;
        Seed = seed;
    }

    internal long WorkloadId { get; }
    internal int Index { get; }
    public T Seed { get; }
}

internal sealed class PreparedReductions
{
    private readonly object[] _definitions;

    public PreparedReductions(long workloadId, object[] definitions)
    {
        WorkloadId = workloadId;
        _definitions = definitions;
    }

    public long WorkloadId { get; }

    public ReductionDefinition<T> Require<T>(DeterministicReduction<T> reduction) where T : unmanaged
    {
        if (reduction.WorkloadId != WorkloadId || (uint)reduction.Index >= (uint)_definitions.Length)
            throw new ArgumentException("The reduction is not registered with this runner.", nameof(reduction));
        return (ReductionDefinition<T>)_definitions[reduction.Index];
    }
}

internal sealed class ReductionDefinition<T> where T : unmanaged
{
    private readonly DeterministicReduction<T> _channel;
    private readonly Func<T, T, T> _combine;

    public ReductionDefinition(DeterministicReduction<T> channel, Func<T, T, T> combine)
    {
        _channel = channel;
        _combine = combine;
    }

    public void Apply(Dictionary<int, object> reductions, T leaf)
    {
        var accumulator = reductions.TryGetValue(_channel.Index, out var value) ? (T)value : _channel.Seed;
        reductions[_channel.Index] = _combine(accumulator, leaf);
    }
}

/// <summary>Immutable event trace and reduction results for one completed tick.</summary>
public sealed class DeterministicTickResult
{
    private readonly PreparedReductions _definitions;
    private readonly Dictionary<int, object> _reductions;

    internal DeterministicTickResult(long tickIndex, DeterministicEvent[] events,
        PreparedReductions definitions, Dictionary<int, object> reductions)
    {
        TickIndex = tickIndex;
        Events = Array.AsReadOnly(events);
        _definitions = definitions;
        _reductions = reductions;
    }

    public long TickIndex { get; }

    public IReadOnlyList<DeterministicEvent> Events { get; }

    public T GetReduction<T>(DeterministicReduction<T> reduction) where T : unmanaged
    {
        _definitions.Require(reduction);
        return _reductions.TryGetValue(reduction.Index, out var value) ? (T)value : reduction.Seed;
    }
}
