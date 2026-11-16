using System;

namespace SKECS;

/// <summary>Opt-in settings for retention and reuse of fully empty chunk backings.</summary>
public sealed class ChurnControlOptions
{
    private readonly ChurnStartupBlock[] _startupBlocks;

    public ChurnControlOptions(long targetRetainedBytes, long startupReserveBytes = 0,
        ChurnStartupBlock[]? startupBlocks = null, int initialEntityCapacity = 0,
        int maxOversizePercent = 25, int releaseBatchSize = 4)
    {
        if (targetRetainedBytes < 0) throw new ArgumentOutOfRangeException(nameof(targetRetainedBytes));
        if (startupReserveBytes < 0) throw new ArgumentOutOfRangeException(nameof(startupReserveBytes));
        if (initialEntityCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialEntityCapacity));
        if (maxOversizePercent < 0)
            throw new ArgumentOutOfRangeException(nameof(maxOversizePercent));
        if (releaseBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(releaseBatchSize));

        _startupBlocks = startupBlocks == null || startupBlocks.Length == 0
            ? Array.Empty<ChurnStartupBlock>() : (ChurnStartupBlock[])startupBlocks.Clone();
        if ((startupReserveBytes == 0) != (_startupBlocks.Length == 0))
            throw new ArgumentException("A startup reserve requires both a byte allowance and block sizes.", nameof(startupBlocks));
        foreach (var block in _startupBlocks)
            if (block.NativeBytes <= 0 || block.Count <= 0)
                throw new ArgumentException("Startup blocks require a positive native size and count.", nameof(startupBlocks));

        TargetRetainedBytes = targetRetainedBytes;
        StartupReserveBytes = startupReserveBytes;
        InitialEntityCapacity = initialEntityCapacity;
        MaxOversizePercent = maxOversizePercent;
        ReleaseBatchSize = releaseBatchSize;
    }

    /// <summary>Soft target for estimated world-owned storage.</summary>
    public long TargetRetainedBytes { get; }

    /// <summary>Maximum total native and pinned payload of the startup blocks.</summary>
    public long StartupReserveBytes { get; }

    /// <summary>Initial capacity for the entity records, reusable IDs, and subsequently opened accessors.</summary>
    public int InitialEntityCapacity { get; }

    /// <summary>Largest allowed excess native capacity as a percentage of the requested native size.</summary>
    public int MaxOversizePercent { get; }

    /// <summary>Maximum number of empty backings released by one maintenance pass.</summary>
    public int ReleaseBatchSize { get; }

    /// <summary>The requested startup backing sizes and counts.</summary>
    public ReadOnlySpan<ChurnStartupBlock> StartupBlocks => _startupBlocks;
}

/// <summary>One size and count of native allocation to prepare, each with its own world-capacity ID arrays.</summary>
public readonly struct ChurnStartupBlock
{
    public ChurnStartupBlock(int nativeBytes, int count)
    {
        if (nativeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(nativeBytes));
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        NativeBytes = nativeBytes;
        Count = count;
    }

    public int NativeBytes { get; }
    public int Count { get; }
}
