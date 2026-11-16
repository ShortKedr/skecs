namespace SKECS.GA.Backend.D3D11;

public sealed class GaD3D11Kernel : GaPackedKernel
{
    private readonly GaD3D11Native _api;
    private readonly GaD3D11Native.Lease _kernel;

    internal GaD3D11Kernel(GaD3D11Native api, GaD3D11Native.Lease kernel, uint workgroupSize, uint maxGroups,
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
