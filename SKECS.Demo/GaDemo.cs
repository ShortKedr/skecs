using System.Globalization;
using System.Security.Cryptography;
using SKECS;
using SKECS.GA;
using SKECS.GA.Backend.D3D12;
using SKECS.GA.Backend.Vulkan;
using SKECS.Numerics;
using SKECS.Serialization;

internal static class GaDemo
{
    private const string Artifact = "ga-movement";
    private static readonly CodecRegistry CheckpointSchema = new CodecRegistry()
        .Register(new ComponentSchema(1001, 1, new FieldSchema(1, FieldKind.Int64),
            new FieldSchema(2, FieldKind.Int32)), new PositionCodec())
        .Register(new ComponentSchema(1002, 1, new FieldSchema(1, FieldKind.Int64),
            new FieldSchema(2, FieldKind.Int64), new FieldSchema(3, FieldKind.Boolean)), new VelocityCodec());

    internal static int Run(string[] args)
    {
        var policy = args.Length == 0 ? (GaPolicy?)null : args[0] switch
        {
            "cpu-only" => GaPolicy.CpuOnly,
            "auto" => GaPolicy.Auto,
            "require-gpu" => GaPolicy.RequireGpu,
            _ => null
        };
        string target = args.Length == 9 ? args[8] : "vulkan";
        if (policy == null || args.Length < 3 || args.Length > 9 || target is not ("vulkan" or "d3d12") ||
            !Read(args, 3, 0, 0, out int deviceIndex) ||
            !Read(args, 4, 257, 1, out int count) ||
            !Read(args, 5, 8, 1, out int ticks) ||
            !Read(args, 6, 63, 1, out int chunkCapacity) ||
            !Read(args, 7, 1, 1, out int budget))
        {
            Console.Error.WriteLine("Usage: SKECS.Demo ga <cpu-only|auto|require-gpu> <bundle-dir> <native-library> [device] [entities] [ticks] [chunk-capacity] [job-budget] [vulkan|d3d12]");
            return 2;
        }

        IDisposable? backend = null;
        try
        {
            IGaDevice[] devices = [];
            GaArtifactCandidate[] candidates = [];
            if (policy != GaPolicy.CpuOnly)
            {
                var discovered = Probe(args[2], target, out backend);
                if (discovered.Count != 0)
                {
                    if ((uint)deviceIndex >= discovered.Count)
                        throw new ArgumentOutOfRangeException(nameof(deviceIndex), "The selected graphics device index is unavailable.");
                    devices = [discovered[deviceIndex]];
                }
                candidates = LoadCandidates(args[1], target);
            }

            var contract = new GaContractIdentity(GaCpuAdapterRowCodec.SystemId, "demo.fixed-movement/1",
                GaCpuAdapterRowCodec.SourceDigest, GaCpuAdapterRowCodec.IrDigest, GaCpuAdapterRowCodec.ComponentSchemaDigest,
                GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion);
            var request = new GaSystemRequest(contract, candidates);
            using var startup = GaStartup.Select(policy.Value, [request], devices, allowExperimental: true);
            var decision = startup.Decisions[0];
            Console.WriteLine($"GA route={decision.Route} reason={decision.Reason} device={decision.Device?.Device} api={decision.Device?.ApiVersion} driver={decision.Device?.Driver} profile={decision.ComputeProfile} qualification={decision.Qualification} artifact={decision.ArtifactId}");
            if (!startup.Succeeded)
                return 1;
            var workload = decision.Route == GaRoute.Gpu
                ? new DeterministicWorkload<byte>().AddBatchSystem(SystemStage.Update, GaCpuAdapterRowCodec.CreateQuery(),
                    new GaBatchProcessor<byte>(startup, GaCpuAdapterRowCodec.SystemId,
                        GaCpuAdapterRowCodec.SourceDigest, GaCpuAdapterRowCodec.IrDigest, GaCpuAdapterRowCodec.ComponentSchemaDigest,
                        GaCpuAdapterRowCodec.WordsPerRow, GaCpuAdapterRowCodec.Pack<byte>, GaCpuAdapterRowCodec.Apply<byte>))
                : CpuWorkload();
            VerifyTicks(workload, count, ticks, chunkCapacity, budget);
            if (decision.Route == GaRoute.Gpu)
                VerifyFault(workload, chunkCapacity, budget);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            backend?.Dispose();
        }
    }

    private static IReadOnlyList<IGaDevice> Probe(string path, string target, out IDisposable? backend)
    {
        string? error;
        if (target == "d3d12")
        {
            if (GaD3D12Backend.TryOpen(path, out var d3d12, out error))
            {
                backend = d3d12;
                return d3d12.Devices;
            }
        }
        else if (GaVulkanBackend.TryOpen(path, out var vulkan, out error))
        {
            backend = vulkan;
            return vulkan.Devices;
        }
        Console.Error.WriteLine($"{target} probe unavailable: {error}");
        backend = null;
        return [];
    }

    private static GaArtifactCandidate[] LoadCandidates(string bundleDirectory, string target)
    {
        string stem = Path.Combine(bundleDirectory, Artifact);
        if (!File.Exists(stem + ".manifest.json"))
            return [];
        try
        {
            return [GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
                File.ReadAllText(stem + ".content-id").Trim(), File.ReadAllBytes(stem + (target == "d3d12" ? ".dxil" : ".spv")),
                File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"))];
        }
        catch (IOException error)
        {
            Console.Error.WriteLine($"The Demo GPU bundle is incomplete: {error.Message}");
            return [];
        }
    }

    private static void VerifyTicks(DeterministicWorkload<byte> selected, int count, int ticks, int chunkCapacity, int budget)
    {
        using var cpuWorld = new World(new WorldOptions(32));
        using var selectedWorld = new World(new WorldOptions(chunkCapacity));
        var cpuEntities = Seed(cpuWorld, count, fault: false);
        var selectedEntities = Seed(selectedWorld, count, fault: false);
        using var cpu = new DeterministicRunner<byte>(cpuWorld, CpuWorkload(), workerCount: 2);
        using var runner = new DeterministicRunner<byte>(selectedWorld, selected);
        using var cpuCheckpoint = new MemoryStream();
        using var selectedCheckpoint = new MemoryStream();
        Compare(0);
        long pending = 0;
        for (long tick = 1; tick <= ticks; tick++)
        {
            cpu.BeginTick(tick, 0, Fixed.One);
            var cpuStatus = cpu.Advance(int.MaxValue);
            if (cpuStatus != DeterministicTickStatus.Completed)
                throw new InvalidOperationException($"CPU runner faulted on tick {tick}.", cpu.Fault);

            var last = runner.LastCompleted;
            runner.BeginTick(tick, 0, Fixed.One);
            VerifyLease(selectedWorld, selectedEntities[0]);
            var status = Advance(runner, budget, () =>
            {
                pending++;
                VerifyLease(selectedWorld, selectedEntities[0]);
                if (!ReferenceEquals(last, runner.LastCompleted))
                    throw new InvalidOperationException("The runner published a tick while its work was pending.");
            });
            if (status != DeterministicTickStatus.Completed || runner.LastCompleted?.TickIndex != tick)
                throw new InvalidOperationException($"Selected runner faulted on tick {tick}.", runner.Fault);
            Compare(tick);
            // Exercise fresh device packing after external edits to a completed world.
            if (tick < ticks && (tick & 1) == 0)
            {
                cpuWorld.Get<GaPosition>(cpuEntities[0]).X += Fixed.FromRaw(11);
                selectedWorld.Get<GaPosition>(selectedEntities[0]).X += Fixed.FromRaw(11);
            }
        }
        var hash = Convert.ToHexString(SHA256.HashData(cpuCheckpoint.GetBuffer().AsSpan(0, (int)cpuCheckpoint.Length)));
        Console.WriteLine($"GA runner parity verifiedStates={ticks + 1L} entities={count} pendingPolls={pending} checkpointSha256={hash}");

        void Compare(long tick)
        {
            for (int row = 0; row < cpuEntities.Length; row++)
            {
                var left = cpuEntities[row];
                var right = selectedEntities[row];
                var cpuValue = cpuWorld.Get<GaPosition>(left);
                var gpuValue = selectedWorld.Get<GaPosition>(right);
                var cpuVelocity = cpuWorld.Get<GaVelocity>(left);
                var gpuVelocity = selectedWorld.Get<GaVelocity>(right);
                if (left.Id != right.Id || left.Version != right.Version ||
                    cpuValue.X.Raw != gpuValue.X.Raw || cpuValue.Cell != gpuValue.Cell ||
                    cpuVelocity.Delta.Raw != gpuVelocity.Delta.Raw || cpuVelocity.Step.Raw != gpuVelocity.Step.Raw ||
                    cpuVelocity.Divide != gpuVelocity.Divide)
                    throw new InvalidDataException($"GA runner diverged at tick {tick}, row {row}.");
            }
            cpuCheckpoint.SetLength(0);
            selectedCheckpoint.SetLength(0);
            WorldSerializer.WriteBinary(cpuWorld, cpuCheckpoint, CheckpointSchema);
            WorldSerializer.WriteBinary(selectedWorld, selectedCheckpoint, CheckpointSchema);
            if (!cpuCheckpoint.GetBuffer().AsSpan(0, (int)cpuCheckpoint.Length)
                .SequenceEqual(selectedCheckpoint.GetBuffer().AsSpan(0, (int)selectedCheckpoint.Length)))
                throw new InvalidDataException($"GA canonical checkpoint diverged at tick {tick}.");
        }
    }

    private static void VerifyFault(DeterministicWorkload<byte> selected, int chunkCapacity, int budget)
    {
        using var cpuWorld = new World(new WorldOptions(32));
        using var gpuWorld = new World(new WorldOptions(chunkCapacity));
        var cpuEntities = Seed(cpuWorld, 131, fault: true);
        var gpuEntities = Seed(gpuWorld, 131, fault: true);
        using var cpu = new DeterministicRunner<byte>(cpuWorld, CpuWorkload(), workerCount: 2);
        using var gpu = new DeterministicRunner<byte>(gpuWorld, selected);
        cpu.BeginTick(1, 0, Fixed.One);
        gpu.BeginTick(1, 0, Fixed.One);
        if (Advance(cpu, budget) != DeterministicTickStatus.Faulted ||
            Advance(gpu, budget) != DeterministicTickStatus.Faulted ||
            cpu.Fault is not GaExecutionException expected || gpu.Fault is not GaExecutionException actual ||
            expected.SystemId != actual.SystemId || expected.TickIndex != actual.TickIndex ||
            expected.SourceOrderOrdinal != 64 || actual.SourceOrderOrdinal != expected.SourceOrderOrdinal ||
            expected.SourceId != cpuEntities[64].Id || actual.SourceId != gpuEntities[64].Id ||
            expected.SourceGeneration != actual.SourceGeneration || expected.KernelFault != actual.KernelFault ||
            actual.KernelFault.Kind != GaFaultKind.Overflow || actual.KernelFault.EvaluationOrdinal != 1 ||
            cpu.LastCompleted != null || gpu.LastCompleted != null)
            throw new InvalidOperationException($"GA runner fault parity failed. CPU={cpu.Fault} GPU={gpu.Fault}");
        VerifyLease(gpuWorld, gpuEntities[0]);
        Console.WriteLine($"GA runner fault parity source={actual.SourceOrderOrdinal} evaluation={actual.KernelFault.EvaluationOrdinal} kind={actual.KernelFault.Kind}");
    }

    private static DeterministicWorkload<byte> CpuWorkload() =>
        GaCpuAdapter.Register(new DeterministicWorkload<byte>(), SystemStage.Update);

    private static Entity[] Seed(World world, int count, bool fault)
    {
        var recycled = world.CreateEntity();
        var spare = world.CreateEntity();
        world.DestroyEntity(recycled);
        var entities = new Entity[count];
        for (int row = 0; row < count; row++)
        {
            var position = new GaPosition { X = Fixed.FromInt32(row % 29 - 14) };
            var velocity = new GaVelocity
            {
                Delta = Fixed.FromRaw((row % 7 + 1L) << 26),
                Step = Fixed.FromRatio(5, 4),
                Divide = row % 3 == 0
            };
            if (fault && row == 64)
                position.X = Fixed.MaxValue;
            if (fault && row == 65)
            {
                velocity.Divide = true;
                velocity.Step = Fixed.Zero;
            }
            entities[row] = world.CreateEntity(position, velocity);
        }
        world.DestroyEntity(spare);
        return entities;
    }

    private static DeterministicTickStatus Advance(DeterministicRunner<byte> runner, int budget, Action? pending = null)
    {
        DeterministicTickStatus status;
        var wait = new SpinWait();
        while ((status = runner.Advance(budget)) == DeterministicTickStatus.Pending)
        {
            pending?.Invoke();
            wait.SpinOnce();
        }
        return status;
    }

    private static void VerifyLease(World world, Entity entity)
    {
        try
        {
            _ = world.Get<GaPosition>(entity);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("External access succeeded before the runner released the world.");
    }

    private static bool Read(string[] args, int index, int fallback, int minimum, out int value)
    {
        value = fallback;
        return index >= args.Length ||
            int.TryParse(args[index], NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= minimum;
    }

    private sealed class PositionCodec : IComponentCodec<GaPosition>
    {
        public void Write(in GaPosition value, ComponentWriter fields)
        {
            fields.Set(1, FieldValue.From(value.X.Raw));
            fields.Set(2, FieldValue.From(value.Cell));
        }

        public GaPosition Read(ComponentReader fields) =>
            new() { X = Fixed.FromRaw(fields.Get(1).AsInt64()), Cell = fields.Get(2).AsInt32() };
    }

    private sealed class VelocityCodec : IComponentCodec<GaVelocity>
    {
        public void Write(in GaVelocity value, ComponentWriter fields)
        {
            fields.Set(1, FieldValue.From(value.Delta.Raw));
            fields.Set(2, FieldValue.From(value.Step.Raw));
            fields.Set(3, FieldValue.From(value.Divide));
        }

        public GaVelocity Read(ComponentReader fields) => new()
        {
            Delta = Fixed.FromRaw(fields.Get(1).AsInt64()),
            Step = Fixed.FromRaw(fields.Get(2).AsInt64()),
            Divide = fields.Get(3).AsBoolean()
        };
    }
}
