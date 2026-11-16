using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using SKECS;
using SKECS.Profiler;

if (args.Length != 7 || args[0] is not ("dense" or "churn") || args[1] is not ("off" or "on") ||
    !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var frames) || frames < 1 ||
    !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out var entities) || entities < 1 ||
    !int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out var capacity) || capacity < 1 ||
    string.IsNullOrWhiteSpace(args[5]))
{
    Console.Error.WriteLine("Usage: ProfilerBenchmarks dense|churn off|on frames entities eventCapacity buildId outputPrefix");
    return 2;
}

var dense = args[0] == "dense";
var recording = args[1] == "on";
var buildId = args[5];
var outputPrefix = Path.GetFullPath(args[6]);

// Warm both branches in every process so the measured run starts with the same JIT work.
Run(dense, 24, 256, null, out _);
Run(dense, 24, 256, new ProfilerSession(32768), out _);

var session = recording ? new ProfilerSession(capacity) : null;
var samples = Run(dense, frames, entities, session, out var startup);
var ticks = samples.Select(sample => sample.ElapsedTicks).Order().ToArray();
var result = new
{
    Scenario = args[0],
    Recording = recording,
    BuildId = buildId,
    Runtime = RuntimeInformation.FrameworkDescription,
    OS = RuntimeInformation.OSDescription,
    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    StopwatchFrequency = Stopwatch.Frequency,
    Entities = entities,
    ChunkCapacity = 128,
    EventCapacity = capacity,
    RecordedEvents = session?.Buffer.Count ?? 0,
    DroppedEvents = session?.Buffer.DroppedEvents ?? 0,
    Startup = startup,
    P50Ticks = Percentile(ticks, 0.50),
    P95Ticks = Percentile(ticks, 0.95),
    P99Ticks = Percentile(ticks, 0.99),
    Samples = samples
};
Directory.CreateDirectory(Path.GetDirectoryName(outputPrefix)!);
if (session != null)
{
    using var trace = File.Create(outputPrefix + ".xml");
    session.Export(trace, args[0], buildId);
}
File.WriteAllText(outputPrefix + ".json", JsonSerializer.Serialize(result));
Console.WriteLine(FormattableString.Invariant(
    $"{args[0]} {args[1]} p50={result.P50Ticks} p95={result.P95Ticks} p99={result.P99Ticks} ticks, events={result.RecordedEvents}, dropped={result.DroppedEvents}"));
return result.DroppedEvents == 0 ? 0 : 1;

static long Percentile(long[] sorted, double fraction) => sorted[(int)Math.Ceiling(sorted.Length * fraction) - 1];

static Sample[] Run(bool dense, int frames, int count, ProfilerSession? session, out Measurement startup)
{
    var handles = new Entity[count];
    var samples = new Sample[frames];
    var options = new WorldOptions(chunkCapacity: 128);
    var managedBeforeStartup = GC.GetAllocatedBytesForCurrentThread();
    var startupStart = Stopwatch.GetTimestamp();
    using var world = session == null ? new World(options) : new World(options, session.Buffer);
    var query = world.Query<Position, Velocity>();
    var processor = new Advance();
    world.GetAccessor<Position>();
    if (dense) Create(world, handles);
    var startupElapsed = Stopwatch.GetTimestamp() - startupStart;
    startup = new Measurement(startupElapsed, GC.GetAllocatedBytesForCurrentThread() - managedBeforeStartup);
    session?.RecordDuration("startup", startupElapsed);

    for (var frame = 0; frame < frames; frame++)
    {
        session?.BeginFrame();
        var managedBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        if (dense)
        {
            query.ForEach(ref processor);
        }
        else
        {
            switch (frame % 6)
            {
                case 0:
                    Create(world, handles);
                    break;
                case 1:
                    foreach (var entity in handles) world.Add(entity, new Payload { Marker = entity.Id });
                    break;
                case 2:
                    foreach (var entity in handles) world.Remove<Payload>(entity);
                    break;
                case 3:
                    Destroy(world, handles);
                    break;
                case 4:
                    for (var i = 0; i < handles.Length; i++)
                        handles[i] = world.CreateEntity(new Position { Value = i }, new Velocity { Step = 1 },
                            new Payload { Marker = i });
                    break;
                case 5:
                    Destroy(world, handles);
                    break;
            }
        }
        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - managedBefore;
        session?.RecordDuration(dense ? "position-query" : "structural-work", elapsed);
        session?.EndFrame();
        samples[frame] = new Sample(elapsed, allocated, Checksum(world, handles));
    }
    return samples;
}

static void Create(World world, Entity[] handles)
{
    var factory = world.PrepareEntity<Position, Velocity>();
    for (var i = 0; i < handles.Length; i++)
        handles[i] = factory.Create(new Position { Value = i }, new Velocity { Step = 1 });
}

static void Destroy(World world, Entity[] handles)
{
    foreach (var entity in handles) world.DestroyEntity(entity);
}

static long Checksum(World world, Entity[] handles)
{
    long hash = 17;
    foreach (var entity in handles)
    {
        hash = unchecked(hash * 31 + entity.Id);
        hash = unchecked(hash * 31 + entity.Version);
        if (!world.IsAlive(entity)) continue;
        hash = unchecked(hash * 31 + world.Get<Position>(entity).Value);
        hash = unchecked(hash * 31 + world.Get<Velocity>(entity).Step);
        var hasPayload = world.TryGet<Payload>(entity, out var payload);
        hash = unchecked(hash * 31 + (hasPayload ? payload.Marker + 1 : 0));
    }
    return hash;
}

internal readonly record struct Sample(long ElapsedTicks, long ManagedBytes, long Checksum);
internal readonly record struct Measurement(long ElapsedTicks, long ManagedBytes);

internal struct Position { public int Value; }
internal struct Velocity { public int Step; }

[StructLayout(LayoutKind.Sequential, Size = 128)]
internal struct Payload { public int Marker; }

internal struct Advance : IQueryProcessor<Position, Velocity>
{
    public void Execute(Entity entity, ref Position position, ref Velocity velocity) => position.Value += velocity.Step;
}
