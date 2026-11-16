using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SKECS.RuntimeProfiles.Tools;

internal sealed record CapturedCase(string Scenario, string TraceSha256);
internal sealed record CaptureReceipt(string Version, string CorpusSha256, RunEnvironment Environment,
    ProfileIdentity Identity, CapturedCase[] Cases);
internal sealed record CaseMemory(string Scenario, MemoryPeak LowerBound, long PeakRetained);
internal sealed record Proposal(string Version, string CaptureSha256, CaseMemory[] LowerBounds, Candidate[] Candidates);
internal sealed record Verification(string Version, string ProposalSha256, Guid Attempt, CandidateReport[] Training,
    CandidateReport? Holdout, string? Winner);

internal sealed class TuningSession(string directory)
{
    private const string Version = "1";
    private string PathFor(params string[] names) => Path.Combine([directory, .. names]);

    internal void Capture(string corpusPath)
    {
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new InvalidDataException("Capture requires a new run directory.");
        var bytes = File.ReadAllBytes(corpusPath);
        var corpus = Artifacts.Read<Corpus>(bytes);
        corpus.Validate();
        var hash = Artifacts.Hash(bytes);
        var environment = RunEnvironment.Current();
        Directory.CreateDirectory(PathFor("traces"));
        File.WriteAllBytes(PathFor("corpus.json"), bytes);
        var cases = new List<CapturedCase>();
        var traces = new List<ScenarioTrace>();
        foreach (var scenario in corpus.Scenarios)
        {
            var path = TracePath(scenario);
            using (var stream = File.Create(path))
                ScenarioRunner.Capture(corpus, scenario, TraceWorkload(corpus, scenario, hash), stream);
            traces.Add(ParseTrace(path, corpus, scenario, hash, environment));
            cases.Add(new(scenario.Id, Artifacts.HashFile(path)));
        }
        var identity = Identity(corpus, environment, traces);
        Artifacts.Write(PathFor("capture.json"), new CaptureReceipt(Version, hash, environment, identity, cases.ToArray()));
    }

    internal int Propose()
    {
        var (corpus, _, traces, hash) = ReadCapture();
        var proposal = BuildProposal(corpus, traces, hash);
        WriteAtomic(PathFor("proposal.json"), proposal);
        Console.WriteLine($"{proposal.Candidates.Length} candidate(s). Lower bounds and estimates are in proposal.json.");
        return proposal.Candidates.Length == 0 ? 1 : 0;
    }

    internal int Verify()
    {
        var (corpus, capture, traces, captureHash) = ReadCapture();
        var (proposal, proposalHash) = ReadProposal(corpus, traces, captureHash);
        var result = Evaluate(corpus, capture, traces, proposal, proposalHash, Guid.NewGuid(), run: true);
        File.WriteAllText(PathFor("report.md"), BuildReport(corpus, capture, traces, proposal, result));
        WriteAtomic(PathFor("verification.json"), result);
        Console.WriteLine(result.Winner == null ? "No verified improvement. See report.md." : $"Verified {result.Winner}. See report.md.");
        return result.Winner == null ? 1 : 0;
    }

    internal void Emit(string outputDirectory)
    {
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new InvalidDataException("Emit requires a new output directory.");
        var (corpus, capture, traces, captureHash) = ReadCapture();
        var (proposal, proposalHash) = ReadProposal(corpus, traces, captureHash);
        var evidenceBytes = File.ReadAllBytes(PathFor("verification.json"));
        var receipt = Artifacts.Read<Verification>(evidenceBytes);
        if (receipt.Version != Version || receipt.ProposalSha256 != proposalHash || receipt.Attempt == Guid.Empty)
            throw new InvalidDataException("Verification is stale or incomplete.");
        if (receipt.Winner == null)
            throw new InvalidDataException("Verification found no qualifying improvement.");

        var checkedResult = Evaluate(corpus, capture, traces, proposal, proposalHash, receipt.Attempt, run: false);
        if (checkedResult.Winner == null || !Artifacts.Serialize(checkedResult).AsSpan().SequenceEqual(evidenceBytes))
            throw new InvalidDataException("Verification evidence is incomplete or differs from the recorded result.");
        var candidate = proposal.Candidates.Single(item => item.Id == checkedResult.Winner);
        var profile = new ChurnProfile(ChurnProfile.FormatId, ChurnProfile.Version, capture.Identity, candidate.Policy.ToSettings(),
            new ProfileEvidence(Version, capture.CorpusSha256, Artifacts.Hash(evidenceBytes),
                corpus.Scenarios.Count(scenario => !scenario.Holdout), corpus.Scenarios.Count(scenario => scenario.Holdout), corpus.Repetitions));

        var stage = outputDirectory + "." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        try
        {
            using (var stream = File.Create(Path.Combine(stage, "churn-profile.json"))) profile.Save(stream);
            File.WriteAllText(Path.Combine(stage, "GeneratedChurnProfile.cs"), GenerateSource(profile));
            Directory.Move(stage, outputDirectory);
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
        Console.WriteLine($"Emitted {candidate.Id} to {outputDirectory}.");
    }

    private (Corpus, CaptureReceipt, ScenarioTrace[], string) ReadCapture()
    {
        var corpusBytes = File.ReadAllBytes(PathFor("corpus.json"));
        var corpus = Artifacts.Read<Corpus>(corpusBytes);
        corpus.Validate();
        var hash = Artifacts.Hash(corpusBytes);
        var receiptBytes = File.ReadAllBytes(PathFor("capture.json"));
        var receipt = Artifacts.Read<CaptureReceipt>(receiptBytes);
        var current = RunEnvironment.Current();
        if (receipt.Version != Version || receipt.CorpusSha256 != hash || receipt.Environment != current ||
            receipt.Cases is null || receipt.Cases.Length != corpus.Scenarios.Length)
            throw new InvalidDataException("Capture does not match the corpus, executable build or current measurement environment.");
        var traces = new ScenarioTrace[corpus.Scenarios.Length];
        for (var i = 0; i < traces.Length; i++)
        {
            var scenario = corpus.Scenarios[i];
            var path = TracePath(scenario);
            if (receipt.Cases[i] != new CapturedCase(scenario.Id, Artifacts.HashFile(path)))
                throw new InvalidDataException($"Capture trace differs for {scenario.Id}.");
            traces[i] = ParseTrace(path, corpus, scenario, hash, current);
        }
        if (receipt.Identity != Identity(corpus, current, traces))
            throw new InvalidDataException("Capture schema or core build identity is stale.");
        return (corpus, receipt, traces, Artifacts.Hash(receiptBytes));
    }

    private (Proposal, string) ReadProposal(Corpus corpus, ScenarioTrace[] traces, string captureHash)
    {
        var bytes = File.ReadAllBytes(PathFor("proposal.json"));
        var expected = BuildProposal(corpus, traces, captureHash);
        if (!bytes.AsSpan().SequenceEqual(Artifacts.Serialize(expected)))
            throw new InvalidDataException("Proposal is stale or differs from the deterministic search result.");
        return (expected, Artifacts.Hash(bytes));
    }

    private static Proposal BuildProposal(Corpus corpus, ScenarioTrace[] traces, string captureHash) => new(Version, captureHash,
        traces.Where(entry => !entry.Scenario.Holdout).Select(entry => new CaseMemory(entry.Scenario.Id,
            entry.Trace.LowerBound, entry.Trace.PeakRetained)).ToArray(),
        PolicySearch.Propose(traces.Where(entry => !entry.Scenario.Holdout).ToArray(), corpus.Gates.MaxRetainedBytes));

    private Verification Evaluate(Corpus corpus, CaptureReceipt capture, ScenarioTrace[] traces, Proposal proposal,
        string proposalHash, Guid attempt, bool run)
    {
        var training = proposal.Candidates.Select(candidate => EvaluateFold(candidate, holdout: false)).ToArray();
        var qualified = training.Where(report => report.Failures.Length == 0).ToArray();
        if (qualified.Length == 0) return new(Version, proposalHash, attempt, training, null, null);
        var best = qualified.Max(report => report.Gains.Best);
        var candidate = proposal.Candidates.Where(item => qualified.Any(report => report.CandidateId == item.Id &&
                report.Gains.Best >= best - corpus.Gates.KneePercent))
            .OrderBy(item => item.Policy.TargetRetainedBytes).ThenBy(item => item.Policy.StartupReserveBytes)
            .ThenBy(item => item.Policy.InitialEntityCapacity).ThenBy(item => item.Id, StringComparer.Ordinal).First();
        var holdout = EvaluateFold(candidate, holdout: true);
        return new(Version, proposalHash, attempt, training, holdout, holdout.Failures.Length == 0 ? candidate.Id : null);

        CandidateReport EvaluateFold(Candidate candidate, bool holdout)
        {
            var reports = new List<CaseReport>();
            foreach (var entry in traces.Where(entry => entry.Scenario.Holdout == holdout))
            {
                if (run) Console.WriteLine($"{candidate.Id} {(holdout ? "holdout" : "training")} {entry.Scenario.Id}");
                var pairs = new PairReport[corpus.Repetitions];
                var failures = new List<string>();
                var policies = new Dictionary<string, Policy?>
                {
                    ["off"] = null,
                    ["generic"] = Policy.Generic(corpus.GenericTargetRetainedBytes),
                    ["candidate"] = candidate.Policy
                };
                if (candidate.Policy.StartupReserveBytes != 0 || candidate.Policy.InitialEntityCapacity != 0)
                    policies.Add("no-reserve", candidate.Policy with { StartupReserveBytes = 0, StartupBlocks = [], InitialEntityCapacity = 0 });
                var variants = policies.Keys.ToArray();
                string[]? reference = null;
                for (var repetition = 0; repetition < pairs.Length; repetition++)
                {
                    var location = PathFor("runs", attempt.ToString("N"), candidate.Id, entry.Scenario.Id,
                        repetition.ToString(CultureInfo.InvariantCulture));
                    if (run) Directory.CreateDirectory(location);
                    var measurements = new Dictionary<string, (RunResult Result, string Sha256)>();
                    for (var index = 0; index < variants.Length; index++)
                    {
                        var variant = variants[(index + repetition) % variants.Length];
                        var request = new RunRequest(entry.Scenario, corpus.ChunkCapacity, corpus.EventCapacity, policies[variant]);
                        var file = Path.Combine(location, variant + ".json");
                        if (run) RunChild(location, variant, request, file);
                        var bytes = File.ReadAllBytes(file);
                        var result = Artifacts.Read<RunResult>(bytes);
                        ValidateResult(result, request, capture.Environment, variant);
                        measurements.Add(variant, (result, Artifacts.Hash(bytes)));
                    }
                    var baseline = measurements["off"];
                    var generic = measurements["generic"];
                    var tuned = measurements["candidate"];
                    if (baseline.Result.Diagnostics.PeakRetained != entry.Trace.PeakRetained ||
                        baseline.Result.Diagnostics.LowerBound != entry.Trace.LowerBound)
                        failures.Add($"Default retention in repetition {repetition + 1} differs from capture.");
                    reference ??= baseline.Result.StateHashes;
                    foreach (var variant in variants)
                    {
                        var states = measurements[variant].Result.StateHashes;
                        var different = -1;
                        for (var i = 0; i < states.Length; i++)
                            if (states[i] != reference[i]) { different = i; break; }
                        if (different >= 0)
                            failures.Add($"{variant} state/events differ from default in repetition {repetition + 1}, frame {different - 1}.");
                    }
                    var noReserve = measurements.GetValueOrDefault("no-reserve");
                    pairs[repetition] = new(baseline.Sha256, generic.Sha256, tuned.Sha256, noReserve.Sha256,
                        Metrics.From(baseline.Result), Metrics.From(generic.Result), Metrics.From(tuned.Result),
                        noReserve.Result == null ? null : Metrics.From(noReserve.Result));
                }
                reports.Add(Acceptance.Assess(corpus, entry, candidate.Policy, capture.Environment.StopwatchFrequency, pairs, failures));
            }
            return Acceptance.Summarize(corpus, candidate, reports.ToArray(), holdout);
        }
    }

    private static void ValidateResult(RunResult result, RunRequest request, RunEnvironment environment, string variant)
    {
        if (!Artifacts.Serialize(result.Request).AsSpan().SequenceEqual(Artifacts.Serialize(request)) || result.Environment != environment ||
            result.StartupTicks <= 0 || result.FirstFrameTicks <= 0 || result.StartupManagedBytes < 0 ||
            !ValidMemory(result.Initial) || !ValidMemory(result.AfterWarmup) || result.Diagnostics is not { Events: > 0 } ||
            result.Diagnostics.Version != (request.Policy == null ? 1 : 2) ||
            result.Diagnostics.PeakRetained < result.Diagnostics.LowerBound.Total ||
            result.Diagnostics.PeakRetained < result.Initial.RetainedBytes || result.Diagnostics.PeakRetained < result.AfterWarmup.RetainedBytes ||
            result.StateHashes?.Length != request.Scenario.WarmupFrames + (long)request.Scenario.Frames + 1 ||
            result.StateHashes.Any(hash => hash == null || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) ||
            result.FrameGcCollections is not { Length: 3 } || result.FrameGcCollections.Any(count => count < 0) ||
            result.Frames?.Length != request.Scenario.Frames)
            throw new InvalidDataException($"Invalid or stale {variant} measurement for {request.Scenario.Id}.");

        var last = result.AfterWarmup;
        foreach (var frame in result.Frames)
        {
            if (frame == null || string.IsNullOrWhiteSpace(frame.Checksum) || string.IsNullOrWhiteSpace(frame.Phase) ||
                frame.FrameTicks <= 0 || frame.TrimTicks < 0 || frame.TrimTicks > frame.FrameTicks || frame.ManagedBytes < 0 ||
                !ValidMemory(frame.Memory) || frame.Memory.Allocations < last.Allocations || frame.ExplicitReleases < 0 ||
                frame.Memory.Releases - last.Releases < frame.ExplicitReleases || frame.Memory.RetainedBytes > result.Diagnostics.PeakRetained)
                throw new InvalidDataException($"Invalid frame in {variant} measurement for {request.Scenario.Id}.");
            last = frame.Memory;
        }
    }

    private static bool ValidMemory(MemorySample? memory) => memory is { MandatoryBytes: >= 0, Allocations: >= 0, Releases: >= 0 } &&
        memory.RetainedBytes >= memory.MandatoryBytes;

    private static void RunChild(string location, string variant, RunRequest request, string resultPath)
    {
        var requestPath = Path.Combine(location, variant + ".request.json");
        Artifacts.Write(requestPath, request);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var process = new ProcessStartInfo(string.IsNullOrWhiteSpace(dotnet) ? "dotnet" : dotnet)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.ArgumentList.Add(RunEnvironment.AssemblyPath);
        process.ArgumentList.Add("__run");
        process.ArgumentList.Add(requestPath);
        process.ArgumentList.Add(resultPath);
        using var child = Process.Start(process) ?? throw new IOException("Unable to start the workload process.");
        child.WaitForExit();
        if (child.ExitCode != 0) throw new InvalidDataException($"{variant} process failed for {request.Scenario.Id} (exit {child.ExitCode}).");
    }

    private string TracePath(Scenario scenario) => PathFor("traces", "case-" + scenario.Id + ".xml");

    private static ScenarioTrace ParseTrace(string path, Corpus corpus, Scenario scenario, string corpusSha256, RunEnvironment environment)
    {
        using var stream = File.OpenRead(path);
        var trace = TraceReader.Read(stream);
        var header = trace.Header;
        if (header.SchemaVersion != 1 || header.Scenario != scenario.Id || header.WorkloadId != TraceWorkload(corpus, scenario, corpusSha256) ||
            header.Runtime != environment.Runtime || header.Os != environment.Os || header.Architecture != environment.Architecture ||
            header.StopwatchFrequency != environment.StopwatchFrequency || trace.ChunkCapacity != corpus.ChunkCapacity ||
            trace.Frames != scenario.WarmupFrames + scenario.Frames)
            throw new InvalidDataException($"Trace identity or frame count differs for {scenario.Id}.");
        return new(scenario, trace);
    }

    private static string TraceWorkload(Corpus corpus, Scenario scenario, string hash) => $"{corpus.WorkloadId}:{scenario.Id}:{hash}";

    private static ProfileIdentity Identity(Corpus corpus, RunEnvironment environment, IEnumerable<ScenarioTrace> traces)
    {
        var layouts = traces.SelectMany(entry => entry.Trace.Layouts).Select(layout => Convert.ToHexString(Artifacts.Serialize(layout)))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return ProfileIdentity.Current(environment.BuildId, corpus.WorkloadId, Artifacts.Hash(Artifacts.Serialize(layouts)),
            corpus.TargetId, corpus.ChunkCapacity);
    }

    private static void WriteAtomic<T>(string path, T value)
    {
        var pending = path + "." + Guid.NewGuid().ToString("N");
        try { Artifacts.Write(pending, value); File.Move(pending, path, overwrite: true); }
        finally { File.Delete(pending); }
    }

    private static string GenerateSource(ChurnProfile profile)
    {
        var identity = profile.Identity;
        var settings = profile.Settings;
        var blocks = string.Join(", ", settings.StartupBlocks.Select(block =>
            FormattableString.Invariant($"new global::SKECS.ChurnStartupBlock({block.NativeBytes}, {block.Count})")));
        return FormattableString.Invariant($$"""
            // Generated from verified SKECS churn evidence {{profile.Evidence.VerificationSha256}}.
            internal static class GeneratedChurnProfile
            {
                internal static global::SKECS.WorldOptions CreateOptions(string buildId, string workloadId,
                    string schemaFingerprint, string targetId, int chunkCapacity, bool diagnosticsEnabled = false)
                {
                    var core = typeof(global::SKECS.World).Assembly;
                    if (buildId != {{Literal(identity.BuildId)}} || workloadId != {{Literal(identity.WorkloadId)}} ||
                        schemaFingerprint != {{Literal(identity.SchemaFingerprint)}} || targetId != {{Literal(identity.TargetId)}} ||
                        chunkCapacity != {{identity.ChunkCapacity}} ||
                        core.GetName().Version?.ToString() != {{Literal(identity.CoreVersion)}} ||
                        core.ManifestModule.ModuleVersionId.ToString("D") != {{Literal(identity.CoreBuildId)}} ||
                        global::System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription != {{Literal(identity.Runtime)}} ||
                        global::System.Runtime.InteropServices.RuntimeInformation.OSDescription != {{Literal(identity.Os)}} ||
                        global::System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() != {{Literal(identity.Architecture)}})
                        throw new global::System.InvalidOperationException("Incompatible generated SKECS churn profile.");

                    return new global::SKECS.WorldOptions({{identity.ChunkCapacity}}, diagnosticsEnabled).WithChurnControl(
                        new global::SKECS.ChurnControlOptions({{settings.TargetRetainedBytes}}L, {{settings.StartupReserveBytes}}L,
                            new global::SKECS.ChurnStartupBlock[] { {{blocks}} }, {{settings.InitialEntityCapacity}},
                            {{settings.MaxOversizePercent}}, {{settings.ReleaseBatchSize}}));
                }
            }
            """) + "\n";

        static string Literal(string value) => JsonSerializer.Serialize(value);
    }

    private static string BuildReport(Corpus corpus, CaptureReceipt capture, ScenarioTrace[] traces, Proposal proposal, Verification result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# SKECS churn profile verification").AppendLine();
        builder.AppendLine(result.Winner == null ? "No candidate qualified." : $"Verified candidate `{result.Winner}`.").AppendLine();
        builder.AppendLine($"- Corpus SHA-256 `{capture.CorpusSha256}`");
        builder.AppendLine($"- Executable SHA-256 `{capture.Identity.BuildId}` and core build `{capture.Identity.CoreBuildId}`");
        builder.AppendLine($"- Profiler build `{capture.Environment.ProfilerBuildId}`");
        builder.AppendLine($"- Runtime `{capture.Environment.Runtime}`, OS `{capture.Environment.Os}`, architecture `{capture.Environment.Architecture}`");
        builder.AppendLine($"- CPU `{capture.Environment.Cpu}` with {capture.Environment.LogicalProcessors} logical processors, server GC `{capture.Environment.ServerGc}`");
        builder.AppendLine($"- {corpus.Repetitions} independent process repetitions per case and policy, with order rotated within each case");
        builder.AppendLine("- Simulated frontier in `proposal.json`; every measured frame and state hash in `runs/`.");
        builder.AppendLine().AppendLine("| Scenario | Fold | Peak frame | Minimum retained bytes | Occupied native + pinned | Tables | Baseline peak bytes |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var entry in traces)
        {
            var peak = entry.Trace.LowerBound;
            builder.AppendLine(FormattableString.Invariant($"| {entry.Scenario.Id} | {(entry.Scenario.Holdout ? "holdout" : "training")} | {peak.Frame} | {peak.Total} | {peak.OccupiedNative + peak.OccupiedPinned} | {peak.EntityRecords + peak.FreeIds + peak.Accessors} | {entry.Trace.PeakRetained} |"));
        }
        builder.AppendLine().AppendLine("| Candidate | Target bytes | Reserve bytes | Predicted peak bytes | Predicted allocations | Training best improvement | Holdout best improvement |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var candidate in proposal.Candidates)
        {
            var training = result.Training.Single(report => report.CandidateId == candidate.Id);
            var holdout = result.Holdout?.CandidateId == candidate.Id
                ? result.Holdout.Gains.Best.ToString("F2", CultureInfo.InvariantCulture) + "%" : "—";
            builder.AppendLine(FormattableString.Invariant($"| {candidate.Id} | {candidate.Policy.TargetRetainedBytes} | {candidate.Policy.StartupReserveBytes} | {candidate.Estimated.PeakRetained} | {candidate.Estimated.MeasuredAllocations} | {training.Gains.Best:F2}% | {holdout} |"));
        }
        var qualified = proposal.Candidates.Where(candidate => result.Training.Any(report => report.CandidateId == candidate.Id &&
            report.Failures.Length == 0)).Select(candidate => candidate.Policy.TargetRetainedBytes).ToArray();
        if (qualified.Length != 0)
            builder.AppendLine().AppendLine($"Qualifying target range on training cases: {qualified.Min()}–{qualified.Max()} bytes.");

        var representative = corpus.Scenarios.Where(scenario => !scenario.Holdout && !Corpus.IsDense(scenario))
            .MaxBy(scenario => scenario.Weight)!;
        var points = result.Training.Select(report => (report.CandidateId,
            Pairs: report.Cases.Single(item => item.ScenarioId == representative.Id).Pairs)).Select(item =>
            (item.CandidateId,
            Peak: Metrics.Percentile(item.Pairs.Select(pair => pair.Candidate.PeakRetained), 0.5),
            Allocations: Metrics.Percentile(item.Pairs.Select(pair => pair.Candidate.Allocations), 0.5),
            P99: Acceptance.Microseconds(Metrics.Percentile(item.Pairs.Select(pair => pair.Candidate.P99Ticks), 0.5),
                capture.Environment.StopwatchFrequency))).OrderBy(item => item.Peak).ToArray();
        if (points.Length > 1)
        {
            var labels = string.Join(", ", points.Select(point => $"\"{point.CandidateId} / {point.Peak} B\""));
            Chart("Physical allocations", "count", points.Select(point => (double)point.Allocations));
            Chart("Frame p99", "µs", points.Select(point => point.P99));

            void Chart(string title, string unit, IEnumerable<double> values)
            {
                var y = values.ToArray();
                builder.AppendLine().AppendLine("```mermaid").AppendLine("xychart-beta");
                builder.AppendLine($"    title \"{title} at observed peak retention for {representative.Id}\"");
                builder.AppendLine($"    x-axis [{labels}]");
                builder.AppendLine(FormattableString.Invariant($"    y-axis \"{unit}\" 0 --> {Math.Max(1, Math.Ceiling(y.Max()))}"));
                builder.AppendLine($"    line [{string.Join(", ", y.Select(value => value.ToString("0.##", CultureInfo.InvariantCulture)))}]");
                builder.AppendLine("```");
            }
        }
        foreach (var report in result.Training.Concat(result.Holdout == null ? [] : new[] { result.Holdout }))
        {
            builder.AppendLine().AppendLine($"## {report.CandidateId} {(report == result.Holdout ? "holdout" : "training")}");
            foreach (var failure in report.Failures) builder.AppendLine().AppendLine(failure);
            builder.AppendLine().AppendLine("| Scenario | Policy | Peak bytes | Steady bytes | Allocations | p50 µs | p95 µs | p99 µs | Startup µs | First frame µs |");
            builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (var entry in report.Cases)
            {
                var frequency = capture.Environment.StopwatchFrequency;
                Add("default", pair => pair.Baseline);
                Add("generic", pair => pair.Generic);
                Add("candidate", pair => pair.Candidate);
                if (entry.Pairs[0].NoReserve != null) Add("same policy without reserve", pair => pair.NoReserve!);

                void Add(string label, Func<PairReport, Metrics> get)
                {
                    var metrics = entry.Pairs.Select(get).ToArray();
                    long Median(Func<Metrics, long> metric) => Metrics.Percentile(metrics.Select(metric), 0.5);
                    double Time(Func<Metrics, long> metric) => Acceptance.Microseconds(Median(metric), frequency);
                    builder.AppendLine(FormattableString.Invariant($"| {entry.ScenarioId} | {label} | {Median(value => value.PeakRetained)} | {Median(value => value.SteadyRetained)} | {Median(value => value.Allocations)} | {Time(value => value.P50Ticks):F2} | {Time(value => value.P95Ticks):F2} | {Time(value => value.P99Ticks):F2} | {Time(value => value.StartupTicks):F2} | {Time(value => value.FirstFrameTicks):F2} |"));
                }
            }
            foreach (var entry in report.Cases)
                foreach (var failure in entry.Failures) builder.AppendLine().AppendLine($"- `{entry.ScenarioId}`. {failure}");
        }
        return builder.ToString();
    }
}
