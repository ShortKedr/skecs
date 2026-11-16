namespace SKECS.RuntimeProfiles;

/// <summary>Churn control settings supported by profile schema 1.</summary>
public sealed class ChurnProfileSettings
{
    private readonly ChurnStartupBlock[] _startupBlocks;

    public ChurnProfileSettings(long TargetRetainedBytes, long StartupReserveBytes,
        ChurnStartupBlock[] StartupBlocks, int InitialEntityCapacity, int MaxOversizePercent, int ReleaseBatchSize)
    {
        if (StartupBlocks == null) throw new ArgumentNullException(nameof(StartupBlocks));
        this.TargetRetainedBytes = TargetRetainedBytes;
        this.StartupReserveBytes = StartupReserveBytes;
        _startupBlocks = (ChurnStartupBlock[])StartupBlocks.Clone();
        this.InitialEntityCapacity = InitialEntityCapacity;
        this.MaxOversizePercent = MaxOversizePercent;
        this.ReleaseBatchSize = ReleaseBatchSize;
    }

    public long TargetRetainedBytes { get; }
    public long StartupReserveBytes { get; }
    public ChurnStartupBlock[] StartupBlocks => (ChurnStartupBlock[])_startupBlocks.Clone();
    public int InitialEntityCapacity { get; }
    public int MaxOversizePercent { get; }
    public int ReleaseBatchSize { get; }

    internal ChurnControlOptions CreateOptions(int chunkCapacity)
    {
        var options = new ChurnControlOptions(TargetRetainedBytes, StartupReserveBytes, _startupBlocks,
            InitialEntityCapacity, MaxOversizePercent, ReleaseBatchSize);
        if (StartupReserveBytes > TargetRetainedBytes)
            throw new ArgumentException("The startup reserve exceeds the target retained bytes.", nameof(StartupReserveBytes));

        var remaining = StartupReserveBytes;
        foreach (var block in _startupBlocks)
        {
            var bytesPerBlock = block.NativeBytes + 2L * sizeof(int) * chunkCapacity;
            if (block.Count > remaining / bytesPerBlock)
                throw new ArgumentException("Startup blocks exceed the reserve including pinned ID and version arrays.", nameof(StartupBlocks));
            remaining -= block.Count * bytesPerBlock;
        }
        return options;
    }
}
