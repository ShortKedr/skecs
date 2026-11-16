using System.Diagnostics;

namespace SKECS.GA;

/// <summary>Timing for one successfully applied graphics batch, in <see cref="Stopwatch.GetTimestamp"/> units.</summary>
/// <remarks>Device time covers the native upload, dispatch, fence wait, and readback call.</remarks>
public readonly record struct GaBatchTimings(int Rows, long PackTicks, long DeviceTicks, long ApplyTicks);

/// <summary>Executes a range of canonical runner jobs through a prepared graphics kernel.</summary>
/// <remarks>Keep the startup result alive until the runner and its pending submissions have drained.</remarks>
public class GaBatchProcessor<TInput> : IDeterministicBatchProcessor<TInput> where TInput : unmanaged
{
    public delegate void PackJob(DeterministicBatchReadJob<TInput> job, Span<uint> destination);
    public delegate void ApplyJob(DeterministicJob<TInput> job, int successfulRows, ReadOnlySpan<uint> successfulPrefix);

    private readonly GaPackedKernel _kernel;
    private readonly string _systemId;
    private readonly int _wordsPerRow;
    private readonly int _eventCapacity;
    private readonly PackJob _pack;
    private readonly ApplyJob _apply;
    private readonly Action<GaBatchTimings>? _observe;
    private readonly GaTickSnapshot.Reader? _snapshot;

    /// <summary>Binds the prepared artifact to the identity and methods emitted with its row codec.</summary>
    public GaBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply)
        : this(startup, systemId, sourceDigest, irDigest, componentSchemaDigest, wordsPerRow, pack, apply, 0)
    {
    }

    public GaBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply, int eventCapacity)
        : this(startup, systemId, sourceDigest, irDigest, componentSchemaDigest, wordsPerRow, pack, apply, eventCapacity,
            observe: null)
    {
    }

    /// <summary>Binds a generated row codec and optionally reports timing after each successfully applied batch.</summary>
    public GaBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply, int eventCapacity,
        Action<GaBatchTimings>? observe = null, GaTickSnapshot? snapshot = null)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wordsPerRow);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(apply);
        if ((uint)eventCapacity > GaEventPacket.MaximumCapacityPerSource)
            throw new ArgumentOutOfRangeException(nameof(eventCapacity));

        var decision = startup.Decisions.SingleOrDefault(item => item.Contract.SystemId == systemId);
        if (!startup.Succeeded || decision?.Route != GaRoute.Gpu)
            throw new InvalidOperationException($"GA startup did not select a GPU kernel for '{systemId}'.");

        var contract = decision.Contract;
        if (contract.SourceDigest != sourceDigest || contract.IrDigest != irDigest ||
            contract.ComponentSchemaDigest != componentSchemaDigest || contract.NumericVersion != GaProgram.NumericVersion ||
            contract.SubsetVersion != GaProgram.SubsetVersion || contract.IrVersion != GaProgram.IrVersion ||
            contract.EventCapacity != eventCapacity || contract.UsesSnapshot != (snapshot is not null))
            throw new InvalidDataException($"The generated row codec does not match the prepared GA artifact for '{systemId}'.");

        _kernel = startup.GetPreparedKernel(systemId) as GaPackedKernel
            ?? throw new InvalidOperationException($"The prepared GA kernel for '{systemId}' cannot execute packed rows.");
        _systemId = systemId;
        _wordsPerRow = wordsPerRow;
        _eventCapacity = eventCapacity;
        _pack = pack;
        _apply = apply;
        _observe = observe;
        _snapshot = snapshot is null ? null : new GaTickSnapshot.Reader(snapshot);
    }

    public IDeterministicBatchSubmission<TInput> Begin(DeterministicBatch<TInput> batch)
    {
        var measure = _observe is not null;
        var packStart = measure ? Stopwatch.GetTimestamp() : 0;
        var snapshot = _snapshot?.Get(batch.TickIndex);
        var endJobId = checked(batch.FirstJobId + batch.JobCount);
        var rowCount = 0;
        for (var jobId = batch.FirstJobId; jobId < endJobId; jobId++)
            rowCount = checked(rowCount + batch.GetReadJob(jobId).Count);

        var payload = new uint[checked(1 + rowCount * _wordsPerRow)];
        payload[0] = (uint)rowCount;
        var wordOffset = 1;
        for (var jobId = batch.FirstJobId; jobId < endJobId; jobId++)
        {
            var job = batch.GetReadJob(jobId);
            var wordCount = checked(job.Count * _wordsPerRow);
            _pack(job, payload.AsSpan(wordOffset, wordCount));
            wordOffset += wordCount;
        }

        var packTicks = measure ? Stopwatch.GetTimestamp() - packStart : 0;
        var pending = new Submission(this, batch, rowCount, packTicks);
        pending.SetDeviceSubmission(_kernel.BeginDispatchWithOutput(payload, _wordsPerRow, EventWords(rowCount),
            cancellationToken: default, measureDevice: measure, snapshot: snapshot is null ? default : snapshot.Words));
        return pending;
    }

    private int EventWords(int rowCount) =>
        _eventCapacity == 0 || rowCount == 0 ? 0 : checked(GaEventPacket.HeaderWords + rowCount * (1 + _eventCapacity));

    private static int DecodeInt32Event(ReadOnlySpan<uint> words) => unchecked((int)words[0]);

    private sealed class Submission : IDeterministicBatchSubmission<TInput>
    {
        private readonly GaBatchProcessor<TInput> _owner;
        private readonly int _firstJobId;
        private readonly int _jobCount;
        private readonly long _tickIndex;
        private readonly SystemStage _stage;
        private readonly int _systemOrder;
        private readonly int _rowCount;
        private readonly long _packTicks;
        private GaPackedSubmission? _deviceSubmission;

        internal Submission(GaBatchProcessor<TInput> owner, DeterministicBatch<TInput> batch, int rowCount, long packTicks)
        {
            _owner = owner;
            _firstJobId = batch.FirstJobId;
            _jobCount = batch.JobCount;
            _tickIndex = batch.TickIndex;
            _stage = batch.Stage;
            _systemOrder = batch.SystemOrder;
            _rowCount = rowCount;
            _packTicks = packTicks;
        }

        internal void SetDeviceSubmission(GaPackedSubmission submission) => _deviceSubmission = submission;

        public bool IsCompleted => GetDeviceSubmission().IsCompleted;

        public void Apply(DeterministicBatch<TInput> batch)
        {
            if (batch.FirstJobId != _firstJobId || batch.JobCount != _jobCount || batch.TickIndex != _tickIndex ||
                batch.Stage != _stage || batch.SystemOrder != _systemOrder)
                throw new InvalidOperationException("The runner batch does not match the submitted graphics work.");

            var submission = GetDeviceSubmission();
            if (!submission.IsCompleted)
                throw new InvalidOperationException("The graphics submission is still pending.");

            // The fence has completed even if readback raises an error. Consume the result once.
            _deviceSubmission = null;
            var result = submission.Drain();
            var observe = _owner._observe;
            var applyStart = observe is null ? 0 : Stopwatch.GetTimestamp();
            var wordsPerRow = _owner._wordsPerRow;
            int rowWords = checked(_rowCount * wordsPerRow);
            int eventWords = _owner.EventWords(_rowCount);
            if (result.RowCount != _rowCount || result.Output.Length != checked(rowWords + eventWords))
                throw new InvalidDataException("Graphics readback size differs from the submitted canonical rows.");
            GaEventPacket events = default;
            if (eventWords != 0)
            {
                events = new GaEventPacket(result.Output[rowWords..]);
                if (events.SourceCount != _rowCount || events.CapacityPerSource != _owner._eventCapacity || events.EventWords != 1)
                    throw new InvalidDataException("Graphics event readback does not match its declared Int32 stream.");
            }

            var firstFault = result.FindFirstFault();
            var successfulRows = firstFault?.Row ?? _rowCount;
            var endJobId = _firstJobId + _jobCount;
            var rowOffset = 0;
            for (var jobId = _firstJobId; jobId < endJobId; jobId++)
            {
                var job = batch.GetJob(jobId);
                var prefix = Math.Min(job.Count, successfulRows - rowOffset);
                if (prefix != 0)
                {
                    _owner._apply(job, prefix, result.Output.Slice(rowOffset * wordsPerRow, prefix * wordsPerRow));
                    if (eventWords != 0)
                        events.ApplyEntityRows<TInput, int>(job, rowOffset, prefix, DecodeInt32Event);
                }
                if (prefix < job.Count && firstFault is { } failure)
                {
                    var entity = job.GetEntity(prefix);
                    var ordinal = (long)job.JobId * DeterministicRunner<TInput>.LogicalJobSize + prefix;
                    throw new GaExecutionException(_owner._systemId, batch.TickIndex, ordinal, entity.Id, entity.Version, failure.Fault);
                }
                rowOffset += job.Count;
            }

            if (observe is not null)
                observe(new GaBatchTimings(_rowCount, _packTicks, result.DeviceTicks, Stopwatch.GetTimestamp() - applyStart));
        }

        public void Discard()
        {
            var submission = _deviceSubmission;
            _deviceSubmission = null;
            submission?.Discard();
        }

        public void Dispose() => Discard();

        private GaPackedSubmission GetDeviceSubmission() =>
            _deviceSubmission ?? throw new ObjectDisposedException(nameof(Submission));
    }
}
