using System.Diagnostics.CodeAnalysis;

namespace SKECS.GA;

public sealed class GaPackedDispatchResult
{
    public const int FaultStride = 4;
    private readonly uint[] _output;
    private readonly uint[] _faults;
    private readonly IReadOnlyList<GaSourceSpan> _sourceMap;

    internal GaPackedDispatchResult(uint[] output, uint[] faults, IReadOnlyList<GaSourceSpan> sourceMap)
    {
        _output = output;
        _faults = faults;
        _sourceMap = sourceMap;
    }

    public ReadOnlySpan<uint> Output => _output;
    public ReadOnlySpan<uint> Faults => _faults;
    public int RowCount => _faults.Length / FaultStride;

    public GaKernelFault? ReadFault(int row)
    {
        if ((uint)row >= RowCount)
            throw new ArgumentOutOfRangeException(nameof(row));
        var words = Faults.Slice(row * FaultStride, FaultStride);
        if (words[0] == 0)
        {
            if (words[1..].IndexOfAnyExcept(0u) >= 0)
                throw new InvalidDataException("A successful GA row returned fault metadata.");
            return null;
        }
        var kind = (GaFaultKind)words[0];
        uint site = words[3];
        if (!Enum.IsDefined(kind) || site >= _sourceMap.Count)
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
    private int _discarded;

    internal GaPackedSubmission(Task<GaPackedDispatchResult> completion) => _completion = completion;

    public bool IsCompleted => _completion.IsCompleted;

    public bool TryGetResult([NotNullWhen(true)] out GaPackedDispatchResult? result)
    {
        result = IsCompleted ? Drain() : null;
        return result is not null;
    }

    public GaPackedDispatchResult Drain()
    {
        var result = _completion.GetAwaiter().GetResult();
        if (Volatile.Read(ref _discarded) != 0)
            throw new OperationCanceledException("The graphics submission was drained and discarded.");
        return result;
    }

    // Commands already running on the GPU drain before their result is discarded.
    public void Discard()
    {
        Interlocked.Exchange(ref _discarded, 1);
        _completion.GetAwaiter().GetResult();
    }
}

public abstract class GaPackedKernel : IGaPreparedKernel
{
    private readonly object _gate = new();
    private readonly uint _workgroupSize;
    private readonly uint _maxGroups;
    private readonly uint _maxBufferBytes;
    private readonly IReadOnlyList<GaSourceSpan> _sourceMap;
    private Task<GaPackedDispatchResult>? _active;
    private bool _disposed;

    protected GaPackedKernel(uint workgroupSize, uint maxGroups, uint maxBufferBytes,
        IReadOnlyList<GaSourceSpan> sourceMap)
    {
        ArgumentOutOfRangeException.ThrowIfZero(workgroupSize);
        ArgumentNullException.ThrowIfNull(sourceMap);
        _workgroupSize = workgroupSize;
        _maxGroups = maxGroups;
        _maxBufferBytes = maxBufferBytes;
        _sourceMap = sourceMap;
    }

    public GaPackedSubmission BeginDispatch(
        ReadOnlySpan<uint> input, int rowWidthWords, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_active is { IsCompleted: false })
                throw new InvalidOperationException("The previous graphics submission is still pending.");
            if (_active is { IsCompletedSuccessfully: false })
                throw new InvalidOperationException("The graphics kernel failed its previous submission.", _active.Exception);
            if (rowWidthWords <= 0 || input.IsEmpty)
                throw new ArgumentException("Graphics input needs a row count and a positive row width.");

            long count = input[0];
            long outputLength = count * rowWidthWords;
            long faultLength = count * GaPackedDispatchResult.FaultStride;
            if (outputLength + 1 != input.Length)
                throw new ArgumentException("The row count and width do not match the input payload.", nameof(input));
            var groups = (count + _workgroupSize - 1) / _workgroupSize;
            if (groups > _maxGroups || faultLength > Array.MaxLength ||
                (long)input.Length * sizeof(uint) > _maxBufferBytes || faultLength * sizeof(uint) > _maxBufferBytes)
                throw new ArgumentException("The payload exceeds the device's compute limits.", nameof(input));

            if (count == 0)
                return new GaPackedSubmission(Task.FromResult(new GaPackedDispatchResult([], [], _sourceMap)));

            var capturedInput = input.ToArray();
            var output = new uint[outputLength];
            var faults = new uint[faultLength];
            var completion = Task.Run(() =>
            {
                Dispatch((uint)groups, capturedInput, output, faults);
                return new GaPackedDispatchResult(output, faults, _sourceMap);
            });
            _active = completion;
            return new GaPackedSubmission(completion);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                _active?.GetAwaiter().GetResult();
            }
            catch (Exception) when (_active is { IsCompletedSuccessfully: false })
            {
                // The submission retains its failure for polling and drain observers.
            }
            finally
            {
                _active = null;
                Release();
            }
        }
    }

    protected abstract void Dispatch(uint groups, uint[] input, uint[] output, uint[] faults);
    protected abstract void Release();
}
