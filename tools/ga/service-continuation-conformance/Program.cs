using SKECS;
using SKECS.GA;
#if !GA_BROWSER_CONFORMANCE
using SKECS.GA.Backend.D3D11;
using SKECS.GA.Backend.D3D12;
using SKECS.GA.Backend.OpenGl;
using SKECS.GA.Backend.Vulkan;

if (args is ["cpu"])
{
    await ContinuationChecks.RunAsync(CpuPlan, "cpu", 127);
    return;
}

if (args.Length != 4 || args[0] is not ("vulkan" or "d3d12" or "d3d11" or "opengl") ||
    !int.TryParse(args[3], out int deviceIndex) || deviceIndex < 0)
    throw new ArgumentException("Usage: ServiceContinuationConformance cpu | <vulkan|d3d12|d3d11|opengl> <bundle> <bridge> <index>");
#endif

var contract = new GaContractIdentity(ServiceAdapterRowCodec.SystemId, "service-continuation/1",
    ServiceAdapterRowCodec.SourceDigest, ServiceAdapterRowCodec.IrDigest, ServiceAdapterRowCodec.ComponentSchemaDigest,
    GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
{
    EventCapacity = ServiceAdapterRowCodec.EventCapacity,
    TickResourceType = ServiceAdapterRowCodec.TickResourceType,
    TickResourceWords = ServiceAdapterRowCodec.TickResourceWords,
    EventTypeCount = ServiceAdapterRowCodec.EventTypeCount,
    EventPayloadWords = ServiceAdapterRowCodec.EventPayloadWords
};
ContinuationChecks.Require(contract is { EventCapacity: 1, EventTypeCount: 1, TickResourceWords: 2 },
    "Unexpected integration contract.");

string digest = GaArtifactManifest.ComputeDigest(System.Text.Encoding.UTF8.GetBytes(ContactService.Contract));
var serviceContract = new GaContractIdentity("contact-service", ContactService.Contract, digest, digest, digest,
    GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion);
#if GA_BROWSER_CONFORMANCE
var browser = await BrowserConformance.OpenAsync(args, contract, "continuation",
    new GaSystemRequest(serviceContract, [], DeclaredCpuStage: true));
using var device = browser.Device;
using var startup = browser.Startup;
ContinuationChecks.Require(browser.Profile == "continuation", "The browser service fixture requires the continuation profile.");
const string lane = "webgpu";
#else
string stem = Path.Combine(args[1], contract.SystemId);
var artifact = GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
    File.ReadAllText(stem + ".content-id").Trim(),
    File.ReadAllBytes(stem + (args[0] switch { "vulkan" => ".spv", "d3d11" => ".dxbc", "opengl" => ".glsl", _ => ".dxil" })),
    File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"));
var devices = Open(args[0], args[2], out var owner);
using var nativeBackend = owner;
ContinuationChecks.Require(deviceIndex < devices.Count, "Device index is out of range.");
var device = devices[deviceIndex];
using var startup = GaStartup.Select(GaPolicy.RequireGpu,
    [new(contract, [artifact]), new(serviceContract, [], DeclaredCpuStage: true)], [device], allowExperimental: true);
string lane = args[0];
#endif
ContinuationChecks.Require(startup.Succeeded && startup.SelectedPlan == GaSelectedPlan.Mixed &&
    startup.Decisions[0].Route == GaRoute.Gpu && startup.Decisions[1] is { Route: GaRoute.Cpu, Reason: GaStartupReason.DeclaredCpuStage },
    string.Join(", ", startup.Decisions.Select(choice => $"{choice.Route} {choice.Reason} {choice.Detail}")));

int deviceRows = 0;
DeterministicWorkload<IntegrationFrame> GpuPlan(ContactService service) =>
    new DeterministicWorkload<IntegrationFrame>().AddCommitGroup(SystemStage.Update, group =>
    {
        var processor = new GaBatchProcessor<IntegrationFrame>(startup, contract.SystemId, contract.SourceDigest,
            contract.IrDigest, contract.ComponentSchemaDigest, ServiceAdapterRowCodec.WordsPerRow,
            ServiceAdapterRowCodec.Pack<IntegrationFrame>, ServiceAdapterRowCodec.Apply<IntegrationFrame>,
            contract.EventCapacity, timing => deviceRows += timing.Rows, null, false, [], [],
            ServiceAdapterRowCodec.TickResource, null, ServiceAdapterRowCodec.EventTypes);
        group.AddBatchSystem(ServiceAdapterRowCodec.CreateQuery(), processor);
        AddService(group, service);
    });

var cpu = await ContinuationChecks.RunAsync(CpuPlan, "cpu", 127);
#if GA_BROWSER_CONFORMANCE
bool pending = false;
var gpu = await ContinuationChecks.RunAsync(GpuPlan, lane, 64, () => pending = true);
#else
var gpu = await ContinuationChecks.RunAsync(GpuPlan, lane, 64);
#endif
int passRows = gpu.Sum(frame => frame.Rows);
int replayRows = gpu.Where(frame => frame.Tick > ContinuationChecks.ForkTick).Sum(frame => frame.Rows);
int witnessRows = gpu[ContinuationChecks.ForkTick].Rows + 2 * gpu[^1].Rows;
ContinuationChecks.Require(passRows + replayRows + witnessRows == deviceRows,
    "Device rows differ across continuation and fault witnesses.");
ContinuationChecks.Compare(cpu, gpu, "CPU and mixed execution");
#if GA_BROWSER_CONFORMANCE
ContinuationChecks.Require(pending, "WebGPU ServiceContinuation did not observe a pending device readback.");
Console.WriteLine("WebGPU ServiceContinuation pendingReadback=passed");
#endif
Console.WriteLine($"device={device.Info.Device} api={device.Info.Api} driver={device.Info.Driver} artifact={startup.Decisions[0].ArtifactId} " +
    $"source={contract.SourceDigest} ir={contract.IrDigest} irVersion={contract.IrVersion} route=mixed deviceRows={deviceRows} " +
    $"service={serviceContract.SimulationContractId} serviceSchema={ContactService.ExtensionVersion} crossExecutor=passed");

static DeterministicWorkload<IntegrationFrame> CpuPlan(ContactService service) =>
    new DeterministicWorkload<IntegrationFrame>().AddCommitGroup(SystemStage.Update, group =>
    {
        ServiceAdapter.Register(group);
        AddService(group, service);
    });

static void AddService(DeterministicCommitGroup<IntegrationFrame> group, ContactService service) =>
    group.AddOrderedApply(new DeterministicQuery().Write<ContactMotion>(), service.Apply);

#if !GA_BROWSER_CONFORMANCE
static IReadOnlyList<IGaDevice> Open(string backend, string library, out IDisposable owner)
{
    if (backend == "vulkan")
    {
        var device = GaVulkanBackend.Open(library);
        owner = device;
        return device.Devices;
    }
    if (backend == "d3d11")
    {
        var d3d11 = GaD3D11Backend.Open(library);
        owner = d3d11;
        return d3d11.Devices;
    }
    if (backend == "opengl")
    {
        var gl = GaOpenGlBackend.Open(library);
        owner = gl;
        return gl.Devices;
    }
    var d3d12 = GaD3D12Backend.Open(library);
    owner = d3d12;
    return d3d12.Devices;
}
#endif
