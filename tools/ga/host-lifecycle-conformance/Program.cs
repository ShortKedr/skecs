using System.Diagnostics;
using SKECS;
using SKECS.GA;
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

static DeterministicWorkload<HostInput> WithObserver(DeterministicWorkload<HostInput> workload) =>
    workload.AddJob(SystemStage.PostUpdate, new DeterministicQuery(), new Observer());

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

static async Task<GaExecutionException> Exercise(Func<DeterministicWorkload<HostInput>> workload, string lane, int chunk,
    AuditedBatches? batches = null, GaPackedKernel? asyncKernel = null, bool checkVisibility = false)
{
    using var host = new HostSession(workload, chunk);
    host.Reset();
    var initial = host.Render();
    VerifyFrame(initial, 0);

    var input = new HostInput { Divisor = 2 };
    if (batches is not null) batches.Hold = true;
    Require(host.Update(input) == DeterministicTickStatus.Pending, "Tick 1 completed within a one-job budget.");
    VerifyLease(host);
    input.Divisor = 4;
#if GA_BROWSER_CONFORMANCE
    if (checkVisibility)
    {
        Require(!BrowserVisibility.IsHidden(), "The GPU tick must start in a visible tab.");
        Console.WriteLine("WebGPU HostLifecycle waitingForHidden=ready");
        await BrowserVisibility.WaitForVisibility(true);
        host.Paused = BrowserVisibility.IsHidden();
    }
    else
#endif
    host.Paused = true;
    int submitted = batches?.Submitted ?? 0;
    for (int poll = 0; poll < 3; poll++)
        Require(host.Update(input) == DeterministicTickStatus.Pending && host.Poll() == DeterministicTickStatus.Pending,
            "Paused host advanced its logical work.");
    var pausedFrame = OperatingSystem.IsBrowser() ? host.Render() : Task.Run(host.Render).GetAwaiter().GetResult();
    Require((batches?.Submitted ?? 0) == submitted && host.Captures == 1 && ReferenceEquals(initial, pausedFrame),
        "Pause changed the published frame or queued additional work.");
#if GA_BROWSER_CONFORMANCE
    if (checkVisibility)
    {
        Require(BrowserVisibility.IsHidden(), "The driver restored the tab before polling it while hidden.");
        Console.WriteLine("WebGPU HostLifecycle hiddenPending=passed");
        await BrowserVisibility.WaitForVisibility(false);
        host.Paused = BrowserVisibility.IsHidden();
        Require(!host.Paused, "The visible tab did not resume.");
        Console.WriteLine("WebGPU HostLifecycle visibility=passed");
    }
    else
#endif
    host.Paused = false;
    if (batches is not null)
    {
        Require(host.Update(input) == DeterministicTickStatus.Pending && batches.InFlight == 1 &&
            ReferenceEquals(host.Render(), initial), "Poll passed the held GPU completion.");
        batches.Hold = false;
    }
    Require(await Drain(host) == DeterministicTickStatus.Completed, "Tick 1 failed.");
    var first = host.Render();
    VerifyFrame(first, 0, 2);
    Require(host.Poll() == DeterministicTickStatus.Completed && host.Captures == 2 &&
        ReferenceEquals(first, host.Render()), "Polling a completed tick republished it.");
    Require(host.Update(input) == DeterministicTickStatus.Pending && await Drain(host) == DeterministicTickStatus.Completed,
        "Tick 2 failed.");
    var second = host.Render();
    VerifyFrame(second, 0, 2, 4);
    VerifyFrame(first, 0, 2);
    VerifyFrame(initial, 0);
    Require(((IList<long>)first.Positions).IsReadOnly && ((IList<RenderEvent>)first.Events).IsReadOnly,
        "Render exposed mutable collections.");

    if (batches is not null) batches.Hold = true;
    Require(host.Update(new HostInput { Divisor = 1 }) == DeterministicTickStatus.Pending,
        "Reset scenario did not start a pending tick.");
    host.Paused = true;
    host.Reset();
    if (asyncKernel is not null)
        await asyncKernel.WaitForIdleAsync();
    if (batches is not null)
    {
        Require(batches.InFlight == 0 && batches.Discarded == 1, "Reset failed to drain the device batch.");
        batches.Hold = false;
    }
    Require(!host.Paused, "Reset did not restart the host clock.");
    VerifyFrame(host.Render(), 1);
    VerifyFrame(second, 0, 2, 4);
    Require(host.Update(new HostInput { Divisor = 2 }) == DeterministicTickStatus.Pending &&
        await Drain(host) == DeterministicTickStatus.Completed, "Reset could not restart the workload.");
    var beforeFault = host.Render();
    VerifyFrame(beforeFault, 1, 2);

    Require(host.Update(default) == DeterministicTickStatus.Pending && await Drain(host) == DeterministicTickStatus.Faulted,
        "An arithmetic fault did not follow the first successful slice.");
    // The row's integer remainder precedes its Fixed division in the fault evaluation order.
    Require(host.Fault is GaExecutionException { TickIndex: 2, Stage: SystemStage.Update, SystemOrder: 0, KernelOrder: 0,
        SourceOrderOrdinal: 64, SourceId: 64, SourceGeneration: 1, KernelFault.Kind: GaFaultKind.DivideByZero,
        KernelFault.EvaluationOrdinal: 1 }, $"Wrong canonical fault: {host.Fault}");
    var fault = (GaExecutionException)host.Fault!;
    submitted = batches?.Submitted ?? 0;
    Require(host.Update(input) == DeterministicTickStatus.Faulted && host.Poll() == DeterministicTickStatus.Faulted &&
        (batches?.Submitted ?? 0) == submitted && ReferenceEquals(beforeFault, host.Render()),
        "A fault published partial state or retried its tick.");
    VerifyLease(host);
    VerifyFrame(beforeFault, 1, 2);

    host.Reset();
    VerifyFrame(host.Render(), 2);
    Require(host.Update(new HostInput { Divisor = 2 }) == DeterministicTickStatus.Pending &&
        await Drain(host) == DeterministicTickStatus.Completed, "Host did not restart after discarding the faulted world.");
    var last = host.Render();
    VerifyFrame(last, 2, 2);
    if (batches is not null) batches.Hold = true;
    Require(host.Update(input) == DeterministicTickStatus.Pending, "Dispose scenario did not start a pending tick.");
    host.Dispose();
    if (asyncKernel is not null)
        await asyncKernel.WaitForIdleAsync();
    if (batches is not null)
        Require(batches.InFlight == 0 && batches.Discarded == 3, "Dispose failed to drain the device batch.");
    Require(host.Captures == 7 && ReferenceEquals(last, host.Render()), "Dispose published a partial frame.");
    VerifyFrame(last, 2, 2);
    Console.WriteLine($"{lane}: frames={host.Captures} exactCompletedEvents={4 * (HostSession.RowCount + 1)} " +
        $"canonicalFault=divideByZero/source{fault.SourceOrderOrdinal}/evaluation{fault.KernelFault.EvaluationOrdinal} " +
        "pendingOwnership=passed pauseResume=passed reset=passed dispose=passed immutableRender=passed");
    return fault;
}

static async Task<DeterministicTickStatus> Drain(HostSession host)
{
    var published = host.Render();
    var timer = Stopwatch.StartNew();
    var wait = new SpinWait();
    for (;;)
    {
        var state = host.Poll();
        if (state != DeterministicTickStatus.Pending)
            return state;
        Require(ReferenceEquals(published, host.Render()), "The host published a partial tick.");
        if (timer.Elapsed > TimeSpan.FromSeconds(20))
            throw new TimeoutException("The host stayed pending for 20 seconds.");
        if (OperatingSystem.IsBrowser())
            await Task.Delay(1);
        else
            wait.SpinOnce();
    }
}

static void VerifyFrame(Frame frame, int generation, params int[] divisors)
{
    Require(frame.Generation == generation && frame.Tick == divisors.Length && frame.Positions.Count == HostSession.RowCount,
        "Render frame identity or row count differs.");
    for (int row = 0; row < HostSession.RowCount; row++)
    {
        long position = row * 17L - 39;
        long step = (row % 7 - 3) * 4_294_967_296L;
        foreach (int divisor in divisors)
            position += row is 64 or 65 ? step / divisor : step;
        Require(frame.Positions[row] == position, $"Fixed raw output differs at row {row}.");
    }
    if (divisors.Length == 0)
    {
        Require(frame.Events.Count == 0, "Seed published tick events.");
        return;
    }
    Require(frame.Events.Count == HostSession.RowCount + 1, "Completed event count differs.");
    for (int row = 0; row < HostSession.RowCount; row++)
    {
        var item = frame.Events[row];
        Require(item.Value == row && item.Key is
            { Stage: SystemStage.Update, SystemOrder: 0, ProducerOrder: 0, SourceKind: DeterministicSourceKind.Entity,
                SourceGeneration: 1, LocalOrdinal: 0, DomainKey: null } &&
            item.Key.SourceId == row && item.Key.TickIndex == frame.Tick, $"Row event differs at {row}.");
    }
    var marker = frame.Events[^1];
    Require(marker.Value == -7 * divisors[^1] && marker.Key is
        { Stage: SystemStage.PostUpdate, SystemOrder: 1, ProducerOrder: 0, SourceKind: DeterministicSourceKind.Job,
            SourceId: 0, SourceGeneration: 0, LocalOrdinal: 0, DomainKey: null } && marker.Key.TickIndex == frame.Tick,
        "Common post-update CPU event differs.");
}

static void VerifyLease(HostSession host)
{
    try
    {
        host.ReadLiveWorld();
    }
    catch (InvalidOperationException)
    {
        return;
    }
    throw new InvalidOperationException("The world permitted an external read while owned by the runner.");
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

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal readonly struct Observer : IDeterministicJobProcessor<HostInput>
{
    public void Execute(DeterministicJob<HostInput> job) => job.ForJob().Emit(-7 * job.Input.Divisor);
}
