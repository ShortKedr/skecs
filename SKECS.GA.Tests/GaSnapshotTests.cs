using SKECS.Numerics;

namespace SKECS.GA.Tests;

public sealed class GaSnapshotTests
{
    private static readonly GaSnapshotLayout MotionLayout = new([GaScalar.Fixed, GaScalar.Bool]);

    [Fact]
    public void PacksCanonicalRowsAndCopiesInputStorage()
    {
        GaScalar[] fields = [GaScalar.Fixed, GaScalar.Bool];
        var layout = new GaSnapshotLayout(fields);
        GaSnapshotKey[] keys = [new(17, 2), new(1, 9)];
        uint[] values = [0xDDEEFF00, 0x99AABBCC, 1, 0x55667788, 0x11223344, 0];

        var packet = GaSnapshotPacket.FromRows(layout, keys, values);
        fields[0] = GaScalar.Int32;
        keys[1] = new GaSnapshotKey(5, 42);
        values[3] = 0;

        Assert.Equal(2, packet.RowCount);
        Assert.Equal(5, layout.RowWordCount);
        Assert.Equal(3, layout.ValueWordCount);
        Assert.Equal(GaScalar.Fixed, layout.Fields[0]);
        Assert.Equal(new uint[]
        {
            1, 2, 3,
            1, 9, 0x55667788, 0x11223344, 0,
            17, 2, 0xDDEEFF00, 0x99AABBCC, 1
        }, packet.Words.ToArray());
        Assert.Equal(new uint[] { 0x55667788, 0x11223344, 0 }, Get(packet, new(1, 9)));
        Assert.Equal(new uint[] { 0xDDEEFF00, 0x99AABBCC, 1 }, Get(packet, new(17, 2)));
        Assert.False(packet.TryGet(new(1, 8), out var stale));
        Assert.True(stale.IsEmpty);
        Assert.False(packet.TryGet(new(5, 42), out _));

        var bytes = new byte[sizeof(uint) * packet.Words.Length];
        packet.WriteLittleEndian(bytes);
        Assert.Equal(Convert.FromHexString(
            "0100000002000000030000000100000009000000887766554433221100000000" +
            "110000000200000000FFEEDDCCBBAA9901000000"), bytes);
        Assert.Throws<ArgumentException>(() => packet.WriteLittleEndian(new byte[bytes.Length - 1]));
    }

    [Fact]
    public void RejectsInvalidKeysLayoutsAndDimensions()
    {
        Assert.Throws<ArgumentException>(() => new GaSnapshotLayout([(GaScalar)99]));
        Assert.Throws<ArgumentException>(() => GaSnapshotPacket.FromRows(MotionLayout, [new(-1, 1)], [1, 0, 1]));
        Assert.Throws<ArgumentException>(() => GaSnapshotPacket.FromRows(MotionLayout, [new(1, 0)], [1, 0, 1]));
        Assert.Throws<ArgumentException>(() => GaSnapshotPacket.FromRows(MotionLayout,
            [new(1, 1), new(1, 2)], [1, 0, 1, 2, 0, 1]));
        Assert.Throws<ArgumentException>(() => GaSnapshotPacket.FromRows(MotionLayout, [new(1, 1)], [1, 0]));
        Assert.Throws<ArgumentException>(() => GaSnapshotPacket.FromRows(MotionLayout, [new(1, 1)], [1, 0, 2]));

        var empty = GaSnapshotPacket.FromRows(MotionLayout, [], []);
        Assert.Equal(new uint[] { 1, 0, 3 }, empty.Words.ToArray());
        Assert.False(empty.TryGet(new(0, 1), out _));

        var tag = GaSnapshotPacket.FromRows(new GaSnapshotLayout([]), [new(0, 1)], []);
        Assert.True(tag.TryGet(new(0, 1), out var present));
        Assert.True(present.IsEmpty);
        Assert.False(tag.TryGet(new(0, 0), out _));
        Assert.False(tag.TryGet(new(-1, 1), out _));
        Assert.False(tag.TryGet(new(0, 2), out _));
    }

    [Fact]
    public void RecaptureObservesRawRefEditsAndEntityGenerationChanges()
    {
        GaSnapshotPacket captured;
        Entity original;
        using (var world = new World())
        {
            original = world.CreateEntity(new Motion { Value = Fixed.FromRaw(-2), Enabled = true });
            var marker = world.CreateEntity(new Marker());
            captured = CaptureMotion(world);
            Assert.Equal(new uint[] { 0xFFFFFFFE, 0xFFFFFFFF, 1 }, Get(captured, Key(original)));
            Assert.False(captured.TryGet(Key(marker), out _));

            ref var edit = ref world.Get<Motion>(original);
            edit.Value = Fixed.FromRaw(0x1122334455667788);
            edit.Enabled = false;
            var afterEdit = CaptureMotion(world);
            Assert.Equal(new uint[] { 0x55667788, 0x11223344, 0 }, Get(afterEdit, Key(original)));
            Assert.Equal(new uint[] { 0xFFFFFFFE, 0xFFFFFFFF, 1 }, Get(captured, Key(original)));

            world.DestroyEntity(original);
            Assert.False(CaptureMotion(world).TryGet(Key(original), out _));
            var reused = world.CreateEntity(new Motion { Value = Fixed.FromRaw(5), Enabled = false });
            Assert.Equal(original.Id, reused.Id);
            Assert.NotEqual(original.Version, reused.Version);
            var recaptured = CaptureMotion(world);
            Assert.False(recaptured.TryGet(Key(original), out _));
            Assert.False(captured.TryGet(Key(reused), out _));
            Assert.Equal(new uint[] { 5, 0, 0 }, Get(recaptured, Key(reused)));
        }
        Assert.Equal(new uint[] { 0xFFFFFFFE, 0xFFFFFFFF, 1 }, Get(captured, Key(original)));
    }

    [Fact]
    public void ComponentRegistrationAndChunkCapacityDoNotAffectPacket()
    {
        using var left = new World(new WorldOptions(1));
        using var right = new World(new WorldOptions(64));
        right.GetAccessor<Marker>();

        for (int i = 0; i < 12; i++)
        {
            var motion = new Motion { Value = Fixed.FromRaw(-10L * i), Enabled = i % 2 == 0 };
            if (i % 3 == 0)
            {
                left.CreateEntity(motion, new Marker());
                right.CreateEntity(new Marker(), motion);
            }
            else
            {
                left.CreateEntity(motion);
                right.CreateEntity(motion);
            }
        }
        Assert.Equal(CaptureMotion(left).Words.ToArray(), CaptureMotion(right).Words.ToArray());

        var tags = GaSnapshotPacket.Capture<Marker>(left, new GaSnapshotLayout([]),
            static (in Marker _, Span<uint> _) => { });
        Assert.Equal(4, tags.RowCount);
        Assert.True(tags.TryGet(new(6, 1), out var present));
        Assert.True(present.IsEmpty);
        Assert.False(tags.TryGet(new(5, 1), out _));
    }

    [Fact]
    public void CaptureRequiresCompletedWorldBoundary()
    {
        using var world = new World();
        var entity = world.CreateEntity(new Motion { Value = Fixed.FromRaw(4) });
        using var runner = new DeterministicRunner<byte>(world, new DeterministicWorkload<byte>());
        runner.BeginTick(1, default, Fixed.One);
        Assert.Throws<InvalidOperationException>(() => CaptureMotion(world));
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(1));
        Assert.Equal(new uint[] { 4, 0, 0 }, Get(CaptureMotion(world), Key(entity)));
    }

    private static GaSnapshotPacket CaptureMotion(World world) =>
        GaSnapshotPacket.Capture<Motion>(world, MotionLayout, static (in Motion component, Span<uint> values) =>
        {
            ulong raw = unchecked((ulong)component.Value.Raw);
            values[0] = unchecked((uint)raw);
            values[1] = unchecked((uint)(raw >> 32));
            values[2] = component.Enabled ? 1u : 0u;
        });

    private static uint[] Get(GaSnapshotPacket packet, GaSnapshotKey key)
    {
        Assert.True(packet.TryGet(key, out var values));
        return values.ToArray();
    }

    private static GaSnapshotKey Key(Entity entity) => new(entity.Id, entity.Version);
    private struct Motion { public Fixed Value; public bool Enabled; }
    private readonly struct Marker;
}
