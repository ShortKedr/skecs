namespace SKECS;

/// <summary>Estimated world storage payload and cumulative physical chunk activity.</summary>
/// <remarks>The estimate includes native chunk allocations, pinned ID and version arrays,
/// and entity record, free ID and accessor table payload. Runtime and allocator overhead is excluded.</remarks>
public readonly struct ChurnControlStatistics
{
    internal ChurnControlStatistics(bool enabled, long targetRetainedBytes, long livePayloadBytes,
        long occupiedChunkCapacityBytes, long reusableEmptyBytes, long tableBytes,
        long allocationCount, long reuseCount, long releaseCount)
    {
        Enabled = enabled;
        TargetRetainedBytes = targetRetainedBytes;
        LivePayloadBytes = livePayloadBytes;
        OccupiedChunkCapacityBytes = occupiedChunkCapacityBytes;
        ReusableEmptyBytes = reusableEmptyBytes;
        NonReclaimableBytes = occupiedChunkCapacityBytes + tableBytes;
        RetainedBytes = NonReclaimableBytes + reusableEmptyBytes;
        AllocationCount = allocationCount;
        ReuseCount = reuseCount;
        ReleaseCount = releaseCount;
    }

    public bool Enabled { get; }
    public long TargetRetainedBytes { get; }
    public long LivePayloadBytes { get; }
    public long OccupiedChunkCapacityBytes { get; }
    public long ReusableEmptyBytes { get; }
    public long NonReclaimableBytes { get; }
    public long RetainedBytes { get; }
    public long AllocationCount { get; }
    public long ReuseCount { get; }
    public long ReleaseCount { get; }
    public long OverBudgetBytes => Enabled ? System.Math.Max(0, RetainedBytes - TargetRetainedBytes) : 0;
    public long UnreclaimableOverBudgetBytes => Enabled ? System.Math.Max(0, NonReclaimableBytes - TargetRetainedBytes) : 0;
}
