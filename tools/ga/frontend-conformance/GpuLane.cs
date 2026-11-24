using System.Diagnostics;
using SKECS.GA;
using SKECS.GA.Backend.D3D12;
using SKECS.GA.Backend.Vulkan;

internal sealed record GpuRow(string Label, uint[] Input, uint[] Output, GaKernelFault? Fault);

internal sealed class GpuLane(string backend, IDisposable owner, IGaDevice device,
    string slang, string tools, string output) : IDisposable
{
    private const string Contract = "fixtures.ga.frontend/1";

    internal string Backend => backend;

    internal static GpuLane Open(string backend, string probeDirectory, string toolchain, string outputFolder, int index)
    {
        string slang = Path.Combine(probeDirectory, "ga-slang-probe.exe");
        return backend switch
        {
            "vulkan" => OpenVulkan(),
            "d3d12" => OpenD3D12(),
            _ => throw new ArgumentException($"Unknown GA device backend '{backend}'.")
        };

        GpuLane OpenVulkan()
        {
            var native = GaVulkanBackend.Open(Path.Combine(probeDirectory, "ga-vulkan-native.dll"));
            return Select(native, native.Devices);
        }

        GpuLane OpenD3D12()
        {
            var native = GaD3D12Backend.Open(Path.Combine(probeDirectory, "ga-d3d12-native.dll"));
            return Select(native, native.Devices);
        }

        GpuLane Select(IDisposable native, IReadOnlyList<IGaDevice> devices)
        {
            if ((uint)index >= devices.Count)
            {
                native.Dispose();
                throw new InvalidOperationException($"Requested {backend} device {index}, found {devices.Count}.");
            }
            var device = devices[index];
            Console.WriteLine($"{backend} device={index} api={device.Info.Api} version={device.Info.ApiVersion}" +
                $" name='{device.Info.Device}' driver='{device.Info.Driver}'");
            return new GpuLane(backend, native, device, slang, toolchain, outputFolder);
        }
    }

    internal void Compare(GaProgram program, IReadOnlyList<GpuRow> rows)
    {
        string name = program.SystemId.Split('.')[^1];
        Directory.CreateDirectory(output);
        var bundle = (backend == "vulkan"
            ? GaVulkanPackager.Build(program, Contract, slang, output, name)
            : GaD3D12Packager.Build(program, Contract, slang, tools, output, name)).GetAwaiter().GetResult();
        if (backend == "vulkan")
            ValidateSpirV(Path.Combine(tools, "spirv-val.exe"), Path.Combine(output, name + ".spv"));

        var request = new GaSystemRequest(bundle.Manifest.Contract, [new GaArtifactCandidate(bundle)]);
        using var startup = GaStartup.Select(GaPolicy.RequireGpu, [request], [device], allowExperimental: true);
        if (startup.SelectedPlan != GaSelectedPlan.Gpu ||
            startup.Decisions[0].ArtifactId != bundle.ContentId ||
            startup.GetPreparedKernel(program.SystemId) is not GaPackedKernel kernel)
            throw new InvalidOperationException($"{program.SystemId} could not prepare {backend}. {startup.Decisions[0]}");

        int width = program.WordCount;
        uint[] input = new uint[checked(1 + rows.Count * width)];
        input[0] = (uint)rows.Count;
        for (int row = 0; row < rows.Count; row++)
            rows[row].Input.CopyTo(input, 1 + row * width);
        var result = kernel.BeginDispatch(input, width).Drain();
        if (result.RowCount != rows.Count || result.Output.Length != rows.Count * width)
            throw new InvalidOperationException($"{program.SystemId} returned malformed {backend} row buffers.");
        int firstFault = -1;
        for (int row = 0; row < rows.Count; row++)
        {
            var expected = rows[row];
            var actual = result.ReadFault(row);
            if (actual != expected.Fault)
                throw new InvalidOperationException($"{expected.Label} {backend} fault {actual}; expected {expected.Fault}.");
            if (actual is null)
            {
                if (!result.Output.Slice(row * width, width).SequenceEqual(expected.Output))
                    throw new InvalidOperationException($"{expected.Label} {backend} output differs from direct C#.");
            }
            else if (firstFault < 0)
                firstFault = row;
        }
        if ((result.FindFirstFault()?.Row ?? -1) != firstFault)
            throw new InvalidOperationException($"{program.SystemId} {backend} selected an unexpected first failing row.");
        Console.WriteLine($"{backend} {program.SystemId} rows={rows.Count} artifact={bundle.ContentId}");
    }

    private static void ValidateSpirV(string validator, string shader)
    {
        var start = new ProcessStartInfo(validator) { UseShellExecute = false };
        start.ArgumentList.Add("--target-env");
        start.ArgumentList.Add("vulkan1.1");
        start.ArgumentList.Add(shader);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot open spirv-val.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidDataException($"spirv-val rejected {shader}.");
    }

    public void Dispose() => owner.Dispose();
}
