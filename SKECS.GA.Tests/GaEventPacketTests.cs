using SKECS.Numerics;

namespace SKECS.GA.Tests;

public sealed class GaEventPacketTests
{
    [Fact]
    public void UsesExistingCommitOrderAndLocalOrdinalsForEntityAndJobSources()
    {
        using var world = new World(new WorldOptions(3));
        for (int i = 0; i < 65; i++)
            world.CreateEntity(new Value(i));
        var workload = new DeterministicWorkload<byte>().AddSystem(SystemStage.Update,
            new DeterministicQuery().With<Value>().Read<Value>(), new EmitPacket());
        using var runner = new DeterministicRunner<byte>(world, workload, workerCount: 2);
        runner.BeginTick(3, 0, Fixed.One);
        Assert.Equal(DeterministicTickStatus.Completed, runner.Advance(2));
        var events = runner.LastCompleted!.Events;
        Assert.Equal(130, events.Count);
        for (int row = 0; row < 64; row++)
        {
            var first = events[2 * row];
            var second = events[2 * row + 1];
            Assert.Equal(DeterministicSourceKind.Entity, first.Key.SourceKind);
            Assert.Equal(row, first.Key.SourceId);
            Assert.Equal(row, first.Get<int>());
            Assert.Equal(0, first.Key.LocalOrdinal);
            Assert.Equal(row + 200, second.Get<int>());
            Assert.Equal(1, second.Key.LocalOrdinal);
        }
        // The second logical job contains just the failing/excluded row.
        Assert.Equal(DeterministicSourceKind.Job, events[128].Key.SourceKind);
        Assert.Equal(0, events[128].Key.SourceId);
        Assert.Equal(100, events[128].Get<int>());
        Assert.Equal(DeterministicSourceKind.Job, events[129].Key.SourceKind);
        Assert.Equal(1, events[129].Key.SourceId);
        Assert.Equal(101, events[129].Get<int>());
    }

    [Theory]
    [InlineData(2, 1, 1, 1, 1)] // Unknown version
    [InlineData(1, 1, 1, 0, 1)] // Event width
    [InlineData(1, 2, 1, 1, 1)] // Truncated record table
    [InlineData(1, 1, 1, 1, 2)] // Per-source overflow
    [InlineData(1, 1, int.MaxValue, int.MaxValue, 0)] // Arithmetic overflow risk
    public void RejectsInvalidHeaderAndAllSourceCounts(int version, int sources, int capacity, int width, int count)
    {
        uint[] words = [(uint)version, (uint)sources, (uint)capacity, (uint)width, (uint)count, 0];
        Assert.Throws<InvalidDataException>(() => ReadCount(words));
    }

    private static int ReadCount(uint[] words) => new GaEventPacket(words).SourceCount;

    private readonly record struct Value(int Number);

    private readonly struct EmitPacket : IDeterministicJobProcessor<byte>
    {
        public void Execute(DeterministicJob<byte> job)
        {
            var sources = job.Count + 1;
            Span<uint> words = stackalloc uint[GaEventPacket.HeaderWords + 3 * sources];
            words[0] = GaEventPacket.Version;
            words[1] = (uint)sources;
            words[2] = 2;
            words[3] = 1;
            for (int row = 0; row < job.Count; row++)
            {
                uint number = (uint)job.Read<Value>(row).Number;
                words[4 + 3 * row] = 2;
                words[5 + 3 * row] = number;
                words[6 + 3 * row] = number + 200;
            }
            words[4 + 3 * job.Count] = 1;
            words[5 + 3 * job.Count] = (uint)(100 + job.JobId);
            var packet = new GaEventPacket(words);
            packet.ApplyLogicalJob<byte, int>(job, job.Count, Decode);
            packet.ApplyEntityRows<byte, int>(job, 0, job.JobId == 0 ? job.Count : 0, Decode);
        }

        private static int Decode(ReadOnlySpan<uint> words) => checked((int)words[0]);
    }
}
