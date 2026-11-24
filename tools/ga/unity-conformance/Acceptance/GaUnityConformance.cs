using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using SKECS;
using SKECS.GA;
using SKECS.GA.Unity;
using SKECS.Numerics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class GaUnityConformance
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static async void RunPlayer()
    {
        int exit = await Run();
        Application.Quit(exit);
    }

#if UNITY_EDITOR
    public static async void RunEditor()
    {
        int exit = await Run();
        UnityEditor.EditorApplication.Exit(exit);
    }
#endif

    private static async Task<int> Run()
    {
        try
        {
            bool cpuOnlyHost = Environment.GetCommandLineArgs().Contains("-ga-cpu");
            var movement = UnityMovement.LoadAsset();
            var nested = UnityNestedFault.LoadAsset();
            var targetRead = UnityTargetRead.LoadAsset();
            var shotBurst = UnityShotBurst.LoadAsset();
            var spatialParent = UnitySpatialParent.LoadAsset();
            var thermalInventory = UnityThermalInventory.LoadAsset();
            var multiHit = UnityMultiHit.LoadAsset();
            var carrierCapacity = UnityCarrierCapacity.LoadAsset();
            var assets = new[] { movement, nested, targetRead, shotBurst, spatialParent, thermalInventory, multiHit, carrierCapacity };
            Check(Array.TrueForAll(assets, asset => asset), "The packaged Unity GA assets are missing.");
            var movementRequest = UnityMovement.CreateRequest(movement);
            var nestedRequest = UnityNestedFault.CreateRequest(nested);
            var requests = new[] { movementRequest, nestedRequest,
                UnityTargetRead.CreateRequest(targetRead), UnityShotBurst.CreateRequest(shotBurst),
                UnitySpatialParent.CreateRequest(spatialParent), UnityThermalInventory.CreateRequest(thermalInventory),
                UnityMultiHit.CreateRequest(multiHit), UnityCarrierCapacity.CreateRequest(carrierCapacity) };
            var devices = GaUnityDevice.Discover(assets);
            cpuOnlyHost |= Application.platform == RuntimePlatform.WebGLPlayer && devices.Count == 0;
            Debug.Log($"GA_UNITY_DEVICE Unity={Application.unityVersion} api={SystemInfo.graphicsDeviceType} " +
                $"version={SystemInfo.graphicsDeviceVersion} gpu={SystemInfo.graphicsDeviceName} " +
                $"compute={SystemInfo.supportsComputeShaders} readback={SystemInfo.supportsAsyncGPUReadback}");
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                !Application.isEditor && assembly.GetName().Name == "SKECS.GA.Compiler"), "Compiler assembly was loaded in the Player.");

            var dim = new DefaultAsyncProbeDevice();
            var defaultPreparation = await ((IGaDevice)dim).PrepareAsync(movementRequest.Artifacts.Single().Bundle);
            Check(dim.Calls == 1 && defaultPreparation.Reason == GaStartupReason.ProbeFailed,
                "Default IGaDevice.PrepareAsync did not invoke the implementation of Prepare.");

            using var cpu = await GaStartup.SelectAsync(GaPolicy.CpuOnly, requests, devices);
            VerifyPlan(cpu, GaSelectedPlan.CpuOnly, GaStartupReason.CpuOnlyPolicy);
            using var noCompute = await GaStartup.SelectAsync(GaPolicy.Auto, requests, Array.Empty<IGaDevice>());
            VerifyPlan(noCompute, GaSelectedPlan.CpuOnly, GaStartupReason.NoComputeApi);
            using var strictNoCompute = await GaStartup.SelectAsync(GaPolicy.RequireGpu, requests, Array.Empty<IGaDevice>());
            VerifyPlan(strictNoCompute, GaSelectedPlan.StartupFailure, GaStartupReason.NoComputeApi);

            if (cpuOnlyHost)
            {
                Check(devices.Count == 0, "The CPU-only host must use a graphics device without GA compute.");
                await CompareMovement(cpu, noCompute, 65, 1200, 0);
                CompareFault(await RunNested(cpu), await RunNested(noCompute));
                await GaUnityGameplayConformance.Run(cpu, noCompute);
                await GaUnityExtendedGameplayConformance.Run(cpu, noCompute);
                await GaUnityServiceConformance.Run(useGpu: false);
                await GaUnityLifecycleConformance.Run(useGpu: false);
                Debug.Log("SKECS_GA_UNITY_OK policy=CpuOnly,Auto,RequireGpu deviceRows=0 ticks=1200 defaultAsync=passed");
                return 0;
            }

            Check(devices.Count == 1, "The GPU lane requires a Unity compute and asynchronous readback device.");
            using var defaultAuto = await GaStartup.SelectAsync(GaPolicy.Auto, requests, devices);
            VerifyPlan(defaultAuto, GaSelectedPlan.CpuOnly, GaStartupReason.UnqualifiedConfiguration);
            using var strict = await GaStartup.SelectAsync(GaPolicy.RequireGpu, requests, devices);
            VerifyPlan(strict, GaSelectedPlan.StartupFailure, GaStartupReason.UnqualifiedConfiguration);
            using var auto = await GaStartup.SelectAsync(GaPolicy.Auto, requests, devices, allowExperimental: true);
            VerifyPlan(auto, GaSelectedPlan.Gpu, GaStartupReason.None);
            using var gpu = await GaStartup.SelectAsync(GaPolicy.RequireGpu, requests, devices, allowExperimental: true);
            VerifyPlan(gpu, GaSelectedPlan.Gpu, GaStartupReason.None);

            var missing = UnityMovement.CreateRequest(null);
            using var missingPlan = await GaStartup.SelectAsync(GaPolicy.Auto, new[] { missing }, devices, allowExperimental: true);
            VerifyPlan(missingPlan, GaSelectedPlan.CpuOnly, GaStartupReason.MissingArtifact);
            var stale = new GaSystemRequest(movementRequest.Contract with { SimulationContractId = "stale/1" }, movementRequest.Artifacts);
            using var stalePlan = await GaStartup.SelectAsync(GaPolicy.RequireGpu, new[] { stale }, devices, allowExperimental: true);
            VerifyPlan(stalePlan, GaSelectedPlan.StartupFailure, GaStartupReason.IncompatibleManifest);

            long gpuRows = 0;
            foreach (int size in new[] { 0, 1, 63, 64, 65, 129 })
                gpuRows += await CompareMovement(cpu, auto, size, 6, size);
            long[] concurrentRows = await Task.WhenAll(
                CompareMovement(cpu, auto, 65, 1200, 27),
                CompareMovement(noCompute, gpu, 129, 30, 91));
            gpuRows += concurrentRows.Sum();
            CompareFault(await RunNested(cpu), await RunNested(gpu));
            await VerifyDiscard((GaPackedKernel)auto.GetPreparedKernel(nestedRequest.Contract.SystemId));
            await GaUnityGameplayConformance.Run(cpu, gpu);
            await GaUnityExtendedGameplayConformance.Run(cpu, gpu);
            await GaUnityServiceConformance.Run(useGpu: true);
            await GaUnityLifecycleConformance.Run(useGpu: true);

            Debug.Log($"SKECS_GA_UNITY_OK policy=CpuOnly,Auto,RequireGpu deviceRows={gpuRows} ticks=1200 " +
                "fault=nested-divide-by-zero discard=passed concurrent=passed defaultAsync=passed qualification=Experimental");
            return 0;
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            return 1;
        }
    }

    private static void VerifyPlan(GaStartupResult startup, GaSelectedPlan plan, GaStartupReason reason)
    {
        foreach (var decision in startup.Decisions)
            Debug.Log($"GA_UNITY_PLAN policy={startup.RequestedPolicy} system={decision.Contract.SystemId} " +
                $"route={decision.Route} reason={decision.Reason} artifact={decision.ArtifactId} " +
                $"profile={decision.ComputeProfile} qualification={decision.Qualification} detail={decision.Detail}");
        Check(startup.SelectedPlan == plan && startup.Decisions.All(item => item.Reason == reason),
            $"Expected plan {plan}/{reason} for {startup.RequestedPolicy}, saw {startup.SelectedPlan}.");
    }

    private static async Task<long> CompareMovement(GaStartupResult cpu, GaStartupResult target, int count, int ticks, int seed)
    {
        using var expected = new World(new WorldOptions(31));
        using var actual = new World(new WorldOptions(127));
        var expectedRows = PopulateMovement(expected, count, seed);
        var actualRows = PopulateMovement(actual, count, seed);
        long dispatched = 0;
        var cpuWork = UnityMovement.Register(new DeterministicWorkload<MovementFrame>(), SystemStage.Update, cpu);
        var targetWork = UnityMovement.Register(new DeterministicWorkload<MovementFrame>(), SystemStage.Update, target,
            observe: timings => dispatched += timings.Rows);
        using var cpuRunner = new DeterministicRunner<MovementFrame>(expected, cpuWork);
        using var targetRunner = new DeterministicRunner<MovementFrame>(actual, targetWork);
        Compare(0);
        for (int tick = 1; tick <= ticks; tick++)
        {
            var frame = new MovementFrame { DeltaTime = Fixed.FromRaw(tick % 2 == 0 ? 1L << 31 : -(1L << 30)) };
            cpuRunner.BeginTick(tick, frame, frame.DeltaTime);
            targetRunner.BeginTick(tick, frame, frame.DeltaTime);
            Check((await Advance(cpuRunner, 3)).Status == DeterministicTickStatus.Completed, "Movement CPU tick faulted.", cpuRunner.Fault);
            Check((await Advance(targetRunner, 1)).Status == DeterministicTickStatus.Completed, "Movement target tick faulted.", targetRunner.Fault);
            Check(cpuRunner.LastCompleted.TickIndex == tick && targetRunner.LastCompleted.TickIndex == tick &&
                cpuRunner.LastCompleted.Events.Count == 0 && targetRunner.LastCompleted.Events.Count == 0, "Unexpected movement publication.");
            Compare(tick);
        }
        Check(dispatched == (target.SelectedPlan == GaSelectedPlan.Gpu ? (long)count * ticks : 0),
            $"Unexpected device row count for movement rows={count} ticks={ticks}: {dispatched}.");
        return dispatched;

        void Compare(int tick)
        {
            for (int row = 0; row < count; row++)
            {
                var a = expected.Get<MovementPosition>(expectedRows[row]);
                var b = actual.Get<MovementPosition>(actualRows[row]);
                Check(a.X.Raw == b.X.Raw && a.Y.Raw == b.Y.Raw && a.Angle.Raw == b.Angle.Raw &&
                    a.PreviousX.Raw == b.PreviousX.Raw && a.PreviousY.Raw == b.PreviousY.Raw &&
                    a.CellX == b.CellX && a.CellY == b.CellY && a.Steps == b.Steps,
                    $"Movement mismatch at tick {tick}, row {row}, seed {seed}.");
            }
        }
    }

    private static Entity[] PopulateMovement(World world, int count, int seed)
    {
        var rows = new Entity[count];
        for (int row = 0; row < count; row++)
        {
            rows[row] = world.CreateEntity(new MovementPosition
            {
                X = Fixed.FromRaw((long)row << 30),
                Y = Fixed.FromRaw(-((long)seed << 28)),
                Angle = Fixed.FromRaw(row),
                Steps = row == 0 ? int.MaxValue : row
            }, new MovementVelocity
            {
                X = Fixed.FromRaw(row % 2 == 0 ? row + 1 : -row),
                Y = Fixed.FromRaw((long)(row + seed) << 28),
                Angular = Fixed.FromRaw(7L - seed),
                IsDebris = row % 2 == 0
            });
        }
        return rows;
    }

    private static async Task<GaExecutionException> RunNested(GaStartupResult startup)
    {
        using var world = new World();
        var rows = new Entity[67];
        for (int row = 0; row < rows.Length; row++)
            rows[row] = world.CreateEntity(new NestedFaultRow { Divisor = 3, Result = 7 });
        var work = UnityNestedFault.Register(new DeterministicWorkload<byte>(), SystemStage.Update, startup);
        using var runner = new DeterministicRunner<byte>(world, work);
        runner.BeginTick(0, 0, Fixed.One);
        Check((await Advance(runner, 2)).Status == DeterministicTickStatus.Completed, "Nested baseline tick faulted.", runner.Fault);
        foreach (var entity in rows)
            Check(world.Get<NestedFaultRow>(entity).Result == 45, "Nested baseline returned the wrong sum.");
        world.Get<NestedFaultRow>(rows[65]).Divisor = 2;
        world.Get<NestedFaultRow>(rows[66]).Divisor = 1;
        runner.BeginTick(1, 0, Fixed.One);
        var status = (await Advance(runner, 2)).Status;
        Check(status == DeterministicTickStatus.Faulted && runner.LastCompleted.TickIndex == 0,
            "A nested fault was published as a completed tick.", runner.Fault);
        var fault = runner.Fault as GaExecutionException;
        Check(fault != null && fault.SystemId == "unity.nested-fault" && fault.TickIndex == 1 &&
            fault.SourceOrderOrdinal == 65 && fault.SourceId == rows[65].Id &&
            fault.KernelFault.Kind == GaFaultKind.DivideByZero && fault.KernelFault.EvaluationOrdinal == 14,
            "The first nested GA fault had the wrong source or evaluation order.", runner.Fault);
        runner.DiscardWorld();
        return fault;
    }

    private static void CompareFault(GaExecutionException cpu, GaExecutionException target)
    {
        Check(cpu.KernelFault.Equals(target.KernelFault) && cpu.SourceGeneration == target.SourceGeneration &&
            cpu.Stage == target.Stage && cpu.SystemOrder == target.SystemOrder && cpu.KernelOrder == target.KernelOrder,
            "CPU and target selected different canonical nested faults.");
    }

    private static async Task VerifyDiscard(GaPackedKernel kernel)
    {
        var submission = kernel.BeginDispatch(new uint[] { 2, 7, 3, 7, 2 }, 2);
        Check(!submission.IsCompleted, "The discard check requires a pending Unity readback.");
        submission.Discard();
        await kernel.DisposeAsync();
        try
        {
            await submission.DrainAsync();
            throw new InvalidOperationException("A canceled Unity kernel published output after disposal.");
        }
        catch (OperationCanceledException)
        {
            Check(submission.IsCompleted, "The canceled Unity kernel was released before draining.");
        }
    }

    internal static async ValueTask<(DeterministicTickStatus Status, bool Yielded)> Advance<T>(
        DeterministicRunner<T> runner, int budget) where T : unmanaged
    {
        var timeout = Stopwatch.StartNew();
        var status = runner.Advance(budget);
        bool yielded = false;
        while (status == DeterministicTickStatus.Pending)
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(25))
                throw new TimeoutException("The Unity deterministic runner did not finish the tick.");
            yielded = true;
            await Task.Yield();
            status = runner.Advance(budget);
        }
        return (status, yielded);
    }

    private static void Check(bool condition, string message, Exception cause = null)
    {
        if (!condition)
            throw new InvalidOperationException(message, cause);
    }

    private sealed class DefaultAsyncProbeDevice : IGaDevice
    {
        internal int Calls;
        public GaDeviceInfo Info => new GaDeviceInfo("probe", "unity", "1", "probe", "1");
        public GaArtifactFormat Format => GaArtifactFormat.UnityCompute;
        public IReadOnlyList<GaDeviceProfile> Profiles => Array.Empty<GaDeviceProfile>();

        public GaPreparation Prepare(GaArtifactBundle artifact)
        {
            Calls++;
            return new GaPreparation(null, GaStartupReason.ProbeFailed);
        }
    }
}
