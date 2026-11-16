using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using SKECS;
using SKECS.GA;
using SKECS.GA.Backend.Vulkan;
using SKECS.Numerics;

return await EventConformance.Run(args);

internal static class EventConformance
{
    private const string SystemId = "fixtures.event-transport";
    private const int EventCapacity = 2;
    private const int EventWidth = 2;
    private const int RowWidth = 1;
    private const int EntityCount = 131;
    private const int TickCount = 8;

    internal static async Task<int> Run(string[] args)
    {
        if (args.Length < 5)
        {
            Console.Error.WriteLine("Usage: Ga.EventConformance <ga-slang-probe> <spirv-val> <native Vulkan library> <output directory> <device indexes...>");
            return 2;
        }

        string outputDirectory = Path.GetFullPath(args[3]);
        Directory.CreateDirectory(outputDirectory);
        var bundle = await Package(args[0], args[1], outputDirectory);
        using var backend = GaVulkanBackend.Open(args[2]);
        foreach (string argument in args[4..])
        {
            if (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                || (uint)index >= backend.Devices.Count)
                throw new ArgumentException($"Unknown Vulkan device index {argument}.");
            VerifyDevice(backend.Devices[index], bundle);
        }
        return 0;
    }

    private static async Task<GaArtifactBundle> Package(string compiler, string validator, string directory)
    {
        using var resource = typeof(EventConformance).Assembly.GetManifestResourceStream("Ga.EventTransport.slang")
            ?? throw new InvalidOperationException("Missing explicit Slang event transport shader.");
        using var source = new MemoryStream();
        await resource.CopyToAsync(source);
        var sourceBytes = source.ToArray();
        string sourcePath = Path.Combine(directory, "events.slang");
        string binaryPath = Path.Combine(directory, "events.spv");
        string reflectionPath = Path.Combine(directory, "events.layout");
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        await Execute(compiler, "compile", sourcePath, "main", binaryPath, reflectionPath);
        await Execute(validator, "--target-env", "vulkan1.1", binaryPath);

        var expectedReflection = new[]
        {
            "slang\t2025.6.1", "target\tspirv\tspirv_1_3", "entry\tmain", "workgroup\t64\t1\t1",
            "binding\tgaInput\t0\t0\t1\tstorage\tr\tused\t4\t4",
            "binding\tgaOutput\t0\t1\t1\tstorage\trw\tused\t4\t4",
            "binding\tgaFaults\t0\t2\t1\tstorage\trw\tused\t4\t4"
        };
        if (!File.ReadAllLines(reflectionPath).SequenceEqual(expectedReflection))
            throw new InvalidDataException("Event transport reflection differs from its declared buffer ABI.");

        var binary = await File.ReadAllBytesAsync(binaryPath);
        var reflection = await File.ReadAllBytesAsync(reflectionPath);
        var sourceMap = "[]"u8.ToArray();
        var contract = new GaContractIdentity(SystemId, "fixtures.event-transport/1",
            GaArtifactManifest.ComputeDigest(sourceBytes),
            GaArtifactManifest.ComputeDigest("explicit diagnostic shader; no generated IR"u8),
            GaArtifactManifest.ComputeDigest("state:uint; event:{first:uint,second:uint}; packet:v1"u8),
            GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion);
        var manifest = new GaArtifactManifest(GaArtifactManifest.CurrentSchemaVersion, contract,
            new GaToolchain("2025.6.1", "Slang direct SPIR-V emitter", GaArtifactFormat.SpirV, "spirv_1_3",
                ["GenerateSpirvDirectly", "VulkanUseEntryPointName"], [], []), "main",
            [Binding("gaInput", 0, GaResourceAccess.Read), Binding("gaOutput", 1, GaResourceAccess.ReadWrite),
             Binding("gaFaults", 2, GaResourceAccess.ReadWrite)],
            [new("gaInput", true), new("gaOutput", true), new("gaFaults", true)],
            new GaKernelRequirements(GaComputeProfiles.Portable32, [], 64, 1, 1, 0, 0),
            GaArtifactManifest.ComputeDigest(binary), GaArtifactManifest.ComputeDigest(reflection),
            GaArtifactManifest.ComputeDigest(sourceMap));
        await File.WriteAllBytesAsync(Path.Combine(directory, "events.manifest.json"), manifest.GetCanonicalBytes());
        await File.WriteAllBytesAsync(Path.Combine(directory, "events.sourcemap.json"), sourceMap);
        return GaArtifactBundle.Load(manifest, manifest.GetContentId(), binary, reflection, sourceMap);
    }

    private static GaResourceBinding Binding(string name, int index, GaResourceAccess access) =>
        new(name, GaBindingNamespace.VulkanDescriptor, 0, index, 1, GaResourceKind.StorageBuffer, access,
            new GaResourceLayout(4, 4, 4, []));

    private static void VerifyDevice(IGaDevice device, GaArtifactBundle bundle)
    {
        var request = new GaSystemRequest(bundle.Manifest.Contract, [new GaArtifactCandidate(bundle)]);
        using var startup = GaStartup.Select(GaPolicy.RequireGpu, [request], [device], allowExperimental: true);
        var decision = startup.Decisions[0];
        if (!startup.Succeeded || decision.Route != GaRoute.Gpu || decision.Device != device.Info ||
            startup.GetPreparedKernel(SystemId) is not GaVulkanKernel kernel)
            throw new InvalidOperationException($"Managed Vulkan startup did not select the event shader: {decision}.");

        using var cpuWorld = new World(new WorldOptions(3));
        using var gpuWorld = new World(new WorldOptions(37));
        var cpuEntities = Seed(cpuWorld);
        var gpuEntities = Seed(gpuWorld);
        var gpuProcessor = new GpuProcessor(kernel);
        var cpuWorkload = Markers().AddSystem(SystemStage.Update, Query(), new CpuProducer());
        var gpuWorkload = Markers().AddBatchSystem(SystemStage.Update, Query(), gpuProcessor);
        using var cpu = new DeterministicRunner<uint>(cpuWorld, cpuWorkload, workerCount: 3);
        using var gpu = new DeterministicRunner<uint>(gpuWorld, gpuWorkload);
        using var trace = new MemoryStream();
        using var writer = new BinaryWriter(trace);
        int totalEntityEvents = 0;
        int pendingPolls = 0;
        var arities = new bool[EventCapacity + 1];

        for (int tick = 1; tick <= TickCount; tick++)
        {
            int[] counts = new int[EntityCount];
            for (int i = 0; i < counts.Length; i++)
            {
                uint input = cpuWorld.Get<State>(cpuEntities[i]).Number;
                counts[i] = (int)(input % 3);
                arities[counts[i]] = true;
            }

            if (Advance(cpu, tick, 4, out _) != DeterministicTickStatus.Completed)
                throw new InvalidOperationException($"CPU event runner failed at tick {tick}.", cpu.Fault);
            if (Advance(gpu, tick, (tick - 1) % 3 + 1, out int polls) != DeterministicTickStatus.Completed)
                throw new InvalidOperationException($"GPU event runner failed at tick {tick}.", gpu.Fault);
            pendingPolls += polls;
            var expected = cpu.LastCompleted!;
            var actual = gpu.LastCompleted!;
            int expectedEntityEvents = counts.Sum();
            if (expected.TickIndex != tick || actual.TickIndex != tick ||
                actual.Events.Count != expectedEntityEvents + 2 || actual.Events.Count != expected.Events.Count)
                throw new InvalidDataException($"Event count or tick diverged at tick {tick}.");

            for (int i = 0; i < actual.Events.Count; i++)
            {
                var left = expected.Events[i];
                var right = actual.Events[i];
                if (left.PayloadType != typeof(Pulse) || right.PayloadType != typeof(Pulse) ||
                    !left.Key.Equals(right.Key) || left.Get<Pulse>() != right.Get<Pulse>() ||
                    i != 0 && right.Key.CompareTo(actual.Events[i - 1].Key) <= 0)
                    throw new InvalidDataException($"Event key, order or payload diverged at tick {tick}, event {i}.");
                Record(writer, right);
            }
            for (int i = 0; i < EntityCount; i++)
            {
                var left = cpuEntities[i];
                var right = gpuEntities[i];
                if (left.Id != right.Id || left.Version != right.Version ||
                    cpuWorld.Get<State>(left).Number != gpuWorld.Get<State>(right).Number)
                    throw new InvalidDataException($"Ordinary state diverged at tick {tick}, entity {i}.");
            }
            totalEntityEvents += expectedEntityEvents;
            Console.WriteLine($"  Tick {tick}: {expectedEntityEvents} entity pulses and 2 job markers agree.");
        }
        if (!arities.All(covered => covered) || gpuProcessor.AppliedRows != TickCount * EntityCount)
            throw new InvalidDataException("The event conformance fixture missed a required source arity or ordinary row.");

        // A malformed count occurs after earlier valid rows in the first batch.
        gpuWorld.Get<State>(gpuEntities[16]).Number = uint.MaxValue;
        int beforeFault = gpuProcessor.AppliedRows;
        var completed = gpu.LastCompleted;
        if (Advance(gpu, TickCount + 1, 2, out _) != DeterministicTickStatus.Faulted ||
            gpu.Fault is not InvalidDataException fault || !fault.Message.Contains("GA event capacity exceeded", StringComparison.Ordinal) ||
            !ReferenceEquals(completed, gpu.LastCompleted) || gpuProcessor.AppliedRows != beforeFault)
            throw new InvalidOperationException("A malformed GPU count did not fault before applying its batch.", gpu.Fault);

        writer.Flush();
        string digest = Convert.ToHexString(SHA256.HashData(trace.GetBuffer().AsSpan(0, (int)trace.Length)));
        Console.WriteLine($"{device.Info.Device}: {TickCount} ticks, {EntityCount} ordinary rows/tick, {totalEntityEvents} entity pulses, " +
            $"{pendingPolls} pending polls, event SHA-256 {digest}; overflow was rejected before publication.");
    }

    private static DeterministicTickStatus Advance(DeterministicRunner<uint> runner, int tick, int budget, out int pendingPolls)
    {
        var previous = runner.LastCompleted;
        runner.BeginTick(tick, (uint)tick, Fixed.One);
        var wait = new SpinWait();
        pendingPolls = 0;
        while (true)
        {
            var status = runner.Advance(budget);
            if (status != DeterministicTickStatus.Pending)
                return status;
            if (!ReferenceEquals(previous, runner.LastCompleted))
                throw new InvalidDataException("The runner published a partial event trace.");
            pendingPolls++;
            wait.SpinOnce();
        }
    }

    private static DeterministicWorkload<uint> Markers() =>
        new DeterministicWorkload<uint>()
            .AddJob(SystemStage.PreUpdate, new DeterministicQuery(), new MarkerJob(false))
            .AddJob(SystemStage.PostUpdate, new DeterministicQuery(), new MarkerJob(true));

    private static DeterministicQuery Query() => new DeterministicQuery().With<State>().ReadWrite<State>();

    private static Entity[] Seed(World world)
    {
        var entities = new Entity[EntityCount];
        for (int row = 0; row < entities.Length; row++)
        {
            var state = new State { Number = (uint)row };
            if (row % 9 == 0)
                world.CreateEntity(new Marker());
            entities[row] = row % 2 == 0 ? world.CreateEntity(state) : world.CreateEntity(new Marker(), state);
        }
        var old = entities[7];
        world.DestroyEntity(old);
        var reused = world.CreateEntity(new State { Number = 7 });
        if (reused.Id != old.Id || reused.Version == old.Version)
            throw new InvalidDataException("The event fixture did not recycle an entity generation.");
        entities[7] = reused;
        return entities;
    }

    private static void Record(BinaryWriter writer, DeterministicEvent value)
    {
        var key = value.Key;
        writer.Write(key.TickIndex);
        writer.Write((int)key.Stage);
        writer.Write(key.SystemOrder);
        writer.Write((int)key.SourceKind);
        writer.Write(key.SourceId);
        writer.Write(key.SourceGeneration);
        writer.Write(key.LocalOrdinal);
        var pulse = value.Get<Pulse>();
        writer.Write(pulse.First);
        writer.Write(pulse.Second);
    }

    private static async Task Execute(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{executable}: {await output}{await errors}");
    }

    private readonly record struct Pulse(uint First, uint Second);
    private struct State { public uint Number; }
    private readonly struct Marker;

    private readonly struct MarkerJob(bool after) : IDeterministicJobProcessor<uint>
    {
        public void Execute(DeterministicJob<uint> job) =>
            job.ForJob().Emit(new Pulse(job.Input, after ? 1u : 0u));
    }

    private readonly struct CpuProducer : IDeterministicJobProcessor<uint>
    {
        public void Execute(DeterministicJob<uint> job)
        {
            for (int i = 0; i < job.Count; i++)
            {
                uint before = job.Read<State>(i).Number;
                uint after = unchecked((uint)((ulong)before * 1_664_525 + 1_013_904_223));
                if (before % 3 != 0)
                    job.ForEntity(i).Emit(new Pulse(before, after));
                if (before % 3 == 2)
                    job.ForEntity(i).Emit(new Pulse(after, before ^ 0xfeed1234u));
                job.Write(i, new State { Number = after });
            }
        }
    }

    private sealed class GpuProcessor(GaPackedKernel kernel) : IDeterministicBatchProcessor<uint>
    {
        internal int AppliedRows { get; private set; }

        public IDeterministicBatchSubmission<uint> Begin(DeterministicBatch<uint> batch)
        {
            int end = batch.FirstJobId + batch.JobCount;
            int rows = 0;
            for (int id = batch.FirstJobId; id < end; id++)
                rows = checked(rows + batch.GetReadJob(id).Count);
            var input = new uint[1 + rows];
            input[0] = (uint)rows;
            int offset = 1;
            for (int id = batch.FirstJobId; id < end; id++)
            {
                var job = batch.GetReadJob(id);
                for (int row = 0; row < job.Count; row++)
                    input[offset++] = job.Read<State>(row).Number;
            }
            int packetWords = checked(GaEventPacket.HeaderWords + rows * (1 + EventCapacity * EventWidth));
            return new Submission(this, rows, kernel.BeginDispatchWithOutput(input, RowWidth, packetWords));
        }

        private sealed class Submission(GpuProcessor processor, int rows, GaPackedSubmission work) : IDeterministicBatchSubmission<uint>
        {
            public bool IsCompleted => work.IsCompleted;

            public void Apply(DeterministicBatch<uint> batch)
            {
                var result = work.Drain();
                if (result.RowCount != rows || result.FindFirstFault() is not null)
                    throw new InvalidDataException("The event dispatch reported a failed or incomplete ordinary result.");

                var packet = new GaEventPacket(result.Output[(rows * RowWidth)..]);
                if (packet.SourceCount != rows || packet.CapacityPerSource != EventCapacity || packet.EventWords != EventWidth)
                    throw new InvalidDataException("The event dispatch changed its registered packet layout.");
                int offset = 0;
                for (int id = batch.FirstJobId; id < batch.FirstJobId + batch.JobCount; id++)
                {
                    var job = batch.GetJob(id);
                    packet.ApplyEntityRows<uint, Pulse>(job, offset, job.Count, Decode);
                    for (int row = 0; row < job.Count; row++)
                        job.Write(row, new State { Number = result.Output[offset + row] });
                    offset += job.Count;
                }
                processor.AppliedRows += offset;
            }

            public void Discard() => work.Discard();
            public void Dispose()
            {
                if (!work.IsCompleted)
                    work.Discard();
            }

            private static Pulse Decode(ReadOnlySpan<uint> words) => new(words[0], words[1]);
        }
    }
}
