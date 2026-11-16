using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using SKECS.Profiler;

namespace SKECS.RuntimeProfiles.Tools;

internal sealed record RunRequest(Scenario Scenario, int ChunkCapacity, int EventCapacity, Policy? Policy);
internal sealed record MemorySample(long RetainedBytes, long MandatoryBytes, long Allocations, long Releases);
internal sealed record TimedFrame(string Phase, long FrameTicks, long TrimTicks, long ManagedBytes,
    int ExplicitReleases, string Checksum, MemorySample Memory);
internal sealed record DiagnosticSummary(int Version, int Events, long PeakRetained, MemoryPeak LowerBound);
internal sealed record RunResult(RunRequest Request, RunEnvironment Environment, long StartupTicks, long StartupManagedBytes,
    long FirstFrameTicks, MemorySample Initial, MemorySample AfterWarmup, int[] FrameGcCollections,
    string[] StateHashes, DiagnosticSummary Diagnostics, TimedFrame[] Frames);

internal static class ScenarioRunner
{
    internal static void Capture(Corpus corpus, Scenario scenario, string workloadId, Stream destination)
    {
        var session = new ProfilerSession(corpus.EventCapacity);
        var workload = new Workload(scenario.Workload, scenario.Entities, scenario.Seed);
        using (var world = new World(new WorldOptions(corpus.ChunkCapacity), session.Buffer))
        {
            var start = Stopwatch.GetTimestamp();
            workload.Initialize(world);
            session.RecordDuration("startup", Stopwatch.GetTimestamp() - start);
            for (var frame = 0; frame < scenario.WarmupFrames + scenario.Frames; frame++)
            {
                session.BeginFrame();
                _ = workload.Step(world, frame);
                session.EndFrame();
            }
        }
        session.Export(destination, scenario.Id, workloadId);
    }

    internal static RunResult Run(RunRequest request)
    {
        // JIT both branches in each isolated process before preparing the measured world.
        var warmup = request.Scenario with { Entities = 32, WarmupFrames = 4, Frames = 8 };
        PrepareCode(warmup, new WorldOptions(request.ChunkCapacity));
        PrepareCode(warmup, new WorldOptions(request.ChunkCapacity).WithChurnControl(new ChurnControlOptions(long.MaxValue)));

        var options = Options(request);
        var scenario = request.Scenario;
        var workload = new Workload(scenario.Workload, scenario.Entities, scenario.Seed);
        var frames = new TimedFrame[scenario.Frames];
        var startupManagedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startupStart = Stopwatch.GetTimestamp();
        using var world = new World(options);
        workload.Initialize(world);
        var startupTicks = Stopwatch.GetTimestamp() - startupStart;
        var startupManaged = GC.GetAllocatedBytesForCurrentThread() - startupManagedBefore;
        var initial = Memory(world, request.Policy != null);
        long firstFrameTicks = 0;
        for (var frame = 0; frame < scenario.WarmupFrames; frame++)
        {
            var start = Stopwatch.GetTimestamp();
            _ = workload.Step(world, frame);
            if (request.Policy != null) world.TrimChurnMemory();
            if (frame == 0) firstFrameTicks = Stopwatch.GetTimestamp() - start;
        }
        var afterWarmup = Memory(world, request.Policy != null);
        var gcBefore = Enumerable.Range(0, GC.MaxGeneration + 1).Select(GC.CollectionCount).ToArray();
        for (var index = 0; index < frames.Length; index++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            var phase = workload.Step(world, scenario.WarmupFrames + index);
            var workEnd = Stopwatch.GetTimestamp();
            var released = request.Policy == null ? 0 : world.TrimChurnMemory();
            var end = request.Policy == null ? workEnd : Stopwatch.GetTimestamp();
            var managed = GC.GetAllocatedBytesForCurrentThread() - allocated;
            frames[index] = new(phase, end - start, end - workEnd, managed, released,
                workload.Checksum(world), Memory(world, request.Policy != null));
        }
        var gcCollections = gcBefore.Select((count, generation) => GC.CollectionCount(generation) - count).ToArray();
        world.Dispose();

        var (hashes, diagnostics) = CheckState(request, frames);
        return new(request, RunEnvironment.Current(), startupTicks, startupManaged, firstFrameTicks,
            initial, afterWarmup, gcCollections, hashes, diagnostics, frames);
    }

    private static void PrepareCode(Scenario scenario, WorldOptions options)
    {
        using var world = new World(options);
        var workload = new Workload(scenario.Workload, scenario.Entities, scenario.Seed);
        workload.Initialize(world);
        for (var frame = 0; frame < scenario.WarmupFrames + scenario.Frames; frame++)
        {
            _ = workload.Step(world, frame);
            if (options.ChurnControl != null) world.TrimChurnMemory();
        }
    }

    private static WorldOptions Options(RunRequest request)
    {
        var options = new WorldOptions(request.ChunkCapacity);
        var policy = request.Policy;
        return policy == null ? options : options.WithChurnControl(new ChurnControlOptions(policy.TargetRetainedBytes,
            policy.StartupReserveBytes, policy.StartupBlocks.Select(block => new ChurnStartupBlock(block.NativeBytes, block.Count)).ToArray(),
            policy.InitialEntityCapacity, policy.MaxOversizePercent, policy.ReleaseBatchSize));
    }

    private static MemorySample Memory(World world, bool controlEnabled)
    {
        var storage = world.GetChurnControlStatistics();
        if (storage.Enabled != controlEnabled) throw new InvalidOperationException("World did not apply the requested churn policy.");
        return new(storage.RetainedBytes, storage.NonReclaimableBytes, storage.AllocationCount, storage.ReleaseCount);
    }

    internal static (string[] StateHashes, DiagnosticSummary Diagnostics) CheckState(RunRequest request, TimedFrame[]? timed = null)
    {
        var scenario = request.Scenario;
        var workload = new Workload(scenario.Workload, scenario.Entities, scenario.Seed);
        var session = new ProfilerSession(request.EventCapacity);
        using var world = new World(Options(request), session.Buffer);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var components = new Dictionary<Type, WriteComponent>();
        var hashes = new string[scenario.WarmupFrames + scenario.Frames + 1];
        workload.Initialize(world);
        hashes[0] = Snapshot(world, workload.Checksum(world), "initial", stream, writer, components);
        for (var frame = 0; frame < hashes.Length - 1; frame++)
        {
            session.BeginFrame();
            var phase = workload.Step(world, frame);
            if (request.Policy != null) world.TrimChurnMemory();
            session.EndFrame();
            var checksum = workload.Checksum(world);
            if (timed != null && frame >= scenario.WarmupFrames)
            {
                var sample = timed[frame - scenario.WarmupFrames];
                if (sample.Checksum != checksum || sample.Phase != phase)
                    throw new InvalidDataException($"Timed and untimed {scenario.Id} executions differ at frame {frame}.");
            }
            hashes[frame + 1] = Snapshot(world, checksum, phase, stream, writer, components);
        }
        world.Dispose();
        stream.SetLength(0);
        session.Export(stream, scenario.Id, "verification");
        stream.Position = 0;
        var trace = TraceReader.Read(stream);
        if (trace.Header.SchemaVersion != (request.Policy == null ? 1 : 2) || trace.Frames != hashes.Length - 1 ||
            trace.ChunkCapacity != request.ChunkCapacity)
            throw new InvalidDataException($"Verification diagnostic trace does not match {scenario.Id}.");
        return (hashes, new DiagnosticSummary(trace.Header.SchemaVersion, trace.Header.RecordedEvents,
            trace.PeakRetained, trace.LowerBound));
    }

    private delegate void WriteComponent(World.CheckpointView view, Entity entity, BinaryWriter writer);

    private static string Snapshot(World world, string workloadChecksum, string phase, MemoryStream stream,
        BinaryWriter writer, Dictionary<Type, WriteComponent> components)
    {
        var view = world.OpenCheckpoint();
        stream.SetLength(0);
        writer.Write(phase);
        writer.Write(workloadChecksum);
        writer.Write(view.FreeCount);
        var free = new int[view.FreeCount];
        view.CopyFreeIds(free);
        foreach (var id in free) writer.Write(id);
        writer.Write(view.SlotCount);
        for (var id = 0; id < view.SlotCount; id++)
        {
            var slot = view.GetSlot(id);
            writer.Write(slot.Version);
            writer.Write(slot.Alive);
            if (!slot.Alive) continue;
            var entity = world.BindEntity(id, slot.Version);
            var count = view.GetComponentCount(entity);
            var types = new Type[count];
            for (var index = 0; index < count; index++) types[index] = view.GetComponentType(entity, index);
            Array.Sort(types, (left, right) => StringComparer.Ordinal.Compare(left.AssemblyQualifiedName, right.AssemblyQualifiedName));
            writer.Write(count);
            foreach (var type in types)
            {
                if (!components.TryGetValue(type, out var serialize))
                {
                    var method = typeof(ScenarioRunner).GetMethod(nameof(ComponentWriter), BindingFlags.NonPublic | BindingFlags.Static)!;
                    serialize = method.MakeGenericMethod(type).CreateDelegate<WriteComponent>();
                    components.Add(type, serialize);
                }
                writer.Write(type.AssemblyQualifiedName!);
                serialize(view, entity, writer);
            }
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static void ComponentWriter<T>(World.CheckpointView view, Entity entity, BinaryWriter writer) where T : unmanaged
    {
        if (!view.TryGet<T>(entity, out var value)) throw new InvalidDataException($"Checkpoint lost component {typeof(T)}.");
        foreach (var field in Fields<T>.Ordered)
        {
            // All registered benchmark components encode their logical data in signed 64-bit fields.
            // Layout padding is deliberately excluded from semantic equality.
            if (field.GetValue(value) is not long number)
                throw new InvalidDataException($"Unsupported benchmark field {typeof(T)}.{field.Name}.");
            writer.Write(number);
        }
    }

    private static class Fields<T>
    {
        internal static readonly FieldInfo[] Ordered = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
    }
}
