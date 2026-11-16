using System;
using System.Collections.Generic;
using SKECS.Numerics;

namespace SKECS;

/// <summary>Consumes a commit group's sorted events once on the runner thread.</summary>
public delegate void DeterministicOrderedApply<TInput>(DeterministicOrderedApplyContext<TInput> context)
    where TInput : unmanaged;

/// <summary>Scoped input and declared world access for an ordered apply stage.</summary>
/// <remarks>Producer commands have already committed. Writes to existing components are visible
/// immediately. Commands and events from this stage replay together after the callback.</remarks>
public readonly ref struct DeterministicOrderedApplyContext<TInput> where TInput : unmanaged
{
    private readonly World.RunnerLease _lease;
    private readonly Dictionary<Type, PreparedAccess> _access;
    private readonly long _worldId;

    internal DeterministicOrderedApplyContext(DeterministicJob<TInput> job, long worldId, World.RunnerLease lease,
        Dictionary<Type, PreparedAccess> access, ReadOnlySpan<DeterministicEvent> events)
    {
        _lease = lease;
        _access = access;
        _worldId = worldId;
        Events = events;
        TickIndex = job.TickIndex;
        Stage = job.Stage;
        SystemOrder = job.SystemOrder;
        Input = job.Input;
        Step = job.Step;
        Commands = job.ForJob();
    }

    public ReadOnlySpan<DeterministicEvent> Events { get; }
    public long TickIndex { get; }
    public SystemStage Stage { get; }
    public int SystemOrder { get; }
    public TInput Input { get; }
    public Fixed Step { get; }
    public DeterministicSource Commands { get; }

    /// <summary>Copies an existing component of the given generation from this world.</summary>
    public bool TryRead<T>(int id, int generation, out T value) where T : unmanaged =>
        _lease.TryRead(new Entity(id, generation, _worldId), PreparedAccess.Require<T>(_access, ComponentRuleKind.Read), out value);

    /// <summary>Updates an existing component without changing archetype membership.</summary>
    public bool TryWrite<T>(int id, int generation, T value) where T : unmanaged =>
        _lease.TryWrite(new Entity(id, generation, _worldId), PreparedAccess.Require<T>(_access, ComponentRuleKind.Write), value);
}
