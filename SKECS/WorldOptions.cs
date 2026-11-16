namespace SKECS;

public readonly struct WorldOptions
{
    public WorldOptions() : this(256, false) { }

    public WorldOptions(int chunkCapacity = 256, bool diagnosticsEnabled = false)
        : this(chunkCapacity, diagnosticsEnabled, null, null) { }

    private WorldOptions(int chunkCapacity, bool diagnosticsEnabled, ChurnControlOptions? churnControl, ComponentPlacementOptions? componentPlacement)
    {
        ChunkCapacity = chunkCapacity <= 0 ? 256 : chunkCapacity;
        DiagnosticsEnabled = diagnosticsEnabled;
        ChurnControl = churnControl;
        ComponentPlacement = componentPlacement;
    }

    public int ChunkCapacity { get; }

    public bool DiagnosticsEnabled { get; }

    public ChurnControlOptions? ChurnControl { get; }

    public ComponentPlacementOptions? ComponentPlacement { get; }

    public WorldOptions WithChurnControl(ChurnControlOptions churnControl) =>
        new WorldOptions(ChunkCapacity, DiagnosticsEnabled,
            churnControl ?? throw new System.ArgumentNullException(nameof(churnControl)), ComponentPlacement);

    public WorldOptions WithComponentPlacement(ComponentPlacementOptions componentPlacement) =>
        new WorldOptions(ChunkCapacity, DiagnosticsEnabled, ChurnControl,
            componentPlacement ?? throw new System.ArgumentNullException(nameof(componentPlacement)));
}
