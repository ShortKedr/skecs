using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace SKECS.GA;

public sealed class GaPackedDispatchResult
{
    public const int FaultStride = 4;
    private readonly uint[] _output;
    private readonly uint[] _faults;
    private readonly IReadOnlyList<GaSourceSpan> _sourceMap;

    internal GaPackedDispatchResult(uint[] output, uint[] faults, IReadOnlyList<GaSourceSpan> sourceMap, long deviceTicks = 0)
    {
        _output = output;
        _faults = faults;
        _sourceMap = sourceMap;
        DeviceTicks = deviceTicks;
    }

    public ReadOnlySpan<uint> Output => _output;
    public ReadOnlySpan<uint> Faults => _faults;
    public int RowCount => _faults.Length / FaultStride;
    internal long DeviceTicks { get; }

    public GaKernelFault? ReadFault(int row)
    {
        if ((uint)row >= RowCount)
            throw new ArgumentOutOfRangeException(nameof(row));
        var words = Faults.Slice(row * FaultStride, FaultStride);
        if (words[0] == 0)
        {
            if (GaPackedWords.HasNonZeroWord(words[1..]))
                throw new InvalidDataException("A successful GA row returned fault metadata.");
            return null;
        }
        var kind = (GaFaultKind)words[0];
        uint site = words[3];
        if (!GaEnum.IsDefined(kind) || site >= _sourceMap.Count)
            throw new InvalidDataException("A GA row returned a fault outside the artifact source map.");
        return new GaKernelFault(kind, words[1] | ((ulong)words[2] << 32), _sourceMap[(int)site]);
    }

    public (int Row, GaKernelFault Fault)? FindFirstFault()
    {
        for (int row = 0; row < RowCount; row++)
            if (ReadFault(row) is { } fault)
                return (row, fault);
        return null;
    }
}

public sealed class GaPackedSubmission
{
    private readonly Task<GaPackedDispatchResult> _completion;
    private readonly bool _canBlock;
    private int _discarded;

    internal GaPackedSubmission(Task<GaPackedDispatchResult> completion, bool canBlock = true)
    {
        _completion = completion;
        _canBlock = canBlock;
    }

    public bool IsCompleted => _completion.IsCompleted;

    public bool TryGetResult([NotNullWhen(true)] out GaPackedDispatchResult? result)
    {
        result = IsCompleted ? Drain() : null;
        return result is not null;
    }

    public GaPackedDispatchResult Drain()
    {
        if (!_canBlock && !IsCompleted)
            throw new InvalidOperationException("This graphics submission requires asynchronous readback.");
        return ReadResult(_completion.GetAwaiter().GetResult());
    }

    public async ValueTask<GaPackedDispatchResult> DrainAsync() =>
        ReadResult(await _completion.ConfigureAwait(false));

    private GaPackedDispatchResult ReadResult(GaPackedDispatchResult result)
    {
        if (Volatile.Read(ref _discarded) != 0)
            throw new OperationCanceledException("The graphics submission was drained and discarded.");
        return result;
    }

    // Browser readback can complete after discard. The kernel retains ownership until then.
    public void Discard()
    {
        Interlocked.Exchange(ref _discarded, 1);
        if (_canBlock || IsCompleted)
            _completion.GetAwaiter().GetResult();
    }
}

public abstract class GaPackedKernel : IGaPreparedKernel, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly uint _workgroupSize;
    private readonly uint _maxGroups;
    private readonly uint _maxBufferBytes;
    private readonly int _tickResourceWords;
    private readonly int? _bufferRowWords;
    private readonly int _expectedRowWidthWords;
    private readonly IReadOnlyList<GaSourceSpan> _sourceMap;
    private Task<GaPackedDispatchResult>? _active;
    private Task? _releaseTask;
    private bool _disposed;

    protected GaPackedKernel(uint workgroupSize, uint maxGroups, uint maxBufferBytes,
        IReadOnlyList<GaSourceSpan> sourceMap)
        : this(workgroupSize, maxGroups, maxBufferBytes, sourceMap, 0)
    {
    }

    protected GaPackedKernel(uint workgroupSize, uint maxGroups, uint maxBufferBytes,
        IReadOnlyList<GaSourceSpan> sourceMap, int tickResourceWords)
        : this(workgroupSize, maxGroups, maxBufferBytes, sourceMap, tickResourceWords, null)
    {
    }

    protected GaPackedKernel(uint workgroupSize, uint maxGroups, uint maxBufferBytes,
        IReadOnlyList<GaSourceSpan> sourceMap, int tickResourceWords, int? bufferRowWords)
        : this(workgroupSize, maxGroups, maxBufferBytes, sourceMap, tickResourceWords, bufferRowWords, 0)
    {
    }

    /// <summary>Prepares a packed kernel with an expected input row width. Zero accepts any positive width.</summary>
    protected GaPackedKernel(uint workgroupSize, uint maxGroups, uint maxBufferBytes,
        IReadOnlyList<GaSourceSpan> sourceMap, int tickResourceWords, int? bufferRowWords, int expectedRowWidthWords)
    {
        if (workgroupSize == 0)
            throw new ArgumentOutOfRangeException(nameof(workgroupSize), workgroupSize, "Value must be non-zero.");
        if (tickResourceWords < 0)
            throw new ArgumentOutOfRangeException(nameof(tickResourceWords), tickResourceWords, "Value must be non-negative.");
        if (bufferRowWords < 0)
            throw new ArgumentOutOfRangeException(nameof(bufferRowWords));
        if (expectedRowWidthWords < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRowWidthWords), expectedRowWidthWords, "Value must be non-negative.");
        if (sourceMap is null)
            throw new ArgumentNullException(nameof(sourceMap));
        _workgroupSize = workgroupSize;
        _maxGroups = maxGroups;
        _maxBufferBytes = maxBufferBytes;
        _sourceMap = sourceMap;
        _tickResourceWords = tickResourceWords;
        _bufferRowWords = bufferRowWords;
        _expectedRowWidthWords = expectedRowWidthWords;
    }

    protected virtual bool SupportsBlockingDispatch => true;

    /// <summary>Waits for the current device submission, including one discarded by the runner.</summary>
    public ValueTask WaitForIdleAsync()
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().FullName);
            return _active is null ? default : new ValueTask(_active);
        }
    }

    public GaPackedSubmission BeginDispatch(
        ReadOnlySpan<uint> input, int rowWidthWords, CancellationToken cancellationToken = default) =>
        BeginDispatchWithOutput(input, rowWidthWords, additionalOutputWords: 0, cancellationToken);

    /// <summary>Submits canonical rows and declared inputs, reserving an optional output section.</summary>
    public GaPackedSubmission BeginDispatchWithOutput(
        ReadOnlySpan<uint> input, int rowWidthWords, int additionalOutputWords, CancellationToken cancellationToken = default) =>
        BeginDispatchWithOutput(input, rowWidthWords, additionalOutputWords, cancellationToken, measureDevice: false);

    internal GaPackedSubmission BeginDispatchWithOutput(
        ReadOnlySpan<uint> input, int rowWidthWords, int additionalOutputWords, CancellationToken cancellationToken,
        bool measureDevice, ReadOnlySpan<uint> snapshot = default)
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().FullName);
            cancellationToken.ThrowIfCancellationRequested();
            if (_active is { IsCompleted: false })
                throw new InvalidOperationException("The previous graphics submission is still pending.");
            if (_active is { IsCompletedSuccessfully: false })
                throw new InvalidOperationException("The graphics kernel failed its previous submission.", _active.Exception);
            if (rowWidthWords <= 0 || input.IsEmpty)
                throw new ArgumentException("Graphics input needs a row count and a positive row width.");
            if (_expectedRowWidthWords > 0 && rowWidthWords != _expectedRowWidthWords)
                throw new ArgumentException("The row width differs from the prepared graphics kernel layout.", nameof(rowWidthWords));
            if (additionalOutputWords < 0)
                throw new ArgumentOutOfRangeException(nameof(additionalOutputWords), additionalOutputWords, "Value must be non-negative.");

            long count = input[0];
            long rowWords = count * rowWidthWords;
            long faultLength = count * GaPackedDispatchResult.FaultStride;
            int tickResourceWords = count == 0 ? 0 : _tickResourceWords;
            long payloadWords = rowWords + 1 + tickResourceWords;
            if (count != 0 && _bufferRowWords is { } bufferStride)
            {
                if (payloadWords >= input.Length || input[(int)payloadWords] > int.MaxValue)
                    throw new ArgumentException("The input is missing a valid read buffer header.", nameof(input));
                payloadWords += 1L + (long)input[(int)payloadWords] * bufferStride;
            }
            if (payloadWords != input.Length)
                throw new ArgumentException("The row count, width and declared inputs do not match the input payload.", nameof(input));
            long outputLength = rowWords + additionalOutputWords;
            long inputLength = (long)input.Length + snapshot.Length;
            var groups = (count + _workgroupSize - 1) / _workgroupSize;
            if (groups > _maxGroups || inputLength > GaPackedWords.MaximumLength || outputLength > GaPackedWords.MaximumLength ||
                faultLength > GaPackedWords.MaximumLength || inputLength * sizeof(uint) > _maxBufferBytes ||
                outputLength * sizeof(uint) > _maxBufferBytes || faultLength * sizeof(uint) > _maxBufferBytes)
                throw new ArgumentException("The payload exceeds the device's compute limits.", nameof(input));

            if (count == 0)
            {
                if (additionalOutputWords != 0 || !snapshot.IsEmpty)
                    throw new ArgumentException("An empty dispatch cannot use additional input or output sections.", nameof(input));
                return new GaPackedSubmission(Task.FromResult(new GaPackedDispatchResult([], [], _sourceMap)));
            }

            var capturedInput = new uint[(int)inputLength];
            input.CopyTo(capturedInput);
            snapshot.CopyTo(capturedInput.AsSpan(input.Length));
            var output = new uint[outputLength];
            var faults = new uint[faultLength];
            bool canBlock = SupportsBlockingDispatch;
            var completion = canBlock
                ? Task.Run(() =>
                {
                    var deviceStart = measureDevice ? Stopwatch.GetTimestamp() : 0;
                    Dispatch((uint)groups, capturedInput, output, faults);
                    var deviceTicks = measureDevice ? Stopwatch.GetTimestamp() - deviceStart : 0;
                    return new GaPackedDispatchResult(output, faults, _sourceMap, deviceTicks);
                })
                : CompleteAsync((uint)groups, capturedInput, output, faults, measureDevice);
            _active = completion;
            return new GaPackedSubmission(completion, canBlock);
        }
    }

    private async Task<GaPackedDispatchResult> CompleteAsync(
        uint groups, uint[] input, uint[] output, uint[] faults, bool measureDevice)
    {
        var deviceStart = measureDevice ? Stopwatch.GetTimestamp() : 0;
        await DispatchAsync(groups, input, output, faults);
        var deviceTicks = measureDevice ? Stopwatch.GetTimestamp() - deviceStart : 0;
        return new GaPackedDispatchResult(output, faults, _sourceMap, deviceTicks);
    }

    public void Dispose()
    {
        bool canBlock = SupportsBlockingDispatch;
        var release = StartRelease(canBlock);
        if (canBlock || release.IsCompleted)
            release.GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync() => new(StartRelease(SupportsBlockingDispatch));

    private Task StartRelease(bool canBlock)
    {
        lock (_gate)
        {
            if (_disposed)
                return _releaseTask ?? Task.CompletedTask;
            _disposed = true;
            var pending = _active;
            _active = null;
            if (!canBlock)
                return _releaseTask = ReleaseAfterReadback(pending);
            try
            {
                pending?.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // The submission retains its failure for polling and drain observers.
            }
            Release();
            return Task.CompletedTask;
        }
    }

    private async Task ReleaseAfterReadback(Task<GaPackedDispatchResult>? pending)
    {
        try
        {
            if (pending is not null)
                await pending;
        }
        catch (Exception)
        {
            // The submission retains its failure for polling and drain observers.
        }
        Release();
    }

    protected abstract void Dispatch(uint groups, uint[] input, uint[] output, uint[] faults);
    protected virtual Task DispatchAsync(uint groups, uint[] input, uint[] output, uint[] faults) =>
        throw new NotSupportedException("This graphics kernel requires blocking dispatch.");
    protected abstract void Release();
}
