namespace SKECS;

public readonly struct WorldOptions
{
    public WorldOptions() : this(256, false) { }

    public WorldOptions(int chunkCapacity = 256, bool diagnosticsEnabled = false)
        : this(chunkCapacity, diagnosticsEnabled, null) { }

    private WorldOptions(int chunkCapacity, bool diagnosticsEnabled, ChurnControlOptions? churnControl)
    {
        ChunkCapacity = chunkCapacity <= 0 ? 256 : chunkCapacity;
        DiagnosticsEnabled = diagnosticsEnabled;
        ChurnControl = churnControl;
    }

    public int ChunkCapacity { get; }

    public bool DiagnosticsEnabled { get; }

    public ChurnControlOptions? ChurnControl { get; }

    public WorldOptions WithChurnControl(ChurnControlOptions churnControl) =>
        new WorldOptions(ChunkCapacity, DiagnosticsEnabled,
            churnControl ?? throw new System.ArgumentNullException(nameof(churnControl)));
}
