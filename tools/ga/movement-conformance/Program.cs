using System.Diagnostics;
using System.Numerics;
using SKECS;
using SKECS.GA;
#if GA_BROWSER_CONFORMANCE
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SKECS.GA.Backend.WebGpu;
#else
using SKECS.GA.Backend.D3D11;
using SKECS.GA.Backend.D3D12;
using SKECS.GA.Backend.OpenGl;
using SKECS.GA.Backend.Vulkan;
#endif
using SKECS.Numerics;

#if !GA_BROWSER_CONFORMANCE
if (args is ["cpu", var cpuProfile])
{
    Console.WriteLine($"source={MovementAdapterRowCodec.SourceDigest} ir={MovementAdapterRowCodec.IrDigest} " +
        $"numeric={GaProgram.NumericVersion} subset={GaProgram.SubsetVersion} irVersion={GaProgram.IrVersion}");
    await Run(cpuProfile, "generated CPU", null, null);
    return;
}

if (args.Length != 5 || args[0] is not ("vulkan" or "d3d12" or "d3d11" or "opengl" or "gles") ||
    !int.TryParse(args[3], out int deviceIndex) || deviceIndex < 0)
    throw new ArgumentException("Use 'cpu <profile>' or '<vulkan|d3d12|d3d11|opengl|gles> <bundle> <native bridge> <device index> <profile>'.");
#endif

var contract = new GaContractIdentity(MovementAdapterRowCodec.SystemId, "movement/1",
    MovementAdapterRowCodec.SourceDigest, MovementAdapterRowCodec.IrDigest, MovementAdapterRowCodec.ComponentSchemaDigest,
    GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
{
    TickResourceType = MovementAdapterRowCodec.TickResourceType,
    TickResourceWords = MovementAdapterRowCodec.TickResourceWords
};
Check(contract.TickResourceWords == 2 && MovementAdapterRowCodec.EventCapacity == 0 && !MovementAdapterRowCodec.UsesCommands,
    "Unexpected Movement ABI.");
#if GA_BROWSER_CONFORMANCE
if (!OperatingSystem.IsBrowser())
    throw new PlatformNotSupportedException();
var host = WebAssemblyHostBuilder.CreateDefault(args).Build();
var navigation = host.Services.GetRequiredService<NavigationManager>();
var site = new Uri(navigation.BaseUri);
string query = new Uri(navigation.Uri).Query;
string profile = query switch { "?short" => "short", "?long" => "long", _ => "boundaries" };
using var http = new HttpClient { BaseAddress = site };
string stem = "bundle/" + contract.SystemId;
var manifestBytes = await http.GetByteArrayAsync(stem + ".manifest.json");
var contentId = await http.GetStringAsync(stem + ".content-id");
var shaderBytes = await http.GetByteArrayAsync(stem + ".wgsl");
var layoutBytes = await http.GetByteArrayAsync(stem + ".layout");
var sourceMapBytes = await http.GetByteArrayAsync(stem + ".source-map.json");
var artifact = GaArtifactCandidate.Load(manifestBytes, contentId.Trim(), shaderBytes, layoutBytes, sourceMapBytes);
var request = new GaSystemRequest(contract, [artifact]);
var opened = await GaWebGpuBackend.TryOpenAsync(new Uri(site, "ga-webgpu.mjs").AbsoluteUri);
using var backend = opened.Backend;
if (query == "?fallback")
{
    Check(backend is null && opened.Error is { Length: > 0 }, "The fallback fixture must disable browser WebGPU.");
    using var unavailable = await GaStartup.SelectAsync(GaPolicy.Auto, [request], [], allowExperimental: true);
    Check(unavailable.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.NoComputeApi },
        "WebGPU unavailability did not select CPU fallback.");
    await Run(profile, "WebGPU unavailable CPU fallback", null, null);
    return;
}
if (backend is null)
    throw new InvalidOperationException(opened.Error);
var device = backend.Info;
using var cpuPolicy = await GaStartup.SelectAsync(GaPolicy.CpuOnly, [request], [backend]);
using var autoPolicy = await GaStartup.SelectAsync(GaPolicy.Auto, [request], [backend]);
using var missingDevice = await GaStartup.SelectAsync(GaPolicy.RequireGpu, [request], []);
Check(cpuPolicy.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.CpuOnlyPolicy } &&
    autoPolicy.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.UnqualifiedConfiguration } &&
    missingDevice.Decisions.Single() is { Route: GaRoute.Rejected, Reason: GaStartupReason.NoComputeApi },
    "WebGPU startup policy mismatch.");
var originalManifest = artifact.Bundle?.Manifest ?? throw new InvalidDataException(artifact.Error);
var wrongGroup = originalManifest with { Requirements = originalManifest.Requirements with { WorkgroupX = 32 } };
var incompatibleArtifact = GaArtifactCandidate.Load(wrongGroup.GetCanonicalBytes(), wrongGroup.GetContentId(),
    shaderBytes, layoutBytes, sourceMapBytes);
Check(incompatibleArtifact.Bundle is not null, "The incompatible-workgroup fixture is invalid.");
using var fallback = await GaStartup.SelectAsync(GaPolicy.Auto, [new(contract, [incompatibleArtifact])],
    [backend], allowExperimental: true);
Check(fallback.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.IncompatibleManifest },
    "WebGPU accepted a workgroup that disagrees with the WGSL entry point.");
using var startup = await GaStartup.SelectAsync(GaPolicy.RequireGpu, [request], [backend], allowExperimental: true);
#else
string profile = args[4];
string stem = Path.Combine(args[1], contract.SystemId);
string binaryExtension = args[0] switch { "vulkan" => ".spv", "d3d11" => ".dxbc", "opengl" or "gles" => ".glsl", _ => ".dxil" };
var artifact = GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
    File.ReadAllText(stem + ".content-id").Trim(),
    File.ReadAllBytes(stem + binaryExtension),
    File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"));
using IDisposable backend = Open(args[0], args[2], out var devices);
Check(deviceIndex < devices.Count, $"Only {devices.Count} devices are available.");
if (args[0] is "d3d11" or "opengl" or "gles")
    VerifyStartup(contract, artifact, devices[deviceIndex]);
using var startup = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [artifact])], [devices[deviceIndex]], allowExperimental: true);
var device = devices[deviceIndex].Info;
#endif
var decision = startup.Decisions.Single();
Check(startup.Succeeded && decision.Route == GaRoute.Gpu, $"{decision.Reason} {decision.Detail}");
var manifest = artifact.Bundle!.Manifest;
Console.WriteLine($"api={device.Api} apiVersion={device.ApiVersion} device={device.Device} driver={device.Driver} " +
    $"source={contract.SourceDigest} ir={contract.IrDigest} numeric={contract.NumericVersion} subset={contract.SubsetVersion} " +
    $"irVersion={contract.IrVersion} artifact={manifest.ArtifactDigest} reflection={manifest.ReflectionDigest} " +
    $"contentId={decision.ArtifactId} workgroup={manifest.Requirements.WorkgroupX} slang={manifest.Toolchain.SlangVersion} " +
    $"target={manifest.Toolchain.TargetProfile} downstream={manifest.Toolchain.DownstreamVersion}");
#if GA_BROWSER_CONFORMANCE
using (var abortedStartup = await GaStartup.SelectAsync(GaPolicy.RequireGpu, [request], [backend], allowExperimental: true))
    await VerifyPendingAbort(abortedStartup, contract);
#endif
await Run(profile, $"{device.Api} {device.Device}", startup, contract);

#if GA_BROWSER_CONFORMANCE
static async Task VerifyPendingAbort(GaStartupResult startup, GaContractIdentity contract)
{
    await using var kernel = (GaPackedKernel)startup.GetPreparedKernel(contract.SystemId);
    var oracle = new MovementOracle(new Scenario(65, 27, 127, 64, 1, 1, 1), new Counters());
    using var world = new World(new WorldOptions(64));
    Populate(world, oracle);
    long appliedRows = 0;
    var workload = new DeterministicWorkload<MovementFrame>();
    RegisterGpu(workload, startup, contract, timing => appliedRows += timing.Rows);
    using var runner = new DeterministicRunner<MovementFrame>(world, workload);

    var frame = new MovementFrame { DeltaTime = Fixed.FromRaw(MovementOracle.StepAt(1)) };
    runner.BeginTick(1, frame, frame.DeltaTime);
    Check(runner.Advance(1) == DeterministicTickStatus.Pending, "Browser readback did not yield.");
    runner.Abort();
    Check(runner.Status == DeterministicTickStatus.Faulted && runner.Fault is OperationCanceledException &&
        runner.LastCompleted is null, "An aborted WebGPU tick was published.");
    runner.DiscardWorld();
    await kernel.DisposeAsync();
    Check(appliedRows == 0 && runner.LastCompleted is null, "A discarded WebGPU submission applied rows.");
    Console.WriteLine("WebGPU pending readback, abort and asynchronous release passed.");
}
#endif

#if !GA_BROWSER_CONFORMANCE
static void VerifyStartup(GaContractIdentity contract, GaArtifactCandidate artifact, IGaDevice device)
{
    string api = device.Info.Api;
    var bundle = artifact.Bundle ?? throw new InvalidOperationException(artifact.Error);
    var request = new GaSystemRequest(contract, [artifact]);
    using var cpu = GaStartup.Select(GaPolicy.CpuOnly, [request], [device]);
    using var unqualified = GaStartup.Select(GaPolicy.Auto, [request], [device]);
    using var absent = GaStartup.Select(GaPolicy.RequireGpu, [request], []);
    Check(cpu.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.CpuOnlyPolicy },
        $"{api} CpuOnly did not select the CPU.");
    Check(unqualified.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.UnqualifiedConfiguration },
        $"{api} Auto selected an Experimental route without opt-in.");
    Check(absent.Decisions.Single() is { Route: GaRoute.Rejected, Reason: GaStartupReason.NoComputeApi },
        $"{api} RequireGpu accepted a missing device.");

    string[] unverifiedOptions = device.Format == GaArtifactFormat.Dxbc
        ? ["Slang downstream FXC"]
        : bundle.Manifest.Toolchain.Options.SkipLast(1).ToArray();
    var unverifiedCompiler = bundle.Manifest with
    {
        Toolchain = bundle.Manifest.Toolchain with { Options = unverifiedOptions }
    };
    var unverifiedCandidate = GaArtifactCandidate.Load(unverifiedCompiler.GetCanonicalBytes(), unverifiedCompiler.GetContentId(),
        bundle.Artifact, bundle.Reflection, bundle.SourceMap);
    Check(unverifiedCandidate.Bundle is not null, $"Invalid {api} compiler-options test fixture: {unverifiedCandidate.Error}");
    using var unverified = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [unverifiedCandidate])],
        [device], allowExperimental: true);
    Check(unverified.Decisions.Single() is { Route: GaRoute.Rejected, Reason: GaStartupReason.IncompatibleManifest },
        $"{api} accepted unverified compiler options.");

    var wrongGroup = bundle.Manifest with
    {
        Requirements = bundle.Manifest.Requirements with
        {
            WorkgroupX = bundle.Manifest.Requirements.WorkgroupX == 32 ? 64 : 32
        }
    };
    var candidate = GaArtifactCandidate.Load(wrongGroup.GetCanonicalBytes(), wrongGroup.GetContentId(),
        bundle.Artifact, bundle.Reflection, bundle.SourceMap);
    Check(candidate.Bundle is not null, $"Invalid {api} workgroup test fixture: {candidate.Error}");
    using var rejected = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [candidate])], [device], allowExperimental: true);
    using var fallback = GaStartup.Select(GaPolicy.Auto, [new(contract, [candidate])], [device], allowExperimental: true);
    Check(rejected.Decisions.Single() is { Route: GaRoute.Rejected, Reason: GaStartupReason.PipelineCreationFailed },
        $"{api} RequireGpu accepted a workgroup that disagrees with the shader.");
    Check(fallback.Decisions.Single() is { Route: GaRoute.Cpu, Reason: GaStartupReason.PipelineCreationFailed },
        $"{api} Auto did not fall back after rejecting a mismatched workgroup.");
    Console.WriteLine($"{api} startup policies, compiler options, missing device and workgroup mismatch passed.");
}
#endif

static async Task Run(string profile, string lane, GaStartupResult? startup, GaContractIdentity? contract)
{
    int ticks;
    Scenario[] scenarios;
    switch (profile)
    {
        case "boundaries":
            ticks = 6;
            int[] sizes = [0, 1, 63, 64, 65, 127, 128, 129, 1024];
            uint[] seeds = [0, 1, 27, 7331, uint.MaxValue];
            int[] chunks = [1, 64, 127, 256];
            int[] workers = [1, 2, Environment.ProcessorCount];
            int[] budgets = [1, 3, 64];
            scenarios = Enumerable.Range(0, sizes.Length * seeds.Length).Select(i => new Scenario(
                sizes[i / seeds.Length], seeds[i % seeds.Length], chunks[i % chunks.Length], chunks[(i + 1) % chunks.Length],
                workers[i % workers.Length], budgets[i % budgets.Length], budgets[(i + 1) % budgets.Length])).ToArray();
            break;
        case "short":
        case "long":
            ticks = profile == "short" ? 1_000 : 100_000;
            scenarios = [new(65, 27, 127, 64, 2, 3, 64)];
            break;
        default:
            throw new ArgumentException($"Unknown profile '{profile}'.");
    }
    if (OperatingSystem.IsBrowser())
        scenarios = scenarios.Select(scenario => scenario with { Workers = 1 }).ToArray();

    var counters = new Counters();
    foreach (var scenario in scenarios)
        await RunCase(scenario, ticks, startup, contract, counters);
    Check(counters.HalfEven > 0 && counters.HalfOdd > 0 && counters.PositiveHalf > 0 && counters.NegativeHalf > 0 &&
        counters.WrappedSteps > 0 && counters.DebrisRows > 0 && counters.ParticleRows > 0, "Scenario missed a motion branch.");
    Console.WriteLine($"{lane}: profile={profile} cases={scenarios.Length} ticks={scenarios.Length * ticks} " +
        $"sizes={string.Join(',', scenarios.Select(value => value.Rows).Distinct().Order())} " +
        $"seeds={string.Join(',', scenarios.Select(value => value.Seed).Distinct().Order())} " +
        $"workers={string.Join(',', scenarios.Select(value => value.Workers).Distinct().Order())} " +
        $"checkedRows={counters.DebrisRows + counters.ParticleRows} deviceRows={counters.DeviceRows} " +
        $"deviceBatches={counters.DeviceBatches} particleRows={counters.ParticleRows} debrisRows={counters.DebrisRows} " +
        $"halfEven={counters.HalfEven} halfOdd={counters.HalfOdd} positiveHalf={counters.PositiveHalf} " +
        $"negativeHalf={counters.NegativeHalf} wrappedSteps={counters.WrappedSteps} exactQ32_32=passed allocator=passed");
}

static async Task RunCase(Scenario scenario, int ticks, GaStartupResult? startup, GaContractIdentity? contract, Counters counters)
{
    var oracle = new MovementOracle(scenario, counters);
    using var cpu = new World(new WorldOptions(scenario.CpuChunk));
    using var gpu = startup == null ? null : new World(new WorldOptions(scenario.GpuChunk));
    Populate(cpu, oracle);
    if (gpu != null)
        Populate(gpu, oracle);

    var cpuWorkload = new DeterministicWorkload<MovementFrame>();
    MovementAdapter.Register(cpuWorkload, SystemStage.Update);
    using var cpuRunner = new DeterministicRunner<MovementFrame>(cpu, cpuWorkload, scenario.Workers);
    long caseGpuRows = 0;
    var gpuWorkload = new DeterministicWorkload<MovementFrame>();
    if (startup != null && contract != null)
    {
        RegisterGpu(gpuWorkload, startup, contract, timing =>
        {
            caseGpuRows += timing.Rows;
            counters.DeviceRows += timing.Rows;
            counters.DeviceBatches++;
        });
    }
    using var gpuRunner = gpu == null ? null : new DeterministicRunner<MovementFrame>(gpu, gpuWorkload);
    string cpuLabel = $"CPU {scenario}";
    string gpuLabel = $"GPU {scenario}";
    VerifyWorld(cpu, oracle, cpuLabel, 0);
    if (gpu != null)
        VerifyWorld(gpu, oracle, gpuLabel, 0);
    for (int tick = 1; tick <= ticks; tick++)
    {
        long step = MovementOracle.StepAt(tick);
        oracle.Integrate(step);
        var frame = new MovementFrame { DeltaTime = Fixed.FromRaw(step) };
        cpuRunner.BeginTick(tick, frame, frame.DeltaTime);
        gpuRunner?.BeginTick(tick, frame, frame.DeltaTime);
        await Complete(cpuRunner, scenario.CpuBudget, tick);
        VerifyWorld(cpu, oracle, cpuLabel, tick);
        if (gpuRunner != null && gpu != null)
        {
            await Complete(gpuRunner, scenario.GpuBudget, tick);
            if (caseGpuRows != (long)scenario.Rows * tick)
                throw new InvalidOperationException($"{gpuLabel} tick={tick}: missing device rows, saw {caseGpuRows}.");
            VerifyWorld(gpu, oracle, gpuLabel, tick);
        }
        if (ticks >= 1_000 && tick % 1_000 == 0)
            Console.WriteLine($"progress tick={tick}/{ticks} deviceRows={caseGpuRows}");
    }
}

static void RegisterGpu(DeterministicWorkload<MovementFrame> workload, GaStartupResult startup,
    GaContractIdentity contract, Action<GaBatchTimings> observe)
{
    var batch = new GaBatchProcessor<MovementFrame>(startup, contract.SystemId, contract.SourceDigest, contract.IrDigest,
        contract.ComponentSchemaDigest, MovementAdapterRowCodec.WordsPerRow, MovementAdapterRowCodec.Pack<MovementFrame>,
        MovementAdapterRowCodec.Apply<MovementFrame>, 0, observe, null, false, [], [], MovementAdapterRowCodec.TickResource);
    workload.AddBatchSystem(SystemStage.Update, MovementAdapterRowCodec.CreateQuery(), batch);
}

static void Populate(World world, MovementOracle oracle)
{
    world.CreateEntity(MovementOracle.PositionOnly.ToComponent());
    world.CreateEntity(MovementOracle.VelocityOnly.ToComponent());
    if (oracle.Positions.Length > 0)
        world.DestroyEntity(world.CreateEntity());
    for (int row = 0; row < oracle.Positions.Length; row++)
        world.CreateEntity(oracle.Positions[row].ToComponent(), oracle.Velocities[row].ToComponent());
    world.DestroyEntity(world.CreateEntity());
}

static void VerifyWorld(World world, MovementOracle oracle, string lane, int tick)
{
    var snapshot = world.OpenCheckpoint();
    int rows = oracle.Positions.Length;
    if (snapshot.SlotCount != rows + 3 || snapshot.FreeCount != 1)
        throw new InvalidOperationException($"{lane} tick={tick}: allocator shape changed.");
    Span<int> free = stackalloc int[1];
    snapshot.CopyFreeIds(free);
    if (free[0] != rows + 2 || snapshot.GetSlot(free[0]) is not { Alive: false, Version: 2 })
        throw new InvalidOperationException($"{lane} tick={tick}: free slot changed.");
    Entity positionOnly = world.BindEntity(0, 1);
    Entity velocityOnly = world.BindEntity(1, 1);
    if (snapshot.GetComponentCount(positionOnly) != 1 || snapshot.GetComponentCount(velocityOnly) != 1 ||
        MotionPosition.Read(world.Get<MovementPosition>(positionOnly)) != MovementOracle.PositionOnly ||
        MotionVelocity.Read(world.Get<MovementVelocity>(velocityOnly)) != MovementOracle.VelocityOnly)
        throw new InvalidOperationException($"{lane} tick={tick}: a row excluded by the movement query changed.");

    for (int row = 0; row < rows; row++)
    {
        int generation = row == 0 ? 2 : 1;
        var slot = snapshot.GetSlot(row + 2);
        if (!slot.Alive || slot.Version != generation)
            throw new InvalidOperationException($"{lane} tick={tick} row={row}: unexpected generation {slot.Version} or alive {slot.Alive}.");
        Entity entity = world.BindEntity(row + 2, generation);
        var position = MotionPosition.Read(world.Get<MovementPosition>(entity));
        var velocity = MotionVelocity.Read(world.Get<MovementVelocity>(entity));
        if (snapshot.GetComponentCount(entity) != 2 || position != oracle.Positions[row] || velocity != oracle.Velocities[row])
            throw new InvalidOperationException($"{lane} tick={tick} row={row} id={entity.Id}:{generation}: " +
                $"expected {oracle.Positions[row]}, {oracle.Velocities[row]}; actual {position}, {velocity}.");
    }
}

static async Task Complete(DeterministicRunner<MovementFrame> runner, int budget, int tick)
{
    var timer = Stopwatch.StartNew();
    var spinner = new SpinWait();
    var status = runner.Advance(budget);
    while (status == DeterministicTickStatus.Pending && timer.Elapsed < TimeSpan.FromSeconds(20))
    {
        if (OperatingSystem.IsBrowser())
            await Task.Delay(1);
        else
            spinner.SpinOnce(sleep1Threshold: -1);
        status = runner.Advance(budget);
    }
    if (status != DeterministicTickStatus.Completed || runner.LastCompleted is not { } result ||
        result.TickIndex != tick || result.Events.Count != 0)
        throw new InvalidOperationException($"Movement tick {tick} returned {status} or unexpected events.", runner.Fault);
}

#if !GA_BROWSER_CONFORMANCE
static IDisposable Open(string target, string library, out IReadOnlyList<IGaDevice> devices)
{
    if (target == "vulkan")
    {
        var backend = GaVulkanBackend.Open(library);
        devices = backend.Devices;
        return backend;
    }
    if (target == "d3d11")
    {
        var backend = GaD3D11Backend.Open(library);
        devices = backend.Devices;
        return backend;
    }
    if (target is "opengl" or "gles")
    {
        var backend = target == "gles"
            ? GaOpenGlBackend.OpenEsFromEnvironment(library)
            : GaOpenGlBackend.Open(library);
        devices = backend.Devices;
        return backend;
    }
    var d3d12 = GaD3D12Backend.Open(library);
    devices = d3d12.Devices;
    return d3d12;
}
#endif

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal readonly record struct Scenario(int Rows, uint Seed, int CpuChunk, int GpuChunk, int Workers, int CpuBudget, int GpuBudget);

internal sealed class Counters
{
    internal long DeviceRows;
    internal long DeviceBatches;
    internal long DebrisRows;
    internal long ParticleRows;
    internal long HalfEven;
    internal long HalfOdd;
    internal long PositiveHalf;
    internal long NegativeHalf;
    internal long WrappedSteps;
}

internal readonly record struct MotionPosition(long X, long Y, long PreviousX, long PreviousY, long Angle, int CellX, int CellY, int Steps)
{
    internal MovementPosition ToComponent() => new()
    {
        X = Fixed.FromRaw(X), Y = Fixed.FromRaw(Y),
        PreviousX = Fixed.FromRaw(PreviousX), PreviousY = Fixed.FromRaw(PreviousY),
        Angle = Fixed.FromRaw(Angle), CellX = CellX, CellY = CellY, Steps = Steps
    };

    internal static MotionPosition Read(MovementPosition position) => new(
        position.X.Raw, position.Y.Raw, position.PreviousX.Raw, position.PreviousY.Raw,
        position.Angle.Raw, position.CellX, position.CellY, position.Steps);
}

internal readonly record struct MotionVelocity(long X, long Y, long Angular, bool IsDebris)
{
    internal MovementVelocity ToComponent() => new()
    {
        X = Fixed.FromRaw(X), Y = Fixed.FromRaw(Y), Angular = Fixed.FromRaw(Angular), IsDebris = IsDebris
    };

    internal static MotionVelocity Read(MovementVelocity velocity) => new(
        velocity.X.Raw, velocity.Y.Raw, velocity.Angular.Raw, velocity.IsDebris);
}

internal sealed class MovementOracle
{
    private const long Units = 4_294_967_296L;
    private readonly Counters _counters;
    internal static readonly MotionPosition PositionOnly = new(Units + 9, -5 * Units, 17, -23, 81, 41, -12, 421);
    internal static readonly MotionVelocity VelocityOnly = new(-10, 12, Units - 31, true);

    internal MotionPosition[] Positions { get; }
    internal MotionVelocity[] Velocities { get; }

    internal MovementOracle(Scenario scenario, Counters counters)
    {
        _counters = counters;
        Positions = new MotionPosition[scenario.Rows];
        Velocities = new MotionVelocity[scenario.Rows];
        uint random = scenario.Seed;
        for (int row = 0; row < scenario.Rows; row++)
        {
            var (x, y, angle) = (Sample(ref random), Sample(ref random), Sample(ref random));
            Positions[row] = new MotionPosition(x, y, -17, 29, angle, checked((int)(x / Units)), checked((int)(y / Units)),
                row == 0 ? int.MaxValue - 1 : row);
            Velocities[row] = row == 0 ? new MotionVelocity(1, -3, -1, true) :
                new MotionVelocity(Sample(ref random), Sample(ref random), Sample(ref random), row % 2 == 0);
        }
    }

    internal static long StepAt(int tick) => (tick % 5) switch
    {
        0 => 0,
        1 => Units / 2,
        2 => Units / 64 + 1,
        3 => Units / 32,
        _ => Units / 128 - 1
    };

    internal void Integrate(long step)
    {
        for (int row = 0; row < Positions.Length; row++)
        {
            var previous = Positions[row];
            var direction = Velocities[row];
            long x = checked(previous.X + Scale(direction.X, step));
            long y = checked(previous.Y + Scale(direction.Y, step));
            long angle = previous.Angle;
            if (direction.IsDebris)
            {
                angle = checked(angle + Scale(direction.Angular, step));
                _counters.DebrisRows++;
            }
            else
                _counters.ParticleRows++;
            bool wrapped = previous.Steps == int.MaxValue;
            if (wrapped)
                _counters.WrappedSteps++;
            int steps = wrapped ? int.MinValue : previous.Steps + 1;
            Positions[row] = new MotionPosition(x, y, previous.X, previous.Y, angle,
                checked((int)(x / Units)), checked((int)(y / Units)), steps);
        }
    }

    private long Scale(long value, long delta)
    {
        BigInteger product = (BigInteger)value * delta;
        BigInteger whole = BigInteger.DivRem(BigInteger.Abs(product), Units, out var left);
        BigInteger twice = left << 1;
        if (twice == Units)
        {
            if (whole.IsEven)
                _counters.HalfEven++;
            else
                _counters.HalfOdd++;
            if (product.Sign > 0)
                _counters.PositiveHalf++;
            else
                _counters.NegativeHalf++;
        }
        if (twice > Units || twice == Units && !whole.IsEven)
            whole += BigInteger.One;
        return (long)(product.Sign < 0 ? -whole : whole);
    }

    private static long Sample(ref uint state)
    {
        state = unchecked(state * 1_664_525u + 1_013_904_223u);
        return unchecked((int)state) * 13L;
    }
}
