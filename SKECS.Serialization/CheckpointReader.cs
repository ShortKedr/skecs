using static SKECS.Serialization.CheckpointFormat;

namespace SKECS.Serialization;

internal static class CheckpointReader
{
    internal static World Read(WireReader reader, CodecRegistry registry, CheckpointReadOptions options,
        CheckpointLimits limits, out IReadOnlyList<CheckpointExtension> extensions)
    {
        extensions = Array.Empty<CheckpointExtension>();
        registry.Seal();
        reader.BeginFile();
        Begin(reader, Header);
        var major = reader.ReadUInt32();
        var minor = reader.ReadUInt32();
        var capabilities = reader.ReadUInt32();
        var allocator = reader.ReadUInt32();
        if (major != Major || minor != Minor || capabilities != Capabilities || allocator != Allocator)
            throw new InvalidDataException($"Unsupported checkpoint version {major}.{minor}, capabilities {capabilities}, allocator {allocator}.");
        var fingerprint = reader.ReadBytes();
        if (fingerprint.Length != 32) throw new InvalidDataException("Invalid checkpoint schema fingerprint length.");
        if (options.RequireExactSchema && !registry.GetFingerprint().AsSpan().SequenceEqual(fingerprint))
            throw new InvalidDataException("Checkpoint schema fingerprint does not match the codec registry.");
        var slotCount = Count(reader, limits.MaxSlots, "slot count");
        var freeCount = Count(reader, slotCount, "free ID count");
        var schemaCount = Count(reader, limits.MaxSchemas, "schema count");
        var extensionCount = Count(reader, limits.MaxExtensions, "extension count");
        reader.EndFrame();

        var omitted = new List<string>();
        long workingBytes = 64L * slotCount + 8L * freeCount + 128L * schemaCount + 64L * extensionCount;
        CheckLimit(workingBytes, limits.MaxWorkingBytes, "working storage");
        var structuralPath = registry.GetCheckpointMigrationPath(fingerprint);
        var schema = ReadSchemas(reader, registry, options, limits, schemaCount, omitted, structuralPath.Length != 0, ref workingBytes);
        if (!Fingerprint(schema.Values.Select(plan => plan.Source)).AsSpan().SequenceEqual(fingerprint))
            throw new InvalidDataException("Checkpoint schema fingerprint does not match the manifest.");

        var slots = ReadSlots(reader, slotCount, limits, out var liveCount);
        var free = ReadFreeIds(reader, freeCount, limits);
        if (structuralPath.Length != 0)
            return ReadLogical(reader, registry, options, limits, schema, slots, free, structuralPath, extensionCount,
                omitted, workingBytes, out extensions);

        var world = CreateWorld(slots, free, liveCount, options, workingBytes, limits, out var rootBytes);
        try
        {
            var componentBlocks = new ComponentBlocks(slots, limits, workingBytes, world, rootBytes: rootBytes);
            var frame = ReadComponents(reader, schema, componentBlocks);
            workingBytes += componentBlocks.StorageBytes;
            var restoredExtensions = ReadExtensions(reader, frame, extensionCount, options, limits, omitted, workingBytes);
            reader.EndFile();

            foreach (var message in omitted) options.OnOmitted?.Invoke(message);
            extensions = restoredExtensions.AsReadOnly();
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    internal static World CreateWorld(CheckpointSlot[] slots, int[] free, int liveCount, CheckpointReadOptions options,
        long workingBytes, CheckpointLimits limits, out long rootBytes)
    {
        var capacity = options.WorldOptions.ChunkCapacity;
        if (capacity == 0) capacity = new WorldOptions().ChunkCapacity;
        var control = options.WorldOptions.ChurnControl;
        var perChunkOverhead = control is null ? 128L : 512L;
        rootBytes = ((liveCount + (long)capacity - 1) / capacity) * (8L * capacity + 1 + perChunkOverhead) + 256;
        var available = limits.MaxWorkingBytes - workingBytes;
        if (control is not null)
        {
            // Charge the optional table reservation even when it exceeds the number of restored slots.
            rootBytes += 40L * control.InitialEntityCapacity;
            CheckLimit(rootBytes, available, "working storage");
            foreach (var block in control.StartupBlocks)
            {
                var bytes = block.NativeBytes + 8L * capacity + perChunkOverhead;
                if (block.Count > (available - rootBytes) / bytes)
                    throw new InvalidDataException("Checkpoint startup reserve exceeds its working storage limit.");
                rootBytes += block.Count * bytes;
            }
        }
        CheckLimit(rootBytes, available, "working storage");
        try { return World.RestoreCheckpoint(slots, free, options.WorldOptions); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid checkpoint allocator metadata.", error); }
    }

    private static World ReadLogical(WireReader reader, CodecRegistry registry, CheckpointReadOptions options,
        CheckpointLimits limits, SortedDictionary<uint, LoadPlan> schema, CheckpointSlot[] slots, int[] free,
        Action<LogicalCheckpoint>[] path, int extensionCount, List<string> omitted, long workingBytes,
        out IReadOnlyList<CheckpointExtension> extensions)
    {
        extensions = Array.Empty<CheckpointExtension>();
        try { World.ValidateCheckpointAllocator(slots, free); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid checkpoint allocator metadata.", error); }
        var logical = new LogicalCheckpoint(slots, free);
        CaptureLogical(reader, options, limits, schema, logical, extensionCount, omitted, workingBytes);
        var baseBytes = workingBytes - 64L * slots.Length - 8L * free.Length;
        foreach (var step in path)
        {
            step(logical);
            logical.ValidateLimits(limits, baseBytes);
        }
        var restoredExtensions = new List<CheckpointExtension>();
        var extensionIds = new HashSet<uint>();
        foreach (var extension in logical.Extensions.OrderBy(extension => extension.Id))
        {
            if (!extensionIds.Add(extension.Id)) throw new InvalidDataException("Duplicate migrated extension ID.");
            if (RecognizeExtension(extension.Id, extension.Version, extension.Required, options, omitted))
                restoredExtensions.Add(extension);
        }
        ValidateRequiredExtensions(options, restoredExtensions);

        var world = logical.Restore(registry, options, limits, baseBytes);
        try
        {
            foreach (var message in omitted) options.OnOmitted?.Invoke(message);
            extensions = restoredExtensions.AsReadOnly();
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    private static void CaptureLogical(WireReader reader, CheckpointReadOptions options, CheckpointLimits limits,
        SortedDictionary<uint, LoadPlan> schema, LogicalCheckpoint logical, int extensionCount, List<string> omitted,
        long workingBytes)
    {
        var blocks = new ComponentBlocks(logical.Slots, limits, workingBytes, world: null, logical);
        var frame = ReadComponents(reader, schema, blocks);
        foreach (var extension in ReadExtensions(reader, frame, extensionCount, options, limits, omitted,
            workingBytes + blocks.StorageBytes, retainAll: true))
            logical.Extensions.Add(extension);
        reader.EndFile();
    }

    private static byte ReadComponents(WireReader reader, SortedDictionary<uint, LoadPlan> schema, ComponentBlocks blocks)
    {
        var frame = reader.BeginFrame();
        while (frame == Component)
        {
            blocks.Read(reader, schema);
            frame = reader.BeginFrame();
        }
        return frame;
    }

    private static SortedDictionary<uint, LoadPlan> ReadSchemas(WireReader reader, CodecRegistry registry,
        CheckpointReadOptions options, CheckpointLimits limits, int count, List<string> omitted, bool logical, ref long workingBytes)
    {
        var plans = new SortedDictionary<uint, LoadPlan>();
        uint previousId = 0;
        for (var index = 0; index < count; index++)
        {
            Begin(reader, Schema);
            var id = reader.ReadUInt32();
            var version = reader.ReadUInt32();
            if (id <= previousId || version == 0) throw new InvalidDataException("Invalid or unordered schema ID or version.");
            previousId = id;
            var fieldCount = Count(reader, limits.MaxFields, "schema field count");
            workingBytes += 96L * fieldCount;
            CheckLimit(workingBytes, limits.MaxWorkingBytes, "working storage");
            var fields = new FieldSchema[fieldCount];
            uint previousTag = 0;
            for (var field = 0; field < fieldCount; field++)
            {
                var tag = reader.ReadUInt32();
                if (tag <= previousTag) throw new InvalidDataException($"Invalid or unordered field tag in component {id}.");
                previousTag = tag;
                fields[field] = new FieldSchema(tag, ReadKind(reader), reader.ReadBoolean(), reader.ReadBoolean());
            }
            reader.EndFrame();
            var source = new ComponentSchema(id, version, fields);
            registry.TryGet(id, out var codec);
            plans.Add(id, new LoadPlan(source, codec, registry, options.AllowUnknown, omitted, logical));
        }
        return plans;
    }

    private static CheckpointSlot[] ReadSlots(WireReader reader, int count, CheckpointLimits limits, out int liveCount)
    {
        var slots = new CheckpointSlot[count];
        liveCount = 0;
        for (var start = 0; start < count;)
        {
            Begin(reader, Slots);
            var rows = NonEmptyCount(reader, Math.Min(count - start, Math.Min(RowsPerBlock, limits.MaxRowsPerBlock)), "slots");
            for (var i = 0; i < rows; i++)
            {
                var version = reader.ReadInt32();
                var alive = reader.ReadBoolean();
                if (version < 1) throw new InvalidDataException("Invalid slot generation.");
                slots[start + i] = new CheckpointSlot(version, alive);
                if (alive) liveCount++;
            }
            reader.EndFrame();
            start += rows;
        }
        return slots;
    }

    private static int[] ReadFreeIds(WireReader reader, int count, CheckpointLimits limits)
    {
        var free = new int[count];
        for (var start = 0; start < count;)
        {
            Begin(reader, Free);
            var rows = NonEmptyCount(reader, Math.Min(count - start, Math.Min(RowsPerBlock, limits.MaxRowsPerBlock)), "free IDs");
            for (var i = 0; i < rows; i++) free[start + i] = reader.ReadInt32();
            reader.EndFrame();
            start += rows;
        }
        return free;
    }

    private static List<CheckpointExtension> ReadExtensions(WireReader reader, byte frame, int count,
        CheckpointReadOptions options, CheckpointLimits limits, List<string> omitted, long workingBytes, bool retainAll = false)
    {
        var restored = new List<CheckpointExtension>(count);
        uint previousId = 0;
        for (var i = 0; i < count; i++)
        {
            if (frame != Extension) throw new InvalidDataException("Missing checkpoint extension frame.");
            var id = reader.ReadUInt32();
            var version = reader.ReadUInt32();
            var required = reader.ReadBoolean();
            if (id <= previousId || version == 0) throw new InvalidDataException("Invalid or unordered extension ID or version.");
            previousId = id;

            var recognized = retainAll || RecognizeExtension(id, version, required, options, omitted);
            var data = reader.ReadBytes();
            CheckLimit(workingBytes + data.Length, limits.MaxWorkingBytes, "working storage");
            reader.EndFrame();
            if (recognized)
            {
                restored.Add(new CheckpointExtension(id, version, required, data));
                workingBytes += data.Length;
            }
            frame = reader.BeginFrame();
        }

        if (frame != End) throw new InvalidDataException("Missing checkpoint end frame.");
        reader.EndFrame();
        if (!retainAll) ValidateRequiredExtensions(options, restored);
        return restored;
    }

    private static bool RecognizeExtension(uint id, uint version, bool required, CheckpointReadOptions options, List<string> omitted)
    {
        if (options.Extensions.TryGetValue(id, out var supportedVersion) && supportedVersion == version) return true;
        if (required || options.RequiredExtensions.Contains(id))
            throw new InvalidDataException($"Unsupported required extension {id} version {version}.");
        omitted.Add($"Omitted optional extension {id} version {version}.");
        return false;
    }

    private static void ValidateRequiredExtensions(CheckpointReadOptions options, List<CheckpointExtension> restored)
    {
        foreach (var id in options.RequiredExtensions)
            if (!restored.Exists(extension => extension.Id == id))
                throw new InvalidDataException($"Missing required extension {id}.");
    }
}
