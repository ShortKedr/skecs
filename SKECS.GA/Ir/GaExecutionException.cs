namespace SKECS.GA;

/// <summary>An entity kernel fault in canonical source order.</summary>
public sealed class GaExecutionException : InvalidOperationException
{
    public GaExecutionException(string systemId, long tickIndex, long sourceOrderOrdinal,
        int sourceId, int sourceGeneration, GaKernelFault kernelFault)
        : base(FormattableString.Invariant($"GA system '{systemId}' faulted with {kernelFault.Kind} at tick {tickIndex}, source {sourceOrderOrdinal}, evaluation {kernelFault.EvaluationOrdinal}."))
    {
        SystemId = systemId;
        TickIndex = tickIndex;
        SourceOrderOrdinal = sourceOrderOrdinal;
        SourceId = sourceId;
        SourceGeneration = sourceGeneration;
        KernelFault = kernelFault;
    }

    public string SystemId { get; }
    public long TickIndex { get; }
    public long SourceOrderOrdinal { get; }
    public int SourceId { get; }
    public int SourceGeneration { get; }
    public GaKernelFault KernelFault { get; }
}
