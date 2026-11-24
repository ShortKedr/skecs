namespace SKECS.GA;

/// <summary>Runs generated CPU jobs against the exact packet supplied to the GPU route.</summary>
public sealed class GaBufferCpuBatch<TInput> : IDeterministicBatchProcessor<TInput> where TInput : unmanaged
{
    public delegate void ExecuteJob(DeterministicJob<TInput> job, GaBufferPacket buffer, GaSnapshotPacket? snapshot);

    private readonly GaTickBuffer.Reader _buffer;
    private readonly GaTickSnapshot.Reader? _snapshot;
    private readonly ExecuteJob _execute;

    public GaBufferCpuBatch(GaTickBuffer buffer, string schema, int rowWords, ExecuteJob execute,
        GaTickSnapshot? snapshot = null, string? snapshotSchema = null)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));
        if (schema is null)
            throw new ArgumentNullException(nameof(schema));
        if (string.IsNullOrWhiteSpace(schema))
            throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", nameof(schema));
        if (rowWords < 0)
            throw new ArgumentOutOfRangeException(nameof(rowWords), rowWords, "Value must be non-negative.");
        if (execute is null)
            throw new ArgumentNullException(nameof(execute));
        if (snapshotSchema is not null && (snapshot is null || !GaSnapshotLayout.IsValidSchema(snapshotSchema)))
            throw new ArgumentException("A snapshot schema requires a matching snapshot binding.", nameof(snapshotSchema));
        _buffer = new GaTickBuffer.Reader(buffer, schema, rowWords);
        _snapshot = snapshot is null ? null : new GaTickSnapshot.Reader(snapshot, snapshotSchema);
        _execute = execute;
    }

    public IDeterministicBatchSubmission<TInput> Begin(DeterministicBatch<TInput> batch) =>
        new Submission(_buffer.Get(batch.TickIndex),
            _snapshot?.Get(batch.TickIndex, batch.Stage, batch.SystemOrder, batch.KernelOrder), _execute);

    private sealed class Submission(GaBufferPacket buffer, GaSnapshotPacket? snapshot, ExecuteJob execute)
        : IDeterministicBatchSubmission<TInput>
    {
        public bool IsCompleted => true;

        public void Apply(DeterministicBatch<TInput> batch)
        {
            int end = checked(batch.FirstJobId + batch.JobCount);
            for (int job = batch.FirstJobId; job < end; job++)
                execute(batch.GetJob(job), buffer, snapshot);
        }

        public void Discard() { }
        public void Dispose() { }
    }
}
