using System.Buffers.Binary;
using SKECS;
using SKECS.Serialization;
using SKECS.Serialization.Generated;

namespace Skecs.Samples.Serialization;

internal static class Sample
{
    public static Task RunAsync(string[] args)
    {
        if (args.Length != 0)
            throw new ArgumentException("The serialization sample takes no arguments.");

        const uint hostStateId = 1;
        const uint hostStateVersion = 1;
        const int checkpointTick = 12;
        const int continuationTicks = 12;
        var schema = WorldSchema.Create();
        using var original = new World(new WorldOptions(chunkCapacity: 32));
        var originalHost = new HostState(0, 0x5CF631B859UL);
        for (var i = 0; i < checkpointTick; i++)
            Advance(original, ref originalHost);

        // Store application-owned state alongside the generated Motion component schema.
        using var checkpoint = new MemoryStream();
        WorldSerializer.WriteBinary(original, checkpoint, schema, new CheckpointWriteOptions
        {
            Extensions = [new CheckpointExtension(hostStateId, hostStateVersion, true, originalHost.Encode())],
        });
        checkpoint.Position = 0;

        using var restored = WorldSerializer.ReadBinary(checkpoint, schema, out var extensions,
            new CheckpointReadOptions
            {
                WorldOptions = new WorldOptions(chunkCapacity: 7),
                Extensions = new Dictionary<uint, uint> { [hostStateId] = hostStateVersion },
                RequiredExtensions = [hostStateId],
            });
        var restoredHost = HostState.Decode(extensions.Single().Data);
        CheckState();
        Console.WriteLine($"Restored tick {restoredHost.Tick} from a {checkpoint.Length}-byte checkpoint.");

        for (var i = 0; i < continuationTicks; i++)
        {
            var expected = Advance(original, ref originalHost);
            var actual = Advance(restored, ref restoredHost);
            if ((expected.Id, expected.Version) != (actual.Id, actual.Version))
                throw new InvalidOperationException($"Entity allocation diverged at tick {originalHost.Tick}.");
            CheckState();
        }

        Console.WriteLine($"Verified {continuationTicks} continuation ticks through tick {restoredHost.Tick}.");
        return Task.CompletedTask;

        void CheckState()
        {
            if (originalHost != restoredHost ||
                !CanonicalState(original, schema).AsSpan().SequenceEqual(CanonicalState(restored, schema)))
                throw new InvalidOperationException($"Checkpoint state diverged at tick {originalHost.Tick}.");
        }
    }

    private static Entity Advance(World world, ref HostState host)
    {
        var integrate = new Integrate();
        world.Query<Motion>().ForEach(ref integrate);
        var random = host.Rng;
        random ^= random << 13;
        random ^= random >> 7;
        random ^= random << 17;
        host = new HostState(host.Tick + 1, random);
        return world.CreateEntity(new Motion((int)(random % 1000), 1 + (int)(random % 17)));
    }

    private static byte[] CanonicalState(World world, CodecRegistry schema)
    {
        using var buffer = new MemoryStream();
        WorldSerializer.WriteJson(world, buffer, schema);
        return buffer.ToArray();
    }

    private struct Integrate : IQueryProcessor<Motion>
    {
        public void Execute(Entity entity, ref Motion motion) => motion.Position += motion.Velocity;
    }

    private readonly record struct HostState(long Tick, ulong Rng)
    {
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
                throw new InvalidDataException("Expected a host tick and RNG state.");
            var tick = BinaryPrimitives.ReadInt64LittleEndian(bytes);
            var rng = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
            if (tick < 0 || rng == 0)
                throw new InvalidDataException("The checkpoint contains an invalid host tick or RNG state.");
            return new HostState(tick, rng);
        }
    }
}

[WorldComponent(1001)]
internal struct Motion(int position, int velocity)
{
    [WorldField(1)] public int Position = position;
    [WorldField(2)] public int Velocity = velocity;
}
