using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using SKECS.Numerics;

namespace SKECS.Benchmarks.Numerics;

[MemoryDiagnoser]
public class AddInt32DispatchBenchmarks
{
    [Benchmark(Baseline = true)]
    public void Scalar() => NumericKernels.AddInt32(default, default, default, KernelMode.Scalar);

    [Benchmark]
    public void Auto() => NumericKernels.AddInt32(default, default, default, KernelMode.Auto);
}

[MemoryDiagnoser]
public class AddInt32KernelBenchmarks
{
    private Memory<int> _left;
    private Memory<int> _right;
    private Memory<int> _destination;

    [Params(256, 259)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _left = AllocateUnaligned(Count);
        _right = AllocateUnaligned(Count);
        _destination = AllocateUnaligned(Count);
        var expected = new int[Count];
        for (var i = 0; i < Count; i++)
        {
            _left.Span[i] = int.MaxValue - i;
            _right.Span[i] = i * 17 - 1024;
            expected[i] = unchecked(_left.Span[i] + _right.Span[i]);
            _destination.Span[i] = ~expected[i];
        }

        Scalar();
        for (var i = 0; i < Count; i++)
        {
            if (_destination.Span[i] != expected[i])
                throw new InvalidOperationException($"Scalar AddInt32 produced an incorrect value at index {i}.");
            _destination.Span[i] = ~expected[i];
        }

        Auto();
        if (!_destination.Span.SequenceEqual(expected))
            throw new InvalidOperationException("Auto AddInt32 produced an incorrect result.");
    }

    [Benchmark(Baseline = true)]
    public void Scalar() => NumericKernels.AddInt32(_left.Span, _right.Span, _destination.Span, KernelMode.Scalar);

    [Benchmark]
    public void Auto() => NumericKernels.AddInt32(_left.Span, _right.Span, _destination.Span, KernelMode.Auto);

    private static unsafe Memory<int> AllocateUnaligned(int count)
    {
        var buffer = GC.AllocateArray<int>(count + 1, pinned: true);
        fixed (int* start = buffer)
        {
            var offset = (nuint)start % 16 == 0 ? 1 : 0;
            return buffer.AsMemory(offset, count);
        }
    }
}

[MemoryDiagnoser]
public class AddInt32QueryBenchmarks
{
    private const int EntityCount = 16_384 + 3;
    private World _world = null!;

    [Params(1, 63, 256)]
    public int ChunkCapacity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World(new WorldOptions(chunkCapacity: ChunkCapacity));
        var entities = new Entity[EntityCount];
        var expected = new int[EntityCount];
        for (var i = 0; i < EntityCount; i++)
        {
            var left = new Left { Value = int.MaxValue - i };
            var right = new Right { Value = i * 17 - 1024 };
            expected[i] = unchecked(left.Value + right.Value);
            entities[i] = _world.CreateEntity(left, right, new Sum { Value = ~expected[i] });
        }

        Scalar();
        for (var i = 0; i < EntityCount; i++)
        {
            ref var sum = ref _world.Get<Sum>(entities[i]);
            if (sum.Value != expected[i])
                throw new InvalidOperationException($"Scalar chunk addition produced an incorrect value for row {i}.");
            sum.Value = ~expected[i];
        }

        Auto();
        for (var i = 0; i < EntityCount; i++)
        {
            if (_world.Get<Sum>(entities[i]).Value != expected[i])
                throw new InvalidOperationException($"Auto chunk addition produced an incorrect value for row {i}.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    [Benchmark(Baseline = true)]
    public void Scalar() => RunQuery(KernelMode.Scalar);

    [Benchmark]
    public void Auto() => RunQuery(KernelMode.Auto);

    private void RunQuery(KernelMode mode)
    {
        var processor = new AddChunkProcessor(mode);
        _world.Query<Left, Right, Sum>().ForEachChunk(ref processor);
    }

    private struct Left { public int Value; }
    private struct Right { public int Value; }
    private struct Sum { public int Value; }

    private readonly struct AddChunkProcessor(KernelMode mode) : IQueryChunkProcessor<Left, Right, Sum>
    {
        public unsafe void Execute(QueryChunkView<Left, Right, Sum> chunk)
        {
            var left = MemoryMarshal.Cast<Left, int>(new ReadOnlySpan<Left>(chunk.Component1, chunk.Count));
            var right = MemoryMarshal.Cast<Right, int>(new ReadOnlySpan<Right>(chunk.Component2, chunk.Count));
            var destination = MemoryMarshal.Cast<Sum, int>(new Span<Sum>(chunk.Component3, chunk.Count));
            NumericKernels.AddInt32(left, right, destination, mode);
        }
    }
}
