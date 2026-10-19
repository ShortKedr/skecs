using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SpaceBattle;
using Battle = SpaceBattle.Simulations.StaticEcs.Battle;
using static Runner;

// Independent concrete host: the measured call is directly to this framework's core.
internal static class StaticEcsRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, IncludeFields = true };
    public static void Run(RunOptions options)
    {
        var reference = ReplayOracle.Reference(options.Config, options.Ticks);
        // Warmup uses a separate battle so measured gameplay starts at tick zero.
        using (var warmup = new Battle(options.Config))
        {
            for (var i = 0; i < options.WarmupTicks; i++)
            {
                warmup.Step();
            }
        }
        using var battle = new Battle(options.Config);
        var fresh = battle.Store;
        var replay = new ReplayHash(battle.CopyReplayState, battle.Counters, battle.Tactics);
        var hashes = new ulong[options.Ticks];
        ulong currentHash = replay.Capture();
        ReplayHash.RequireMatch(battle.Framework, 0, reference[0], currentHash);
        var timing = new double[options.Ticks]; var hudTiming = new double[options.Ticks]; var allocation = new long[options.Ticks];
        var stages = Battle.StageNames.Select(_ => new double[options.Ticks]).ToArray();
        var entityCounts = new (int Ships, int Projectiles, int Debris, int Particles, int Turrets)[options.Ticks];
        var structuralCounts = new (long Adds, long Removes)[options.Ticks];
        var collections = new (int Gen0, int Gen1, int Gen2)[options.Ticks];
        var windows = new List<object>(); var hudWindows = new List<object>(); var checkpoints = new List<object>();
        using var process = Process.GetCurrentProcess();
        Directory.CreateDirectory(Path.GetDirectoryName(options.Output)!);
        var csvPath = Path.ChangeExtension(options.Output, ".ticks.csv");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var initialCollections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        var started = Stopwatch.StartNew(); var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        object? initialWindow = null;
        for (var i = 0; i < options.Ticks; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            battle.Step();
            timing[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            hudTiming[i] = battle.LastTickMs;
            allocation[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            currentHash = hashes[i] = replay.Capture();
            ReplayHash.RequireMatch(battle.Framework, battle.Tick, reference[i + 1], currentHash);
            for (var stage = 0; stage < stages.Length; stage++) stages[stage][i] = battle.LastStageMs[stage];
            entityCounts[i] = (battle.ShipCount, battle.ProjectileCount, battle.DebrisCount, battle.ParticleCount, battle.TurretCount);
            structuralCounts[i] = (battle.Counters.StructuralAdds, battle.Counters.StructuralRemoves);
            collections[i] = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
            if ((i + 1) % SimulationStatistics.WindowCapacity == 0 || i == options.Ticks - 1)
            {
                var begin = i / SimulationStatistics.WindowCapacity * SimulationStatistics.WindowCapacity;
                var count = i - begin + 1;
                var sums = new long[5];
                for (var tick = begin; tick <= i; tick++)
                {
                    var c = entityCounts[tick];
                    sums[0] += c.Ships; sums[1] += c.Projectiles; sums[2] += c.Debris; sums[3] += c.Particles; sums[4] += c.Turrets;
                }
                hudWindows.Add(new { StartTick = begin + 1, EndTick = i + 1, Timing = Stats(hudTiming.AsSpan(begin, count)),
                    Stages = Battle.StageNames.Select((name, s) => new { Name = name, Timing = Stats(stages[s].AsSpan(begin, count)) }).ToArray(),
                    MeanShips = (double)sums[0] / count, MeanProjectiles = (double)sums[1] / count,
                    MeanDebris = (double)sums[2] / count, MeanParticles = (double)sums[3] / count, MeanTurrets = (double)sums[4] / count });
            }
            if ((i + 1) % 3600 == 0 || i == options.Ticks - 1)
            {
                process.Refresh();
                var begin = i / 3600 * 3600; var count = i - begin + 1;
                windows.Add(new { StartTick = begin + 1, EndTick = i + 1, Timing = Stats(timing.AsSpan(begin, count)), AllocatedBytes = allocation.AsSpan(begin, count).ToArray().Sum(),
                    Ships = battle.ShipCount, Projectiles = battle.ProjectileCount, Debris = battle.DebrisCount, Particles = battle.ParticleCount, Turrets = battle.TurretCount,
                    Gc = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) },
                    ManagedHeapBytes = GC.GetTotalMemory(false), WorkingSetBytes = process.WorkingSet64, PrivateBytes = process.PrivateMemorySize64,
                    Store = fresh.Diagnostics(), Counters = battle.Counters.Copy() });
                checkpoints.Add(new { Tick = battle.Tick, Checksum = currentHash.ToString("X16") });
                Console.WriteLine($"{fresh.Name}: {battle.Tick}/{options.Ticks} ticks; {Stats(timing.AsSpan(begin, count))}; ships={battle.ShipCount}; hits={battle.Counters.Hits}; built={battle.Counters.Builds}");
            }
            if (i + 1 == Math.Min(2000, options.Ticks))
                initialWindow = new { StartTick = 1, EndTick = i + 1, Timing = Stats(timing.AsSpan(0, i + 1)),
                    Stages = Battle.StageNames.Select((name, s) => new { Name = name, Timing = Stats(stages[s].AsSpan(0, i + 1)) }).ToArray(),
                    Ships = battle.ShipCount, Projectiles = battle.ProjectileCount, Debris = battle.DebrisCount,
                    Particles = battle.ParticleCount, Turrets = battle.TurretCount,
                    Counters = battle.Counters.Copy(), Checksum = currentHash.ToString("X16") };
            if (options.Realtime)
            {
                var due = (i + 1) / 60.0;
                while (started.Elapsed.TotalSeconds < due)
                {
                    var remaining = due - started.Elapsed.TotalSeconds;
                    if (remaining > 0.002) Thread.Sleep(1); else Thread.SpinWait(50);
                }
            }
        }
        var elapsed = started.Elapsed.TotalSeconds;
        var measuredAllocation = allocation.Sum();
        object OpeningSummary(int count)
        {
            var sums = new long[5];
            for (var tick = 0; tick < count; tick++)
            {
                var c = entityCounts[tick];
                sums[0] += c.Ships; sums[1] += c.Projectiles; sums[2] += c.Debris; sums[3] += c.Particles; sums[4] += c.Turrets;
            }
            return new { StartTick = 1, EndTick = count, Timing = Stats(timing.AsSpan(0, count)),
                HudTiming = Stats(hudTiming.AsSpan(0, count)),
                Stages = Battle.StageNames.Select((name, s) => new { Name = name, Timing = Stats(stages[s].AsSpan(0, count)) }).ToArray(),
                MeanShips = (double)sums[0] / count, MeanProjectiles = (double)sums[1] / count,
                MeanDebris = (double)sums[2] / count, MeanParticles = (double)sums[3] / count, MeanTurrets = (double)sums[4] / count };
        }
        object RemainderSummary()
        {
            const int start = 2000;
            var count = options.Ticks - start;
            return new { StartTick = start + 1, EndTick = options.Ticks, Timing = Stats(timing.AsSpan(start, count)),
                Stages = Battle.StageNames.Select((name, s) => new { Name = name, Timing = Stats(stages[s].AsSpan(start, count)) }).ToArray() };
        }
        var report = new
        {
            Metadata = new { Framework = fresh.Name, Implementation = "independent-native-cores-v1",
                ReplayValidation = new { Revision = ReplayHash.Revision, Reference = "SKECS", ReferenceProcess = "separate", ValidatedTicks = options.Ticks, InitialStateValidated = true, InitialStateHash = reference[0].ToString("X16"), TraceSha256 = ReplayHash.TraceDigest(hashes), TimerScope = "outside Step timer and allocation counter" }, Runtime = RuntimeInformation.FrameworkDescription, Os = RuntimeInformation.OSDescription,
                Machine = BenchmarkMachineLabel.Value, Date = DateTimeOffset.Now, TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                TargetTickRate = 60, BudgetMs = 1000.0 / 60, options.Ticks, options.WarmupTicks, Realtime = options.Realtime,
                SimulatedSeconds = options.Ticks / 60.0, WallSeconds = elapsed, Options = options.Config, options.Preset,
                TimingScope = "Outer tickMs includes Battle.Step systems/physics/structural commits; excludes setup, snapshots, validation and telemetry serialization",
                HudTimingScope = "Battle.LastTickMs as displayed by desktop HUD; consecutive 600-tick windows, excluding outer call timer overhead",
                MemoryScope = "Managed tick allocation, managed heap, process working/private bytes; SKECS native storage separately; process includes harness buffers; process-wide GC counts include unmeasured validation/harness work",
                Collision = "circle-kernel-v1; homogeneous disk ships; relative-motion analytic disk/rectangle projectile CCD",
                GameplayRules = new { Revision = ArmamentRules.Revision, TacticalRevision = TacticalRules.Revision, PhysicsRevision = PhysicsSettings.Revision,
                    DamageRevision = DamageRules.Revision, DamageProfiles = options.Config.Damage,
                    Armament = "independent attached cannon/plasma turrets; aligned hull heavy cannon with class-scaled damage and cooldown",
                    Ai = "kill-cost target leases, stable squads of 2-4, focus fire and wounded-ship screening",
                    TargetLockTicks = TacticalRules.MinimumTargetLockTicks, SwitchImprovement = TacticalRules.SwitchImprovement,
                    MaxSquadSize = TacticalRules.MaxSquadSize,
                    Aim = "constant-velocity intercept including inherited shooter velocity and muzzle offset",
                    ImpreciseAimProbability = 0.25, MaxSpreadRadians = FireControl.MaxSpreadRadians,
                    ProjectileTeam = "none", CollisionImmunity = "owner ship only (parent for mounts); disabled/docked bodies excluded",
                    FriendlyFireCounter = "observed hits with a still-live owner identity; excludes attribution after owner destruction" } },
            Provenance = new
            {
                Sources = new { SKECS = "optimized working tree + default WorldOptions fix", Lite = "5053141d7d663c6b1855c8409ac93dfac807b239", Classic = "1213c31c9ef8325d48e74a52b345c4db242d038b",
                    Morpeh = "9f6369384792a01d74df24efd35b08ae313e4f12", StaticEcs = "23b1cf36d89497e8a126d38459d688e2b0a5dea3", StaticPack = "7bfb6cf925901b9366dda884258d727a89fff76e" },
                Binaries = Directory.GetFiles(AppContext.BaseDirectory, "*.dll").Order().ToDictionary(path => Path.GetFileName(path)!, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
            },
            Timing = Stats(timing), AllocatedBytes = measuredAllocation,
            HarnessAndSimulationAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart,
            GcCollections = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - initialCollections[g]).ToArray(),
            Stages = Battle.StageNames.Select((name, i) => new { Name = name, Timing = Stats(stages[i]) }).ToArray(),
            HudTiming = Stats(hudTiming), HudWindows = hudWindows,
            InitialWindow = initialWindow,
            OpeningPhases = new { First600 = OpeningSummary(Math.Min(600, options.Ticks)), First2000 = OpeningSummary(Math.Min(2000, options.Ticks)),
                RemainderAfter2000 = options.Ticks > 2000 ? RemainderSummary() : null },
            Windows = windows, Checkpoints = checkpoints,
            Counters = battle.Counters.Copy(), Tactics = battle.Tactics, Checksum = currentHash.ToString("X16")
        };
        File.WriteAllText(options.Output, JsonSerializer.Serialize(report, JsonOptions));
        using var csv = new StreamWriter(csvPath);
        csv.WriteLine("tick,simulationSeconds,tickMs,allocatedBytes,gen0,gen1,gen2,ships,projectiles,debris,particles,adds,removes,turrets,hudMs," + string.Join(',', Battle.StageNames) + ",stateHash");
        for (var i = 0; i < options.Ticks; i++)
        {
            var c = entityCounts[i]; var s = structuralCounts[i]; var g = collections[i];
            csv.WriteLine($"{i + 1},{(i + 1) / 60.0:R},{timing[i]:R},{allocation[i]},{g.Gen0},{g.Gen1},{g.Gen2},{c.Ships},{c.Projectiles},{c.Debris},{c.Particles},{s.Adds},{s.Removes},{c.Turrets},{hudTiming[i]:R}," + string.Join(',', stages.Select(stage => stage[i].ToString("R", CultureInfo.InvariantCulture))) + $",{hashes[i]:X16}");
        }
        Console.WriteLine($"Wrote {options.Output} and {csvPath}");
    }
}
