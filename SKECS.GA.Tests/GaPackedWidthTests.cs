namespace SKECS.GA.Tests;

public sealed class GaPackedWidthTests
{
    [Fact]
    public void RejectsWrongRowWidthBeforeDispatchAndAllowsTheNextValidSubmission()
    {
        using var kernel = new FixtureKernel(2);
        var first = kernel.BeginDispatch([1, 10, 20], 2).Drain();
        Assert.Equal(new uint[] { 10, 20 }, first.Output.ToArray());

        Assert.Throws<ArgumentException>("rowWidthWords", () => kernel.BeginDispatch([1, 99], 1));
        Assert.Equal(1, kernel.DispatchCount);

        var next = kernel.BeginDispatch([1, 30, 40], 2).Drain();
        Assert.Equal(new uint[] { 30, 40 }, next.Output.ToArray());
        Assert.Equal(2, kernel.DispatchCount);
    }

    [Fact]
    public void ExistingConstructorAcceptsDifferentPositiveRowWidths()
    {
        using var kernel = new FixtureKernel();
        Assert.Equal(new uint[] { 10 }, kernel.BeginDispatch([1, 10], 1).Drain().Output.ToArray());
        Assert.Equal(new uint[] { 20, 30 }, kernel.BeginDispatch([1, 20, 30], 2).Drain().Output.ToArray());
    }

    [Fact]
    public void RejectsNegativeExpectedRowWidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>("expectedRowWidthWords", () => new FixtureKernel(-1));
    }

    private sealed class FixtureKernel : GaPackedKernel
    {
        internal FixtureKernel() : base(64, 1, 64, [], 0, null) { }
        internal FixtureKernel(int width) : base(64, 1, 64, [], 0, null, width) { }

        internal int DispatchCount { get; private set; }

        protected override void Dispatch(uint groups, uint[] input, uint[] output, uint[] faults)
        {
            DispatchCount++;
            input.AsSpan(1).CopyTo(output);
        }

        protected override void Release() { }
    }
}
