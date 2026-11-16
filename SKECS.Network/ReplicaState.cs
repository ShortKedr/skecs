using System;
using System.Collections.Generic;

namespace SKECS.Network;

/// <summary>A replication identity scoped to the authority session that issued it.</summary>
public readonly record struct NetworkEntityId(Guid SessionId, ulong Value);

/// <summary>The replica world and entity mapping at one accepted tick.</summary>
/// <remarks>The receiver owns this world and disposes it when the state is replaced or the receiver is disposed.</remarks>
public sealed class ReplicaState
{
    private bool _disposed;

    internal ReplicaState(Guid sessionId, ulong tick, World world, Dictionary<ulong, Entity> entities, ulong highWater)
    {
        SessionId = sessionId;
        Tick = tick;
        World = world;
        Entities = entities;
        HighWater = highWater;
    }

    public World World { get; }
    public ulong Tick { get; }
    public int EntityCount => Entities.Count;
    public Guid SessionId { get; }

    internal ulong HighWater { get; }
    internal IReadOnlyDictionary<ulong, Entity> Entities { get; }

    /// <summary>Enumerates the network entities present at this state's accepted tick.</summary>
    public IEnumerable<NetworkEntityId> GetEntityIds()
    {
        foreach (ulong id in Entities.Keys)
            yield return new NetworkEntityId(SessionId, id);
    }

    public bool TryResolve(NetworkEntityId id, out Entity entity)
    {
        if (!_disposed && id.SessionId == SessionId && Entities.TryGetValue(id.Value, out entity) && World.IsAlive(entity))
            return true;

        entity = default;
        return false;
    }

    internal void DisposeWorld()
    {
        if (_disposed) return;
        _disposed = true;
        World.Dispose();
    }
}
