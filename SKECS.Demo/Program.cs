using System.Globalization;
using System.Numerics;
using System.Runtime.Intrinsics;
using SKECS;
using SKECS.Numerics;

var requestedMode = args.Length > 0 ? args[0] : "all";
string[] modes = ["scalar", "simd-auto", "runner", "sliced"];
if (args.Length > 4 || (requestedMode != "all" && !modes.Contains(requestedMode)) ||
    !PositiveArgument(args, 1, 257, out var entities) ||
    !PositiveArgument(args, 2, 12, out var ticks) ||
    !PositiveArgument(args, 3, 63, out var chunkCapacity))
{
    Console.Error.WriteLine("Usage: SKECS.Demo [all|scalar|simd-auto|runner|sliced] [entities] [ticks] [chunk-capacity]");
    return 2;
}

Console.WriteLine($"entities={entities} ticks={ticks} chunkCapacity={chunkCapacity}");
Console.WriteLine($"hardwareVector={Vector.IsHardwareAccelerated} hardwareVector128={Vector128.IsHardwareAccelerated}");
foreach (var mode in modes)
    if (requestedMode == "all" || mode == requestedMode)
        Run(mode, entities, ticks, chunkCapacity);
return 0;

static bool PositiveArgument(string[] arguments, int index, int defaultValue, out int value)
{
    if (index < arguments.Length)
        return int.TryParse(arguments[index], NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    value = defaultValue;
    return true;
}

static void Run(string mode, int count, int ticks, int chunkCapacity)
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

    var useRunner = mode is "runner" or "sliced";
    using var runner = useRunner ? CreateRunner(world) : null;
    var query = world.Query<Position, Velocity>();
    var moveChunks = new MoveChunks(mode == "scalar" ? KernelMode.Scalar : KernelMode.Auto);
    var advances = 0L;
    var checksum = Verify(world, slots, expected, speeds, 0);

    for (long tick = 1; tick <= ticks; tick++)
    {
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
                advances++;
            } while (status == DeterministicTickStatus.Pending);

            if (status != DeterministicTickStatus.Completed)
                throw new InvalidOperationException($"Runner stopped at tick {tick} with status {status}.", runner.Fault);
        }

        for (var index = 0; index < count; index++)
            expected[index] = unchecked(expected[index] + speeds[index]);
        checksum = Verify(world, slots, expected, speeds, tick);
    }

    Console.WriteLine($"{mode}: verified={ticks + 1L} checksum={checksum:X16} advances={advances}");
}

static DeterministicRunner<MovementInput> CreateRunner(World world)
{
    var movement = new DeterministicQuery().With<Position>().With<Velocity>().ReadWrite<Position>().Read<Velocity>();
    var workload = new DeterministicWorkload<MovementInput>().AddSystem(SystemStage.Update, movement, new MoveRows());
    return new DeterministicRunner<MovementInput>(world, workload);
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
