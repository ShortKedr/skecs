using System.Buffers.Binary;
using SKECS;
using SKECS.Serialization;
using SKECS.Serialization.Generated;

if (args.Length == 0)
{
    RunDemo();
    return 0;
}

if (args is ["benchmark", var scenario, var format, ..] &&
    scenario is "tiny" or "dense" or "churn" &&
    format is "binary" or "json" && args.Length <= 5 &&
    (args.Length < 4 || Positive(args[3])) &&
    (args.Length < 5 || Positive(args[4])))
{
    var iterations = args.Length >= 4 ? int.Parse(args[3]) : 5;
    var entities = args.Length >= 5 ? int.Parse(args[4]) : scenario == "tiny" ? 32 : 100_000;
    CheckpointBenchmark.Run(scenario, format, iterations, entities);
    return 0;
}

Console.Error.WriteLine("Usage: SKECS.Serialization.Sample [benchmark <tiny|dense|churn> <binary|json> [iterations] [entities]]");
return 2;

static bool Positive(string value) => int.TryParse(value, out var parsed) && parsed > 0;

static void RunDemo()
{
    const uint hostStateId = 1;
    var schema = WorldSchema.Create();
    using var source = new World(new WorldOptions(32));
    var original = new HostState(0, 0x5CF631B859UL);
    for (var i = 0; i < 12; i++)
        Advance(source, original);

    using var save = new MemoryStream();
    WorldSerializer.WriteBinary(source, save, schema, new CheckpointWriteOptions
    {
        Extensions = [new CheckpointExtension(hostStateId, 1, true, original.Encode())],
    });
    save.Position = 0;

    using var restored = WorldSerializer.ReadBinary(save, schema, out var extensions, new CheckpointReadOptions
    {
        WorldOptions = new WorldOptions(7),
        Extensions = new Dictionary<uint, uint> { [hostStateId] = 1 },
        RequiredExtensions = [hostStateId],
    });
    var restoredHost = HostState.Decode(extensions.Single().Data.ToArray());
    for (var i = 0; i < 12; i++)
    {
        var expected = Advance(source, original);
        var actual = Advance(restored, restoredHost);
        if ((expected.Id, expected.Version) != (actual.Id, actual.Version) ||
            !original.Encode().AsSpan().SequenceEqual(restoredHost.Encode()) ||
            !Canonical(source, schema).AsSpan().SequenceEqual(Canonical(restored, schema)))
            throw new InvalidOperationException($"Checkpoint continuation diverged at tick {original.Tick}.");
    }
    Console.WriteLine($"Verified continuation through tick {restoredHost.Tick} after restoring a {save.Length}-byte checkpoint.");
}

static Entity Advance(World world, HostState state)
{
    var integrator = new Integrate();
    world.Query<Motion>().ForEach(ref integrator);
    var value = state.Advance();
    return world.CreateEntity(new Motion((int)(value % 1000), 1 + (int)(value % 17)));
}

static byte[] Canonical(World world, CodecRegistry schema)
{
    using var buffer = new MemoryStream();
    WorldSerializer.WriteJson(world, buffer, schema);
    return buffer.ToArray();
}

[WorldComponent(1001)]
internal record struct Motion
{
    public Motion(int position, int velocity)
    {
        Position = position;
        Velocity = velocity;
    }

    [WorldField(1)] public int Position;
    [WorldField(2)] public int Velocity;
}

[WorldComponent(1002)]
internal record struct Label
{
    public Label(int group) => Group = group;

    [WorldField(1)] public int Group;
}

internal struct Integrate : IQueryProcessor<Motion>
{
    public void Execute(Entity entity, ref Motion value)
        => value.Position += value.Velocity;
}

internal sealed class HostState(long tick, ulong rng)
{
    public long Tick { get; private set; } = tick;
    private ulong Rng { get; set; } = rng;

    public ulong Advance()
    {
        Tick++;
        var value = Rng;
        value ^= value << 13;
        value ^= value >> 7;
        value ^= value << 17;
        return Rng = value;
    }

    public byte[] Encode()
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, Tick);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), Rng);
        return bytes;
    }

    public static HostState Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16)
            throw new InvalidDataException("Host state must contain a tick and RNG state.");
        var tick = BinaryPrimitives.ReadInt64LittleEndian(bytes);
        var rng = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        if (tick < 0 || rng == 0)
            throw new InvalidDataException("Host state has an invalid tick or RNG state.");
        return new HostState(tick, rng);
    }
}
