namespace SKECS.GA.Tests;

public sealed class GaPackedOutputTests
{
    [Fact]
    public void PreservesRowsWhileCollectingAdditionalOutput()
    {
        using var kernel = new FixtureKernel();
        var result = kernel.BeginDispatchWithOutput([2, 19, 23], 1, additionalOutputWords: 3).Drain();
        Assert.Equal(new uint[] { 19, 23, 2, 1, 9 }, result.Output.ToArray());
        Assert.Equal(2, result.RowCount);
        Assert.Null(result.FindFirstFault());
        Assert.Equal(1, kernel.DispatchCount);
    }

    [Fact]
    public void BoundsAdditionalOutputBeforeDispatch()
    {
        using var kernel = new FixtureKernel();
        Assert.Throws<ArgumentOutOfRangeException>(() => kernel.BeginDispatchWithOutput([1, 19], 1, additionalOutputWords: -1));
        Assert.Throws<ArgumentException>(() => kernel.BeginDispatchWithOutput([0], 1, additionalOutputWords: 1));
        Assert.Throws<ArgumentException>(() => kernel.BeginDispatchWithOutput([2, 19, 23], 1, additionalOutputWords: 15));
        Assert.Equal(0, kernel.DispatchCount);
    }

    private sealed class FixtureKernel : GaPackedKernel
    {
        internal FixtureKernel() : base(64, 1, 64, []) { }

        internal int DispatchCount { get; private set; }

        protected override void Dispatch(uint groups, uint[] input, uint[] output, uint[] faults)
        {
            DispatchCount++;
            Assert.Equal(1u, groups);
            input.AsSpan(1).CopyTo(output);
            output[^3] = 2;
            output[^2] = 1;
            output[^1] = 9;
        }

        protected override void Release() { }
    }
}
