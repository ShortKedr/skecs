using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SKECS;

if (args.Length != 8 || !Workload.Supports(args[0]) ||
    !TryPositive(args[2], out var frames) || !TryPositive(args[3], out var warmup) ||
    !TryPositive(args[4], out var entities) || !TryPositive(args[5], out var chunkCapacity) ||
    !int.TryParse(args[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed) ||
    string.IsNullOrWhiteSpace(args[7]))
    return Usage();

long? target = null;
if (args[1] != "off")
{
#if CHURN_CONTROL
    if (!long.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var bytes))
        return Usage();
    target = bytes;
#else
    return Usage();
#endif
}

var scenario = args[0];
var options = new WorldOptions(chunkCapacity);
#if CHURN_CONTROL
if (target is { } retainedBytes) options = options.WithChurnControl(new ChurnControlOptions(retainedBytes));
#endif

// Allocate the harness arrays outside the startup and frame intervals.
var workload = new Workload(scenario, entities, seed);
var samples = new FrameSample[frames];
var beforeStartup = GC.GetAllocatedBytesForCurrentThread();
var startupStart = Stopwatch.GetTimestamp();
using var world = new World(options);
workload.Initialize(world);
var startupTicks = Stopwatch.GetTimestamp() - startupStart;
var startup = new StartupSample(startupTicks, GC.GetAllocatedBytesForCurrentThread() - beforeStartup);

var initial = CaptureMemory(world, workload);
var initialChecksum = workload.Checksum(world);
for (var frame = 0; frame < warmup; frame++)
{
    workload.Step(world, frame);
    if (target.HasValue) Trim(world);
}

var afterWarmup = CaptureMemory(world, workload);
var afterWarmupChecksum = workload.Checksum(world);
for (var i = 0; i < samples.Length; i++)
{
    var frame = checked(warmup + i);
    var managedBefore = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    var phase = workload.Step(world, frame);
    var workEnd = Stopwatch.GetTimestamp();
    var released = target.HasValue ? Trim(world) : 0;
    var end = Stopwatch.GetTimestamp();
    var managed = GC.GetAllocatedBytesForCurrentThread() - managedBefore;
    var checksum = workload.Checksum(world);
    samples[i] = new FrameSample(phase, end - start, target.HasValue ? end - workEnd : 0, managed, released, checksum,
        CaptureMemory(world, workload));
}

using var coreAssembly = File.OpenRead(typeof(World).Assembly.Location);
Console.WriteLine(JsonSerializer.Serialize(new
{
    Scenario = scenario,
    TargetRetainedBytes = target,
    BuildId = args[7],
    CoreAssemblySha256 = Convert.ToHexString(SHA256.HashData(coreAssembly)),
    Runtime = RuntimeInformation.FrameworkDescription,
    Os = RuntimeInformation.OSDescription,
    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
    LogicalProcessors = Environment.ProcessorCount,
    ServerGc = System.Runtime.GCSettings.IsServerGC,
    Stopwatch.Frequency,
    EntitiesPerGroup = entities,
    ChunkCapacity = chunkCapacity,
    Seed = seed,
    WarmupFrames = warmup,
    Startup = startup,
    Initial = initial,
    InitialChecksum = initialChecksum,
    AfterWarmup = afterWarmup,
    AfterWarmupChecksum = afterWarmupChecksum,
    P50Ticks = Percentile(samples.Select(s => s.FrameTicks), 0.50),
    P95Ticks = Percentile(samples.Select(s => s.FrameTicks), 0.95),
    P99Ticks = Percentile(samples.Select(s => s.FrameTicks), 0.99),
    TrimP99Ticks = Percentile(samples.Select(s => s.TrimTicks), 0.99),
    Phases = samples.GroupBy(s => s.Phase).Select(group => new
    {
        Phase = group.Key,
        P50Ticks = Percentile(group.Select(s => s.FrameTicks), 0.50),
        P99Ticks = Percentile(group.Select(s => s.FrameTicks), 0.99)
    }),
    Samples = samples
}));
return 0;

static int Usage()
{
    Console.Error.WriteLine("Usage: ChurnControlBenchmarks for-each|chunks|get|set|same|separated|simultaneous|sizes|quiet|transitions off|targetRetainedBytes frames warmupFrames entitiesPerGroup chunkCapacity seed buildId");
    return 2;
}

static bool TryPositive(string text, out int value) =>
    int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;

static long Percentile(IEnumerable<long> ticks, double fraction)
{
    var sorted = ticks.Order().ToArray();
    return sorted[(int)Math.Ceiling(sorted.Length * fraction) - 1];
}

static int Trim(World world)
{
#if CHURN_CONTROL
    return world.TrimChurnMemory();
#else
    return 0;
#endif
}

static MemorySample CaptureMemory(World world, Workload workload)
{
    var storage = world.GetStorageStatistics();
    var occupied = workload.OccupiedChunks();
#if CHURN_CONTROL
    var churn = world.GetChurnControlStatistics();
    return new MemorySample(storage.EntityCount, occupied, storage.ChunkCount, storage.NativeBytes,
        storage.AccessorBufferBytes, churn.LivePayloadBytes, churn.OccupiedChunkCapacityBytes, churn.ReusableEmptyBytes,
        churn.NonReclaimableBytes, churn.RetainedBytes, churn.AllocationCount, churn.ReuseCount, churn.ReleaseCount,
        churn.Enabled ? churn.OverBudgetBytes : null, churn.Enabled ? churn.UnreclaimableOverBudgetBytes : null);
#else
    // Without a controller there is exactly one physical allocation per retained chunk and no releases before Dispose.
    return new MemorySample(storage.EntityCount, occupied, storage.ChunkCount, storage.NativeBytes,
        storage.AccessorBufferBytes, null, null, null, null, null, storage.ChunkCount, 0, 0, null, null);
#endif
}

internal readonly record struct StartupSample(long Ticks, long ManagedBytes);

internal readonly record struct FrameSample(string Phase, long FrameTicks, long TrimTicks, long ManagedBytes,
    int ExplicitReleases, string Checksum, MemorySample Memory);

internal readonly record struct MemorySample(int Entities, int OccupiedChunks, int StorageChunks, long NativeBytes,
    long AccessorBufferBytes, long? LivePayloadBytes, long? OccupiedChunkCapacityBytes, long? ReusableEmptyBytes,
    long? NonReclaimableBytes, long? RetainedBytes, long PhysicalAllocations, long Reuses, long Releases,
    long? OverBudgetBytes, long? UnreclaimableOverBudgetBytes);
