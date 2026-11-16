namespace SKECS;

/// <summary>An entity ID and generation bound to its owning world.</summary>
public readonly struct Entity : System.IEquatable<Entity>
{
    /// <summary>Creates an unbound ID and generation. Use World.BindEntity to interpret them in a world.</summary>
    public Entity(int id, int version) : this(id, version, 0) { }

    internal Entity(int id, int version, long worldId)
    {
        Id = id;
        Version = version;
        WorldId = worldId;
    }

    public int Id { get; }

    public int Version { get; }

    internal long WorldId { get; }

    /// <summary>Whether this handle is bound and has a valid shape. Use World.IsAlive to check liveness.</summary>
    public bool IsValid => WorldId != 0 && Id >= 0 && Version > 0;

    public bool Equals(Entity other) => Id == other.Id && Version == other.Version && WorldId == other.WorldId;

    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    public override int GetHashCode()
    {
        unchecked { return (((Id * 397) ^ Version) * 397) ^ WorldId.GetHashCode(); }
    }

    public override string ToString() => $"Entity({Id}:{Version})";

    public static bool operator ==(Entity left, Entity right) => left.Equals(right);

    public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
}
