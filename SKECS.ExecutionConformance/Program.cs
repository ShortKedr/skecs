using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text.Json;
using SKECS;
using SKECS.Numerics;

#if SKECS_EXECUTION_CONFORMANCE_GA
if (args.Length > 0 && args[0] == "ga")
    return GaExecutionConformance.Run(args[1..]);
#endif

var measure = args.Length > 0 && args[0] == "--measure";
if (measure) args = args[1..];
var requestedMode = args.Length > 0 ? args[0] : "all";
string[] modes = ["scalar", "simd-auto", "runner", "sliced", "parallel"];
if (args.Length > 4 || (requestedMode != "all" && !modes.Contains(requestedMode)) ||
    !PositiveArgument(args, 1, 257, out var entities) ||
    !PositiveArgument(args, 2, 12, out var ticks) ||
    !PositiveArgument(args, 3, 63, out var chunkCapacity))
{
    Console.Error.WriteLine("Usage: SKECS.ExecutionConformance [--measure] [all|scalar|simd-auto|runner|sliced|parallel] [entities] [ticks] [chunk-capacity]");
    return 2;
}

if (!measure)
{
    Console.WriteLine($"entities={entities} ticks={ticks} chunkCapacity={chunkCapacity}");
    Console.WriteLine($"hardwareVector={Vector.IsHardwareAccelerated} hardwareVector128={Vector128.IsHardwareAccelerated}");
}
foreach (var mode in modes)
    if (requestedMode == "all" || mode == requestedMode)
        Run(mode, entities, ticks, chunkCapacity, measure);
return 0;

static bool PositiveArgument(string[] arguments, int index, int defaultValue, out int value)
{
    if (index < arguments.Length)
        return int.TryParse(arguments[index], NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    value = defaultValue;
    return true;
}

static void Run(string mode, int count, int ticks, int chunkCapacity, bool measure)
{
    using var world = new World(new WorldOptions(chunkCapacity));
    var slots = new Entity[count];
    var expected = new int[count];
    var speeds = new int[count];
    for (var index = 0; index < count; index++)
    {
        expected[index] = unchecked((index * 31) ^ int.MaxValue);
        speeds[index] = index % 17 - 8;
        slots[index] = world.CreateEntity(new Position(expected[index]), new Velocity(speeds[index]));
    }

    var useRunner = mode is "runner" or "sliced" or "parallel";
    var workers = mode == "parallel" ? 4 : 1;
    using var runner = useRunner ? CreateRunner(world, workers) : null;
    var query = world.Query<Position, Velocity>();
    var moveChunks = new MoveChunks(mode == "scalar" ? KernelMode.Scalar : KernelMode.Auto);
    var warmup = measure ? Math.Clamp(ticks / 4, 16, 128) : 0;
    var samples = measure ? new TickSample[ticks] : [];
    var advances = 0L;
    var checksum = Verify(world, slots, expected, speeds, 0);

    for (long tick = 1; tick <= ticks + (long)warmup; tick++)
    {
        var sample = measure && tick > warmup;
        var allocatedBefore = sample ? GC.GetTotalAllocatedBytes(precise: true) : 0;
        var timestamp = sample ? Stopwatch.GetTimestamp() : 0;
        if (runner is null)
        {
            query.ForEachChunk(ref moveChunks);
        }
        else
        {
            runner.BeginTick(tick, default, Fixed.One);
            DeterministicTickStatus status;
            do
            {
                status = runner.Advance(mode == "sliced" ? 1 : int.MaxValue);
                if (tick > warmup) advances++;
            } while (status == DeterministicTickStatus.Pending);

            if (status != DeterministicTickStatus.Completed)
                throw new InvalidOperationException($"Runner stopped at tick {tick} with status {status}.", runner.Fault);
        }

        if (sample)
        {
            var elapsed = Stopwatch.GetElapsedTime(timestamp);
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            samples[(int)(tick - warmup - 1)] = new TickSample(elapsed.TotalMicroseconds, allocated);
        }

        for (var index = 0; index < count; index++)
            expected[index] = unchecked(expected[index] + speeds[index]);
        checksum = Verify(world, slots, expected, speeds, tick);
    }

    if (!measure)
    {
        Console.WriteLine($"{mode}: verified={ticks + 1L} checksum={checksum:X16} advances={advances} workerLimit={workers}");
        return;
    }

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        mode,
        entities = count,
        chunkCapacity,
        warmup,
        workerLimit = workers,
        advances,
        verifiedStates = ticks + (long)warmup + 1,
        checksum = checksum.ToString("X16", CultureInfo.InvariantCulture),
        runtime = RuntimeInformation.FrameworkDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        operatingSystem = RuntimeInformation.OSDescription,
        hardwareVector128 = Vector128.IsHardwareAccelerated,
        hardwareVectorWidth = Vector.IsHardwareAccelerated ? Vector<int>.Count : 0,
        samples,
    }));
}

static DeterministicRunner<MovementInput> CreateRunner(World world, int workers)
{
    var movement = new DeterministicQuery().With<Position>().With<Velocity>().ReadWrite<Position>().Read<Velocity>();
    var workload = new DeterministicWorkload<MovementInput>().AddSystem(SystemStage.Update, movement, new MoveRows());
    return new DeterministicRunner<MovementInput>(world, workload, workers);
}

static ulong Verify(World world, Entity[] slots, int[] expected, int[] speeds, long tick)
{
    var checksum = 14695981039346656037UL;
    for (var index = 0; index < slots.Length; index++)
    {
        var position = world.Get<Position>(slots[index]).Value;
        var speed = world.Get<Velocity>(slots[index]).Value;
        if (position != expected[index] || speed != speeds[index])
            throw new InvalidOperationException($"Movement mismatch at tick {tick}, entity {slots[index].Id}.");
        checksum = unchecked((checksum ^ (uint)position) * 1099511628211UL);
        checksum = unchecked((checksum ^ (uint)speed) * 1099511628211UL);
    }
    return checksum;
}

readonly record struct Position(int Value);
readonly record struct Velocity(int Value);
readonly record struct MovementInput;
readonly record struct TickSample(double Microseconds, long ManagedBytes);

readonly struct MoveChunks(KernelMode mode) : IQueryChunkProcessor<Position, Velocity>
{
    public unsafe void Execute(QueryChunkView<Position, Velocity> chunk)
    {
        var positions = new Span<int>(chunk.Component1, chunk.Count);
        var velocities = new ReadOnlySpan<int>(chunk.Component2, chunk.Count);
        NumericKernels.AddInt32(positions, velocities, positions, mode);
    }
}

readonly struct MoveRows : IDeterministicJobProcessor<MovementInput>
{
    public void Execute(DeterministicJob<MovementInput> job)
    {
        for (var row = 0; row < job.Count; row++)
            job.Write(row, new Position(unchecked(job.Read<Position>(row).Value + job.Read<Velocity>(row).Value)));
    }
}
