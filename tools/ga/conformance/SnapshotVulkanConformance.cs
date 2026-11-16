using System.Buffers.Binary;
using System.Globalization;
using SKECS;
using SKECS.GA;
using SKECS.Numerics;
using static GaConformanceIo;

internal static class SnapshotVulkanConformance
{
    private const string Kernel = """
        [[vk::binding(0, 0)]] StructuredBuffer<uint> gaInput;
        [[vk::binding(1, 0)]] RWStructuredBuffer<uint> gaOutput;
        [[vk::binding(2, 0)]] RWStructuredBuffer<uint> gaFaults;

        [shader("compute")]
        [numthreads(64, 1, 1)]
        void main(uint3 id : SV_DispatchThreadID)
        {
            uint inputWords, inputStride;
            gaInput.GetDimensions(inputWords, inputStride);
            if (inputWords < 3u) return;
            uint queries = gaInput[0];
            if (id.x >= queries || queries > (inputWords - 3u) / 2u) return;
            uint valueWords = gaInput[1];
            if (valueWords > 0x7ffffffdu) return;

            uint packetStart = 3u + queries * 2u;
            GaSnapshotViewV1 snapshot;
            bool valid = gaTryOpenSnapshotV1(gaInput, packetStart, gaInput[2], valueWords, snapshot);
            uint valuesOffset = 0u;
            bool found = false;
            if (valid)
            {
                uint query = 3u + id.x * 2u;
                found = gaTryFindSnapshotV1(gaInput, snapshot, gaInput[query], gaInput[query + 1u], valuesOffset);
            }

            uint output = id.x * (valueWords + 1u);
            gaOutput[output] = uint(found);
            // Diagnostic marker for malformed packet metadata.
            gaFaults[id.x] = uint(!valid);
            for (uint word = 0u; word < valueWords; ++word)
            {
                uint value = 0u;
                if (found) value = gaInput[valuesOffset + word];
                gaOutput[output + 1u + word] = value;
            }
        }
        """;

    private sealed record Trial(string Name, int ValueWords, uint[] Packet, GaSnapshotKey[] Keys,
        IReadOnlyDictionary<GaSnapshotKey, uint[]> Expected, bool Malformed = false, uint? SectionWords = null);

    internal static async Task Compare(string compiler, string validate, string executor, string directory,
        string[] devices)
    {
        using var library = typeof(GaSlangEmitter).Assembly.GetManifestResourceStream("SKECS.GA.GaSnapshotV1.slang")
            ?? throw new InvalidOperationException("The Snapshot V1 Slang resource is missing.");
        using var reader = new StreamReader(library);
        string source = Path.Combine(directory, "snapshot.slang");
        string binary = Path.Combine(directory, "snapshot.spv");
        string layout = Path.Combine(directory, "snapshot.layout");
        await File.WriteAllTextAsync(source, await reader.ReadToEndAsync() + "\n" + Kernel);
        await Run(compiler, "compile", source, "main", binary, layout);
        await Run(validate, "--target-env", "vulkan1.1", binary);

        var trials = Trials();
        foreach (string device in devices)
        {
            foreach (var trial in trials)
                await CompareTrial(executor, directory, binary, device, trial);
            Console.WriteLine($"Vulkan device {device}: {trials.Sum(trial => trial.Keys.Length)} snapshot lookups across {trials.Count} packets agree with CPU.");
        }
    }

    private static async Task CompareTrial(string executor, string directory, string binary, string device, Trial trial)
    {
        int queryCount = trial.Keys.Length;
        int packetStart = 3 + 2 * queryCount;
        var input = new uint[packetStart + trial.Packet.Length];
        input[0] = (uint)queryCount;
        input[1] = (uint)trial.ValueWords;
        input[2] = trial.SectionWords ?? (uint)trial.Packet.Length;
        for (int i = 0; i < queryCount; i++)
        {
            input[3 + 2 * i] = unchecked((uint)trial.Keys[i].Id);
            input[4 + 2 * i] = unchecked((uint)trial.Keys[i].Generation);
        }
        trial.Packet.CopyTo(input, packetStart);

        int outputStride = trial.ValueWords + 1;
        var readback = new uint[queryCount * outputStride];
        var invalid = new uint[queryCount];
        Array.Fill(readback, 0xcc10ffeeu);
        Array.Fill(invalid, uint.MaxValue);
        string inputPath = Path.Combine(directory, $"snapshot-{trial.Name}.in");
        string outputPath = Path.Combine(directory, $"snapshot-{trial.Name}-{device}.out");
        string faultPath = Path.Combine(directory, $"snapshot-{trial.Name}-{device}.fault");
        Write(inputPath, input);
        Write(outputPath, readback);
        Write(faultPath, invalid);
        int threads = GaSlangModule.ThreadsPerGroup;
        var groups = ((queryCount + threads - 1) / threads).ToString(CultureInfo.InvariantCulture);
        await Run(executor, "dispatch", device, binary, "main", groups,
            "0", "0", "r", inputPath, "0", "1", "rw", outputPath, "0", "2", "rw", faultPath);
        Read(outputPath, readback);
        Read(faultPath, invalid);

        for (int i = 0; i < queryCount; i++)
        {
            var key = trial.Keys[i];
            trial.Expected.TryGetValue(key, out var expected);
            if (trial.Malformed)
                expected = null;
            int row = i * outputStride;
            uint expectedInvalid = trial.Malformed ? 1u : 0u;
            uint expectedFound = expected is null ? 0u : 1u;
            if (invalid[i] != expectedInvalid || readback[row] != expectedFound)
                throw new InvalidDataException($"Snapshot GPU {device}, {trial.Name} row {i} ({key}): expected flags ({expectedFound}, {expectedInvalid}), got ({readback[row]}, {invalid[i]}).");
            for (int word = 0; word < trial.ValueWords; word++)
                if (readback[row + 1 + word] != (expected?[word] ?? 0u))
                    throw new InvalidDataException($"Snapshot GPU {device} changed {trial.Name} row {i} ({key}) word {word}.");
        }
    }

    private static List<Trial> Trials()
    {
        var live = Capture(1, 131);
        var wide = Capture(37, 65);
        if (!live.Packet.SequenceEqual(wide.Packet))
            throw new InvalidDataException("Snapshot capture differs across chunk capacities.");

        GaSnapshotKey[] tagKeys = [new(int.MaxValue, int.MaxValue), new(27, 9), new(0, 1)];
        var tags = tagKeys.ToDictionary(key => key, _ => Array.Empty<uint>());
        var tagPacket = GaSnapshotPacket.FromRows(new GaSnapshotLayout([]), tagKeys, []);
        GaSnapshotKey[] tagQueries = [new(0, 1), new(27, 9), new(int.MaxValue, int.MaxValue),
            new(int.MaxValue, int.MaxValue - 1), new(13, 1), new(-1, 1), new(27, 0), new(27, -1)];

        var scalar = new Dictionary<GaSnapshotKey, uint[]>
        {
            [new(16, 5)] = [0x80000000u],
            [new(2, 1)] = [0xffffffffu]
        };
        GaSnapshotKey[] scalarKeys = [new(16, 5), new(2, 1)];
        var scalarPacket = GaSnapshotPacket.FromRows(new GaSnapshotLayout([GaScalar.Int32]),
            scalarKeys, scalarKeys.SelectMany(key => scalar[key]).ToArray());
        var empty = GaSnapshotPacket.FromRows(new GaSnapshotLayout([GaScalar.UInt32]), [], []);
        var malformedWidth = scalarPacket.Words.ToArray();
        malformedWidth[2] = uint.MaxValue;
        var malformedVersion = scalarPacket.Words.ToArray();
        malformedVersion[0] = 2;
        var malformedCount = scalarPacket.Words.ToArray();
        malformedCount[1] = uint.MaxValue;
        var scalarTrial = new Trial("scalar", 1, scalarPacket.Words.ToArray(), [new(16, 5)], scalar);

        return
        [
            live, wide, scalarTrial,
            new("tags", 0, tagPacket.Words.ToArray(), tagQueries, tags),
            new("empty", 1, empty.Words.ToArray(), [new(0, 1)], new Dictionary<GaSnapshotKey, uint[]>()),
            scalarTrial with { Name = "bad-version", Packet = malformedVersion, Malformed = true },
            scalarTrial with { Name = "bad-width", Packet = malformedWidth, Malformed = true },
            scalarTrial with { Name = "bad-count", Packet = malformedCount, Malformed = true },
            scalarTrial with { Name = "short-header", Packet = scalarPacket.Words[..2].ToArray(), Malformed = true },
            scalarTrial with { Name = "truncated-row", Packet = scalarPacket.Words[..^1].ToArray(), Malformed = true },
            scalarTrial with { Name = "trailing-word", Packet = [.. scalarPacket.Words, 0u], Malformed = true },
            scalarTrial with { Name = "bounded-slice", Packet = [.. scalarPacket.Words, 0u], SectionWords = (uint)scalarPacket.Words.Length },
            scalarTrial with { Name = "oversized-slice", SectionWords = uint.MaxValue, Malformed = true }
        ];
    }

    private static Trial Capture(int chunkCapacity, int queryCount)
    {
        using var world = new World(new WorldOptions(chunkCapacity));
        if (chunkCapacity != 1)
            world.GetAccessor<Marker>();
        var entities = new Entity[79];
        for (int i = 0; i < entities.Length; i++)
        {
            var motion = new Motion { Value = Fixed.FromRaw((long)int.MinValue * i - i), Enabled = i % 2 == 0 };
            entities[i] = i % 4 == 0 ? world.CreateEntity(new Marker()) :
                i % 4 == 1 ? world.CreateEntity(motion, new Marker()) : world.CreateEntity(motion);
        }
        var old = entities[1];
        world.DestroyEntity(old);
        var reused = world.CreateEntity(new Motion { Value = Fixed.FromRaw(long.MinValue), Enabled = true });
        if (reused.Id != old.Id || reused.Version == old.Version)
            throw new InvalidOperationException("Snapshot fixture did not reuse the original entity ID.");
        entities[1] = reused;
        world.Get<Motion>(entities[2]).Value = Fixed.FromRaw(long.MaxValue);

        var snapshot = GaSnapshotPacket.Capture<Motion>(world, new GaSnapshotLayout([GaScalar.Fixed, GaScalar.Bool]),
            static (in Motion motion, Span<uint> words) =>
            {
                words[0] = unchecked((uint)motion.Value.Raw);
                words[1] = unchecked((uint)(motion.Value.Raw >> 32));
                words[2] = motion.Enabled ? 1u : 0u;
            });
        // Read the world directly and convert through bytes for the independent oracle.
        var expected = new Dictionary<GaSnapshotKey, uint[]>();
        Span<byte> bytes = stackalloc byte[8];
        foreach (var entity in entities)
        {
            if (!world.TryGet<Motion>(entity, out var motion))
                continue;
            BinaryPrimitives.WriteInt64LittleEndian(bytes, motion.Value.Raw);
            expected.Add(Key(entity), [BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]), motion.Enabled ? 1u : 0u]);
        }
        var queries = new GaSnapshotKey[queryCount];
        for (int i = 0; i < queryCount; i++)
            queries[i] = Key(entities[(i * 29) % entities.Length]);
        queries[0] = Key(old);
        queries[1] = Key(reused);
        queries[2] = Key(entities[2]);
        queries[3] = new GaSnapshotKey(int.MaxValue, 1);
        return new Trial($"capture-{chunkCapacity}", snapshot.Layout.ValueWordCount, snapshot.Words.ToArray(), queries, expected);
    }

    private static GaSnapshotKey Key(Entity entity) => new(entity.Id, entity.Version);
    private struct Motion { public Fixed Value; public bool Enabled; }
    private readonly struct Marker;
}
