namespace SKECS.GA.Backend.Metal;

public sealed class GaMetalKernel : GaPackedKernel
{
    private readonly GaMetalNative _api;
    private readonly GaMetalNative.Lease _kernel;

    internal GaMetalKernel(GaMetalNative api, GaMetalNative.Lease kernel, uint workgroupSize,
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
