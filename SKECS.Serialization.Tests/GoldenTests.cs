using System.Security.Cryptography;
using System.Text;

namespace SKECS.Serialization.Tests;

public sealed partial class CheckpointTests
{
    [Fact]
    public void V1BinaryFixtureMatchesIndependentReader()
    {
        using var input = new MemoryStream(Fixture(json: false));
        using var reader = new BinaryReader(input);
        Assert.Equal("SKECSCP\0", Encoding.ASCII.GetString(reader.ReadBytes(8)));

        var header = ReadFrame(reader, 1);
        Assert.Equal(1u, header.ReadUInt32());
        Assert.Equal(0u, header.ReadUInt32());
        Assert.Equal(0u, header.ReadUInt32());
        Assert.Equal(1u, header.ReadUInt32());
        Assert.Equal(32u, header.ReadUInt32());
        var fingerprint = header.ReadBytes(32);
        Assert.Equal(new uint[] { 4, 2, 1, 0 }, Enumerable.Range(0, 4).Select(_ => header.ReadUInt32()));
        AssertConsumed(header);

        var schema = ReadFrame(reader, 2);
        Assert.Equal(SHA256.HashData(((MemoryStream)schema.BaseStream).ToArray()), fingerprint);
        Assert.Equal(new uint[] { 100, 1, 1, 1, 6 }, Enumerable.Range(0, 5).Select(_ => schema.ReadUInt32()));
        Assert.False(schema.ReadBoolean());
        Assert.False(schema.ReadBoolean());
        AssertConsumed(schema);

        var slots = ReadFrame(reader, 3);
        Assert.Equal(4u, slots.ReadUInt32());
        Assert.Equal(new[] { (1, true), (2, false), (1, true), (2, false) },
            Enumerable.Range(0, 4).Select(_ => (slots.ReadInt32(), slots.ReadBoolean())));
        AssertConsumed(slots);

        var free = ReadFrame(reader, 4);
        Assert.Equal(2u, free.ReadUInt32());
        Assert.Equal(3, free.ReadInt32());
        Assert.Equal(1, free.ReadInt32());
        AssertConsumed(free);

        var component = ReadFrame(reader, 5);
        Assert.Equal(100u, component.ReadUInt32());
        Assert.Equal(1u, component.ReadUInt32());
        Assert.Equal(0, component.ReadInt32());
        Assert.Equal(1, component.ReadInt32());
        Assert.Equal(1u, component.ReadUInt32());
        Assert.Equal(1u, component.ReadUInt32());
        Assert.Equal(6u, component.ReadUInt32());
        Assert.Equal(4u, component.ReadUInt32());
        Assert.Equal(42, component.ReadInt32());
        AssertConsumed(component);
        AssertConsumed(ReadFrame(reader, 0));
        Assert.Equal(input.Length, input.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void V1GoldenFixtureLoadsAndReencodesAcrossFormats(bool json)
    {
        var schema = IntRegistry();
        var fixture = Fixture(json);
        using var restored = Load(fixture, schema, json);
        var first = restored.BindEntity(0, 1);
        var empty = restored.BindEntity(2, 1);
        Assert.Equal(42, restored.Get<int>(first));
        Assert.True(restored.IsAlive(empty));
        Assert.False(restored.Has<int>(empty));
        Assert.Equal(fixture, Save(restored, schema, json));
        Assert.Equal(Fixture(!json), Save(restored, schema, !json));
        Assert.Equal(restored.BindEntity(3, 2), restored.CreateEntity());
        Assert.Equal(restored.BindEntity(1, 2), restored.CreateEntity());
        Assert.Equal(restored.BindEntity(4, 1), restored.CreateEntity());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsUnknownMajorAndCorruptFreeList(bool json)
    {
        var bytes = Fixture(json);
        byte[] unknownMajor;
        byte[] duplicateFree;
        if (json)
        {
            var checkpoint = Encoding.UTF8.GetString(bytes);
            unknownMajor = Encoding.UTF8.GetBytes(checkpoint.Replace("\"header\",1", "\"header\",2", StringComparison.Ordinal));
            duplicateFree = Encoding.UTF8.GetBytes(checkpoint.Replace("\"free\",2,3,1", "\"free\",2,3,3", StringComparison.Ordinal));
        }
        else
        {
            unknownMajor = (byte[])bytes.Clone();
            unknownMajor[13] = 2;
            duplicateFree = (byte[])bytes.Clone();
            const int freeIdsOffset = 8 + (5 + 68) + (5 + 22) + (5 + 24) + 5 + 4;
            duplicateFree[freeIdsOffset + 4] = 3;
        }

        Assert.Throws<InvalidDataException>(() => Load(unknownMajor, IntRegistry(), json));
        Assert.Throws<InvalidDataException>(() => Load(duplicateFree, IntRegistry(), json));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsEveryTruncatedGoldenPrefix(bool json)
    {
        var bytes = Fixture(json);
        var registry = IntRegistry();
        for (var length = 0; length < bytes.Length; length++)
            Assert.Throws<InvalidDataException>(() => Load(bytes[..length], registry, json));
    }

    [Theory]
    [InlineData(false, 0x1201)]
    [InlineData(true, 0x1767)]
    public void MutatedFixtureEitherFailsCleanlyOrCanBeSavedAgain(bool json, int seed)
    {
        var source = Fixture(json);
        var registry = IntRegistry();
        var random = new Random(seed);
        var options = new CheckpointReadOptions
        {
            Limits = new CheckpointLimits
            {
                MaxSlots = 16,
                MaxSchemas = 4,
                MaxFields = 4,
                MaxRowsPerBlock = 16,
                MaxFrameBytes = 256,
                MaxComponentValues = 16,
                MaxDocumentBytes = 512,
                MaxWorkingBytes = 102_400,
            },
        };
        for (var i = 0; i < 256; i++)
        {
            var mutated = (byte[])source.Clone();
            mutated[random.Next(mutated.Length)] ^= (byte)(1 << random.Next(8));
            World world;
            try
            {
                world = Load(mutated, registry, json, options);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            using (world)
            {
                var canonical = Save(world, registry, json);
                using var repeated = Load(canonical, registry, json, options);
                Assert.Equal(canonical, Save(repeated, registry, json));
            }
        }
    }

    private static CodecRegistry IntRegistry()
        => new CodecRegistry().Register(new ComponentSchema(100, 1, new FieldSchema(1, FieldKind.Int32)), new IntCodec());

    private static byte[] Fixture(bool json)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", json ? "checkpoint-v1.json" : "checkpoint-v1.bin"));

    private static BinaryReader ReadFrame(BinaryReader input, byte kind)
    {
        Assert.Equal(kind, input.ReadByte());
        var length = input.ReadInt32();
        var payload = input.ReadBytes(length);
        Assert.Equal(length, payload.Length);
        return new BinaryReader(new MemoryStream(payload, writable: false));
    }

    private static void AssertConsumed(BinaryReader reader)
    {
        using (reader)
            Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
    }
}
