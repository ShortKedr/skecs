namespace SKECS.Serialization;

public sealed class CheckpointLimits
{
    public int MaxSlots { get; set; } = 4_000_000;
    public int MaxSchemas { get; set; } = 1024;
    public int MaxFields { get; set; } = 256;
    public int MaxRowsPerBlock { get; set; } = 256;
    public int MaxExtensions { get; set; } = 128;
    public int MaxFrameBytes { get; set; } = 4 * 1024 * 1024;
    public int MaxValueBytes { get; set; } = 1024 * 1024;
    public int MaxDepth { get; set; } = 8;
    public long MaxComponentValues { get; set; } = 64_000_000;
    public long MaxDocumentBytes { get; set; } = 1024L * 1024 * 1024;
    public long MaxWorkingBytes { get; set; } = 256L * 1024 * 1024;

    internal CheckpointLimits Copy()
    {
        var limits = (CheckpointLimits)MemberwiseClone();
        if (limits.MaxSlots < 0 || limits.MaxSchemas < 0 || limits.MaxFields < 0 ||
            limits.MaxRowsPerBlock <= 0 || limits.MaxExtensions < 0 || limits.MaxFrameBytes <= 0 ||
            limits.MaxValueBytes < 0 || limits.MaxDepth <= 0 || limits.MaxComponentValues < 0 ||
            limits.MaxDocumentBytes <= 0 || limits.MaxWorkingBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(CheckpointLimits), "Invalid checkpoint limit.");
        return limits;
    }
}

public sealed class CheckpointWriteOptions
{
    public CheckpointLimits Limits { get; set; } = new();
    public IReadOnlyList<CheckpointExtension> Extensions { get; set; } = Array.Empty<CheckpointExtension>();
}

public sealed class CheckpointReadOptions
{
    public CheckpointLimits Limits { get; set; } = new();
    public WorldOptions WorldOptions { get; set; }
    /// <summary>Requires the checkpoint schema to match the entire registry before loading state or planning migrations.</summary>
    public bool RequireExactSchema { get; set; }
    public bool AllowUnknown { get; set; }
    public Action<string>? OnOmitted { get; set; }
    public IReadOnlyDictionary<uint, uint> Extensions { get; set; } = new Dictionary<uint, uint>();
    public IReadOnlyCollection<uint> RequiredExtensions { get; set; } = Array.Empty<uint>();
}

/// <summary>A versioned host-owned payload. Publish the decoded host state and world together.</summary>
public sealed class CheckpointExtension
{
    public CheckpointExtension(uint id, uint version, bool required, byte[] data)
    {
        if (id == 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (version == 0) throw new ArgumentOutOfRangeException(nameof(version));
        Id = id;
        Version = version;
        Required = required;
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public uint Id { get; }
    public uint Version { get; }
    public bool Required { get; }
    public byte[] Data { get; }
}
