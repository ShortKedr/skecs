using SKECS;
using SKECS.GA;
using static HostLifecycleChecks;
#if GA_BROWSER_CONFORMANCE
using System.Runtime.InteropServices.JavaScript;
#else
using SKECS.GA.Backend.D3D11;
using SKECS.GA.Backend.D3D12;
using SKECS.GA.Backend.OpenGl;
using SKECS.GA.Backend.Metal;
using SKECS.GA.Backend.Vulkan;
#endif

#if GA_BROWSER_CONFORMANCE
var browserContract = CreateContract();
var browser = await BrowserConformance.OpenAsync(args, browserContract, "lifecycle", ObserverRequest(browserContract));
using var browserDevice = browser.Device;
using var browserStartup = browser.Startup;
bool checkVisibility = browser.Profile == "visibility";
using var visibilityModule = checkVisibility
    ? await JSHost.ImportAsync(BrowserVisibility.Module, new Uri(browser.Site, "host-visibility.mjs").AbsoluteUri)
    : null;
await ExerciseDevice("webgpu", browserDevice, browserStartup, browserContract, checkVisibility);
#else
bool endurance = args is ["cpu", "endurance"] or [_, _, _, _, "endurance"];
int cycles = endurance ? 256 : 1;
if (args is ["cpu"] or ["cpu", "endurance"])
{
    for (int cycle = 0; cycle < cycles; cycle++)
        await Exercise(CpuWorkload, "CPU-only", 127);
    if (endurance)
        Console.WriteLine($"endurance=passed fixture=host-lifecycle lane=cpu cycles={cycles}");
}
else
{
    if (args.Length != (endurance ? 5 : 4) || args[0] is not ("vulkan" or "d3d12" or "d3d11" or "opengl" or "gles" or "metal") ||
        !int.TryParse(args[3], out int index) || index < 0)
        throw new ArgumentException("Usage: HostLifecycleConformance (cpu | <vulkan|d3d12|d3d11|opengl|gles|metal> <bundle> <native bridge> <device index>) [endurance]");
    await RunDevice(args[0], args[1], args[2], index, cycles);
}
#endif

static DeterministicWorkload<HostInput> CpuWorkload() =>
    WithObserver(HostAdapter.Register(new DeterministicWorkload<HostInput>(), SystemStage.Update));

static GaContractIdentity CreateContract()
{
    var contract = new GaContractIdentity(HostAdapterRowCodec.SystemId, "host-lifecycle/1",
        HostAdapterRowCodec.SourceDigest, HostAdapterRowCodec.IrDigest, HostAdapterRowCodec.ComponentSchemaDigest,
        GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
    {
        EventCapacity = HostAdapterRowCodec.EventCapacity,
        TickResourceType = HostAdapterRowCodec.TickResourceType,
        TickResourceWords = HostAdapterRowCodec.TickResourceWords
    };
    Require(contract is { EventCapacity: 1, TickResourceWords: 1 }, "Unexpected host kernel schema.");
    return contract;
}

static GaSystemRequest ObserverRequest(GaContractIdentity contract) =>
    new(contract with { SystemId = "host-observer" }, [], DeclaredCpuStage: true);

#if !GA_BROWSER_CONFORMANCE
static async Task RunDevice(string backend, string bundle, string native, int index, int cycles)
{
    var contract = CreateContract();
    var stem = Path.Combine(bundle, contract.SystemId);
    var artifact = GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
        File.ReadAllText(stem + ".content-id").Trim(),
        File.ReadAllBytes(stem + (backend switch { "vulkan" => ".spv", "d3d11" => ".dxbc", "metal" => ".metallib", "opengl" or "gles" => ".glsl", _ => ".dxil" })),
        File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"));
    var devices = Open(backend, native, out var owner);
    using var nativeOwner = owner;
    Require(index < devices.Count, $"Device index {index} exceeds available devices {devices.Count}.");
    var device = devices[index];
    using var startup = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [artifact]), ObserverRequest(contract)],
        [device], allowExperimental: true);
    var enduranceKernel = cycles > 1 && startup.Succeeded ? (GaPackedKernel)startup.GetPreparedKernel(contract.SystemId) : null;
    int totalRows = 0, totalSubmissions = 0, totalDrains = 0;
    for (int cycle = 0; cycle < cycles; cycle++)
    {
        var result = await ExerciseDevice(backend, device, startup, contract);
        totalRows += result.Rows;
        totalSubmissions += result.Submissions;
        totalDrains += result.Drains;
        if (enduranceKernel is not null)
        {
            Require(ReferenceEquals(enduranceKernel, startup.GetPreparedKernel(contract.SystemId)),
                "Host lifecycle replaced the prepared kernel.");
            await enduranceKernel.WaitForIdleAsync();
        }
    }
    if (enduranceKernel is not null)
    {
        startup.Dispose();
        bool disposed = false;
        try { await enduranceKernel.WaitForIdleAsync(); }
        catch (ObjectDisposedException) { disposed = true; }
        Require(disposed, "Startup disposal left the prepared kernel usable.");
        Console.WriteLine($"endurance=passed fixture=host-lifecycle lane={backend} cycles={cycles} " +
            $"deviceRows={totalRows} submissions={totalSubmissions} drains={totalDrains} " +
            $"completedEvents={cycles * 4 * (HostSession.RowCount + 1)} pendingResets={cycles} faultResets={cycles} " +
            $"pendingDisposals={cycles} preparedKernelReused=passed preparedKernelDisposal=passed");
    }
}
#endif

static async Task<(int Rows, int Submissions, int Drains)> ExerciseDevice(
    string backend, IGaDevice device, GaStartupResult startup, GaContractIdentity contract,
    bool checkVisibility = false)
{
    Require(startup.Succeeded && startup.SelectedPlan == GaSelectedPlan.Mixed &&
        startup.Decisions[0].Route == GaRoute.Gpu && startup.Decisions[1].Route == GaRoute.Cpu,
        string.Join(", ", startup.Decisions.Select(item => $"{item.Route} {item.Reason} {item.Detail}")));
    int deviceRows = 0;
    var batch = new AuditedBatches(new GaBatchProcessor<HostInput>(startup, contract.SystemId, contract.SourceDigest,
        contract.IrDigest, contract.ComponentSchemaDigest, HostAdapterRowCodec.WordsPerRow,
        HostAdapterRowCodec.Pack<HostInput>, HostAdapterRowCodec.Apply<HostInput>, contract.EventCapacity,
        timing => deviceRows += timing.Rows, null, false, [], [], HostAdapterRowCodec.TickResource));
    var kernel = OperatingSystem.IsBrowser() ? (GaPackedKernel)startup.GetPreparedKernel(contract.SystemId) : null;
    var cpu = await Exercise(CpuWorkload, "CPU baseline", 127);
    var gpu = await Exercise(() => WithObserver(new DeterministicWorkload<HostInput>()
        .AddBatchSystem(SystemStage.Update, HostAdapterRowCodec.CreateQuery(), batch)), $"{backend} {device.Info.Device}", 64,
        batch, kernel, checkVisibility);
    Require(cpu.TickIndex == gpu.TickIndex && cpu.Stage == gpu.Stage && cpu.SystemOrder == gpu.SystemOrder &&
        cpu.KernelOrder == gpu.KernelOrder && cpu.SourceOrderOrdinal == gpu.SourceOrderOrdinal &&
        cpu.SourceId == gpu.SourceId && cpu.SourceGeneration == gpu.SourceGeneration && cpu.KernelFault == gpu.KernelFault,
        "Canonical CPU and GPU fault records differ.");
    Require(deviceRows == 4 * HostSession.RowCount + 64 && batch.Submitted == 16 &&
        batch.Discarded == 3 && batch.InFlight == 0, "Device rows, submissions, or drains differ.");
    Console.WriteLine($"device={device.Info.Device} api={device.Info.Api} version={device.Info.ApiVersion} driver={device.Info.Driver} " +
        $"artifact={startup.Decisions[0].ArtifactId} source={contract.SourceDigest} ir={contract.IrDigest} " +
        $"deviceRows={deviceRows} submissions={batch.Submitted} drains={batch.Discarded} route=mixed faultParity=passed");
    return (deviceRows, batch.Submitted, batch.Discarded);
}

#if !GA_BROWSER_CONFORMANCE
static IReadOnlyList<IGaDevice> Open(string backend, string path, out IDisposable owner)
{
    if (backend == "vulkan")
    {
        var vulkan = GaVulkanBackend.Open(path);
        owner = vulkan;
        return vulkan.Devices;
    }
    if (backend == "d3d11")
    {
        var d3d11 = GaD3D11Backend.Open(path);
        owner = d3d11;
        return d3d11.Devices;
    }
    if (backend is "opengl" or "gles")
    {
        var gl = backend == "gles"
            ? GaOpenGlBackend.OpenEsFromEnvironment(path)
            : GaOpenGlBackend.Open(path);
        owner = gl;
        return gl.Devices;
    }
    if (backend == "metal")
    {
        var metal = GaMetalBackend.Open(path);
        owner = metal;
        return metal.Devices;
    }
    var d3d12 = GaD3D12Backend.Open(path);
    owner = d3d12;
    return d3d12.Devices;
}
#endif
