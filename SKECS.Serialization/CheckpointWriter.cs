using static SKECS.Serialization.CheckpointFormat;

namespace SKECS.Serialization;

internal static class CheckpointWriter
{
    internal static void Write(WireWriter writer, World world, CodecRegistry registry,
        IReadOnlyList<CheckpointExtension> extensions, CheckpointLimits limits)
    {
        var view = world.OpenCheckpoint();
        var schemas = registry.GetSchemas();
        var rowsPerBlock = Math.Min(RowsPerBlock, limits.MaxRowsPerBlock);
        CheckLimit(view.SlotCount, limits.MaxSlots, "slot count");
        CheckLimit(schemas.Count, limits.MaxSchemas, "schema count");
        CheckLimit(extensions.Count, limits.MaxExtensions, "extension count");

        long workingBytes = 4L * view.FreeCount;
        foreach (var schema in schemas)
        {
            CheckLimit(schema.Fields.Count, limits.MaxFields, "field count");
            workingBytes += 64L + 16L * schema.Fields.Count;
        }
        CheckLimit(workingBytes, limits.MaxWorkingBytes, "working storage");

        var selected = SelectComponents(view, world, registry, limits, ref workingBytes);
        var orderedExtensions = extensions.OrderBy(extension => extension.Id).ToArray();
        uint previousExtension = 0;
        foreach (var extension in orderedExtensions)
        {
            if (extension.Id <= previousExtension) throw new ArgumentException("Duplicate checkpoint extension ID.", nameof(extensions));
            previousExtension = extension.Id;
            CheckLimit(extension.Data.Length, limits.MaxValueBytes, "extension payload");
        }

        writer.BeginFile();
        writer.BeginFrame(Header);
        writer.WriteUInt32(Major);
        writer.WriteUInt32(Minor);
        writer.WriteUInt32(Capabilities);
        writer.WriteUInt32(Allocator);
        writer.WriteBytes(Fingerprint(schemas));
        writer.WriteUInt32((uint)view.SlotCount);
        writer.WriteUInt32((uint)view.FreeCount);
        writer.WriteUInt32((uint)schemas.Count);
        writer.WriteUInt32((uint)orderedExtensions.Length);
        writer.EndFrame();

        foreach (var schema in schemas) WriteSchema(writer, schema);
        WriteAllocator(writer, view, rowsPerBlock);
        foreach (var group in selected.Values)
            WriteComponents(writer, world, view, group, limits, rowsPerBlock, workingBytes);

        foreach (var extension in orderedExtensions)
        {
            writer.BeginFrame(Extension);
            writer.WriteUInt32(extension.Id);
            writer.WriteUInt32(extension.Version);
            writer.WriteBoolean(extension.Required);
            writer.WriteBytes(extension.Data);
            writer.EndFrame();
        }
        writer.BeginFrame(End);
        writer.EndFrame();
        writer.EndFile();
    }

    private static SortedDictionary<uint, ComponentGroup> SelectComponents(World.CheckpointView view,
        World world, CodecRegistry registry, CheckpointLimits limits, ref long workingBytes)
    {
        var selected = new SortedDictionary<uint, ComponentGroup>();
        long components = 0;
        long values = 0;
        for (var id = 0; id < view.SlotCount; id++)
        {
            var slot = view.GetSlot(id);
            if (!slot.Alive) continue;
            var entity = world.BindEntity(id, slot.Version);
            var count = view.GetComponentCount(entity);
            for (var index = 0; index < count; index++)
            {
                var type = view.GetComponentType(entity, index);
                if (!registry.TryGet(type, out var codec) || codec is null)
                    throw new InvalidDataException($"Live component {type} has no checkpoint codec.");
                if (!selected.TryGetValue(codec.Schema.Id, out var group))
                {
                    group = new ComponentGroup(codec);
                    selected.Add(codec.Schema.Id, group);
                }
                CheckLimit(++components, limits.MaxComponentValues, "component count");
                values += codec.Fields.Length;
                CheckLimit(values, limits.MaxComponentValues, "field value count");
                workingBytes += 8;
                CheckLimit(workingBytes, limits.MaxWorkingBytes, "working storage");
                group.EntityIds.Add(id);
            }
        }
        return selected;
    }

    private static void WriteSchema(WireWriter writer, ComponentSchema schema)
    {
        writer.BeginFrame(Schema);
        writer.WriteUInt32(schema.Id);
        writer.WriteUInt32(schema.Version);
        writer.WriteUInt32((uint)schema.Fields.Count);
        foreach (var field in schema.Fields)
        {
            writer.WriteUInt32(field.Tag);
            writer.WriteUInt32((uint)field.Kind);
            writer.WriteBoolean(field.Optional);
            writer.WriteBoolean(field.Retired);
        }
        writer.EndFrame();
    }

    private static void WriteAllocator(WireWriter writer, World.CheckpointView view, int rowsPerBlock)
    {
        var slotCount = view.SlotCount;
        for (var start = 0; start < slotCount;)
        {
            var count = Math.Min(rowsPerBlock, slotCount - start);
            writer.BeginFrame(Slots);
            writer.WriteUInt32((uint)count);
            for (var i = 0; i < count; i++)
            {
                var slot = view.GetSlot(start + i);
                writer.WriteInt32(slot.Version);
                writer.WriteBoolean(slot.Alive);
            }
            writer.EndFrame();
            start += count;
        }

        var free = new int[view.FreeCount];
        view.CopyFreeIds(free);
        for (var start = 0; start < free.Length;)
        {
            var count = Math.Min(rowsPerBlock, free.Length - start);
            writer.BeginFrame(Free);
            writer.WriteUInt32((uint)count);
            for (var i = 0; i < count; i++) writer.WriteInt32(free[start + i]);
            writer.EndFrame();
            start += count;
        }
    }

    private static void WriteComponents(WireWriter writer, World world, World.CheckpointView view,
        ComponentGroup group, CheckpointLimits limits, int rowsPerBlock, long workingBytes)
    {
        var codec = group.Codec;
        var fields = codec.Fields;
        var stride = fields.Length;
        // Keep variable-width rows independently framed so one large value cannot force
        // the writer to accumulate bytes for other entities before enforcing the limit.
        if (Array.Exists(fields, field => field.Kind == FieldKind.Bytes)) rowsPerBlock = 1;
        rowsPerBlock = Math.Min(rowsPerBlock, group.EntityIds.Count);
        var cells = rowsPerBlock * stride;
        workingBytes += (long)cells * EstimatedValueBytes;
        CheckLimit(workingBytes, limits.MaxWorkingBytes, "working storage");
        var values = new FieldValue[cells];
        var componentWriter = new ComponentWriter(world, fields, values, limits.MaxValueBytes);
        for (var start = 0; start < group.EntityIds.Count;)
        {
            var count = Math.Min(rowsPerBlock, group.EntityIds.Count - start);
            Array.Clear(values, 0, count * stride);
            componentWriter.StartBlock(Math.Min(limits.MaxWorkingBytes - workingBytes, limits.MaxFrameBytes));
            writer.BeginFrame(Component);
            writer.WriteUInt32(codec.Schema.Id);
            writer.WriteUInt32((uint)count);
            for (var row = 0; row < count; row++)
            {
                var id = group.EntityIds[start + row];
                var version = view.GetSlot(id).Version;
                writer.WriteInt32(id);
                writer.WriteInt32(version);
                componentWriter.Start(row * stride);
                codec.Capture(view, world.BindEntity(id, version), componentWriter);
                componentWriter.Complete();
            }
            writer.WriteUInt32((uint)stride);
            for (var column = 0; column < stride; column++)
            {
                writer.WriteUInt32(fields[column].Tag);
                writer.WriteUInt32((uint)fields[column].Kind);
                writer.BeginColumn();
                for (var row = 0; row < count; row++) writer.WriteField(values[row * stride + column]);
                writer.EndColumn();
            }
            writer.EndFrame();
            start += count;
        }
    }

    private sealed class ComponentGroup
    {
        internal ComponentGroup(Codec codec) => Codec = codec;
        internal Codec Codec { get; }
        internal List<int> EntityIds { get; } = new();
    }
}
