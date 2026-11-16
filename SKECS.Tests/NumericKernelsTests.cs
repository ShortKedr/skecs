using System.Numerics;
using System.Runtime.Intrinsics;
using SKECS.Numerics;

namespace SKECS.Tests;

public sealed class NumericKernelsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(19)]
    [InlineData(63)]
    [InlineData(256)]
    [InlineData(259)]
    public void AddInt32MatchesUncheckedAdditionForUnalignedSlices(int count)
    {
        var left = new int[count + 3];
        var right = new int[count + 4];
        var expected = new int[count + 5];
        expected[0] = 123;
        expected[count + 1] = 456;
        for (var i = 0; i < count; i++)
        {
            left[i + 1] = i % 2 == 0 ? int.MaxValue - i : int.MinValue + i;
            right[i + 2] = i % 2 == 0 ? 10 + i * 2 : -10 - i * 2;
            expected[i + 1] = unchecked(left[i + 1] + right[i + 2]);
        }

        var automatic = new int[count + 5];
        var scalar = new int[count + 5];
        automatic[0] = scalar[0] = 123;
        automatic[count + 1] = scalar[count + 1] = 456;
        NumericKernels.AddInt32(left.AsSpan(1, count), right.AsSpan(2, count), automatic.AsSpan(1, count));
        NumericKernels.AddInt32(left.AsSpan(1, count), right.AsSpan(2, count), scalar.AsSpan(1, count), KernelMode.Scalar);

        Assert.Equal(expected, automatic);
        Assert.Equal(expected, scalar);
    }

    [Theory]
    [InlineData(KernelMode.Auto)]
    [InlineData(KernelMode.Scalar)]
    public void AddInt32AllowsExactOutputAliasingAndOverlappingInputs(KernelMode mode)
    {
        var input = Enumerable.Range(0, 19).Select(i => int.MaxValue - i).ToArray();
        var other = Enumerable.Range(0, input.Length).Select(i => 2 * i + 1).ToArray();
        var expected = input.Zip(other, (a, b) => unchecked(a + b)).ToArray();
        var output = (int[])input.Clone();

        NumericKernels.AddInt32(output, other, output, mode);
        Assert.Equal(expected, output);

        other.CopyTo(output, 0);
        NumericKernels.AddInt32(input, output, output, mode);
        Assert.Equal(expected, output);

        input.CopyTo(output, 0);
        NumericKernels.AddInt32(output, output, output, mode);
        Assert.Equal(input.Select(value => unchecked(value + value)), output);

        NumericKernels.AddInt32(input.AsSpan(0, 18), input.AsSpan(1, 18), output.AsSpan(0, 18), mode);
        Assert.Equal(input.Zip(input.Skip(1), (a, b) => unchecked(a + b)), output.Take(18));
    }

    [Theory]
    [InlineData(KernelMode.Auto)]
    [InlineData(KernelMode.Scalar)]
    public void AddInt32RejectsPartialAliasingBeforeWriting(KernelMode mode)
    {
        var values = Enumerable.Range(1, 8).ToArray();
        var other = new int[6];

        Assert.Throws<ArgumentException>("destination", () =>
            NumericKernels.AddInt32(values.AsSpan(0, 6), other, values.AsSpan(1, 6), mode));
        Assert.Throws<ArgumentException>("destination", () =>
            NumericKernels.AddInt32(other, values.AsSpan(1, 6), values.AsSpan(0, 6), mode));
        Assert.Throws<ArgumentException>("destination", () =>
            NumericKernels.AddInt32(values.AsSpan(0, 6), values.AsSpan(1, 6), values.AsSpan(0, 6), mode));
        Assert.Equal(Enumerable.Range(1, 8), values);
    }

    [Fact]
    public void AddInt32ValidatesLengthsAndModeBeforeWriting()
    {
        var input = new[] { 1, 2, 3, 4 };
        var output = new int[4];

        Assert.Throws<ArgumentException>("right", () =>
            NumericKernels.AddInt32(input, input.AsSpan(0, 3), output));
        Assert.Throws<ArgumentException>("destination", () =>
            NumericKernels.AddInt32(input, input, output.AsSpan(0, 3)));
        Assert.Throws<ArgumentOutOfRangeException>("mode", () =>
            NumericKernels.AddInt32(input, input, output, (KernelMode)42));
        Assert.Equal(new int[4], output);
    }

    [Fact]
    public void AutoAddsInt32WithHardwareIntrinsicsDisabledWhenRequested()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic") == "0")
        {
            Assert.False(Vector.IsHardwareAccelerated);
            Assert.False(Vector128.IsHardwareAccelerated);
        }

        var left = Enumerable.Repeat(int.MaxValue, 259).ToArray();
        var right = Enumerable.Range(1, left.Length).ToArray();
        var output = new int[left.Length];
        NumericKernels.AddInt32(left, right, output);
        Assert.Equal(right.Select(value => unchecked(int.MaxValue + value)), output);
    }
}
