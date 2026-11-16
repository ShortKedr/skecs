namespace SKECS.Serialization.Tests;

public sealed partial class CheckpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalDefaultsRetiredTagsAndExplicitUnknownPolicy(bool json)
    {
        using var source = new World();
        source.CreateEntity(81);
        var v1 = new CodecRegistry().Register(new ComponentSchema(100, 1,
            new FieldSchema(1, FieldKind.Int32)), new IntCodec());
        var legacy = Save(source, v1, json);
        var additive = PointRegistry(optional: true);
        using var added = Load(legacy, additive, json);
        Assert.Equal(new Point(81, 5), added.Get<Point>(added.BindEntity(0, 1)));

        var withExtra = Save(added, additive, json);
        Assert.Throws<InvalidDataException>(() => Load(withExtra, v1, json));
        var omitted = new List<string>();
        using var projected = Load(withExtra, v1, json,
            new CheckpointReadOptions { AllowUnknown = true, OnOmitted = omitted.Add });
        Assert.Equal(81, projected.Get<int>(projected.BindEntity(0, 1)));
        Assert.Single(omitted);
        Assert.False(string.IsNullOrWhiteSpace(omitted[0]));

        var retired = new CodecRegistry().Register(new ComponentSchema(100, 1, new FieldSchema(1, FieldKind.Int32),
            new FieldSchema(2, FieldKind.Int32, retired: true)), new IntCodec());
        using var older = Load(withExtra, retired, json);
        Assert.Equal(81, older.Get<int>(older.BindEntity(0, 1)));
        Assert.Throws<InvalidDataException>(() => Load(legacy, PointRegistry(optional: false), json));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownComponentCanBeOmittedOnlyByExplicitPolicy(bool json)
    {
        using var source = new World();
        source.CreateEntity(81);
        var bytes = Save(source, IntRegistry(), json);
        Assert.Throws<InvalidDataException>(() => Load(bytes, new CodecRegistry(), json));
        var omitted = new List<string>();
        using var projected = Load(bytes, new CodecRegistry(), json,
            new CheckpointReadOptions { AllowUnknown = true, OnOmitted = omitted.Add });
        Assert.Single(omitted);
        Assert.True(projected.IsAlive(projected.BindEntity(0, 1)));
        Assert.False(projected.Has<int>(projected.BindEntity(0, 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComponentMigrationRequiresACompleteChain(bool json)
    {
        using var source = new World();
        var entity = source.CreateEntity(250);
        var v1 = new CodecRegistry().Register(new ComponentSchema(100, 1,
            new FieldSchema(1, FieldKind.Int32)), new IntCodec());
        var bytes = Save(source, v1, json);
        var calls = 0;
        var incomplete = LongRegistry()
            .Migrate(100, 1, 2, _ => calls++);
        Assert.Throws<InvalidDataException>(() => Load(bytes, incomplete, json));
        Assert.Equal(0, calls);
        Assert.Equal(250, source.Get<int>(entity));

        var complete = LongRegistry()
            .Migrate(100, 1, 2, fields =>
            {
                fields.Set(2, FieldValue.From(fields.Get(1).AsInt32() / 10));
                fields.Remove(1);
            })
            .Migrate(100, 2, 3, fields =>
            {
                fields.Set(3, FieldValue.From(100_000_000_000L * fields.Get(2).AsInt32()));
                fields.Remove(2);
            });
        using var restored = Load(bytes, complete, json);
        Assert.Equal(2_500_000_000_000L, restored.Get<long>(restored.BindEntity(entity.Id, entity.Version)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeCheckpointMigrationRewritesMembershipAndRejectsInvalidTargets(bool json)
    {
        using var source = new World();
        var live = source.CreateEntity(25);
        var released = source.CreateEntity();
        source.DestroyEntity(released);
        var originalSchema = IntRegistry();
        var original = originalSchema.GetFingerprint();
        var intermediate = MovingRegistry(2001).GetFingerprint();
        var registry = MovingRegistry(2002);
        var destination = registry.GetFingerprint();
        var bytes = Save(source, originalSchema, json);

        registry.MigrateCheckpoint(original, intermediate, checkpoint =>
        {
            var item = checkpoint.Components.Single();
            var renamed = new LogicalComponent(item.EntityId, 2001);
            renamed.Fields.Set(1, FieldValue.From(item.Fields.Get(1).AsInt32() * 10));
            checkpoint.Components[0] = renamed;
        });
        registry.MigrateCheckpoint(intermediate, destination, checkpoint =>
        {
            var item = checkpoint.Components.Single();
            Assert.Equal(2001u, item.ComponentId);
            var renamed = new LogicalComponent(item.EntityId, 2002);
            renamed.Fields.Set(1, FieldValue.From(item.Fields.Get(1).AsInt32() + 7));
            checkpoint.Components[0] = renamed;

            var id = checkpoint.FreeIds[0];
            var reused = new LogicalComponent(id, 2002);
            reused.Fields.Set(1, FieldValue.From(33));
            checkpoint.Slots[id] = new CheckpointSlot(checkpoint.Slots[id].Version, true);
            checkpoint.FreeIds = [];
            checkpoint.Components.Add(reused);
        });
        using var restored = Load(bytes, registry, json);
        Assert.Equal(257, restored.Get<int>(restored.BindEntity(live.Id, live.Version)));
        Assert.Equal(33, restored.Get<int>(restored.BindEntity(released.Id, released.Version + 1)));
        Assert.Equal(restored.BindEntity(2, 1), restored.CreateEntity());

        var invalid = MovingRegistry(2002).MigrateCheckpoint(original, destination, checkpoint =>
        {
            var impossible = new LogicalComponent(99, 2002);
            impossible.Fields.Set(1, FieldValue.From(30));
            checkpoint.Components.Clear();
            checkpoint.Components.Add(impossible);
        });
        Assert.Throws<InvalidDataException>(() => Load(bytes, invalid, json));
        Assert.Equal(25, source.Get<int>(live));
        Assert.False(source.IsAlive(released));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtensionsValidateKnownVersionsAndRequiredIds(bool json)
    {
        using var source = new World();
        source.CreateEntity(81);
        var registry = CreateRegistry();
        var bytes = Save(source, registry, json, new CheckpointWriteOptions
        {
            Extensions =
            [
                new CheckpointExtension(8, 1, false, [42]),
                new CheckpointExtension(7, 2, true, [13, 19, 91]),
            ],
        });
        var accepted = new CheckpointReadOptions
        {
            Extensions = new Dictionary<uint, uint> { [7] = 2 },
            RequiredExtensions = new uint[] { 7 },
            OnOmitted = _ => { },
        };
        using (var input = new MemoryStream(bytes))
        using (var restored = ReadExtensions(input, registry, json, out var extensions, accepted))
        {
            var extension = Assert.Single(extensions);
            Assert.Equal(7u, extension.Id);
            Assert.Equal(2u, extension.Version);
            Assert.True(extension.Required);
            Assert.Equal(new byte[] { 13, 19, 91 }, extension.Data.ToArray());
            Assert.Equal(81, restored.Get<int>(restored.BindEntity(0, 1)));
        }
        Assert.Throws<InvalidDataException>(() => Load(bytes, registry, json));
        Assert.Throws<InvalidDataException>(() => Load(bytes, registry, json,
            new CheckpointReadOptions { Extensions = new Dictionary<uint, uint> { [7] = 1 } }));
        var missing = Save(source, registry, json);
        Assert.Throws<InvalidDataException>(() => Load(missing, registry, json, accepted));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TruncatedOrOversizeInputDoesNotPublishWorldOrCallbacks(bool json)
    {
        using var current = new World();
        var entity = current.CreateEntity(81);
        current.CreateEntity(2);
        var bytes = Save(current, CreateRegistry(), json);

        var omitted = new List<string>();
        Assert.Throws<InvalidDataException>(() => Load(bytes[..^2], new CodecRegistry(), json,
            new CheckpointReadOptions { AllowUnknown = true, OnOmitted = omitted.Add }));
        Assert.Empty(omitted);
        Assert.Equal(81, current.Get<int>(entity));

        Assert.Throws<InvalidDataException>(() => Load(bytes, CreateRegistry(), json,
            new CheckpointReadOptions { Limits = new CheckpointLimits { MaxSlots = 1 } }));
        Assert.Throws<InvalidDataException>(() => Load(bytes, CreateRegistry(), json,
            new CheckpointReadOptions { Limits = new CheckpointLimits { MaxDocumentBytes = bytes.Length - 1 } }));
    }

    private static World ReadExtensions(Stream stream, CodecRegistry schema, bool json,
        out IReadOnlyList<CheckpointExtension> extensions, CheckpointReadOptions options)
        => json ? WorldSerializer.ReadJson(stream, schema, out extensions, options)
            : WorldSerializer.ReadBinary(stream, schema, out extensions, options);

    private static CodecRegistry PointRegistry(bool optional)
        => new CodecRegistry().Register(new ComponentSchema(100, 1, new FieldSchema(1, FieldKind.Int32),
            new FieldSchema(2, FieldKind.Int32, optional: optional)), new PointCodec());

    private static CodecRegistry LongRegistry()
        => new CodecRegistry().Register(new ComponentSchema(100, 3, new FieldSchema(3, FieldKind.Int64)), new LongCodec());

    private static CodecRegistry MovingRegistry(uint id)
        => new CodecRegistry().Register(new ComponentSchema(id, 1, new FieldSchema(1, FieldKind.Int32)), new IntCodec());

    private readonly record struct Point(int X, int Y);

    private sealed class PointCodec : IComponentCodec<Point>
    {
        public void Write(in Point value, ComponentWriter fields)
        {
            fields.Set(1, FieldValue.From(value.X));
            fields.Set(2, FieldValue.From(value.Y));
        }

        public Point Read(ComponentReader fields)
            => new(fields.Get(1).AsInt32(), fields.TryGet(2, out var y) ? y.AsInt32() : 5);
    }

    private sealed class LongCodec : IComponentCodec<long>
    {
        public void Write(in long value, ComponentWriter fields) => fields.Set(3, FieldValue.From(value));
        public long Read(ComponentReader fields) => fields.Get(3).AsInt64();
    }
}
