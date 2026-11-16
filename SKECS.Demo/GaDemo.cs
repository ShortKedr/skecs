using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
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
        .Register(new ComponentSchema(1002, 2, new FieldSchema(1, FieldKind.Int64),
            new FieldSchema(2, FieldKind.Int64), new FieldSchema(3, FieldKind.Boolean),
            new FieldSchema(4, FieldKind.Int32), new FieldSchema(5, FieldKind.Int32),
            new FieldSchema(6, FieldKind.Int32), new FieldSchema(7, FieldKind.Int32)), new VelocityCodec())
        .Register(new ComponentSchema(1003, 1, new FieldSchema(1, FieldKind.Int32)), new SignalCodec());
    private static readonly GaSnapshotLayout SnapshotLayout = new([GaScalar.Int32]);

    internal static int Run(string[] args)
    {
        bool measure = args is ["--measure", ..];
        int warmup = 0;
        if (measure)
        {
            if (args.Length < 2 || !Read(args, 1, 0, 0, out warmup))
                return 2;
            args = args[2..];
        }
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
            Console.Error.WriteLine("Usage: SKECS.Demo ga [--measure warmup-ticks] <cpu-only|auto|require-gpu> <bundle-dir> <native-library> [device] [entities] [ticks] [chunk-capacity] [job-budget] [vulkan|d3d12]");
            return 2;
        }

        IDisposable? backend = null;
        try
        {
            var phases = measure ? new BatchPhases() : null;
            long startupStarted = measure ? Stopwatch.GetTimestamp() : 0;
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
                GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
            {
                EventCapacity = GaCpuAdapterRowCodec.EventCapacity,
                UsesSnapshot = GaCpuAdapterRowCodec.UsesSnapshot,
                UsesCommands = GaCpuAdapterRowCodec.UsesCommands
            };
            var request = new GaSystemRequest(contract, candidates);
            using var startup = GaStartup.Select(policy.Value, [request], devices, allowExperimental: true);
            long startupTicks = measure ? Stopwatch.GetTimestamp() - startupStarted : 0;
            var decision = startup.Decisions[0];
            Console.WriteLine($"GA route={decision.Route} reason={decision.Reason} device={decision.Device?.Device} api={decision.Device?.ApiVersion} driver={decision.Device?.Driver} profile={decision.ComputeProfile} qualification={decision.Qualification} artifact={decision.ArtifactId}");
            if (!startup.Succeeded)
                return 1;
            DeterministicWorkload<byte> Workload(GaTickSnapshot snapshot) => decision.Route == GaRoute.Gpu
                ? new DeterministicWorkload<byte>().AddBatchSystem(SystemStage.Update, GaCpuAdapterRowCodec.CreateQuery(),
                    new GaBatchProcessor<byte>(startup, GaCpuAdapterRowCodec.SystemId,
                        GaCpuAdapterRowCodec.SourceDigest, GaCpuAdapterRowCodec.IrDigest, GaCpuAdapterRowCodec.ComponentSchemaDigest,
                        GaCpuAdapterRowCodec.WordsPerRow, GaCpuAdapterRowCodec.Pack<byte>, GaCpuAdapterRowCodec.Apply<byte>,
                        GaCpuAdapterRowCodec.EventCapacity, phases is null ? null : phases.Record, snapshot,
                        usesCommands: GaCpuAdapterRowCodec.UsesCommands))
                : CpuWorkload(snapshot);
            VerifyTicks(Workload, count, ticks, chunkCapacity, budget, warmup, phases, startupTicks, decision);
            if (decision.Route == GaRoute.Gpu)
                VerifyFault(Workload, chunkCapacity, budget);
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

    private static void VerifyTicks(Func<GaTickSnapshot, DeterministicWorkload<byte>> selected, int count, int ticks, int chunkCapacity, int budget,
        int warmup, BatchPhases? phases, long startupTicks, GaStartupDecision decision)
    {
        using var cpuWorld = new World(new WorldOptions(phases == null ? 32 : chunkCapacity));
        using var selectedWorld = new World(new WorldOptions(chunkCapacity));
        bool destroy = phases is null;
        var cpuEntities = Seed(cpuWorld, count, fault: false, destroy, out var cpuTarget, out var cpuSurvivor);
        var selectedEntities = Seed(selectedWorld, count, fault: false, destroy, out var selectedTarget, out var selectedSurvivor);
        using var allocatorOracle = destroy ? new World(new WorldOptions(32)) : null;
        if (allocatorOracle is not null)
            Seed(allocatorOracle, count, fault: false, destroy: true, out _, out _);
        var cpuSnapshot = new GaTickSnapshot();
        var selectedSnapshot = new GaTickSnapshot();
        long runnerSetupStarted = phases is null ? 0 : Stopwatch.GetTimestamp();
        using var cpu = new DeterministicRunner<byte>(cpuWorld, CpuWorkload(cpuSnapshot), workerCount: 2);
        long cpuRunnerSetupTicks = phases is null ? 0 : Stopwatch.GetTimestamp() - runnerSetupStarted;
        runnerSetupStarted = phases is null ? 0 : Stopwatch.GetTimestamp();
        using var runner = new DeterministicRunner<byte>(selectedWorld, selected(selectedSnapshot));
        long selectedRunnerSetupTicks = phases is null ? 0 : Stopwatch.GetTimestamp() - runnerSetupStarted;
        using var cpuCheckpoint = new MemoryStream();
        using var selectedCheckpoint = new MemoryStream();
        Compare(0);
        var previousCells = new int[count];
        long pending = 0;
        long eventCount = 0;
        var samples = phases is null ? null : new TickSample[ticks];
        long completedTicks = (long)warmup + ticks;
        for (long tick = 1; tick <= completedTicks; tick++)
        {
            for (int row = 0; row < count; row++)
                previousCells[row] = cpuWorld.Get<GaPosition>(cpuEntities[row]).Cell;

            bool sample = samples != null && tick > warmup;
            long cpuBefore = sample ? GC.GetTotalAllocatedBytes(precise: true) : 0;
            long cpuStart = sample ? Stopwatch.GetTimestamp() : 0;
            BindSnapshot(cpuWorld, cpuSnapshot, tick);
            cpu.BeginTick(tick, 0, Fixed.One);
            var cpuStatus = Advance(cpu, phases is null ? int.MaxValue : budget);
            long cpuTicks = sample ? Stopwatch.GetTimestamp() - cpuStart : 0;
            long cpuBytes = sample ? GC.GetTotalAllocatedBytes(precise: true) - cpuBefore : 0;
            if (cpuStatus != DeterministicTickStatus.Completed)
                throw new InvalidOperationException($"CPU runner faulted on tick {tick}.", cpu.Fault);

            var last = runner.LastCompleted;
            phases?.Reset();
            long selectedBefore = sample ? GC.GetTotalAllocatedBytes(precise: true) : 0;
            long selectedStart = sample ? Stopwatch.GetTimestamp() : 0;
            BindSnapshot(selectedWorld, selectedSnapshot, tick);
            runner.BeginTick(tick, 0, Fixed.One);
            if (phases is null)
                VerifyLease(selectedWorld, selectedEntities[0]);
            var status = Advance(runner, budget, out int pendingPolls, phases is null ? () =>
            {
                VerifyLease(selectedWorld, selectedEntities[0]);
                if (!ReferenceEquals(last, runner.LastCompleted))
                    throw new InvalidOperationException("The runner published a tick while its work was pending.");
            } : null);
            long selectedTicks = sample ? Stopwatch.GetTimestamp() - selectedStart : 0;
            long selectedBytes = sample ? GC.GetTotalAllocatedBytes(precise: true) - selectedBefore : 0;
            pending += pendingPolls;
            if (status != DeterministicTickStatus.Completed || runner.LastCompleted?.TickIndex != tick)
                throw new InvalidOperationException($"Selected runner faulted on tick {tick}.", runner.Fault);
            Compare(tick);
            VerifyEvents(tick, cpu.LastCompleted!.Events, runner.LastCompleted!.Events);
            eventCount += runner.LastCompleted.Events.Count;
            if (sample)
            {
                if (decision.Route == GaRoute.Gpu && (phases!.Rows != count || phases.Batches == 0))
                    throw new InvalidDataException($"GA timing missed a submitted row at tick {tick}.");
                samples![(int)(tick - warmup - 1)] = new TickSample(tick, Microseconds(cpuTicks), cpuBytes,
                    Microseconds(selectedTicks), selectedBytes, phases!.Batches, phases.Rows, pendingPolls,
                    Microseconds(phases.Pack), Microseconds(phases.Device), Microseconds(phases.Apply));
            }
            // Exercise fresh device packing after external edits to a completed world.
            if (tick < completedTicks && (tick & 1) == 0)
            {
                cpuWorld.Get<GaPosition>(cpuEntities[0]).X += Fixed.FromRaw(11);
                selectedWorld.Get<GaPosition>(selectedEntities[0]).X += Fixed.FromRaw(11);
                cpuWorld.Get<GaSignal>(cpuEntities[0]).Value -= 13;
                selectedWorld.Get<GaSignal>(selectedEntities[0]).Value -= 13;
            }
        }
        var hash = Convert.ToHexString(SHA256.HashData(cpuCheckpoint.GetBuffer().AsSpan(0, (int)cpuCheckpoint.Length)));
        Console.WriteLine($"GA runner parity verifiedStates={completedTicks + 1} entities={count} events={eventCount} pendingPolls={pending} checkpointSha256={hash}");
        if (destroy)
            Console.WriteLine($"GA structural parity liveDestroyed=True duplicateDestroy=True staleGenerationSurvived={count > 1} ticketDestroyed=True createSurvived=True allocatorContinuation=True");
        if (samples != null)
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "ga-runner-timing",
                route = decision.Route.ToString(),
                decision.Device,
                decision.ComputeProfile,
                qualification = decision.Qualification?.ToString(),
                decision.ArtifactId,
                os = RuntimeInformation.OSDescription,
                runtime = RuntimeInformation.FrameworkDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                entities = count,
                chunkCapacity,
                jobBudget = budget,
                cpuWorkerCount = 2,
                warmupTicks = warmup,
                startupMicroseconds = Microseconds(startupTicks),
                cpuRunnerSetupMicroseconds = Microseconds(cpuRunnerSetupTicks),
                selectedRunnerSetupMicroseconds = Microseconds(selectedRunnerSetupTicks),
                checkpointSha256 = hash,
                samples
            }));

        void VerifyEvents(long tick, IReadOnlyList<DeterministicEvent> expected, IReadOnlyList<DeterministicEvent> actual)
        {
            int index = 0;
            for (int row = 0; row < count; row++)
            {
                Entity entity = cpuEntities[row];
                int ordinal = 0;
                var velocity = cpuWorld.Get<GaVelocity>(entity);
                if (velocity.Divide)
                    CompareEvent(entity, ordinal++, previousCells[row]);
                if (velocity.DestroyGeneration != 0)
                    ordinal += 5;
                CompareEvent(entity, ordinal, cpuWorld.Get<GaPosition>(entity).Cell);
            }
            if (index != expected.Count || index != actual.Count)
                throw new InvalidDataException($"GA runner emitted extra events at tick {tick}.");

            void CompareEvent(Entity entity, int ordinal, int value)
            {
                if (index >= expected.Count || index >= actual.Count)
                    throw new InvalidDataException($"GA runner omitted an event at tick {tick}.");
                var left = expected[index];
                var right = actual[index++];
                if (!left.Key.Equals(right.Key) || left.Key.TickIndex != tick ||
                    left.Key.Stage != SystemStage.Update || left.Key.SystemOrder != 0 ||
                    left.Key.SourceKind != DeterministicSourceKind.Entity || left.Key.SourceId != entity.Id ||
                    left.Key.SourceGeneration != entity.Version || left.Key.LocalOrdinal != ordinal ||
                    left.Get<int>() != value || right.Get<int>() != value)
                    throw new InvalidDataException($"GA event parity diverged at tick {tick}, entity {entity.Id}, ordinal {ordinal}.");
            }
        }

        void Compare(long tick)
        {
            if (destroy && (cpuWorld.IsAlive(cpuTarget) != (tick == 0) ||
                selectedWorld.IsAlive(selectedTarget) != (tick == 0) ||
                !cpuWorld.IsAlive(cpuSurvivor) || !selectedWorld.IsAlive(selectedSurvivor)))
                throw new InvalidDataException($"GA structural command liveness diverged at tick {tick}.");

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
                    cpuVelocity.Divide != gpuVelocity.Divide || cpuVelocity.TargetId != gpuVelocity.TargetId ||
                    cpuVelocity.TargetGeneration != gpuVelocity.TargetGeneration || cpuVelocity.DestroyId != gpuVelocity.DestroyId ||
                    cpuVelocity.DestroyGeneration != gpuVelocity.DestroyGeneration)
                    throw new InvalidDataException($"GA runner diverged at tick {tick}, row {row}.");
                if (tick != 0)
                {
                    Entity target = cpuEntities[0];
                    int oracle = cpuVelocity.TargetId == target.Id && cpuVelocity.TargetGeneration == target.Version
                        ? cpuWorld.Get<GaSignal>(target).Value : -777;
                    if (cpuValue.Cell != unchecked((int)cpuValue.X + oracle))
                        throw new InvalidDataException($"GA snapshot lookup diverged at tick {tick}, row {row}.");
                }
            }
            cpuCheckpoint.SetLength(0);
            selectedCheckpoint.SetLength(0);
            WorldSerializer.WriteBinary(cpuWorld, cpuCheckpoint, CheckpointSchema);
            WorldSerializer.WriteBinary(selectedWorld, selectedCheckpoint, CheckpointSchema);
            if (!cpuCheckpoint.GetBuffer().AsSpan(0, (int)cpuCheckpoint.Length)
                .SequenceEqual(selectedCheckpoint.GetBuffer().AsSpan(0, (int)selectedCheckpoint.Length)))
                throw new InvalidDataException($"GA canonical checkpoint diverged at tick {tick}.");

            if (allocatorOracle is not null && tick != 0)
                VerifyAllocator(tick, allocatorOracle);
        }

        void VerifyAllocator(long tick, World oracle)
        {
            var lastReleased = ApplyRow(cpuTarget.Id, cpuTarget.Version);
            if (count > 1)
                lastReleased = ApplyRow(cpuSurvivor.Id, cpuSurvivor.Version - 1);

            int expectedCount = oracle.GetStorageStatistics().EntityCount;
            if (cpuWorld.GetStorageStatistics().EntityCount != expectedCount ||
                selectedWorld.GetStorageStatistics().EntityCount != expectedCount)
                throw new InvalidDataException($"GA structural entity count diverged at tick {tick}.");

            selectedCheckpoint.Position = 0;
            using var restored = WorldSerializer.ReadBinary(selectedCheckpoint, CheckpointSchema);
            var next = restored.CreateEntity();
            if (next.Id != lastReleased.Id || next.Version != lastReleased.Version + 1)
                throw new InvalidDataException($"GA checkpoint allocator continuation diverged at tick {tick}.");

            Entity ApplyRow(int id, int generation)
            {
                var target = oracle.BindEntity(id, generation);
                if (oracle.IsAlive(target))
                    oracle.DestroyEntity(target);
                var kept = oracle.CreateEntity();
                var temporary = oracle.CreateEntity();
                oracle.DestroyEntity(temporary);

                if (!HasGeneration(cpuWorld, kept) || !HasGeneration(selectedWorld, kept) ||
                    HasGeneration(cpuWorld, temporary) || HasGeneration(selectedWorld, temporary))
                    throw new InvalidDataException($"GA ticket lifecycle diverged at tick {tick}, target {id}.");
                return temporary;
            }

            static bool HasGeneration(World world, Entity expected) =>
                world.IsAlive(world.BindEntity(expected.Id, expected.Version));
        }
    }

    private static void VerifyFault(Func<GaTickSnapshot, DeterministicWorkload<byte>> selected, int chunkCapacity, int budget)
    {
        using var cpuWorld = new World(new WorldOptions(32));
        using var gpuWorld = new World(new WorldOptions(chunkCapacity));
        var cpuEntities = Seed(cpuWorld, 131, fault: true, destroy: true, out _, out _);
        var gpuEntities = Seed(gpuWorld, 131, fault: true, destroy: true, out _, out _);
        var cpuSnapshot = new GaTickSnapshot();
        var gpuSnapshot = new GaTickSnapshot();
        using var cpu = new DeterministicRunner<byte>(cpuWorld, CpuWorkload(cpuSnapshot), workerCount: 2);
        using var gpu = new DeterministicRunner<byte>(gpuWorld, selected(gpuSnapshot));
        BindSnapshot(cpuWorld, cpuSnapshot, 1);
        BindSnapshot(gpuWorld, gpuSnapshot, 1);
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
        VerifyLease(cpuWorld, cpuEntities[0]);
        VerifyLease(gpuWorld, gpuEntities[0]);
        Console.WriteLine($"GA runner fault parity source={actual.SourceOrderOrdinal} evaluation={actual.KernelFault.EvaluationOrdinal} kind={actual.KernelFault.Kind}");
    }

    private static DeterministicWorkload<byte> CpuWorkload(GaTickSnapshot snapshot) =>
        GaCpuAdapter.Register(new DeterministicWorkload<byte>(), SystemStage.Update, snapshot);

    private static void BindSnapshot(World world, GaTickSnapshot snapshot, long tick) =>
        snapshot.Bind(tick, GaSnapshotPacket.Capture<GaSignal>(world, SnapshotLayout,
            static (in GaSignal component, Span<uint> words) => words[0] = unchecked((uint)component.Value)));

    private static Entity[] Seed(World world, int count, bool fault, bool destroy, out Entity destroyTarget, out Entity destroySurvivor)
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
        world.Add(entities[0], new GaSignal { Value = -720 });
        for (int row = 0; row < count; row++)
        {
            ref GaVelocity velocity = ref world.Get<GaVelocity>(entities[row]);
            Entity target = (row % 4) switch
            {
                0 => entities[0],
                1 => recycled,
                2 => spare,
                _ => entities[^1]
            };
            velocity.TargetId = target.Id;
            velocity.TargetGeneration = target.Version;
        }
        world.DestroyEntity(spare);

        destroyTarget = default;
        destroySurvivor = default;
        if (destroy)
        {
            destroyTarget = world.CreateEntity();
            var stale = world.CreateEntity();
            world.DestroyEntity(stale);
            destroySurvivor = world.CreateEntity();
            if (destroySurvivor.Id != stale.Id || destroySurvivor.Version == stale.Version)
                throw new InvalidOperationException("The structural fixture requires a recycled entity generation.");

            SetDestroyTarget(entities[0], destroyTarget);
            if (count > 1)
                SetDestroyTarget(entities[1], stale);
            if (fault)
                SetDestroyTarget(entities[64], destroyTarget);
        }
        return entities;

        void SetDestroyTarget(Entity source, Entity target)
        {
            ref var velocity = ref world.Get<GaVelocity>(source);
            velocity.DestroyId = target.Id;
            velocity.DestroyGeneration = target.Version;
        }
    }

    private static DeterministicTickStatus Advance(DeterministicRunner<byte> runner, int budget, Action? pending = null)
        => Advance(runner, budget, out _, pending);

    private static DeterministicTickStatus Advance(DeterministicRunner<byte> runner, int budget, out int pendingPolls,
        Action? pending = null)
    {
        DeterministicTickStatus status;
        var wait = new SpinWait();
        pendingPolls = 0;
        while ((status = runner.Advance(budget)) == DeterministicTickStatus.Pending)
        {
            pendingPolls++;
            pending?.Invoke();
            wait.SpinOnce();
        }
        return status;
    }

    private static double Microseconds(long ticks) => Stopwatch.GetElapsedTime(0, ticks).TotalMicroseconds;

    private sealed class BatchPhases
    {
        internal long Pack { get; private set; }
        internal long Device { get; private set; }
        internal long Apply { get; private set; }
        internal int Batches { get; private set; }
        internal long Rows { get; private set; }

        internal void Record(GaBatchTimings timing)
        {
            Batches++;
            Rows += timing.Rows;
            Pack += timing.PackTicks;
            Device += timing.DeviceTicks;
            Apply += timing.ApplyTicks;
        }

        internal void Reset() => (Pack, Device, Apply, Batches, Rows) = (0, 0, 0, 0, 0);
    }

    private readonly record struct TickSample(long Tick, double CpuTickMicroseconds, long CpuManagedBytes,
        double SelectedTickMicroseconds, long SelectedManagedBytes, int Batches, long Rows, int PendingPolls,
        double HostPackMicroseconds, double NativeTransferDispatchReadbackMicroseconds, double HostApplyMicroseconds);

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
            fields.Set(4, FieldValue.From(value.TargetId));
            fields.Set(5, FieldValue.From(value.TargetGeneration));
            fields.Set(6, FieldValue.From(value.DestroyId));
            fields.Set(7, FieldValue.From(value.DestroyGeneration));
        }

        public GaVelocity Read(ComponentReader fields) => new()
        {
            Delta = Fixed.FromRaw(fields.Get(1).AsInt64()),
            Step = Fixed.FromRaw(fields.Get(2).AsInt64()),
            Divide = fields.Get(3).AsBoolean(),
            TargetId = fields.Get(4).AsInt32(),
            TargetGeneration = fields.Get(5).AsInt32(),
            DestroyId = fields.Get(6).AsInt32(),
            DestroyGeneration = fields.Get(7).AsInt32()
        };
    }

    private sealed class SignalCodec : IComponentCodec<GaSignal>
    {
        public void Write(in GaSignal value, ComponentWriter fields) => fields.Set(1, FieldValue.From(value.Value));
        public GaSignal Read(ComponentReader fields) => new() { Value = fields.Get(1).AsInt32() };
    }
}
