namespace SKECS.GA.Tests;

public sealed class GaPackedAsyncTests
{
    [Fact]
    public async Task PendingReadbackCanBePolledAndDrainedAsynchronously()
    {
        await using var kernel = new AsyncKernel();
        var submission = kernel.BeginDispatch([1, 42], 1);
        Assert.False(submission.TryGetResult(out _));
        Assert.Throws<InvalidOperationException>(() => submission.Drain());
        Assert.Throws<InvalidOperationException>(() => kernel.BeginDispatch([1, 15], 1));

        var readback = submission.DrainAsync();
        kernel.Complete();
        var result = await readback;

        Assert.Equal(new uint[] { 42 }, result.Output.ToArray());
        Assert.Equal(1, result.RowCount);
    }

    [Fact]
    public async Task DiscardAndDisposeLeavePendingReadbackOwnedUntilItSettles()
    {
        var kernel = new AsyncKernel();
        var submission = kernel.BeginDispatch([1, 42], 1);
        submission.Discard();
        kernel.Dispose();
        Assert.Equal(0, kernel.Releases);
        Assert.Throws<ObjectDisposedException>(() => kernel.BeginDispatch([1, 15], 1));

        var cleanup = kernel.DisposeAsync();
        kernel.Complete();
        await cleanup;
        await Assert.ThrowsAsync<OperationCanceledException>(() => submission.DrainAsync().AsTask());
        Assert.Equal(1, kernel.Releases);
    }

    [Fact]
    public async Task AFailedReadbackRemainsObservableAfterDisposal()
    {
        var kernel = new AsyncKernel();
        var submission = kernel.BeginDispatch([1, 42], 1);
        kernel.Dispose();
        var failure = new InvalidDataException("readback failed");
        kernel.Fail(failure);

        await kernel.DisposeAsync();
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidDataException>(() => submission.DrainAsync().AsTask()));
        Assert.Equal(1, kernel.Releases);
    }

    private sealed class AsyncKernel : GaPackedKernel
    {
        private readonly TaskCompletionSource _readback = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal AsyncKernel() : base(64, 1, 64, []) { }

        internal int Releases { get; private set; }
        internal void Complete() => _readback.SetResult();
        internal void Fail(Exception failure) => _readback.SetException(failure);

        protected override bool SupportsBlockingDispatch => false;
        protected override void Dispatch(uint groups, uint[] input, uint[] output, uint[] faults) =>
            throw new NotSupportedException();
        protected override async Task DispatchAsync(uint groups, uint[] input, uint[] output, uint[] faults)
        {
            await _readback.Task;
            output[0] = input[1];
        }
        protected override void Release() => Releases++;
    }
}
