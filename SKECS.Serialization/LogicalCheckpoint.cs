using static SKECS.Serialization.CheckpointFormat;

namespace SKECS.Serialization;

/// <summary>Mutable, world-independent state available only to an explicit structural migration.</summary>
public sealed class LogicalCheckpoint
{
    internal LogicalCheckpoint(CheckpointSlot[] slots, int[] freeIds) { Slots = slots; FreeIds = freeIds; }

    public CheckpointSlot[] Slots { get; set; }
    public int[] FreeIds { get; set; }
    public IList<LogicalComponent> Components { get; } = new List<LogicalComponent>();
    public IList<CheckpointExtension> Extensions { get; } = new List<CheckpointExtension>();

    internal long ValidateLimits(CheckpointLimits limits, long baseBytes)
    {
        if (Slots is null || FreeIds is null) throw new InvalidDataException("Missing migrated allocator data.");
        CheckLimit(Slots.Length, limits.MaxSlots, "migrated slot count");
        CheckLimit(FreeIds.Length, Slots.Length, "migrated free ID count");
        CheckLimit(Extensions.Count, limits.MaxExtensions, "migrated extension count");
        CheckLimit(Components.Count, limits.MaxComponentValues, "migrated component count");
        long bytes = baseBytes + 64L * Slots.Length + 8L * FreeIds.Length + 24L * Components.Count;
        long values = 0;
        foreach (var component in Components)
        {
            if (component is null) throw new InvalidDataException("Missing migrated component.");
            bytes += 128;
            var count = 0;
            foreach (var field in component.Fields.Fields)
            {
                CheckLimit(++count, limits.MaxFields, "migrated field count");
                CheckLimit(++values, limits.MaxComponentValues, "migrated field values");
                bytes += 72;
                if (field.Value.Kind == FieldKind.Bytes)
                {
                    var length = field.Value.AsBytes().Length;
                    CheckLimit(length, limits.MaxValueBytes, "migrated field value");
                    bytes += length;
                }
                CheckLimit(bytes, limits.MaxWorkingBytes, "migration working storage");
            }
        }
        foreach (var extension in Extensions)
        {
            if (extension is null) throw new InvalidDataException("Missing migrated extension.");
            CheckLimit(extension.Data.Length, limits.MaxValueBytes, "migrated extension payload");
            bytes += 64L + extension.Data.Length;
            CheckLimit(bytes, limits.MaxWorkingBytes, "migration working storage");
        }
        CheckLimit(bytes, limits.MaxWorkingBytes, "migration working storage");
        return bytes;
    }

    internal World Restore(CodecRegistry registry, CheckpointReadOptions options, CheckpointLimits limits, long baseBytes)
    {
        var usedBytes = ValidateLimits(limits, baseBytes);
        var memberSet = new HashSet<(int, uint)>();
        var groups = new SortedDictionary<uint, List<LogicalComponent>>();
        foreach (var component in Components)
        {
            var id = component.EntityId;
            if ((uint)id >= (uint)Slots.Length || !Slots[id].Alive || !memberSet.Add((id, component.ComponentId)))
                throw new InvalidDataException($"Invalid or duplicate migrated component {component.ComponentId} on slot {id}.");
            if (!registry.TryGet(component.ComponentId, out var codec) || codec is null)
                throw new InvalidDataException($"Migrated component {component.ComponentId} is absent from the destination schema.");
            foreach (var field in component.Fields.Fields)
            {
                var index = CodecFields.Find(codec.Fields, field.Key);
                if (index < 0 || codec.Fields[index].Kind != field.Value.Kind)
                    throw new InvalidDataException($"Invalid migrated field {field.Key} for component {component.ComponentId}.");
                if (field.Value.Kind == FieldKind.Entity) CodecFields.ValidateEntity(Slots, field.Value);
            }
            foreach (var field in codec.Fields)
                if (!field.Optional && !component.Fields.TryGet(field.Tag, out _))
                    throw new InvalidDataException($"Migrated component {component.ComponentId} is missing field {field.Tag}.");

            usedBytes += 64;
            CheckLimit(usedBytes, limits.MaxWorkingBytes, "migration working storage");
            if (!groups.TryGetValue(component.ComponentId, out var group))
                groups.Add(component.ComponentId, group = new List<LogicalComponent>());
            group.Add(component);
        }

        var world = CheckpointReader.CreateWorld(Slots, FreeIds, Slots.Count(slot => slot.Alive), options, usedBytes, limits, out var rootBytes);
        try
        {
            var remaining = limits.MaxWorkingBytes - usedBytes - rootBytes;
            foreach (var group in groups)
            {
                registry.TryGet(group.Key, out var codec);
                var fields = codec!.Fields;
                CheckLimit(fields.Length, limits.MaxFields, "migrated schema field count");
                var scratch = (long)fields.Length * EstimatedValueBytes + 128;
                CheckLimit(scratch, remaining, "migration working storage");
                remaining -= scratch;
                var values = new FieldValue[fields.Length];
                var reader = new ComponentReader(world, fields, values);
                group.Value.Sort((left, right) => left.EntityId.CompareTo(right.EntityId));
                foreach (var component in group.Value)
                {
                    for (var i = 0; i < fields.Length; i++)
                        values[i] = component.Fields.TryGet(fields[i].Tag, out var value) ? value : default;
                    var id = component.EntityId;
                    codec.Restore(world, world.BindEntity(id, Slots[id].Version), reader, ref remaining);
                }
                remaining += scratch;
            }
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }
}

public sealed class LogicalComponent
{
    public LogicalComponent(int entityId, uint componentId) { EntityId = entityId; ComponentId = componentId; }

    public int EntityId { get; }
    public uint ComponentId { get; }
    public MigrationFields Fields { get; } = new();
}
