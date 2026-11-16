using System;
using System.Collections.Generic;
using SKECS.Numerics;

namespace SKECS;

/// <summary>The frozen tick values and the rows assigned to one logical job.</summary>
/// <remarks>Read and Write use indices local to this job. Do not keep references to
/// external mutable state in inputs or processors, including through native addresses.</remarks>
public readonly ref struct DeterministicJob<TInput> where TInput : unmanaged
{
    private readonly ReadOnlySpan<RunnerRow> _rows;
    private readonly JobBuffer _buffer;

    internal DeterministicJob(ReadOnlySpan<RunnerRow> rows, JobBuffer buffer, TInput input, Fixed step)
    {
        _rows = rows;
        _buffer = buffer;
        Input = input;
        Step = step;
    }

    public long TickIndex => _buffer.TickIndex;

    public TInput Input { get; }

    public Fixed Step { get; }

    public int JobId => _buffer.JobId;

    public int Count => _rows.Length;

    public Entity GetEntity(int index) => _rows[index].Entity;

    public T Read<T>(int index) where T : unmanaged
    {
        var typeId = _buffer.Require<T>(ComponentRuleKind.Read);
        return _rows[index].Read<T>(typeId);
    }

    public bool TryRead<T>(int index, out T value) where T : unmanaged
    {
        var typeId = _buffer.Require<T>(ComponentRuleKind.Read);
        var row = _rows[index];
        if (!row.Archetype.HasComponent(typeId))
        {
            value = default;
            return false;
        }

        value = row.Read<T>(typeId);
        return true;
    }

    /// <summary>Writes an existing component in this job. Use a deferred command for structural changes.</summary>
    public void Write<T>(int index, T value) where T : unmanaged
    {
        var typeId = _buffer.Require<T>(ComponentRuleKind.Write);
        _rows[index].Write(typeId, value);
    }

    /// <summary>Uses the specified row's entity as the source of commands, events and reduction leaves.</summary>
    public DeterministicSource ForEntity(int index) => new(_buffer, index, _rows[index].Entity);

    /// <summary>Uses the logical job ID as the source, without an originating entity.</summary>
    public DeterministicSource ForJob() => new(_buffer, _rows.Length, default);
}

/// <summary>Enqueues operations and outputs under one source in the current job.</summary>
/// <remarks>Operations share one ordinal sequence per source. At the system barrier, Add to an
/// existing component, Remove of an absent component and any operation on a stale generation are
/// no-ops. Set overwrites or adds. Tickets may only be used by a later operation of their source.</remarks>
public readonly ref struct DeterministicSource
{
    private readonly JobBuffer _buffer;
    private readonly int _slot;
    private readonly Entity _entity;

    internal DeterministicSource(JobBuffer buffer, int slot, Entity entity)
    {
        _buffer = buffer;
        _slot = slot;
        _entity = entity;
    }

    public CreateTicket Create()
    {
        var key = CurrentKey();
        _buffer.Enqueue(_slot, new CreateEmission(key));
        return new CreateTicket(_buffer.TicketScope, key);
    }

    public void Destroy(Entity entity) => Destroy(new CommandTarget(entity));

    /// <summary>Queues destruction of an ID and generation in this job's world.</summary>
    public void Destroy(int entityId, int generation) =>
        Destroy(new Entity(entityId, generation, _buffer?.WorldId ?? 0));

    public void Destroy(CreateTicket ticket) => Destroy(new CommandTarget(ticket));

    public void Add<T>(Entity entity, T value) where T : unmanaged => Component(ComponentCommand.Add, new CommandTarget(entity), value);

    public void Add<T>(CreateTicket ticket, T value) where T : unmanaged => Component(ComponentCommand.Add, new CommandTarget(ticket), value);

    public void Remove<T>(Entity entity) where T : unmanaged => Component(ComponentCommand.Remove, new CommandTarget(entity), default(T));

    public void Remove<T>(CreateTicket ticket) where T : unmanaged => Component(ComponentCommand.Remove, new CommandTarget(ticket), default(T));

    public void Set<T>(Entity entity, T value) where T : unmanaged => Component(ComponentCommand.Set, new CommandTarget(entity), value);

    public void Set<T>(CreateTicket ticket, T value) where T : unmanaged => Component(ComponentCommand.Set, new CommandTarget(ticket), value);

    public void Emit<T>(T payload) where T : unmanaged => _buffer.Enqueue(_slot, new EventEmission(CurrentKey(), payload));

    public void Contribute<T>(DeterministicReduction<T> reduction, T leaf) where T : unmanaged
    {
        var key = CurrentKey();
        var definition = _buffer.Reductions.Require(reduction);
        _buffer.Enqueue(_slot, new ReductionEmission<T>(key, definition, leaf));
    }

    private DeterministicOrderKey CurrentKey()
    {
        if (_buffer is null) throw new InvalidOperationException("The command source was not obtained from a job.");
        return _buffer.CurrentKey(_slot, _entity);
    }

    private void Destroy(CommandTarget target)
    {
        var key = CurrentKey();
        target.Validate(_buffer, key);
        _buffer.Enqueue(_slot, new DestroyEmission(key, target));
    }

    private void Component<T>(ComponentCommand kind, CommandTarget target, T value) where T : unmanaged
    {
        var key = CurrentKey();
        target.Validate(_buffer, key);
        var typeId = _buffer.Require<T>(ComponentRuleKind.Write);
        _buffer.Enqueue(_slot, new ComponentEmission<T>(key, target, kind, typeId, value));
    }
}

internal readonly struct RunnerRow
{
    public RunnerRow(Entity entity, Archetype archetype, int chunk, int row)
    {
        Entity = entity;
        Archetype = archetype;
        Chunk = chunk;
        Row = row;
    }

    public Entity Entity { get; }
    public Archetype Archetype { get; }
    public int Chunk { get; }
    public int Row { get; }

    public T Read<T>(int typeId) where T : unmanaged => Archetype.ReadComponent<T>(Chunk, Row, typeId);

    public void Write<T>(int typeId, T value) where T : unmanaged => Archetype.WriteComponent(Chunk, Row, typeId, value);

    public static int Compare(RunnerRow left, RunnerRow right)
    {
        var order = left.Entity.Id.CompareTo(right.Entity.Id);
        return order != 0 ? order : left.Entity.Version.CompareTo(right.Entity.Version);
    }
}

internal sealed class JobBuffer
{
    private readonly Dictionary<Type, PreparedAccess> _access;
    private readonly int _rowCount;
    private readonly SystemStage _stage;
    private readonly int _systemOrder;
    private int[]? _ordinals;

    public JobBuffer(int jobId, int rowCount, long worldId, long tickIndex, SystemStage stage, int systemOrder,
        Dictionary<Type, PreparedAccess> access, object ticketScope, PreparedReductions reductions)
    {
        JobId = jobId;
        _rowCount = rowCount;
        WorldId = worldId;
        TickIndex = tickIndex;
        _stage = stage;
        _systemOrder = systemOrder;
        _access = access;
        TicketScope = ticketScope;
        Reductions = reductions;
    }

    public int JobId { get; }
    public long WorldId { get; }
    public long TickIndex { get; }
    public object TicketScope { get; }
    public PreparedReductions Reductions { get; }
    public List<RunnerEmission>? Emissions { get; private set; }

    public int Require<T>(ComponentRuleKind kind) where T : unmanaged => PreparedAccess.Require<T>(_access, kind);

    public void Enqueue(int slot, RunnerEmission emission)
    {
        _ordinals ??= new int[_rowCount + 1];
        var nextOrdinal = checked(_ordinals[slot] + 1);
        (Emissions ??= new List<RunnerEmission>()).Add(emission);
        _ordinals[slot] = nextOrdinal;
    }

    public DeterministicOrderKey CurrentKey(int slot, Entity entity)
    {
        var ordinal = _ordinals?[slot] ?? 0;
        return slot == _rowCount
            ? new DeterministicOrderKey(TickIndex, _stage, _systemOrder, DeterministicSourceKind.Job, JobId, 0, ordinal)
            : new DeterministicOrderKey(TickIndex, _stage, _systemOrder, DeterministicSourceKind.Entity, entity.Id, entity.Version, ordinal);
    }
}
