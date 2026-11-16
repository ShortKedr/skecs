namespace SKECS.GA.Backend.OpenGl;

public sealed class GaOpenGlKernel : GaPackedKernel
{
    private readonly GaOpenGlNative _api;
    private readonly GaOpenGlNative.Lease _kernel;

    internal GaOpenGlKernel(GaOpenGlNative api, GaOpenGlNative.Lease kernel, uint workgroupSize, uint maxGroups,
        uint maxBufferBytes, IReadOnlyList<GaSourceSpan> sourceMap, int tickResourceWords, int? bufferRowWords)
        : base(workgroupSize, maxGroups, maxBufferBytes, sourceMap, tickResourceWords, bufferRowWords)
    {
        _api = api;
        _kernel = kernel;
    }

    protected override void Dispatch(uint groups, uint[] input, uint[] output, uint[] faults) =>
        _api.Dispatch(_kernel, groups, input, output, faults);

    protected override void Release() => _kernel.Dispose();
}
