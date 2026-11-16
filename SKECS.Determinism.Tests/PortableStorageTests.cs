namespace SKECS.Determinism;

public sealed class PortableStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoreThanEightComponentTypesSurviveRegistrationAndMigration(bool reverseRegistration)
    {
        using var world = new World(new WorldOptions(chunkCapacity: 2));
        var entity = world.CreateEntity();
        Action[] additions =
        [
            () => world.Add(entity, new Field<byte>(0xE5)),
            () => world.Add(entity, new Field<sbyte>(-105)),
            () => world.Add(entity, new Field<short>(short.MinValue)),
            () => world.Add(entity, new Field<ushort>(ushort.MaxValue)),
            () => world.Add(entity, new Field<int>(int.MinValue)),
            () => world.Add(entity, new Field<uint>(0xFCBACAD7u)),
            () => world.Add(entity, new Field<long>(long.MinValue)),
            () => world.Add(entity, new Field<ulong>(ulong.MaxValue)),
            () => world.Add(entity, new Field<char>('λ')),
        ];
        foreach (var add in reverseRegistration ? additions.Reverse() : additions)
            add();
        Assert.True(world.Remove<Field<ushort>>(entity));
        world.Add(entity, new Field<ushort>(ushort.MaxValue - 3));

        Assert.Equal(0xE5, world.Get<Field<byte>>(entity).Value);
        Assert.Equal(-105, world.Get<Field<sbyte>>(entity).Value);
        Assert.Equal(short.MinValue, world.Get<Field<short>>(entity).Value);
        Assert.Equal(ushort.MaxValue - 3, world.Get<Field<ushort>>(entity).Value);
        Assert.Equal(int.MinValue, world.Get<Field<int>>(entity).Value);
        Assert.Equal(0xFCBACAD7u, world.Get<Field<uint>>(entity).Value);
        Assert.Equal(long.MinValue, world.Get<Field<long>>(entity).Value);
        Assert.Equal(ulong.MaxValue, world.Get<Field<ulong>>(entity).Value);
        Assert.Equal('λ', world.Get<Field<char>>(entity).Value);
    }

    [Theory]
    [InlineData(0x00000000u)] // +0
    [InlineData(0x80000000u)] // -0
    [InlineData(0x00000001u)] // smallest positive subnormal
    [InlineData(0x7F800000u)] // +Infinity
    [InlineData(0xFF800000u)] // -Infinity
    [InlineData(0x7FC12345u)] // NaN with payload
    [InlineData(0xFFC56789u)] // negative NaN with a different payload
    public void FloatCopyPreservesBitsThroughSwapAndMigration(uint bits)
    {
        using var world = new World(new WorldOptions(chunkCapacity: 2));
        var accessor = world.GetAccessor<Field<float>>();
        var removed = world.CreateEntity(new Field<float>(1.0f));
        var payload = BitConverter.Int32BitsToSingle(unchecked((int)bits));
        var survivor = world.CreateEntity(new Field<float>(payload));

        world.DestroyEntity(removed);
        Assert.Equal(bits, unchecked((uint)BitConverter.SingleToInt32Bits(accessor.Get(survivor).Value)));
        world.Add(survivor, new Field<int>(-3));
        Assert.True(world.Remove<Field<int>>(survivor));

        Assert.Equal(bits, unchecked((uint)BitConverter.SingleToInt32Bits(accessor.Get(survivor).Value)));
        Assert.Equal(bits, unchecked((uint)BitConverter.SingleToInt32Bits(world.Get<Field<float>>(survivor).Value)));
    }

    private readonly record struct Field<T>(T Value) where T : unmanaged;
}
