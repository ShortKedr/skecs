using static SKECS.Serialization.CheckpointFormat;

namespace SKECS.Serialization;

internal sealed class LoadPlan
{
    private readonly Codec? _codec;
    private readonly Action<MigrationFields>[] _migration;
    private readonly int[] _sourceByTarget;

    internal LoadPlan(ComponentSchema source, Codec? codec, CodecRegistry registry, bool allowUnknown, List<string> omitted, bool logical)
    {
        Source = source;
        Fields = CodecFields.Active(source);
        _codec = codec;
        _migration = Array.Empty<Action<MigrationFields>>();
        _sourceByTarget = Array.Empty<int>();
        if (logical) return;
        if (codec is null)
        {
            if (!allowUnknown) throw new InvalidDataException($"Unknown component {source.Id}.");
            omitted.Add($"Omitted unknown component {source.Id}.");
            return;
        }

        var target = codec.Schema;
        if (source.Version != target.Version)
        {
            _migration = registry.GetMigrationPath(source, target);
            return;
        }

        var targetFields = target.Fields.ToDictionary(field => field.Tag);
        foreach (var field in Fields)
        {
            if (targetFields.TryGetValue(field.Tag, out var receiver))
            {
                if (receiver.Kind != field.Kind)
                    throw new InvalidDataException($"Component {source.Id} field {field.Tag} changed type at version {source.Version}.");
                continue;
            }
            if (!allowUnknown) throw new InvalidDataException($"Unknown field {field.Tag} in component {source.Id}.");
            omitted.Add($"Omitted unknown field {field.Tag} in component {source.Id}.");
        }

        _sourceByTarget = new int[codec.Fields.Length];
        for (var i = 0; i < codec.Fields.Length; i++)
        {
            var targetField = codec.Fields[i];
            var sourceIndex = CodecFields.Find(Fields, targetField.Tag);
            if (sourceIndex < 0 && !targetField.Optional) throw Missing(targetField.Tag);
            _sourceByTarget[i] = sourceIndex;
        }
    }

    internal ComponentSchema Source { get; }
    internal FieldSchema[] Fields { get; }
    internal bool HasCodec => _codec is not null;

    internal long RestoreBlock(World world, int[] ids, int count, CheckpointSlot[] slots, FieldValue[] values,
        CheckpointLimits limits, long spareBytes)
    {
        if (_codec is null) return 0;
        var target = _codec.Fields;
        CheckLimit(target.Length, limits.MaxFields, "target field count");
        var scratchBytes = (long)target.Length * EstimatedValueBytes + 128;
        CheckLimit(scratchBytes, spareBytes, "working storage");
        var remaining = spareBytes - scratchBytes;
        var row = new FieldValue[target.Length];
        var componentReader = new ComponentReader(world, target, row);
        var bag = _migration.Length == 0 ? null : new MigrationFields();
        for (var i = 0; i < count; i++)
        {
            var offset = i * Fields.Length;
            if (bag is null)
            {
                for (var field = 0; field < target.Length; field++)
                {
                    var sourceIndex = _sourceByTarget[field];
                    row[field] = sourceIndex < 0 ? default : values[offset + sourceIndex];
                }
            }
            else
            {
                PopulateMigratedRow(world, target, bag, values, offset, row, limits, remaining);
            }
            var id = ids[i];
            _codec.Restore(world, world.BindEntity(id, slots[id].Version), componentReader, ref remaining);
        }
        return spareBytes - scratchBytes - remaining;
    }

    private void PopulateMigratedRow(World world, FieldSchema[] target, MigrationFields bag,
        FieldValue[] source, int offset, FieldValue[] row, CheckpointLimits limits, long spareBytes)
    {
        bag.Clear();
        for (var i = 0; i < Fields.Length; i++) bag.Set(Fields[i].Tag, source[offset + i]);
        foreach (var migrate in _migration)
        {
            migrate(bag);
            var count = 0;
            long byteCount = 0;
            foreach (var item in bag.Fields)
            {
                CheckLimit(++count, limits.MaxFields, "migrated field count");
                byteCount += EstimatedValueBytes;
                if (item.Value.Kind == FieldKind.Bytes)
                {
                    var bytes = item.Value.AsBytes().Length;
                    CheckLimit(bytes, limits.MaxValueBytes, "migrated field value");
                    byteCount += bytes;
                }
                CheckLimit(byteCount, spareBytes, "working storage");
            }
        }

        Array.Clear(row, 0, row.Length);
        for (var i = 0; i < target.Length; i++)
        {
            var field = target[i];
            if (!bag.TryGet(field.Tag, out var value))
            {
                if (!field.Optional) throw Missing(field.Tag);
                continue;
            }
            if (value.Kind != field.Kind)
                throw new InvalidDataException($"Migration for component {Source.Id} produced an invalid type for field {field.Tag}.");
            if (value.Kind == FieldKind.Entity) CodecFields.ValidateEntity(world, value);
            row[i] = value;
        }
    }

    private InvalidDataException Missing(uint tag) => new($"Component {Source.Id} is missing required field {tag}.");
}

internal sealed class ComponentBlocks
{
    private readonly World? _world;
    private readonly LogicalCheckpoint? _logical;
    private readonly CheckpointSlot[] _slots;
    private readonly CheckpointLimits _limits;
    private readonly long _baseBytes;
    private readonly int[] _ids;
    private FieldValue[] _values = Array.Empty<FieldValue>();
    private uint _previousSchema;
    private int _previousId = -1;
    private long _rows;
    private long _cells;

    internal ComponentBlocks(CheckpointSlot[] slots, CheckpointLimits limits, long baseBytes, World? world, LogicalCheckpoint? logical = null)
    {
        _world = world;
        _logical = logical;
        _slots = slots;
        _limits = limits;
        _baseBytes = baseBytes;
        var maxRows = Math.Min(RowsPerBlock, Math.Min(limits.MaxRowsPerBlock, slots.Length));
        _ids = new int[maxRows];
        if (world is not null)
        {
            var stats = world.GetStorageStatistics();
            StorageBytes = stats.NativeBytes + stats.ChunkCount * (8L * world.ChunkCapacity + 128) + 256;
        }
        CheckLimit(CurrentBytes(), limits.MaxWorkingBytes, "working storage");
    }

    internal long StorageBytes { get; private set; }

    internal void Read(WireReader reader, SortedDictionary<uint, LoadPlan> schema)
    {
        var schemaId = reader.ReadUInt32();
        if (schemaId < _previousSchema || !schema.TryGetValue(schemaId, out var plan))
            throw new InvalidDataException($"Invalid or unordered component {schemaId}.");
        if (schemaId != _previousSchema) _previousId = -1;
        _previousSchema = schemaId;
        var count = NonEmptyCount(reader, _ids.Length, "component");
        CheckLimit(_rows += count, _limits.MaxComponentValues, "component count");
        for (var row = 0; row < count; row++)
        {
            var id = reader.ReadInt32();
            var version = reader.ReadInt32();
            if (id <= _previousId || (uint)id >= (uint)_slots.Length || !_slots[id].Alive || _slots[id].Version != version)
                throw new InvalidDataException($"Invalid or duplicate component {schemaId} for entity {id}:{version}.");
            _ids[row] = id;
            _previousId = id;
        }

        var stride = plan.Fields.Length;
        if (reader.ReadUInt32() != (uint)stride) throw new InvalidDataException($"Incorrect column count for component {schemaId}.");
        var cellCount = checked(count * stride);
        CheckLimit(_cells += cellCount, _limits.MaxComponentValues, "field value count");
        var totalBytes = CurrentBytes();
        var keep = _logical is not null || plan.HasCodec;
        if (keep && cellCount > _values.Length)
        {
            totalBytes += (long)cellCount * EstimatedValueBytes;
            CheckLimit(totalBytes, _limits.MaxWorkingBytes, "working storage");
            totalBytes -= (long)_values.Length * EstimatedValueBytes;
            _values = new FieldValue[cellCount];
        }

        for (var column = 0; column < stride; column++)
        {
            var field = plan.Fields[column];
            if (reader.ReadUInt32() != field.Tag || ReadKind(reader) != field.Kind)
                throw new InvalidDataException($"Invalid column in component {schemaId} for field {field.Tag}.");
            reader.BeginColumn();
            for (var row = 0; row < count; row++)
            {
                var value = reader.ReadField(field.Kind);
                if (value.Kind == FieldKind.Entity) CodecFields.ValidateEntity(_slots, value);
                if (value.Kind == FieldKind.Bytes)
                {
                    var size = (long)value.AsBytes().Length + 32;
                    CheckLimit(totalBytes + size, _limits.MaxWorkingBytes, "working storage");
                    if (keep) totalBytes += size;
                }
                if (keep) _values[row * stride + column] = value;
            }
            reader.EndColumn();
        }

        reader.EndFrame();
        if (_logical is null)
        {
            StorageBytes += plan.RestoreBlock(_world!, _ids, count, _slots, _values, _limits, _limits.MaxWorkingBytes - totalBytes);
        }
        else
        {
            var metadata = count * 152L + cellCount * 72L;
            CheckLimit(totalBytes + metadata, _limits.MaxWorkingBytes, "migration working storage");
            StorageBytes += totalBytes - CurrentBytes() + metadata;
            for (var row = 0; row < count; row++)
            {
                var component = new LogicalComponent(_ids[row], schemaId);
                for (var column = 0; column < stride; column++)
                    component.Fields.Set(plan.Fields[column].Tag, _values[row * stride + column]);
                _logical.Components.Add(component);
            }
        }
        if (keep) Array.Clear(_values, 0, cellCount);
        CheckLimit(CurrentBytes(), _limits.MaxWorkingBytes, "working storage");
    }

    private long CurrentBytes() => _baseBytes + StorageBytes + (long)_values.Length * EstimatedValueBytes + (long)_ids.Length * sizeof(int);
}
