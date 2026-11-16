namespace SKECS.GA;

/// <summary>Access to operations bound by a compiled GA system.</summary>
public readonly ref struct SystemContext
{
    /// <summary>Emits an ordered signed 32-bit event for the current entity.</summary>
    public void Emit(int value) =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to emit events.");

    /// <summary>Reads the bound Int32 snapshot, returning the fallback for an absent or stale key.</summary>
    public int ReadSnapshotInt32(int entityId, int generation, int fallback) =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to read snapshots.");
}
