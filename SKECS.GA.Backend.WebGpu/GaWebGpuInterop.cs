using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;

namespace SKECS.GA.Backend.WebGpu;

[SupportedOSPlatform("browser")]
internal static partial class GaWebGpuInterop
{
    internal const string ModuleName = "skecs.ga.webgpu";

    [JSImport("openDevice", ModuleName)]
    internal static partial Task<string> OpenDevice();

    [JSImport("prepareKernel", ModuleName)]
    internal static partial Task<int> PrepareKernel(
        int device, string source, string entryPoint, int workgroup, int input, int output, int faults);

    [JSImport("dispatchKernel", ModuleName)]
    [return: JSMarshalAs<JSType.Promise<JSType.Object>>]
    private static partial Task<JSObject> DispatchKernel(
        int kernel, int groups, [JSMarshalAs<JSType.Array<JSType.Number>>] int[] input, int outputLength, int faultLength);

    [JSImport("readKernelResult", ModuleName)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial int[] ReadKernelResult(JSObject result);

    [JSImport("releaseKernel", ModuleName)]
    internal static partial void ReleaseKernel(int kernel);

    [JSImport("releaseDevice", ModuleName)]
    internal static partial void ReleaseDevice(int device);

    internal static async Task Dispatch(int kernel, int groups, uint[] input, uint[] output, uint[] faults)
    {
        int[] argument = MemoryMarshal.Cast<uint, int>(input).ToArray();
        using var result = await DispatchKernel(kernel, groups, argument, output.Length, faults.Length);
        var words = ReadKernelResult(result);
        if (words.Length != (long)output.Length + faults.Length)
            throw new InvalidDataException("WebGPU returned an invalid packed readback length.");
        MemoryMarshal.Cast<int, uint>(words.AsSpan(0, output.Length)).CopyTo(output);
        MemoryMarshal.Cast<int, uint>(words.AsSpan(output.Length)).CopyTo(faults);
    }
}
