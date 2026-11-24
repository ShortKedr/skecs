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
        {
            throw new ArgumentException("The serialization sample takes no arguments.");
        }

        const uint hostStateId = 1;
        const uint hostStateVersion = 1;
        const int checkpointTick = 12;
        const int continuationTicks = 12;

        var schema = WorldSchema.Create();
        using var original = new World(new WorldOptions(chunkCapacity: 32));
        var originalHost = new HostState(0, 0x5CF631B859UL);
        for (var tick = 0; tick < checkpointTick; tick++)
        {
            AdvanceTick(original, ref originalHost);
        }

        // Store application-owned state alongside the generated Motion component schema.
        using var checkpoint = new MemoryStream();
        var hostExtension = new CheckpointExtension(
            hostStateId, hostStateVersion, required: true, data: originalHost.Encode());
        var writeOptions = new CheckpointWriteOptions
        {
            Extensions = [hostExtension],
        };
        WorldSerializer.WriteBinary(original, checkpoint, schema, writeOptions);
        checkpoint.Position = 0;

        // Restore into a new world with a different chunk capacity.
        var readOptions = new CheckpointReadOptions
        {
            WorldOptions = new WorldOptions(chunkCapacity: 7),
            Extensions = new Dictionary<uint, uint> { [hostStateId] = hostStateVersion },
            RequiredExtensions = [hostStateId],
        };
        using var restored = WorldSerializer.ReadBinary(checkpoint, schema, out var extensions, readOptions);
        var restoredHost = HostState.Decode(extensions.Single().Data);
        VerifyMatchingState();
        Console.WriteLine($"Restored tick {restoredHost.Tick} from a {checkpoint.Length}-byte checkpoint.");

        // Advance the original and restored worlds with the same simulation code.
        for (var tick = 0; tick < continuationTicks; tick++)
        {
            var expected = AdvanceTick(original, ref originalHost);
            var actual = AdvanceTick(restored, ref restoredHost);
            if ((expected.Id, expected.Version) != (actual.Id, actual.Version))
            {
                throw new InvalidOperationException($"Entity allocation diverged at tick {originalHost.Tick}.");
            }

            VerifyMatchingState();
        }

        Console.WriteLine($"Verified {continuationTicks} continuation ticks through tick {restoredHost.Tick}.");
        return Task.CompletedTask;

        void VerifyMatchingState()
        {
            var originalState = CanonicalState(original, schema);
            var restoredState = CanonicalState(restored, schema);
            if (originalHost != restoredHost || !originalState.AsSpan().SequenceEqual(restoredState))
            {
                throw new InvalidOperationException($"Checkpoint state diverged at tick {originalHost.Tick}.");
            }
        }
    }

    private static Entity AdvanceTick(World world, ref HostState host)
    {
        var integrate = new Integrate();
        world.Query<Motion>().ForEach(ref integrate);

        // Step the RNG before deriving the new entity's initial motion.
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
        public void Execute(Entity entity, ref Motion motion)
        {
            motion.Position += motion.Velocity;
        }
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
            {
                throw new InvalidDataException("Expected a host tick and RNG state.");
            }

            var tick = BinaryPrimitives.ReadInt64LittleEndian(bytes);
            var rng = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
            if (tick < 0 || rng == 0)
            {
                throw new InvalidDataException("The checkpoint contains an invalid host tick or RNG state.");
            }

            return new HostState(tick, rng);
        }
    }
}

[WorldComponent(1001)]
internal struct Motion(int position, int velocity)
{
    [WorldField(1)]
    public int Position = position;

    [WorldField(2)]
    public int Velocity = velocity;
}
