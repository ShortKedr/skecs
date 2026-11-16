namespace SKECS.GA;

/// <summary>Executes a range of canonical runner jobs through a prepared graphics kernel.</summary>
/// <remarks>Keep the startup result alive until the runner and its pending submissions have drained.</remarks>
public class GaBatchProcessor<TInput> : IDeterministicBatchProcessor<TInput> where TInput : unmanaged
{
    public delegate void PackJob(DeterministicBatchReadJob<TInput> job, Span<uint> destination);
    public delegate void ApplyJob(DeterministicJob<TInput> job, int successfulRows, ReadOnlySpan<uint> successfulPrefix);

    private readonly GaPackedKernel _kernel;
    private readonly string _systemId;
    private readonly int _wordsPerRow;
    private readonly PackJob _pack;
    private readonly ApplyJob _apply;

    /// <summary>Binds the prepared artifact to the identity and methods emitted with its row codec.</summary>
    public GaBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wordsPerRow);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(apply);

        var decision = startup.Decisions.SingleOrDefault(item => item.Contract.SystemId == systemId);
        if (!startup.Succeeded || decision?.Route != GaRoute.Gpu)
            throw new InvalidOperationException($"GA startup did not select a GPU kernel for '{systemId}'.");

        var contract = decision.Contract;
        if (contract.SourceDigest != sourceDigest || contract.IrDigest != irDigest ||
            contract.ComponentSchemaDigest != componentSchemaDigest || contract.NumericVersion != GaProgram.NumericVersion ||
            contract.SubsetVersion != GaProgram.SubsetVersion || contract.IrVersion != GaProgram.IrVersion)
            throw new InvalidDataException($"The generated row codec does not match the prepared GA artifact for '{systemId}'.");

        _kernel = startup.GetPreparedKernel(systemId) as GaPackedKernel
            ?? throw new InvalidOperationException($"The prepared GA kernel for '{systemId}' cannot execute packed rows.");
        _systemId = systemId;
        _wordsPerRow = wordsPerRow;
        _pack = pack;
        _apply = apply;
    }

    public IDeterministicBatchSubmission<TInput> Begin(DeterministicBatch<TInput> batch)
    {
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

        var pending = new Submission(this, batch, rowCount);
        pending.SetDeviceSubmission(_kernel.BeginDispatch(payload, _wordsPerRow));
        return pending;
    }

    private sealed class Submission : IDeterministicBatchSubmission<TInput>
    {
        private readonly GaBatchProcessor<TInput> _owner;
        private readonly int _firstJobId;
        private readonly int _jobCount;
        private readonly long _tickIndex;
        private readonly SystemStage _stage;
        private readonly int _systemOrder;
        private readonly int _rowCount;
        private GaPackedSubmission? _deviceSubmission;

        internal Submission(GaBatchProcessor<TInput> owner, DeterministicBatch<TInput> batch, int rowCount)
        {
            _owner = owner;
            _firstJobId = batch.FirstJobId;
            _jobCount = batch.JobCount;
            _tickIndex = batch.TickIndex;
            _stage = batch.Stage;
            _systemOrder = batch.SystemOrder;
            _rowCount = rowCount;
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
            var wordsPerRow = _owner._wordsPerRow;
            if (result.RowCount != _rowCount || result.Output.Length != checked(_rowCount * wordsPerRow))
                throw new InvalidDataException("Graphics readback size differs from the submitted canonical rows.");

            var firstFault = result.FindFirstFault();
            var successfulRows = firstFault?.Row ?? _rowCount;
            var endJobId = _firstJobId + _jobCount;
            var rowOffset = 0;
            for (var jobId = _firstJobId; jobId < endJobId; jobId++)
            {
                var job = batch.GetJob(jobId);
                var prefix = Math.Min(job.Count, successfulRows - rowOffset);
                if (prefix != 0)
                    _owner._apply(job, prefix, result.Output.Slice(rowOffset * wordsPerRow, prefix * wordsPerRow));
                if (prefix < job.Count && firstFault is { } failure)
                {
                    var entity = job.GetEntity(prefix);
                    var ordinal = (long)job.JobId * DeterministicRunner<TInput>.LogicalJobSize + prefix;
                    throw new GaExecutionException(_owner._systemId, batch.TickIndex, ordinal, entity.Id, entity.Version, failure.Fault);
                }
                rowOffset += job.Count;
            }
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
