using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SKECS;
using SKECS.Profiler;
using AdaptivePlacementFixture;

const int capacity = 128;
const int warmup = 16;
const double frameRegression = 0.10;
const double tailRegression = 0.15;
const double denseRegression = 0.10;
const double startupRegression = 0.15;
const double minimumGain = 0.05;

const string usage = "Pass a local output directory, optionally --smoke and --legacy-coverage report.json.";
if (args.Length == 0) throw new ArgumentException(usage);
(bool smoke, string? coveragePath) = args[1..] switch
{
    [] => (false, null),
    ["--smoke"] => (true, null),
    ["--legacy-coverage", var path] => (false, path),
    ["--smoke", "--legacy-coverage", var path] => (true, path),
    _ => throw new ArgumentException(usage),
};
var frames = smoke ? 8 : 128;
var repetitions = smoke ? 1 : 3;
var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var training = new Scenario("training", 1024, 1973, false);
var holdout = new[] { new Scenario("holdout-churn", 1536, 11939, false), new("holdout-quiet", 1536, 9137, true) };
var planPath = Path.Combine(output, "experiment-plan.json");
var gates = new { frameRegression, tailRegression, denseRegression, startupRegression, minimumGain };
var coreSha256 = Hash(Path.Combine(AppContext.BaseDirectory, "SKECS.dll"));
var profilerSha256 = Hash(Path.Combine(AppContext.BaseDirectory, "SKECS.Profiler.dll"));
var fixtureSha256 = Hash(Path.Combine(AppContext.BaseDirectory, "AdaptivePlacementFixture.dll"));
var coverage = coveragePath == null ? null : CoverageInput.Read(coveragePath, fixtureSha256, coreSha256, profilerSha256);
WriteJson(planPath, new
{
    Format = "adaptive-placement-fixture-v2", Smoke = smoke,
    Runtime = RuntimeInformation.FrameworkDescription, OperatingSystem = RuntimeInformation.OSDescription,
    Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount, Stopwatch.Frequency,
    ChunkCapacity = capacity, Repetitions = repetitions, Warmup = warmup, Frames = frames, Gates = gates,
    Training = training, Holdout = holdout, HoldoutRule = "Verify only the training winner on both controls.",
    CoreSha256 = coreSha256, ProfilerSha256 = profilerSha256, FixtureSha256 = fixtureSha256,
    CoverageSha256 = coverage?.Sha256,
    Controls = new[] { ControlKind.SamePolicy, ControlKind.TunedArchetypes },
});
var planSha256 = Hash(planPath);
var evidence = new TransitionEvidence();
var unlimited = new ChurnControlOptions(long.MaxValue, maxOversizePercent: 0);
var eligible = Array.Empty<Type>();
using (var world = new World(Options(unlimited, eligible)))
{
    var simulation = new Simulation(training.Entities, training.Seed, training.Quiet);
    simulation.Initialize(world);
    for (var i = 0; i < warmup + frames; i++)
    {
        simulation.Step(world, i, evidence);
        simulation.Dense();
    }
}

var signals = evidence.Candidates(training.Entities)
    .Where(candidate => coverage == null || coverage.Permitted.Contains(candidate.Signal.Name)).ToArray();
eligible = signals.Select(item => item.Type).ToArray();
var placements = eligible.Select(type => new[] { type }).ToList();
// Bounded two-type fixture. Try the interaction even if a singleton misses a release gate.
if (eligible.Length > 1) placements.Add(eligible.Take(2).ToArray());
var baselinePolicies = Policies(Array.Empty<Type>());
var baseline = baselinePolicies[^1];
var candidates = placements.Select(types => (Types: types, Policies: Policies(types))).ToArray();
var allTrials = new List<Trial>();
var viable = new List<(Type[] Types, Policy Policy, Trial Trial)>();
GC.Collect();
// Prepare the dense baseline and every JIT specialization before timed repetitions.
foreach (var types in placements.Prepend(Type.EmptyTypes))
    _ = Measure(training with { Entities = 32 }, unlimited, types, sampleFrames: 4);

var baselineTuning = baselinePolicies.Where(policy => policy != baseline)
    .Select(policy => Compare(training, [], policy, baseline, ControlKind.ArchetypeTuning)).ToArray();
var tunedBaseline = baselineTuning.Where(trial => trial.Rejections.Length == 0)
    .OrderBy(trial => trial.CandidatePeakBytes).ThenBy(trial => trial.PairedFrameRatio).FirstOrDefault();
if (tunedBaseline != null) baseline = tunedBaseline.Policy;

foreach (var placement in candidates)
foreach (var policy in placement.Policies)
{
    var trials = CompareAgainstBoth(training, placement.Types, policy);
    allTrials.AddRange(trials);
    if (trials.All(trial => trial.Rejections.Length == 0)) viable.Add((placement.Types, policy, trials[1]));
}

Trial? selected = null;
Trial[] selectedHoldout = [];
var contender = viable.Where(candidate => !viable.Any(item => item.Types.Length < candidate.Types.Length &&
        item.Types.All(candidate.Types.Contains) && item.Trial.CandidatePeakBytes <= candidate.Trial.CandidatePeakBytes &&
        item.Trial.PairedFrameRatio <= candidate.Trial.PairedFrameRatio))
    .OrderBy(item => item.Trial.CandidatePeakBytes).ThenBy(item => item.Trial.PairedFrameRatio)
    .ThenBy(item => item.Types.Length).FirstOrDefault();
if (contender.Trial != null)
{
    var checks = holdout.SelectMany(scenario => CompareAgainstBoth(scenario, contender.Types, contender.Policy)).ToArray();
    allTrials.AddRange(checks);
    if (checks.All(trial => trial.Rejections.Length == 0))
    {
        selected = contender.Trial;
        selectedHoldout = checks;
    }
}

var holdoutStates = new List<object>();
var everyStateMatches = baselineTuning.Concat(allTrials).All(trial => trial.StateMatches);
foreach (var scenario in holdout)
{
    var controlHashes = Replay(scenario, Churn(baseline), [], frames);
    foreach (var placement in candidates)
    {
        var actual = Replay(scenario, Churn(placement.Policies[^1]), placement.Types, frames);
        var matched = controlHashes.SequenceEqual(actual);
        holdoutStates.Add(new { scenario, Types = placement.Types.Select(type => type.FullName), matched,
            Control = controlHashes, Candidate = actual });
        everyStateMatches &= matched;
    }
}

// Only a measured result backed by static consumer coverage may issue profile evidence.
if (smoke || coverage == null || !everyStateMatches) selected = null;
var packedEvidence = selected == null ? null : new
{
    SamePolicySha256 = WriteReceipt(ControlKind.SamePolicy, "same-policy-receipt.json", selected),
    TunedArchetypesSha256 = WriteReceipt(ControlKind.TunedArchetypes, "tuned-archetypes-receipt.json", selected),
};

WriteJson(Path.Combine(output, "qualification.json"), new
{
    Runtime = RuntimeInformation.FrameworkDescription, OperatingSystem = RuntimeInformation.OSDescription,
    Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
    Stopwatch.Frequency, ChunkCapacity = capacity, Repetitions = repetitions, Warmup = warmup, Frames = frames,
    Gates = gates, Smoke = smoke, PlanSha256 = planSha256, CoverageSha256 = coverage?.Sha256, PackedEvidence = packedEvidence,
    Screening = signals.Select(item => item.Signal), Baseline = baseline, BaselineTuning = baselineTuning, Selected = selected?.Placement,
    SelectedPolicy = selected?.Policy, Trials = allTrials, HoldoutStates = holdoutStates,
});
Console.WriteLine($"{allTrials.Count} paired trials. Qualified placement: " +
    (selected == null ? "none" : string.Join(", ", selected.Placement)));
return everyStateMatches && (!smoke || allTrials.Any(trial => trial.Scenario != training)) ? 0 : 1;

WorldOptions Options(ChurnControlOptions churn, Type[] placement) => new WorldOptions(capacity)
    .WithChurnControl(churn).WithComponentPlacement(Simulation.Permission.WithPackedPools(placement));

Policy[] Policies(Type[] placement)
{
    var simulator = new Simulation(training.Entities, training.Seed, training.Quiet);
    var profiler = new ProfilerSession(160_000);
    long peak, floor;
    using (var world = new World(Options(unlimited, placement), profiler.Buffer))
    {
        simulator.Initialize(world);
        peak = world.GetChurnControlStatistics().RetainedBytes;
        floor = world.GetChurnControlStatistics().NonReclaimableBytes;
        for (var frame = 0; frame < warmup + frames; frame++)
        {
            profiler.BeginFrame();
            simulator.Step(world, frame);
            simulator.Dense();
            profiler.EndFrame();
            var stats = world.GetChurnControlStatistics();
            peak = Math.Max(peak, stats.RetainedBytes);
            floor = Math.Max(floor, stats.NonReclaimableBytes);
        }
    }
    if (profiler.Buffer.DroppedEvents != 0) throw new InvalidOperationException("Training trace overflow.");
    using (var stream = File.Create(Path.Combine(output, (placement.Length == 0 ? "archetypes" :
        string.Join('-', placement.Select(type => type.Name))) + ".xml")))
        profiler.Export(stream, training.Name, "adaptive-placement-fixture-v1");

    var startupSizes = new HashSet<int>();
    for (var index = 0; index < profiler.Buffer.Count; index++)
    {
        ref readonly var entry = ref profiler.Buffer[index];
        if (entry.Kind == DiagnosticEventKind.FrameStarted) break;
        if (entry.Kind is DiagnosticEventKind.PackedPageAllocated or DiagnosticEventKind.ChunkAllocated)
            startupSizes.Add(checked((int)entry.NativeBytes));
    }
    var sizes = startupSizes.Order().ToArray();
    return new[] { floor, floor + (peak - floor) / 2, peak }.Distinct()
        .SelectMany(target => new[] { new Policy(target, sizes), new Policy(target, []) }).ToArray();
}

Trial[] CompareAgainstBoth(Scenario scenario, Type[] placement, Policy candidate) =>
    [Compare(scenario, placement, candidate, candidate, ControlKind.SamePolicy),
     Compare(scenario, placement, candidate, baseline, ControlKind.TunedArchetypes)];

Trial Compare(Scenario scenario, Type[] placement, Policy candidate, Policy controlPolicy, string controlKind)
{
    var controls = new Run[repetitions];
    var alternatives = new Run[repetitions];
    var rejections = new HashSet<string>();
    var pairedFrame = new double[repetitions];
    var pairedTail = new double[repetitions];
    var pairedDense = new double[repetitions];
    var pairedStartup = new double[repetitions];
    var managedDelta = new long[repetitions];
    var stateMatches = true;
    for (var repetition = 0; repetition < repetitions; repetition++)
    {
        if (repetition % 2 == 0)
        {
            controls[repetition] = Measure(scenario, Churn(controlPolicy), []);
            alternatives[repetition] = Measure(scenario, Churn(candidate), placement);
        }
        else
        {
            alternatives[repetition] = Measure(scenario, Churn(candidate), placement);
            controls[repetition] = Measure(scenario, Churn(controlPolicy), []);
        }

        var control = controls[repetition];
        var alternative = alternatives[repetition];
        if (!control.Hashes.SequenceEqual(alternative.Hashes))
        {
            stateMatches = false;
            var first = 0;
            while (control.Hashes[first] == alternative.Hashes[first]) first++;
            rejections.Add($"Full-state or event mismatch at hash index {first}.");
        }
        pairedFrame[repetition] = Ratio(Percentile(alternative.Frame, .50), Percentile(control.Frame, .50));
        pairedTail[repetition] = Ratio(Percentile(alternative.Frame, .99), Percentile(control.Frame, .99));
        pairedDense[repetition] = Ratio(Percentile(alternative.Dense, .95), Percentile(control.Dense, .95));
        pairedStartup[repetition] = Ratio(alternative.StartupTicks, control.StartupTicks);
        managedDelta[repetition] = alternative.ManagedBytes - control.ManagedBytes;
    }

    var frameRatio = Median(pairedFrame);
    if (!smoke)
    {
        if (frameRatio > 1 + frameRegression) rejections.Add("Full-frame median regression.");
        if (Median(pairedTail) > 1 + tailRegression) rejections.Add("Full-frame p99 regression.");
        if (Median(pairedDense) > 1 + denseRegression) rejections.Add("Dense traversal regression.");
        if (Median(pairedStartup) > 1 + startupRegression) rejections.Add("Startup regression.");
        if (managedDelta.Order().ElementAt(repetitions / 2) > frames * 64L) rejections.Add("Managed frame allocation regression.");
        if (scenario == training && placement.Length != 0 && frameRatio > 1 - minimumGain)
            rejections.Add("No qualifying full-frame gain.");
    }
    return new Trial(scenario, placement.Select(type => type.FullName!).ToArray(), candidate, controlPolicy, controlKind, stateMatches, frameRatio,
        Median(pairedTail), Median(pairedDense), Median(pairedStartup), alternatives.Max(run => run.PeakBytes),
        rejections.Order().ToArray(), controls, alternatives);
}

string WriteReceipt(string controlKind, string filename, Trial selection)
{
    var matched = allTrials.Single(trial => trial.ControlKind == controlKind && trial.Scenario == training &&
        trial.Policy == selection.Policy && trial.Placement.SequenceEqual(selection.Placement));
    var path = Path.Combine(output, filename);
    WriteJson(path, new
    {
        ControlKind = controlKind, PlanSha256 = planSha256, Training = matched,
        Holdout = selectedHoldout.Where(trial => trial.ControlKind == controlKind).ToArray(),
    });
    return Hash(path);
}

ChurnControlOptions Churn(Policy policy) => new(policy.Target,
    policy.StartupSizes.Sum(size => size + 8L * capacity),
    policy.StartupSizes.Select(size => new ChurnStartupBlock(size, 1)).ToArray(), maxOversizePercent: 0);

Run Measure(Scenario scenario, ChurnControlOptions churn, Type[] placement, int sampleFrames = 0)
{
    if (sampleFrames == 0) sampleFrames = frames;
    var simulator = new Simulation(scenario.Entities, scenario.Seed, scenario.Quiet);
    var frameSamples = new long[sampleFrames];
    var denseSamples = new long[sampleFrames];
    var adaptiveSamples = new long[sampleFrames];
    var startupAllocated = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    using var world = new World(Options(churn, placement));
    simulator.Initialize(world);
    var startup = Stopwatch.GetTimestamp() - start;
    var startupManaged = GC.GetAllocatedBytesForCurrentThread() - startupAllocated;
    var peak = world.GetChurnControlStatistics().RetainedBytes;
    long managed = 0;
    var gc = new int[GC.MaxGeneration + 1];
    for (var frame = 0; frame < warmup + sampleFrames; frame++)
    {
        if (frame == warmup)
            for (var generation = 0; generation < gc.Length; generation++) gc[generation] = GC.CollectionCount(generation);
        var before = GC.GetAllocatedBytesForCurrentThread();
        start = Stopwatch.GetTimestamp();
        var adaptiveTicks = simulator.Step(world, frame);
        var denseStart = Stopwatch.GetTimestamp();
        simulator.Dense();
        var denseEnd = Stopwatch.GetTimestamp();
        world.TrimChurnMemory();
        var end = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (frame >= warmup)
        {
            frameSamples[frame - warmup] = end - start;
            denseSamples[frame - warmup] = denseEnd - denseStart;
            adaptiveSamples[frame - warmup] = adaptiveTicks;
            managed += allocated;
        }
        peak = Math.Max(peak, world.GetChurnControlStatistics().RetainedBytes);
    }
    for (var generation = 0; generation < gc.Length; generation++) gc[generation] = GC.CollectionCount(generation) - gc[generation];
    var stats = world.GetChurnControlStatistics();
    world.Dispose();
    return new Run(startup, startupManaged, frameSamples, denseSamples, adaptiveSamples, managed, gc, peak, stats.RetainedBytes,
        stats.AllocationCount, Replay(scenario, churn, placement, sampleFrames));
}

string[] Replay(Scenario scenario, ChurnControlOptions churn, Type[] placement, int sampleFrames)
{
    using var world = new World(Options(churn, placement));
    var simulator = new Simulation(scenario.Entities, scenario.Seed, scenario.Quiet);
    var hashes = new string[warmup + sampleFrames + 1];
    simulator.Initialize(world);
    hashes[0] = simulator.CanonicalHash(world);
    for (var frame = 0; frame < warmup + sampleFrames; frame++)
    {
        simulator.Step(world, frame);
        simulator.Dense();
        world.TrimChurnMemory();
        hashes[frame + 1] = simulator.CanonicalHash(world);
    }
    return hashes;
}

static long Percentile(long[] input, double fraction) => input.Order().ElementAt((int)Math.Ceiling(input.Length * fraction) - 1);
static double Ratio(long numerator, long denominator) => (double)numerator / Math.Max(1, denominator);
static double Median(double[] input) => input.Order().ElementAt(input.Length / 2);

static void WriteJson(string path, object value)
{
    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    JsonSerializer.Serialize(file, value, new JsonSerializerOptions { WriteIndented = true });
}

static string Hash(string path)
{
    using var file = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(file));
}

internal sealed record Scenario(string Name, int Entities, int Seed, bool Quiet);
internal sealed record Policy(long Target, int[] StartupSizes);
internal static class ControlKind
{
    internal const string ArchetypeTuning = "archetype-tuning";
    internal const string SamePolicy = "same-policy";
    internal const string TunedArchetypes = "tuned-archetypes";
}
internal sealed record Trial(Scenario Scenario, string[] Placement, Policy Policy, Policy ControlPolicy, string ControlKind,
    bool StateMatches, double PairedFrameRatio,
    double PairedTailRatio, double PairedDenseRatio, double PairedStartupRatio, long CandidatePeakBytes, string[] Rejections,
    Run[] BaselineRuns, Run[] CandidateRuns);
internal sealed record Run(long StartupTicks, long StartupManagedBytes, long[] Frame, long[] Dense, long[] Adaptive,
    long ManagedBytes, int[] GcCollections, long PeakBytes, long FinalBytes, long NativeAllocations, string[] Hashes);
