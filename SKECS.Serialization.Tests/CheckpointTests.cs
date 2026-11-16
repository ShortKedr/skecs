using System.Buffers.Binary;

namespace SKECS.Serialization.Tests;

public sealed partial class CheckpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyWorldAndEmptyEntitiesSurvive(bool json)
    {
        using var source = new World();
        var schema = new CodecRegistry();
        using (var empty = Load(Save(source, schema, json), schema, json))
            AssertHandle(source.CreateEntity(), empty.CreateEntity());

        var live = source.CreateEntity();
        var dead = source.CreateEntity();
        source.DestroyEntity(dead);
        using var restored = Load(Save(source, schema, json), schema, json);
        Assert.True(restored.IsAlive(restored.BindEntity(live.Id, live.Version)));
        Assert.False(restored.IsAlive(restored.BindEntity(dead.Id, dead.Version)));
        AssertHandle(source.CreateEntity(), restored.CreateEntity());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTripCrossesFormatsAndRebindsReferences(bool json)
    {
        using var source = new World(new WorldOptions(3));
        var target = source.CreateEntity();
        var destroyed = source.CreateEntity();
        var hidden = source.CreateEntity();
        source.DestroyEntity(hidden);
        source.DestroyEntity(destroyed);
        var entity = source.CreateEntity(new Links(target, destroyed, default));
        source.Add(entity, -18);
        var bytes = Save(source, CreateRegistry(), json);
        using var restored = Load(bytes, CreateRegistry(reverse: true), json, new CheckpointReadOptions
        {
            WorldOptions = new WorldOptions(19),
        });
        Assert.Equal(19, restored.ChunkCapacity);
        Assert.False(restored.IsAlive(target));
        Assert.False(restored.IsAlive(entity));
        var links = restored.Get<Links>(restored.BindEntity(entity.Id, entity.Version));
        Assert.Equal(restored.BindEntity(target.Id, target.Version), links.Live);
        Assert.True(restored.IsAlive(links.Live));
        Assert.Equal(restored.BindEntity(destroyed.Id, destroyed.Version), links.Stale);
        Assert.False(restored.IsAlive(links.Stale));
        Assert.Equal(default(Entity), links.Empty);
        Assert.Equal(-18, restored.Get<int>(restored.BindEntity(entity.Id, entity.Version)));

        Assert.Equal(bytes, Save(restored, CreateRegistry(reverse: true), json));
        var otherFormat = Save(restored, CreateRegistry(), !json);
        using var again = Load(otherFormat, CreateRegistry(reverse: true), !json);
        Assert.Equal(bytes, Save(again, CreateRegistry(), json));
        AssertHandle(source.CreateEntity(), restored.CreateEntity());
        AssertHandle(source.CreateEntity(), restored.CreateEntity());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnboundReferencesRoundTripAndForeignReferencesAreRejected(bool json)
    {
        using var source = new World();
        using var foreign = new World();
        var unbound = new Entity(-1024, -57);
        var entity = source.CreateEntity(new Links(unbound, default, default));
        using var restored = Load(Save(source, CreateRegistry(), json), CreateRegistry(), json);
        Assert.Equal(unbound, restored.Get<Links>(restored.BindEntity(entity.Id, entity.Version)).Live);
        source.Set(entity, new Links(foreign.CreateEntity(), default, default));
        Assert.Throws<InvalidDataException>(() => Save(source, CreateRegistry(), json));
        source.Set(entity, new Links(source.BindEntity(entity.Id, entity.Version + 1), default, default));
        Assert.Throws<InvalidDataException>(() => Save(source, CreateRegistry(), json));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllocatorKeepsSaturatedSlotsRetiredAndReusesLastGenerationOnce(bool json)
    {
        using var source = World.RestoreCheckpoint(
            [new CheckpointSlot(int.MaxValue, false), new CheckpointSlot(int.MaxValue, false)], [1]);
        var schema = new CodecRegistry();
        using var restored = Load(Save(source, schema, json), schema, json);
        var lastGeneration = restored.CreateEntity();
        Assert.Equal(restored.BindEntity(1, int.MaxValue), lastGeneration);
        restored.DestroyEntity(lastGeneration);
        Assert.Equal(restored.BindEntity(2, 1), restored.CreateEntity());
        Assert.False(restored.IsAlive(restored.BindEntity(0, int.MaxValue)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualCodecSupportsFixedLengthNestedValues(bool json)
    {
        using var source = new World();
        var expected = new Range(new Marker(-4, 8), new Marker(111111, -2));
        var e = source.CreateEntity(expected);
        var schema = new CodecRegistry().Register(new ComponentSchema(405, 1,
            new FieldSchema(1, FieldKind.Bytes)), new RangeCodec());
        using var restored = Load(Save(source, schema, json), schema, json);
        Assert.Equal(expected, restored.Get<Range>(restored.BindEntity(e.Id, e.Version)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarEncodingsPreserveBitPatterns(bool json)
    {
        int[] singleBits = [0, int.MinValue, 0x3f802020, 1, 0x7f800000, unchecked((int)0xff800000), 0x7fa00101];
        long[] doubleBits = [0, long.MinValue, 0x3ff0000000000011L, 1, 0x7ff0000000000000L,
            unchecked((long)0xfff0000000000000UL), 0x7ff8000000000059L];
        using var source = new World();
        var entities = new Entity[singleBits.Length];
        for (var i = 0; i < entities.Length; i++)
            entities[i] = source.CreateEntity(new Scalars(BitConverter.Int32BitsToSingle(singleBits[i]),
                BitConverter.Int64BitsToDouble(doubleBits[i]), long.MinValue + i, ulong.MaxValue - (uint)i,
                i % 2 == 0, -128, 255, -32768, 65535, uint.MaxValue));

        using var restored = Load(Save(source, CreateRegistry(), json), CreateRegistry(), json);
        for (var i = 0; i < entities.Length; i++)
        {
            var value = restored.Get<Scalars>(restored.BindEntity(entities[i].Id, entities[i].Version));
            Assert.Equal(singleBits[i], BitConverter.SingleToInt32Bits(value.Single));
            Assert.Equal(doubleBits[i], BitConverter.DoubleToInt64Bits(value.Double));
            Assert.Equal(long.MinValue + i, value.I64);
            Assert.Equal(ulong.MaxValue - (uint)i, value.U64);
            Assert.Equal(i % 2 == 0, value.Boolean);
            Assert.Equal(sbyte.MinValue, value.I8);
            Assert.Equal(byte.MaxValue, value.U8);
            Assert.Equal(short.MinValue, value.I16);
            Assert.Equal(ushort.MaxValue, value.U16);
            Assert.Equal(uint.MaxValue, value.U32);
        }
    }

    [Theory]
    [InlineData(false, 0x10783)]
    [InlineData(false, 0x75319)]
    [InlineData(true, 0x75537)]
    [InlineData(true, 0x59a12)]
    public void SeededChurnContinuesWithSameValuesAndHandles(bool json, int seed)
    {
        using var source = new World(new WorldOptions(5));
        var random = new Random(seed);
        var entities = new List<Entity>();
        for (var i = 0; i < 600; i++)
            Churn(source, entities, random.Next(), random.Next());

        using var restored = Load(Save(source, CreateRegistry(), json), CreateRegistry(reverse: true), json,
            new CheckpointReadOptions { WorldOptions = new WorldOptions(23) });
        var restoredEntities = entities.Select(e => restored.BindEntity(e.Id, e.Version)).ToList();
        for (var i = 0; i < 250; i++)
        {
            Assert.Equal(Save(source, CreateRegistry(), json), Save(restored, CreateRegistry(), json));
            var operation = random.Next();
            var value = random.Next();
            var next = Churn(source, entities, operation, value);
            var restoredNext = Churn(restored, restoredEntities, operation, value);
            Assert.Equal(next.HasValue, restoredNext.HasValue);
            if (next is Entity created && restoredNext is Entity createdAgain)
                AssertHandle(created, createdAgain);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FragmentedStreamRoundTripsWithOpenStreams(bool json)
    {
        using var world = new World();
        for (var i = 0; i < 1025; i++)
            world.CreateEntity(i);
        using var storage = new MemoryStream();
        using var output = new NonSeekableStream(storage);
        Write(world, output, CreateRegistry(), json);
        Assert.True(output.CanWrite);
        var bytes = storage.ToArray();
        using var inputStorage = new MemoryStream(bytes, writable: false);
        using var input = new NonSeekableStream(inputStorage);
        using var restored = Read(input, CreateRegistry(), json);
        Assert.True(input.CanRead);
        Assert.Equal(bytes, Save(restored, CreateRegistry(), json));
    }

    private static Entity? Churn(World world, List<Entity> entities, int operation, int value)
    {
        if (entities.Count == 0 || operation % 7 is 0 or 1 or 2)
        {
            var created = operation % 7 == 0 ? world.CreateEntity() : world.CreateEntity(value);
            entities.Add(created);
            return created;
        }

        var index = (operation / 7) % entities.Count;
        var entity = entities[index];
        switch (operation % 7)
        {
            case 3:
                world.DestroyEntity(entity);
                entities.RemoveAt(index);
                break;
            case 4:
                world.Set(entity, new Links(entities[value % entities.Count], default, default));
                break;
            case 5:
                world.Remove<int>(entity);
                break;
            default:
                world.Set(entity, value);
                break;
        }

        return null;
    }

    private static CodecRegistry CreateRegistry(bool reverse = false)
    {
        var schema = new CodecRegistry();
        if (reverse)
            RegisterScalars(schema);
        schema.Register(new ComponentSchema(100, 1, new FieldSchema(1, FieldKind.Int32)), new IntCodec());
        schema.Register(new ComponentSchema(201, 1, new FieldSchema(1, FieldKind.Entity),
            new FieldSchema(2, FieldKind.Entity), new FieldSchema(3, FieldKind.Entity)), new LinksCodec());
        if (!reverse)
            RegisterScalars(schema);
        return schema;
    }

    private static void RegisterScalars(CodecRegistry registry)
        => registry.Register(new ComponentSchema(302, 1, new FieldSchema(1, FieldKind.Float32),
            new FieldSchema(2, FieldKind.Float64), new FieldSchema(3, FieldKind.Int64),
            new FieldSchema(4, FieldKind.UInt64), new FieldSchema(5, FieldKind.Boolean),
            new FieldSchema(6, FieldKind.Int8), new FieldSchema(7, FieldKind.UInt8),
            new FieldSchema(8, FieldKind.Int16), new FieldSchema(9, FieldKind.UInt16),
            new FieldSchema(10, FieldKind.UInt32)), new ScalarsCodec());

    private static byte[] Save(World world, CodecRegistry schema, bool json, CheckpointWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(world, stream, schema, json, options);
        Assert.True(stream.CanWrite);
        return stream.ToArray();
    }

    private static void Write(World world, Stream stream, CodecRegistry schema, bool json, CheckpointWriteOptions? options = null)
    {
        if (json)
            WorldSerializer.WriteJson(world, stream, schema, options);
        else
            WorldSerializer.WriteBinary(world, stream, schema, options);
    }

    private static World Load(byte[] bytes, CodecRegistry schema, bool json, CheckpointReadOptions? options = null)
    {
        using var stream = new MemoryStream(bytes);
        var restored = Read(stream, schema, json, options);
        Assert.True(stream.CanRead);
        return restored;
    }

    private static World Read(Stream stream, CodecRegistry schema, bool json, CheckpointReadOptions? options = null)
        => json ? WorldSerializer.ReadJson(stream, schema, options) : WorldSerializer.ReadBinary(stream, schema, options);

    private static void AssertHandle(Entity expected, Entity actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Version, actual.Version);
        Assert.True(actual.IsValid);
    }

    private sealed class IntCodec : IComponentCodec<int>
    {
        public void Write(in int value, ComponentWriter fields) => fields.Set(1, FieldValue.From(value));
        public int Read(ComponentReader fields) => fields.Get(1).AsInt32();
    }

    private readonly record struct Links(Entity Live, Entity Stale, Entity Empty);

    private sealed class LinksCodec : IComponentCodec<Links>
    {
        public void Write(in Links value, ComponentWriter fields)
        {
            fields.SetEntity(1, value.Live);
            fields.SetEntity(2, value.Stale);
            fields.SetEntity(3, value.Empty);
        }

        public Links Read(ComponentReader fields)
            => new(fields.GetEntity(1), fields.GetEntity(2), fields.GetEntity(3));
    }

    private readonly record struct Marker(int Position, short Level);
    private readonly record struct Range(Marker Start, Marker End);

    private sealed class RangeCodec : IComponentCodec<Range>
    {
        public void Write(in Range value, ComponentWriter fields)
        {
            var bytes = new byte[12];
            WriteMarker(bytes, value.Start);
            WriteMarker(bytes.AsSpan(6), value.End);
            fields.Set(1, FieldValue.FromBytes(bytes));
        }

        public Range Read(ComponentReader fields)
        {
            var bytes = fields.Get(1).AsBytes();
            if (bytes.Length != 12)
                throw new InvalidDataException("Range must contain two 6-byte markers.");
            return new Range(ReadMarker(bytes), ReadMarker(bytes.AsSpan(6)));
        }

        private static void WriteMarker(Span<byte> bytes, Marker value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value.Position);
            BinaryPrimitives.WriteInt16LittleEndian(bytes[4..], value.Level);
        }

        private static Marker ReadMarker(ReadOnlySpan<byte> bytes)
            => new(BinaryPrimitives.ReadInt32LittleEndian(bytes), BinaryPrimitives.ReadInt16LittleEndian(bytes[4..]));
    }

    private readonly record struct Scalars(float Single, double Double, long I64, ulong U64,
        bool Boolean, sbyte I8, byte U8, short I16, ushort U16, uint U32);

    private sealed class ScalarsCodec : IComponentCodec<Scalars>
    {
        public void Write(in Scalars value, ComponentWriter fields)
        {
            fields.Set(1, FieldValue.From(value.Single));
            fields.Set(2, FieldValue.From(value.Double));
            fields.Set(3, FieldValue.From(value.I64));
            fields.Set(4, FieldValue.From(value.U64));
            fields.Set(5, FieldValue.From(value.Boolean));
            fields.Set(6, FieldValue.From(value.I8));
            fields.Set(7, FieldValue.From(value.U8));
            fields.Set(8, FieldValue.From(value.I16));
            fields.Set(9, FieldValue.From(value.U16));
            fields.Set(10, FieldValue.From(value.U32));
        }

        public Scalars Read(ComponentReader fields)
            => new(fields.Get(1).AsFloat32(), fields.Get(2).AsFloat64(), fields.Get(3).AsInt64(), fields.Get(4).AsUInt64(),
                fields.Get(5).AsBoolean(), fields.Get(6).AsInt8(), fields.Get(7).AsUInt8(),
                fields.Get(8).AsInt16(), fields.Get(9).AsUInt16(), fields.Get(10).AsUInt32());
    }

    private sealed class NonSeekableStream(MemoryStream storage) : Stream
    {
        public override bool CanRead => storage.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => storage.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => storage.Read(buffer, offset, Math.Min(count, 3));
        public override int Read(Span<byte> buffer) => storage.Read(buffer[..Math.Min(buffer.Length, 3)]);
        public override int ReadByte() => storage.ReadByte();
        public override void Flush() => storage.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => storage.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => storage.Write(buffer);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                storage.Dispose();
            base.Dispose(disposing);
        }
    }
}
