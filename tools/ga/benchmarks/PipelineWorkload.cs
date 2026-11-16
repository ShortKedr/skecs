using System.Diagnostics;
using SKECS.GA.Backend.Vulkan;
using SKECS.Numerics;

namespace SKECS.GA.Benchmarks;

internal sealed class PipelineWorkload
{
    private readonly PipelineTarget _target;
    private readonly RowLayout _layout;
    private readonly uint[] _input;
    private readonly uint[] _cpuOutput;
    private readonly uint[] _expected;
    private readonly int _rows;

    internal PipelineWorkload(PipelineTarget target, int rows)
    {
        _target = target;
        _rows = rows;
        _layout = RowLayout.FromProgram(target.Program);
        int words = checked(rows * _layout.Width);
        _input = new uint[checked(words + 1)];
        _cpuOutput = new uint[words];
        _expected = new uint[words];
        _input[0] = (uint)rows;
        for (int index = 0; index < rows; index++)
        {
            Span<uint> row = _input.AsSpan(1 + index * _layout.Width, _layout.Width);
            WriteFixed(row, _layout.X, Fixed.FromRaw((index % 257 - 128) * 0x01000000L));
            row[_layout.Cell] = (uint)index;
            WriteFixed(row, _layout.Delta, Fixed.FromRaw((index % 127 - 64) * 0x10000001L));
            WriteFixed(row, _layout.Scale, Fixed.FromRaw(0x140000001L + index % 257));
            row[_layout.Shift] = unchecked((uint)(index % 47 - 23));
            row[_layout.Divide] = index % 4 == 0 ? 1u : 0u;
        }

        _input.AsSpan(1).CopyTo(_expected);
        for (int index = 0; index < rows; index++)
            if (GaInterpreter.Execute(target.Program, _expected.AsSpan(index * _layout.Width, _layout.Width)) is { } fault)
                throw new InvalidDataException($"The staged input hit {fault.Kind} at row {index}.");

        ValidateCpu(ExecuteCpu());
        ValidateVulkan(ExecuteVulkan());
        int groupSize = GaSlangModule.ThreadsPerGroup;
        long groups = ((long)rows + groupSize - 1) / groupSize;
        Console.WriteLine($"{rows} rows, {_layout.Width} words per row, one Vulkan submission with {groups} workgroups of {groupSize} lanes.");
    }

    internal uint[] ExecuteCpu()
    {
        _input.AsSpan(1).CopyTo(_cpuOutput);
        var processor = new MixedMovement();
        for (int index = 0; index < _rows; index++)
        {
            Span<uint> row = _cpuOutput.AsSpan(index * _layout.Width, _layout.Width);
            var position = new Position { X = ReadFixed(row, _layout.X), Cell = unchecked((int)row[_layout.Cell]) };
            var velocity = new Velocity
            {
                Delta = ReadFixed(row, _layout.Delta),
                Scale = ReadFixed(row, _layout.Scale),
                Shift = unchecked((int)row[_layout.Shift]),
                Divide = row[_layout.Divide] != 0
            };
            processor.Execute(ref position, in velocity);
            WriteFixed(row, _layout.X, position.X);
            row[_layout.Cell] = unchecked((uint)position.Cell);
        }
        return _cpuOutput;
    }

    internal GaPackedDispatchResult ExecuteVulkan() => _target.Kernel.BeginDispatch(_input, _layout.Width).Drain();

    internal void ValidateCpu(uint[] output)
    {
        if (!output.AsSpan().SequenceEqual(_expected))
            throw new InvalidDataException("The staged CPU C# outputs diverged from the IR interpreter.");
    }

    internal void ValidateVulkan(GaPackedDispatchResult result)
    {
        if (result.Faults.Length != (long)_rows * GaSlangModule.FaultStride || result.Faults.IndexOfAnyExcept(0u) >= 0 ||
            !result.Output.SequenceEqual(_expected))
            throw new InvalidDataException("Vulkan output or fault words diverged from the IR interpreter.");
    }

    internal void RunSmoke()
    {
        var cpu = Measure(ExecuteCpu);
        var gpu = Measure(ExecuteVulkan);
        ValidateCpu(cpu.Result);
        ValidateVulkan(gpu.Result);
        Console.WriteLine(FormattableString.Invariant($"CPU staged C# {cpu.Elapsed.TotalMilliseconds:F3} ms, {cpu.Bytes} process managed bytes allocated."));
        Console.WriteLine(FormattableString.Invariant($"Vulkan submit, transfer, wait and readback {gpu.Elapsed.TotalMilliseconds:F3} ms, {gpu.Bytes} process managed bytes allocated."));
        Console.WriteLine("Smoke validated every output word and checked the full Vulkan fault buffer. Durations are single samples.");
    }

    private static (T Result, TimeSpan Elapsed, long Bytes) Measure<T>(Func<T> run)
    {
        long before = GC.GetTotalAllocatedBytes(precise: true);
        long start = Stopwatch.GetTimestamp();
        T result = run();
        long end = Stopwatch.GetTimestamp();
        long after = GC.GetTotalAllocatedBytes(precise: true);
        return (result, Stopwatch.GetElapsedTime(start, end), after - before);
    }

    private static Fixed ReadFixed(ReadOnlySpan<uint> row, int offset) =>
        Fixed.FromRaw(unchecked((long)(row[offset] | (ulong)row[offset + 1] << 32)));

    private static void WriteFixed(Span<uint> row, int offset, Fixed value)
    {
        row[offset] = unchecked((uint)value.Raw);
        row[offset + 1] = unchecked((uint)(value.Raw >> 32));
    }

    private readonly record struct RowLayout(int Width, int X, int Cell, int Delta, int Scale, int Shift, int Divide)
    {
        internal static RowLayout FromProgram(GaProgram program)
        {
            var offsets = new Dictionary<string, (int Offset, GaScalar Type)>(StringComparer.Ordinal);
            int words = 0;
            foreach (var field in program.Fields)
            {
                offsets.Add(field.Path, (words, field.Type));
                words += field.Type == GaScalar.Fixed ? 2 : 1;
            }
            if (offsets.Count != 6 || words != program.WordCount || !program.UsesFixed)
                throw new InvalidDataException("Unexpected mixed benchmark row schema.");

            int At(string path, GaScalar scalar) => offsets.TryGetValue(path, out var value) && value.Type == scalar
                ? value.Offset : throw new InvalidDataException($"Missing {scalar} benchmark field '{path}'.");
            return new RowLayout(words, At("X", GaScalar.Fixed), At("Cell", GaScalar.Int32), At("Delta", GaScalar.Fixed),
                At("Scale", GaScalar.Fixed), At("Shift", GaScalar.Int32), At("Divide", GaScalar.Bool));
        }
    }
}
