namespace SKECS.GA.Backend.D3D12;

public sealed class GaD3D12Kernel : GaPackedKernel
{
    private readonly GaD3D12Native _api;
    private readonly GaD3D12Native.Lease _kernel;

    internal GaD3D12Kernel(GaD3D12Native api, GaD3D12Native.Lease kernel, uint workgroupSize, uint maxGroups,
        uint maxBufferBytes, IReadOnlyList<GaSourceSpan> sourceMap)
        : base(workgroupSize, maxGroups, maxBufferBytes, sourceMap)
    {
        _api = api;
        _kernel = kernel;
    }

    protected override void Dispatch(uint groups, uint[] input, uint[] output, uint[] faults) =>
        _api.Dispatch(_kernel, groups, input, output, faults);

    protected override void Release() => _kernel.Dispose();
}
