using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using SKECS.Serialization;

namespace SKECS.Network;

internal sealed class CheckpointSnapshot
{
    private const uint EntityIdsExtension = 0x4E455449;
    private const uint EntityIdsVersion = 1;
    private const int ExtensionOverhead = 2 * sizeof(uint) + sizeof(byte) + sizeof(uint);
    private const int EntityIdSize = sizeof(ulong);

    private static readonly IReadOnlyDictionary<uint, uint> SupportedExtensions = new Dictionary<uint, uint>
    {
        [EntityIdsExtension] = EntityIdsVersion
    };
    private static readonly IReadOnlyCollection<uint> RequiredExtensions = new[] { EntityIdsExtension };

    private readonly CodecRegistry _registry;
    private readonly CheckpointLimits _limits;
    private readonly int _maxEntityMapBytes;
    private Dictionary<Entity, ulong> _authorityIds = new();
    private World? _authorityWorld;
    private ulong _lastAssignedId;

    internal CheckpointSnapshot(CodecRegistry registry, int maxBytes, int maxSlots)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (maxSlots < 0) throw new ArgumentOutOfRangeException(nameof(maxSlots));

        _maxEntityMapBytes = (int)Math.Min(maxBytes, checked(EntityIdSize * (1L + maxSlots)));
        _limits = new CheckpointLimits { MaxSlots = maxSlots, MaxExtensions = 1 };
        _limits.MaxFrameBytes = (int)Math.Min(maxBytes,
            Math.Max(_limits.MaxFrameBytes, (long)_maxEntityMapBytes + ExtensionOverhead));
        _limits.MaxValueBytes = Math.Min(maxBytes, Math.Max(_limits.MaxValueBytes, _maxEntityMapBytes));
        _limits.MaxDocumentBytes = maxBytes;
        // Tiny checkpoints still need a full initial world chunk and serializer metadata.
        _limits.MaxWorkingBytes = Math.Min(_limits.MaxWorkingBytes, checked(64L * 1024 + 4L * maxBytes + 128L * maxSlots));
    }

    internal static byte[] SchemaFingerprint(CodecRegistry registry)
    {
        registry.GetSchemas(); // Seal the negotiated registry before capturing its fingerprint.
        return registry.GetFingerprint();
    }

    internal bool TryGetCapturedId(Entity entity, out ulong id) => _authorityIds.TryGetValue(entity, out id);

    internal byte[] Capture(World world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (_authorityWorld is not null && !ReferenceEquals(_authorityWorld, world))
            throw new InvalidOperationException("A replication connection uses one authority world.");

        var view = world.OpenCheckpoint();
        int slotCount = view.SlotCount;
        if (slotCount > _limits.MaxSlots)
            throw new InvalidDataException("Checkpoint slot count exceeds the replication limit.");

        int liveCount = 0;
        for (int slotId = 0; slotId < slotCount; slotId++)
            if (view.GetSlot(slotId).Alive) liveCount++;

        long mapBytes = EntityIdSize * (1L + liveCount);
        if (mapBytes > _maxEntityMapBytes)
            throw new InvalidDataException("Network entity map exceeds the replication limit.");

        var data = new byte[(int)mapBytes];
        var ids = new Dictionary<Entity, ulong>(liveCount);
        ulong highWater = _lastAssignedId;
        int offset = EntityIdSize;
        for (int slotId = 0; slotId < slotCount; slotId++)
        {
            var slot = view.GetSlot(slotId);
            if (!slot.Alive) continue;

            var entity = world.BindEntity(slotId, slot.Version);
            if (!_authorityIds.TryGetValue(entity, out ulong id))
                id = checked(++highWater);
            ids.Add(entity, id);
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset), id);
            offset += EntityIdSize;
        }
        BinaryPrimitives.WriteUInt64LittleEndian(data, highWater);

        using var stream = new MemoryStream();
        Write(world, stream, new[] { new CheckpointExtension(EntityIdsExtension, EntityIdsVersion, true, data) });
        var checkpoint = stream.ToArray();

        _authorityWorld = world;
        _authorityIds = ids;
        _lastAssignedId = highWater;
        return checkpoint;
    }

    internal ReplicaState Restore(Guid sessionId, ulong tick, byte[] checkpoint, WorldOptions worldOptions,
        ReplicaState? previous)
    {
        if (checkpoint.Length > _limits.MaxDocumentBytes)
            throw new InvalidDataException("Checkpoint exceeds the replication limit.");
        if (previous is not null && previous.SessionId != sessionId)
            throw new InvalidDataException("Previous replica belongs to a different session.");

        using var stream = new MemoryStream(checkpoint, writable: false);
        var world = WorldSerializer.ReadBinary(stream, _registry, out var extensions, new CheckpointReadOptions
        {
            Limits = _limits,
            WorldOptions = worldOptions,
            RequireExactSchema = true,
            Extensions = SupportedExtensions,
            RequiredExtensions = RequiredExtensions
        });
        try
        {
            if (extensions.Count != 1 || !extensions[0].Required)
                throw new InvalidDataException("Checkpoint must contain the required network entity map.");

            var ids = ReadEntityMap(world, extensions[0].Data, previous, out ulong highWater);
            // The matching schema still needs to reproduce the exact canonical bytes.
            using var canonical = new MemoryStream();
            Write(world, canonical, extensions);
            if (!checkpoint.AsSpan().SequenceEqual(canonical.GetBuffer().AsSpan(0, (int)canonical.Length)))
                throw new InvalidDataException("Checkpoint differs from the canonical session checkpoint.");

            return new ReplicaState(sessionId, tick, world, ids, highWater);
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    private Dictionary<ulong, Entity> ReadEntityMap(World world, byte[] data, ReplicaState? previous,
        out ulong highWater)
    {
        if (data.Length < EntityIdSize || data.Length > _maxEntityMapBytes || data.Length % EntityIdSize != 0)
            throw new InvalidDataException("Invalid network entity map length.");

        var view = world.OpenCheckpoint();
        int count = data.Length / EntityIdSize - 1;
        int liveCount = 0;
        for (int slotId = 0; slotId < view.SlotCount; slotId++)
            if (view.GetSlot(slotId).Alive) liveCount++;
        if (count != liveCount)
            throw new InvalidDataException("Network entity map must cover exactly the live checkpoint slots.");

        highWater = BinaryPrimitives.ReadUInt64LittleEndian(data);
        if (previous is not null && highWater < previous.HighWater)
            throw new InvalidDataException("Network entity ID high-water mark moved backwards.");

        var ids = new Dictionary<ulong, Entity>(count);
        int offset = EntityIdSize;
        for (int slotId = 0; slotId < view.SlotCount; slotId++)
        {
            var slot = view.GetSlot(slotId);
            if (!slot.Alive) continue;

            ulong id = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset));
            offset += EntityIdSize;
            if (id == 0 || id > highWater || ids.ContainsKey(id))
                throw new InvalidDataException("Invalid or duplicate network entity ID.");
            if (previous is not null && id <= previous.HighWater &&
                (!previous.Entities.TryGetValue(id, out var old) || old.Id != slotId || old.Version != slot.Version))
                throw new InvalidDataException("Network entity ID was reused or reassigned.");

            ids.Add(id, world.BindEntity(slotId, slot.Version));
        }

        if (previous is not null)
        {
            foreach (var old in previous.Entities)
            {
                if (old.Value.Id >= view.SlotCount) continue;
                var slot = view.GetSlot(old.Value.Id);
                if (slot.Alive && slot.Version == old.Value.Version && !ids.ContainsKey(old.Key))
                    throw new InvalidDataException("Living entity changed network IDs.");
            }
        }

        return ids;
    }

    private void Write(World world, Stream stream, IReadOnlyList<CheckpointExtension> extensions) =>
        WorldSerializer.WriteBinary(world, stream, _registry, new CheckpointWriteOptions
        {
            Limits = _limits,
            Extensions = extensions
        });
}
