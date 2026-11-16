namespace SKECS.GA;

/// <summary>Binds one component type in an authored kernel's structural command stream.</summary>
public readonly struct GaComponentRemoval
{
    private delegate void EntityRemoval(DeterministicSource source, int entityId, int generation);
    private delegate void TicketRemoval(DeterministicSource source, CreateTicket ticket);

    private readonly EntityRemoval _entity;
    private readonly TicketRemoval _ticket;

    private GaComponentRemoval(EntityRemoval entity, TicketRemoval ticket) =>
        (_entity, _ticket) = (entity, ticket);

    public static GaComponentRemoval For<T>() where T : unmanaged => new(
        static (source, entityId, generation) => source.Remove<T>(entityId, generation),
        static (source, ticket) => source.Remove<T>(ticket));

    internal bool IsBound => _entity is not null;

    internal void Apply(DeterministicSource source, int entityId, int generation) => _entity(source, entityId, generation);

    internal void Apply(DeterministicSource source, CreateTicket ticket) => _ticket(source, ticket);
}
