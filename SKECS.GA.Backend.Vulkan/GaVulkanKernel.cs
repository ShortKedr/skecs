namespace SKECS.GA.Backend.Vulkan;

public sealed class GaVulkanKernel : GaPackedKernel
{
    private readonly GaVulkanNative _api;
    private readonly GaVulkanNative.Lease _kernel;

    internal GaVulkanKernel(GaVulkanNative api, GaVulkanNative.Lease kernel, uint workgroupSize,
        uint maxGroups, uint maxBufferBytes, IReadOnlyList<GaSourceSpan> sourceMap, int tickResourceWords, int? bufferRowWords)
        : base(workgroupSize, maxGroups, maxBufferBytes, sourceMap, tickResourceWords, bufferRowWords)
    {
        _api = api;
        _kernel = kernel;
    }

    protected override void Dispatch(uint groups, uint[] input, uint[] output, uint[] faults) =>
        _api.Dispatch(_kernel, groups, input, output, faults);

    protected override void Release() => _kernel.Dispose();
}
