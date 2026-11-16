namespace SKECS.GA;

/// <summary>An entity kernel fault in canonical source order.</summary>
public sealed class GaExecutionException : InvalidOperationException
{
    public GaExecutionException(string systemId, long tickIndex, long sourceOrderOrdinal,
        int sourceId, int sourceGeneration, GaKernelFault kernelFault)
        : this(systemId, tickIndex, default, 0, 0, sourceOrderOrdinal, sourceId, sourceGeneration, kernelFault)
    {
    }

    public GaExecutionException(string systemId, long tickIndex, SystemStage stage, int systemOrder, int kernelOrder,
        long sourceOrderOrdinal, int sourceId, int sourceGeneration, GaKernelFault kernelFault)
        : this(systemId, tickIndex, stage, systemOrder, kernelOrder, sourceOrderOrdinal, sourceId, sourceGeneration,
            kernelFault, null)
    {
    }

    public GaExecutionException(string systemId, long tickIndex, SystemStage stage, int systemOrder, int kernelOrder,
        long sourceOrderOrdinal, int sourceId, int sourceGeneration, GaKernelFault kernelFault,
        DeterministicDomainKey? sourceDomainKey)
        : base(FormattableString.Invariant($"GA system '{systemId}' faulted with {kernelFault.Kind} at tick {tickIndex}, source {sourceOrderOrdinal}, evaluation {kernelFault.EvaluationOrdinal}."))
    {
        SystemId = systemId;
        TickIndex = tickIndex;
        Stage = stage;
        SystemOrder = systemOrder;
        KernelOrder = kernelOrder;
        SourceOrderOrdinal = sourceOrderOrdinal;
        SourceDomainKey = sourceDomainKey;
        SourceId = sourceId;
        SourceGeneration = sourceGeneration;
        KernelFault = kernelFault;
    }

    public string SystemId { get; }
    public long TickIndex { get; }
    public SystemStage Stage { get; }
    public int SystemOrder { get; }
    public int KernelOrder { get; }
    public long SourceOrderOrdinal { get; }
    /// <summary>The declared source order key for the failing row, if the query orders by one.</summary>
    public DeterministicDomainKey? SourceDomainKey { get; }
    public int SourceId { get; }
    public int SourceGeneration { get; }
    public GaKernelFault KernelFault { get; }
}
