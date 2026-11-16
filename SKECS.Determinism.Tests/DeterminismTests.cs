using System.Runtime.InteropServices;

namespace SKECS.Determinism;

public sealed class DeterminismTests
{
    public static TheoryData<int, int> ChunkBoundaries()
    {
        var cases = new TheoryData<int, int>();
        foreach (var capacity in new[] { 1, 2, 3, 32, 256, 257 })
        {
            var counts = new[] { 0, 1, capacity - 1, capacity, capacity + 1, 2 * capacity + 1 };
            foreach (var count in counts.Distinct())
                cases.Add(capacity, count);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(ChunkBoundaries))]
    public void D1RepeatsAcrossChunkBoundariesWithMigrationAndRecycle(int capacity, int count)
    {
        var operations = Enumerable.Range(0, count)
            .Select(key => new Operation("direct", key, key + 100, -key))
            .ToList();
        int[] targets = count == 0 ? [] : new[] { 0, count / 2, count - 1 }.Distinct().ToArray();
        foreach (var key in targets)
        {
            operations.Add(new("set-marker", key, 0xE5, int.MinValue + key));
            operations.Add(new("probe", key));
            operations.Add(new("remove-marker", key));
            operations.Add(new("probe", key));
            operations.Add(new("destroy", key));
            operations.Add(new("probe", key));
        }
        // Do not refill until every slot is free, so reuse exercises the stack order.
        for (var index = 0; index < targets.Length; index++)
        {
            operations.Add(new("create", count + index));
            operations.Add(new("probe", targets[targets.Length - index - 1]));
        }
        operations.Add(new("tick"));

        var fixture = new Fixture(1, capacity, operations.ToArray());
        var options = new ReplayOptions(capacity, Diagnostics: true);
        using var first = Workload.Replay(fixture, options).GetEnumerator();
        using var second = Workload.Replay(fixture, options).GetEnumerator();
        EntityState[] beforeMigration = [];
        EntityState[] finalState = [];
        for (var step = 0; step <= operations.Count; step++)
        {
            Assert.True(first.MoveNext());
            Assert.True(second.MoveNext());
            Assert.Equal(step, first.Current.Step);
            Assert.Null(Snapshot.Difference(first.Current, second.Current));
            if (step == 0)
            {
                Assert.Equal("initial", first.Current.Operation);
                Assert.Empty(first.Current.Entities);
            }
            if (step == count)
                beforeMigration = first.Current.Entities;
            if (step == operations.Count)
                finalState = first.Current.Entities;
        }
        Assert.False(first.MoveNext());
        Assert.False(second.MoveNext());
        Assert.Equal(count, beforeMigration.Length);
        Assert.Equal(count, finalState.Length);
        for (var index = 0; index < targets.Length; index++)
        {
            var destroyedKey = targets[targets.Length - index - 1];
            var previous = Assert.Single(beforeMigration, state => state.Key == destroyedKey);
            var replacement = Assert.Single(finalState, state => state.Key == count + index);
            Assert.Equal(previous.Id, replacement.Id);
            Assert.Equal(previous.Version + 1, replacement.Version);
            Assert.Null(replacement.Transform);
            Assert.Null(replacement.Motion);
            Assert.Null(replacement.Marker);
        }
    }

    [Theory]
    [InlineData(1, true, false, true, true)]
    [InlineData(2, false, true, true, true)]
    [InlineData(3, true, false, false, false)]
    [InlineData(256, false, true, false, true)]
    [InlineData(257, true, false, true, false)]
    [InlineData(0, false, false, true, true)]
    [InlineData(-17, true, true, true, false)]
    public void D3PreservesEachLogicalStateWithDifferentStorage(
        int capacity, bool diagnostics, bool warmQueries, bool reverseRegistration, bool useChunks)
    {
        var fixture = new Fixture(1, 32,
        [
            new("direct", 17, int.MaxValue, int.MinValue),
            new("create", 12),
            new("factory", 24, 3, -19),
            new("batch", 31, -71, 12),
            new("set-marker", 17, byte.MaxValue, int.MinValue),
            new("set-marker", 24, 27, int.MaxValue),
            new("set-motion", 17, -3, -5),
            new("set-transform", 12, -3, 9),
            new("add-motion", 12, int.MinValue, int.MaxValue),
            new("tick"),
            new("remove-transform", 24),
            new("remove-motion", 32),
            new("remove-marker", 32),
            new("remove-marker", 17),
            new("destroy", 31),
            new("probe", 31),
            new("direct", 37, 5, 7),
            new("probe", 31),
            new("probe", 37),
            new("tick"),
            new("tick"),
        ]);
        var expected = Workload.Replay(fixture).ToArray();
        var variant = new ReplayOptions(capacity, diagnostics, warmQueries, reverseRegistration, useChunks);
        var actual = Workload.Replay(fixture, variant).ToArray();

        Assert.Equal(expected.Length, actual.Length);
        for (var step = 0; step < expected.Length; step++)
            Assert.Null(Snapshot.Difference(expected[step], actual[step], logicalOnly: true));
    }

    [Fact]
    public void D3MatchesPermutedCreationByLogicalKeyAtEachTick()
    {
        Operation[] creation = [new("direct", 4, 11, -7), new("direct", 2, -19, 43), new("create", 6)];
        Operation[] suffix = [new("set-marker", 2, 179, 331), new("tick"), new("tick"), new("tick")];
        var expected = Workload.Replay(new Fixture(1, 2, [.. creation, .. suffix]))
            .Where(step => step.Operation == "tick").ToArray();
        var actual = Workload.Replay(new Fixture(1, 1, [.. creation.Reverse(), .. suffix]))
            .Where(step => step.Operation == "tick").ToArray();

        Assert.Equal(3, expected.Length);
        Assert.Equal(expected.Length, actual.Length);
        for (var tick = 0; tick < expected.Length; tick++)
        {
            Assert.NotEqual(expected[tick].Hash, actual[tick].Hash);
            Assert.Null(Snapshot.Difference(expected[tick], actual[tick], logicalOnly: true));
            Assert.Equal(Snapshot.Hash(expected[tick].Entities, logicalOnly: true),
                Snapshot.Hash(actual[tick].Entities, logicalOnly: true));
        }
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("factory")]
    public void CreationPathsAgreeOnStateAndAssignedHandles(string mode)
    {
        var batch = new Fixture(1, 1, [new("batch", 40, 19, -7), new("tick")]);
        var individual = new Fixture(1, 1, [new(mode, 40, 19, -7), new(mode, 41, 19, -7), new("tick")]);

        var expected = Workload.Replay(batch).Last();
        var actual = Workload.Replay(individual).Last();

        Assert.Equal(2, expected.Entities.Length);
        Assert.Equal(expected.Entities, actual.Entities);
        Assert.Equal(expected.Hash, actual.Hash);
    }

    [Fact]
    public void TickUsesSignedDivisionAndUncheckedArithmeticInStageOrder()
    {
        var fixture = new Fixture(1, 3,
        [
            new("direct", 41, int.MaxValue, int.MinValue),
            new("set-motion", 41, -3, -5),
            new("set-marker", 41, byte.MaxValue, int.MinValue),
            new("tick"),
            new("tick"),
        ]);
        var ticks = Workload.Replay(fixture).Where(step => step.Operation == "tick").ToArray();

        Assert.Equal(2, ticks.Length);
        var first = Assert.Single(ticks[0].Entities);
        Assert.Equal(new Motion(-1, -2), first.Motion);
        Assert.Equal(new Transform(int.MaxValue - 4, int.MaxValue - 1), first.Transform);
        var second = Assert.Single(ticks[1].Entities);
        Assert.Equal(new Motion(0, -1), second.Motion);
        Assert.Equal(new Transform(int.MaxValue - 13, int.MaxValue - 2), second.Transform);
        Assert.Equal(new Marker(byte.MaxValue, int.MinValue), second.Marker);
    }

    [Fact]
    public void SnapshotIgnoresEntityEnumerationOrderAndMarkerPadding()
    {
        var marker = new Marker(0xA7, int.MinValue + 5);
        Span<Marker> storage = stackalloc Marker[1];
        storage[0] = marker;
        var bytes = MemoryMarshal.AsBytes(storage);
        var paddingBytes = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] ^= 0xFF;
            if (storage[0].Equals(marker))
                paddingBytes++;
            else
                bytes[index] ^= 0xFF;
        }
        Assert.True(paddingBytes > 0);

        var state = new EntityState(11, 1, 3, null, null, marker);
        var other = new EntityState(8, 7, 1, null, null, null);
        Assert.Equal(Snapshot.Hash([state, other]), Snapshot.Hash([other, state with { Marker = storage[0] }]));
    }

    [Fact]
    public void SnapshotMatchesLittleEndianSha256Vector()
    {
        var entity = new EntityState(0x01020304, 0x00010203, 0x05060708,
            new Transform(0x10203040, -2), new Motion(0x01020304, int.MinValue), new Marker(0xA5, -3));

        Assert.Equal("b14ff093224560b47c994c8be0a2d8baf050c0a5d2c8864bc434f4a40cc3f601", Snapshot.Hash([entity]));
    }

    [Fact]
    public void SnapshotHashAndComparerDetectStateMutations()
    {
        var entity = new EntityState(17, 2, 1, new Transform(int.MinValue, 3), new Motion(-13, 27), null);
        var expected = Frame(entity);
        (EntityState[] State, string Difference)[] mutations =
        [
            ([], "entity 17"),
            ([entity with { Id = 5 }], "entity 17 id:version"),
            ([entity with { Version = 2 }], "entity 17 id:version"),
            ([entity with { Transform = new Transform(int.MinValue + 1, 3) }],
                "entity 17 transform x: expected -2147483648, actual -2147483647"),
            ([entity with { Motion = null }], "entity 17 motion membership"),
            ([entity with { Marker = new Marker(0, 0) }], "entity 17 marker membership"),
        ];
        foreach (var mutation in mutations)
        {
            var actual = Frame(mutation.State);
            Assert.NotEqual(expected.Hash, actual.Hash);
            Assert.Contains(mutation.Difference, Snapshot.Difference(expected, actual));
        }
    }

    [Fact]
    public void ComparerDetectsMutationsToResultsVisitsAndHash()
    {
        var expected = Frame() with { Result = ["created"], Visits = ["a", "b"] };

        Assert.Contains("result[0]", Snapshot.Difference(expected, expected with { Result = ["destroyed"] }));
        Assert.Contains("visits[0]", Snapshot.Difference(expected, expected with { Visits = ["b", "a"] }));
        Assert.Contains("hash", Snapshot.Difference(expected, expected with { Hash = new string('0', 64) }));
        Assert.Null(Snapshot.Difference(expected, expected with { Result = [], Visits = [] }, logicalOnly: true));
    }

    private static ReplayStep Frame(params EntityState[] entities) =>
        new(1, "tick", [], [], entities, Snapshot.Hash(entities));
}
