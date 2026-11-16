#if !GA_BROWSER_CONFORMANCE
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SKECS;
using SKECS.GA;
using SKECS.GA.Backend.Vulkan;
using SKECS.Numerics;

internal static class MovementMeasurements
{
    private const int WarmupTicks = 64;
    private const int MeasuredTicks = 256;

    internal static async Task Run(string[] args)
    {
        if (args is not ["measure", var bundleDirectory, var library, var deviceArgument, var workerArgument, var outputPath] ||
            !int.TryParse(deviceArgument, NumberStyles.None, CultureInfo.InvariantCulture, out int deviceIndex) ||
            !int.TryParse(workerArgument, NumberStyles.None, CultureInfo.InvariantCulture, out int workers) ||
            deviceIndex < 0 || workers < 1)
            throw new ArgumentException("Use measure <Vulkan bundle> <native bridge> <device index> <CPU workers> <output JSON>.");

        var contract = MovementFixture.CreateContract();
        string stem = Path.Combine(bundleDirectory, contract.SystemId);
        var artifact = GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
            File.ReadAllText(stem + ".content-id").Trim(), File.ReadAllBytes(stem + ".spv"),
            File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"));
        var bundle = artifact.Bundle ?? throw new InvalidDataException(artifact.Error);

        long openStart = Stopwatch.GetTimestamp();
        using var backend = GaVulkanBackend.Open(library);
        long openTicks = Stopwatch.GetTimestamp() - openStart;
        if (deviceIndex >= backend.Devices.Count)
            throw new ArgumentOutOfRangeException(nameof(deviceIndex));
        var device = backend.Devices[deviceIndex];
        long startupStart = Stopwatch.GetTimestamp();
        using var startup = GaStartup.Select(GaPolicy.RequireGpu, [new(contract, [artifact])], [device], allowExperimental: true);
        long startupTicks = Stopwatch.GetTimestamp() - startupStart;
        var decision = startup.Decisions.Single();
        if (!startup.Succeeded || decision.Route != GaRoute.Gpu)
            throw new InvalidOperationException($"{decision.Reason} {decision.Detail}");

        var cases = new List<RowCase>();
        foreach (int rows in new[] { 65, 4096, 16384 })
        {
            var measured = await MeasureCase(rows, workers, startup, contract);
            cases.Add(measured);
            Console.WriteLine($"{device.Info.Device}, workers={workers}, rows={rows}: {measured.Samples.Length} exact timed ticks.");
        }

        File.WriteAllText(Path.GetFullPath(outputPath), JsonSerializer.Serialize(new
        {
            Host = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Runtime = RuntimeInformation.FrameworkDescription,
            LogicalProcessors = Environment.ProcessorCount,
            DeviceIndex = deviceIndex,
            device.Info,
            contract,
            bundle.ContentId,
            bundle.Manifest.ArtifactDigest,
            bundle.Manifest.Toolchain,
            NativeBridgeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library))).ToLowerInvariant(),
            Stopwatch.Frequency,
            CpuWorkers = workers,
            WarmupTicks,
            MeasuredTicks,
            NativeOpenTicks = openTicks,
            PipelineStartupTicks = startupTicks,
            Cases = cases
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<RowCase> MeasureCase(int rows, int workers, GaStartupResult startup, GaContractIdentity contract)
    {
        const int chunk = 128;
        const int budget = 64;
        const uint seed = 27;
        var oracle = new MovementOracle(new Scenario(rows, seed, chunk, chunk, workers, budget, budget), new Counters());

        long cpuSetupStart = Stopwatch.GetTimestamp();
        using var cpuWorld = new World(new WorldOptions(chunk));
        MovementFixture.Populate(cpuWorld, oracle);
        var cpuWorkload = new DeterministicWorkload<MovementFrame>();
        MovementAdapter.Register(cpuWorkload, SystemStage.Update);
        using var cpu = new DeterministicRunner<MovementFrame>(cpuWorld, cpuWorkload, workers);
        long cpuSetup = Stopwatch.GetTimestamp() - cpuSetupStart;

        long gpuSetupStart = Stopwatch.GetTimestamp();
        using var gpuWorld = new World(new WorldOptions(chunk));
        MovementFixture.Populate(gpuWorld, oracle);
        var gpuWorkload = new DeterministicWorkload<MovementFrame>();
        int batches = 0;
        int deviceRows = 0;
        long packTicks = 0, deviceTicks = 0, applyTicks = 0;
        MovementFixture.RegisterGpu(gpuWorkload, startup, contract, batch =>
        {
            batches++;
            deviceRows += batch.Rows;
            packTicks += batch.PackTicks;
            deviceTicks += batch.DeviceTicks;
            applyTicks += batch.ApplyTicks;
        });
        using var gpu = new DeterministicRunner<MovementFrame>(gpuWorld, gpuWorkload);
        long gpuSetup = Stopwatch.GetTimestamp() - gpuSetupStart;

        MovementFixture.VerifyWorld(cpuWorld, oracle, "CPU", 0);
        MovementFixture.VerifyWorld(gpuWorld, oracle, "Vulkan", 0);
        var samples = new TickSample[MeasuredTicks];
        for (int tick = 1; tick <= WarmupTicks + MeasuredTicks; tick++)
        {
            long step = MovementOracle.StepAt(tick);
            oracle.Integrate(step);
            var frame = new MovementFrame { DeltaTime = Fixed.FromRaw(step) };
            batches = deviceRows = 0;
            packTicks = deviceTicks = applyTicks = 0;
            Measurement cpuSample, gpuSample;
            if (tick % 2 == 0)
            {
                cpuSample = await MeasureTick(cpu, frame, budget, tick);
                gpuSample = await MeasureTick(gpu, frame, budget, tick);
            }
            else
            {
                gpuSample = await MeasureTick(gpu, frame, budget, tick);
                cpuSample = await MeasureTick(cpu, frame, budget, tick);
            }

            if (deviceRows != rows || batches < 1)
                throw new InvalidOperationException($"Tick {tick} processed {deviceRows} of {rows} device rows.");
            MovementFixture.VerifyWorld(cpuWorld, oracle, "CPU", tick);
            MovementFixture.VerifyWorld(gpuWorld, oracle, "Vulkan", tick);
            if (tick > WarmupTicks)
                samples[tick - WarmupTicks - 1] = new(tick, cpuSample, gpuSample, batches, deviceRows,
                    packTicks, deviceTicks, applyTicks);
        }
        return new(rows, seed, chunk, budget, cpuSetup, gpuSetup, samples);
    }

    private static async Task<Measurement> MeasureTick(DeterministicRunner<MovementFrame> runner,
        MovementFrame frame, int budget, int tick)
    {
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long start = Stopwatch.GetTimestamp();
        runner.BeginTick(tick, frame, frame.DeltaTime);
        await MovementFixture.Complete(runner, budget, tick);
        long elapsed = Stopwatch.GetTimestamp() - start;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        return new(elapsed, allocated);
    }

    private readonly record struct Measurement(long WallTicks, long ProcessManagedBytes);
    private readonly record struct TickSample(int Tick, Measurement Cpu, Measurement Vulkan, int DeviceBatches,
        int DeviceRows, long PackTicks, long DeviceTicks, long ApplyTicks);
    private sealed record RowCase(int Rows, uint Seed, int Chunk, int Budget, long CpuSetupTicks, long VulkanSetupTicks,
        TickSample[] Samples);
}
#endif
