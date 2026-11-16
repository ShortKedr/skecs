using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using SKECS;
using SKECS.Serialization;
using SKECS.Serialization.Generated;

internal static class CheckpointBenchmark
{
    public static void Run(string scenario, string format, int iterations, int count)
    {
        var schema = WorldSchema.Create();
        using var source = new World();
        var live = Populate(source, count, scenario == "churn");
        var output = Path.GetFullPath(Path.Combine("artifacts", "serialization-sample"));
        Directory.CreateDirectory(output);
        using var file = new FileStream(Path.Combine(output, $"checkpoint-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.DeleteOnClose);

        Write(source, file, schema, format);
        file.Position = 0;
        using (var warmup = Read(file, schema, format))
            Verify(source, warmup);

        using var process = Process.GetCurrentProcess();
        var baselinePeak = PeakWorkingSet(process);
        var writeTicks = 0L;
        var readTicks = 0L;
        var writeAllocated = 0L;
        var readAllocated = 0L;
        var bytes = 0L;
        var largestRead = 0L;
        for (var i = 0; i < iterations; i++)
        {
            file.SetLength(0);
            file.Position = 0;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            Write(source, file, schema, format);
            file.Flush();
            writeTicks += Stopwatch.GetTimestamp() - start;
            writeAllocated += GC.GetAllocatedBytesForCurrentThread() - before;
            bytes += file.Length;

            file.Position = 0;
            before = GC.GetAllocatedBytesForCurrentThread();
            start = Stopwatch.GetTimestamp();
            using var restored = Read(file, schema, format);
            var elapsedRead = Stopwatch.GetTimestamp() - start;
            readAllocated += GC.GetAllocatedBytesForCurrentThread() - before;
            readTicks += elapsedRead;
            largestRead = Math.Max(largestRead, elapsedRead);
            Verify(source, restored);
        }

        var peak = PeakWorkingSet(process);
        var mib = bytes / (1024d * 1024);
        var writeSeconds = writeTicks / (double)Stopwatch.Frequency;
        var readSeconds = readTicks / (double)Stopwatch.Frequency;
        Report($"runtime={RuntimeInformation.FrameworkDescription} os={RuntimeInformation.OSDescription} arch={RuntimeInformation.ProcessArchitecture} logical_cpus={Environment.ProcessorCount} pid={Environment.ProcessId}");
        Report($"scenario={scenario} format={format} iterations={iterations} slots={count} live={live}");
        Report($"bytes_per_checkpoint={bytes / (double)iterations:F2} bytes_per_live_entity={bytes / (double)(iterations * (long)live):F2}");
        Report($"write_mib_per_second={mib / writeSeconds:F3} read_mib_per_second={mib / readSeconds:F3}");
        Report($"write_allocated_bytes_per_iteration={writeAllocated / iterations} read_allocated_bytes_per_iteration={readAllocated / iterations}");
        Report($"read_mean_ms={1000 * readSeconds / iterations:F3} read_max_ms={1000 * largestRead / (double)Stopwatch.Frequency:F3}");
        Report($"process_peak_working_set_bytes_after_warmup={baselinePeak} process_peak_working_set_bytes_after_iterations={peak}");
    }

    private static void Report(FormattableString report) => Console.WriteLine(report.ToString(CultureInfo.InvariantCulture));

    private static long PeakWorkingSet(Process process)
    {
        process.Refresh();
        return process.PeakWorkingSet64;
    }

    private static int Populate(World world, int count, bool churn)
    {
        var handles = new Entity[count];
        for (var i = 0; i < count; i++)
            handles[i] = world.CreateEntity(new Motion(i, 1 + i % 11), new Label(i % 5));
        if (!churn)
            return count;

        var live = count;
        for (var round = 0; round < 4; round++)
        {
            for (var i = round; i < count; i += 4)
            {
                world.DestroyEntity(handles[i]);
                handles[i] = i % 2 == 0 ? world.CreateEntity() : world.CreateEntity(new Motion(-i, round));
            }
            for (var i = 0; i < count; i++)
            {
                if (i % 7 == 0)
                    world.Set(handles[i], new Label(round));
                else if (i % 5 == 0)
                    world.Remove<Label>(handles[i]);
            }
        }
        for (var i = 10; i < count; i += 5)
        {
            world.DestroyEntity(handles[i]);
            live--;
        }
        return live;
    }

    private static void Verify(World source, World restored)
    {
        var expected = source.OpenCheckpoint();
        var actual = restored.OpenCheckpoint();
        if (expected.SlotCount != actual.SlotCount || expected.FreeCount != actual.FreeCount)
            throw new InvalidDataException("Checkpoint restored different allocator counts.");
        for (var id = 0; id < expected.SlotCount; id++)
        {
            var expectedSlot = expected.GetSlot(id);
            var actualSlot = actual.GetSlot(id);
            if (expectedSlot.Version != actualSlot.Version || expectedSlot.Alive != actualSlot.Alive)
                throw new InvalidDataException($"Checkpoint restored different allocator state for slot {id}.");
            if (!expectedSlot.Alive)
                continue;

            var sourceEntity = source.BindEntity(id, expectedSlot.Version);
            var restoredEntity = restored.BindEntity(id, actualSlot.Version);
            var expectedHasMotion = expected.TryGet<Motion>(sourceEntity, out var expectedMotion);
            var actualHasMotion = actual.TryGet<Motion>(restoredEntity, out var actualMotion);
            var expectedHasLabel = expected.TryGet<Label>(sourceEntity, out var expectedLabel);
            var actualHasLabel = actual.TryGet<Label>(restoredEntity, out var actualLabel);
            if (expectedHasMotion != actualHasMotion || expectedMotion != actualMotion ||
                expectedHasLabel != actualHasLabel || expectedLabel != actualLabel)
                throw new InvalidDataException($"Checkpoint restored different component values for slot {id}.");
        }
    }

    private static void Write(World world, Stream stream, CodecRegistry registry, string format)
    {
        if (format == "json")
            WorldSerializer.WriteJson(world, stream, registry);
        else
            WorldSerializer.WriteBinary(world, stream, registry);
    }

    private static World Read(Stream stream, CodecRegistry registry, string format)
        => format == "json" ? WorldSerializer.ReadJson(stream, registry) : WorldSerializer.ReadBinary(stream, registry);
}
