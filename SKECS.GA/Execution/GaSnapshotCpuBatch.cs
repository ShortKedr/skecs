namespace SKECS.GA;

/// <summary>Executes a generated CPU kernel against the same immutable snapshot used by a graphics kernel.</summary>
public sealed class GaSnapshotCpuBatch<TInput> : IDeterministicBatchProcessor<TInput> where TInput : unmanaged
{
    public delegate void ExecuteJob(DeterministicJob<TInput> job, GaSnapshotPacket snapshot);

    private readonly GaTickSnapshot.Reader _snapshot;
    private readonly ExecuteJob _execute;

    public GaSnapshotCpuBatch(GaTickSnapshot snapshot, ExecuteJob execute)
        : this(snapshot, execute, null)
    {
    }

    public GaSnapshotCpuBatch(GaTickSnapshot snapshot, ExecuteJob execute, string? snapshotSchema)
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));
        if (execute is null)
            throw new ArgumentNullException(nameof(execute));
        if (snapshotSchema is not null && !GaSnapshotLayout.IsValidSchema(snapshotSchema))
            throw new ArgumentException("Invalid GA snapshot schema.", nameof(snapshotSchema));
        _snapshot = new GaTickSnapshot.Reader(snapshot, snapshotSchema);
        _execute = execute;
    }

    public IDeterministicBatchSubmission<TInput> Begin(DeterministicBatch<TInput> batch) =>
        new Submission(_snapshot.Get(batch.TickIndex, batch.Stage, batch.SystemOrder, batch.KernelOrder), _execute);

    private sealed class Submission(GaSnapshotPacket snapshot, ExecuteJob execute) : IDeterministicBatchSubmission<TInput>
    {
        public bool IsCompleted => true;

        public void Apply(DeterministicBatch<TInput> batch)
        {
            int end = checked(batch.FirstJobId + batch.JobCount);
            for (int job = batch.FirstJobId; job < end; job++)
                execute(batch.GetJob(job), snapshot);
        }

        public void Discard() { }
        public void Dispose() { }
    }
}
