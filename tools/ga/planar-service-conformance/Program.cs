using System.Text;
using SKECS;
using SKECS.GA;
using SKECS.GA.Backend.D3D12;
using SKECS.GA.Backend.Vulkan;

int deviceIndex = 0;
if (args is not ["cpu"] &&
    (args.Length != 4 || args[0] is not ("vulkan" or "d3d12") ||
     !int.TryParse(args[3], out deviceIndex) || deviceIndex < 0))
    throw new ArgumentException("Usage: PlanarServiceConformance cpu | <vulkan|d3d12> <bundle> <bridge> <device index>");

PlanarChecks.VerifyNumerics();
var cpu = PlanarChecks.Run(CpuPlan, "cpu", 3);
if (args[0] == "cpu") return;

var codec = new GaContractIdentity(PlanarAdapterRowCodec.SystemId, "planar-service/1",
    PlanarAdapterRowCodec.SourceDigest, PlanarAdapterRowCodec.IrDigest, PlanarAdapterRowCodec.ComponentSchemaDigest,
    GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
{
    EventCapacity = PlanarAdapterRowCodec.EventCapacity,
    EventTypeCount = PlanarAdapterRowCodec.EventTypeCount,
    EventPayloadWords = PlanarAdapterRowCodec.EventPayloadWords,
    TickResourceType = PlanarAdapterRowCodec.TickResourceType,
    TickResourceWords = PlanarAdapterRowCodec.TickResourceWords,
};
PlanarChecks.Require(codec is { EventCapacity: 1, EventTypeCount: 1, EventPayloadWords: 10, TickResourceWords: 2 },
    "Unexpected planar row contract.");
string digest = GaArtifactManifest.ComputeDigest(Encoding.UTF8.GetBytes(PlanarState.Contract));
var service = new GaContractIdentity("planar-contact-service", PlanarState.Contract, digest, digest, digest,
    GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion);
string stem = Path.Combine(args[1], codec.SystemId);
var artifact = GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
    File.ReadAllText(stem + ".content-id").Trim(), File.ReadAllBytes(stem + (args[0] == "vulkan" ? ".spv" : ".dxil")),
    File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"));
using var vulkan = args[0] == "vulkan" ? GaVulkanBackend.Open(args[2]) : null;
using var d3d12 = args[0] == "d3d12" ? GaD3D12Backend.Open(args[2]) : null;
var devices = vulkan?.Devices ?? d3d12!.Devices;
PlanarChecks.Require(deviceIndex < devices.Count, "Planar device index is out of range.");
var device = devices[deviceIndex];
using var startup = GaStartup.Select(GaPolicy.RequireGpu,
    [new(codec, [artifact]), new(service, [], DeclaredCpuStage: true)], [device], allowExperimental: true);
PlanarChecks.Require(startup.Succeeded && startup.SelectedPlan == GaSelectedPlan.Mixed &&
    startup.Decisions[0].Route == GaRoute.Gpu && startup.Decisions[1] is { Route: GaRoute.Cpu, Reason: GaStartupReason.DeclaredCpuStage },
    string.Join(", ", startup.Decisions.Select(choice => $"{choice.Route} {choice.Reason} {choice.Detail}")));

int rows = 0;
var gpu = PlanarChecks.Run(state => new DeterministicWorkload<PlanarFrame>().AddCommitGroup(SystemStage.Update, group =>
{
    var processor = new GaBatchProcessor<PlanarFrame>(startup, codec.SystemId, codec.SourceDigest, codec.IrDigest,
        codec.ComponentSchemaDigest, PlanarAdapterRowCodec.WordsPerRow, PlanarAdapterRowCodec.Pack<PlanarFrame>,
        PlanarAdapterRowCodec.Apply<PlanarFrame>, codec.EventCapacity, timing => rows += timing.Rows,
        null, false, [], [], PlanarAdapterRowCodec.TickResource, null, PlanarAdapterRowCodec.EventTypes);
    group.AddBatchSystem(PlanarAdapterRowCodec.CreateQuery(), processor);
    group.AddOrderedApply(new DeterministicQuery().Write<PlanarMotion>(), state.Apply);
}), args[0], 2);
PlanarChecks.Require(cpu.InitialCheckpoint.AsSpan().SequenceEqual(gpu.InitialCheckpoint),
    "Canonical initial state differs across executors.");
PlanarChecks.Compare(cpu.Frames, gpu.Frames);
PlanarChecks.Require(rows == gpu.ProcessedRows, $"Expected {gpu.ProcessedRows} executed device rows, got {rows}.");
Console.WriteLine($"api={device.Info.Api} device={device.Info.Device} driver={device.Info.Driver} " +
    $"artifact={startup.Decisions[0].ArtifactId} source={codec.SourceDigest} ir={codec.IrDigest} " +
    $"deviceRows={rows} service={PlanarState.Contract} schema={PlanarState.ExtensionVersion} crossExecutor=passed");

static DeterministicWorkload<PlanarFrame> CpuPlan(PlanarState state) =>
    new DeterministicWorkload<PlanarFrame>().AddCommitGroup(SystemStage.Update, group =>
    {
        PlanarAdapter.Register(group);
        group.AddOrderedApply(new DeterministicQuery().Write<PlanarMotion>(), state.Apply);
    });
