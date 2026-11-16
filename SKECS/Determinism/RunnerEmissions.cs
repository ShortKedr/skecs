using System;
using System.Collections.Generic;

namespace SKECS;

internal readonly struct CommandTarget
{
    private readonly Entity _entity;
    private readonly CreateTicket _ticket;
    private readonly bool _isTicket;

    public CommandTarget(Entity entity)
    {
        _entity = entity;
        _ticket = default;
        _isTicket = false;
    }

    public CommandTarget(CreateTicket ticket)
    {
        _entity = default;
        _ticket = ticket;
        _isTicket = true;
    }

    public void Validate(JobBuffer buffer, DeterministicOrderKey key)
    {
        if (_isTicket)
        {
            if (!ReferenceEquals(_ticket.Scope, buffer.TicketScope) || !_ticket.Key.SameSource(key)
                || _ticket.Key.LocalOrdinal >= key.LocalOrdinal)
                throw new InvalidOperationException("A ticket can only be used by a later command from its creating source.");
            if (key.DomainKey.HasValue && _ticket.Key.CompareTo(key) >= 0)
                throw new InvalidOperationException("The command's domain key sorts before its ticket's Create command.");
        }
        else if (_entity.WorldId != buffer.WorldId)
        {
            throw new InvalidOperationException("The command target does not belong to this world.");
        }
    }

    public Entity Resolve(RunnerBarrier barrier) => _isTicket ? barrier.Resolve(_ticket.Key) : _entity;
}

internal enum ComponentCommand
{
    Add,
    Remove,
    Set,
}

internal sealed class RunnerBarrier
{
    private Dictionary<DeterministicOrderKey, Entity>? _created;

    public RunnerBarrier(World.RunnerLease lease, List<DeterministicEvent> events, Dictionary<int, object> reductions)
    {
        Lease = lease;
        Events = events;
        Reductions = reductions;
    }

    public World.RunnerLease Lease { get; }
    public List<DeterministicEvent> Events { get; }
    public Dictionary<int, object> Reductions { get; }

    public void Create(DeterministicOrderKey key)
        => (_created ??= new Dictionary<DeterministicOrderKey, Entity>()).Add(key, Lease.Create());

    public Entity Resolve(DeterministicOrderKey key)
    {
        if (_created is null || !_created.TryGetValue(key, out var entity))
            throw new InvalidOperationException("The Create command was not replayed before its ticket reference.");
        return entity;
    }
}

internal abstract class RunnerEmission
{
    protected RunnerEmission(DeterministicOrderKey key) => Key = key;

    public DeterministicOrderKey Key { get; }

    public abstract void Apply(RunnerBarrier barrier);
}

internal sealed class CreateEmission : RunnerEmission
{
    public CreateEmission(DeterministicOrderKey key) : base(key) { }

    public override void Apply(RunnerBarrier barrier) => barrier.Create(Key);
}

internal sealed class DestroyEmission : RunnerEmission
{
    private readonly CommandTarget _target;

    public DestroyEmission(DeterministicOrderKey key, CommandTarget target) : base(key) => _target = target;

    public override void Apply(RunnerBarrier barrier) => barrier.Lease.Destroy(_target.Resolve(barrier));
}

internal sealed class ComponentEmission<T> : RunnerEmission where T : unmanaged
{
    private readonly CommandTarget _target;
    private readonly ComponentCommand _kind;
    private readonly int _typeId;
    private readonly T _value;

    public ComponentEmission(DeterministicOrderKey key, CommandTarget target, ComponentCommand kind, int typeId, T value)
        : base(key)
    {
        _target = target;
        _kind = kind;
        _typeId = typeId;
        _value = value;
    }

    public override void Apply(RunnerBarrier barrier)
    {
        var entity = _target.Resolve(barrier);
        switch (_kind)
        {
            case ComponentCommand.Add:
                barrier.Lease.Add(entity, _typeId, _value);
                break;
            case ComponentCommand.Remove:
                barrier.Lease.Remove(entity, _typeId);
                break;
            case ComponentCommand.Set:
                barrier.Lease.Set(entity, _typeId, _value);
                break;
        }
    }
}

internal sealed class EventEmission : RunnerEmission
{
    private readonly object _payload;

    public EventEmission(DeterministicOrderKey key, object payload) : base(key) => _payload = payload;

    public override void Apply(RunnerBarrier barrier) => barrier.Events.Add(new DeterministicEvent(Key, _payload));
}

internal sealed class ReductionEmission<T> : RunnerEmission where T : unmanaged
{
    private readonly ReductionDefinition<T> _reduction;
    private readonly T _leaf;

    public ReductionEmission(DeterministicOrderKey key, ReductionDefinition<T> reduction, T leaf) : base(key)
    {
        _reduction = reduction;
        _leaf = leaf;
    }

    public override void Apply(RunnerBarrier barrier) => _reduction.Apply(barrier.Reductions, _leaf);
}
