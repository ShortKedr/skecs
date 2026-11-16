namespace SKECS.GA.Backend.Vulkan;

/// <summary>Executes canonical runner jobs through a prepared Vulkan kernel.</summary>
public sealed class GaVulkanBatchProcessor<TInput> : GaBatchProcessor<TInput> where TInput : unmanaged
{
    public GaVulkanBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply)
        : base(startup, systemId, sourceDigest, irDigest, componentSchemaDigest, wordsPerRow, pack, apply)
    {
        if (startup.GetPreparedKernel(systemId) is not GaVulkanKernel)
            throw new InvalidOperationException($"The prepared GA kernel for '{systemId}' is not a Vulkan kernel.");
    }
}
