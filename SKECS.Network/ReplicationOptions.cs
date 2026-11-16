namespace SKECS.Network;

public enum ReplicationRole : byte
{
    Authority = 1,
    Replica = 2
}

public enum SnapshotEncoding : byte
{
    Full = 2,
    Delta = 3
}

public enum SnapshotResult
{
    Applied,
    ResyncRequired
}

/// <summary>Local policy for an already connected peer supplied by the host.</summary>
/// <param name="LocalPeerId">The host-assigned identity to announce.</param>
/// <param name="ExpectedPeerId">The identity authorized by the host for this connection.</param>
/// <param name="Role">The authority publishes state. The replica applies and acknowledges it.</param>
/// <param name="ContractId">Application-defined version of the replicated state and its semantics.</param>
public sealed record ReplicationOptions(
    Guid LocalPeerId, Guid ExpectedPeerId, ReplicationRole Role, string ContractId)
{
    public int MaxFrameBytes { get; init; } = 4 * 1024 * 1024;
    public int MaxSlots { get; init; } = 65_536;
    public bool EnableDelta { get; init; } = true;
    public WorldOptions ReplicaWorldOptions { get; init; }
}

public readonly record struct SnapshotNotice(
    SnapshotEncoding Encoding, ulong Sequence, ulong Tick, int FrameBytes);

public readonly record struct SnapshotSendMetrics(
    SnapshotNotice Snapshot, TimeSpan Capture, TimeSpan Encode, TimeSpan Send);
