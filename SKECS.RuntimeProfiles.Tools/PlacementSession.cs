using System.Text;

namespace SKECS.RuntimeProfiles.Tools;

internal static class PlacementSession
{
    private const string VerifierVersion = "adaptive-placement-v2/1";

    internal static int Verify(string source, string trustPath, string output)
    {
        var input = PlacementInputs.Read(source, trustPath);
        var decision = Assess(input);
        WriteOutput(output, destination =>
        {
            Artifacts.Write(Path.Combine(destination, "placement-verification.json"), Report(input, decision));
            File.WriteAllText(Path.Combine(destination, "placement-report.md"), Markdown(input, decision));
        });
        Console.WriteLine(decision.Passed ? "Placement passed both controls." : string.Join(" ", decision.Failures));
        return decision.Passed ? 0 : 1;
    }

    internal static void Emit(string source, string trustPath, string output)
    {
        var input = PlacementInputs.Read(source, trustPath);
        var decision = Assess(input);
        if (!decision.Passed)
            throw new InvalidDataException("Placement did not pass both controls on training and holdout. " + string.Join(" ", decision.Failures));

        var winner = decision.TrainingWinner!;
        var trust = input.Trust;
        var capacity = input.Plan.ChunkCapacity;
        var policy = winner.Policy;
        var settings = new ChurnProfileSettings(policy.Target, policy.StartupSizes.Sum(bytes => bytes + 8L * capacity),
            policy.StartupSizes.Select(bytes => new ChurnStartupBlock(bytes, 1)).ToArray(), 0, 0, 4);
        var components = winner.Placement.Select(name => trust.Components.Single(component => component.Type == name)).ToArray();
        var identity = ProfileIdentity.Current(trust.FixtureSha256, trust.WorkloadId, trust.SchemaFingerprint, trust.TargetId, capacity);
        var report = Report(input, decision);
        var evidence = new ProfileEvidence(VerifierVersion, input.PlanSha256, Artifacts.Hash(Artifacts.Serialize(report)),
            1, input.Plan.Holdout.Length, input.Plan.Repetitions);
        var placementEvidence = new PackedProfileEvidence(input.SamePolicyReceipt!.Sha256, input.TunedArchetypesReceipt!.Sha256);
        WriteOutput(output, destination =>
        {
            Artifacts.Write(Path.Combine(destination, "churn-profile.json"), new
            {
                Format = ChurnProfile.PackedFormatId,
                SchemaVersion = ChurnProfile.PackedVersion,
                Identity = identity,
                Settings = settings,
                Evidence = evidence,
                Placement = components,
                PlacementEvidence = placementEvidence
            });
            Artifacts.Write(Path.Combine(destination, "placement-verification.json"), report);
            File.WriteAllText(Path.Combine(destination, "placement-report.md"), Markdown(input, decision));
        });
        Console.WriteLine("Emitted a qualified placement profile.");
    }

    private static PlacementDecision Assess(PlacementInputs input)
    {
        var decision = PlacementQualificationCheck.Assess(input);
        if (!input.Traces.Any(trace => trace.Trace.Header.SchemaVersion == 2))
            throw new InvalidDataException("The experiment must include an archetype trace.");
        foreach (var result in decision.Training)
        {
            var types = result.Trial.Placement.Order(StringComparer.Ordinal);
            if (!input.Traces.Any(trace => trace.Trace.Header.SchemaVersion == 3 && types.SequenceEqual(
                    trace.Trace.PackedLayouts.Select(layout => layout.Components.Single().Type).Order(StringComparer.Ordinal))))
                throw new InvalidDataException("No matching packed trace for " + string.Join(", ", types) + ".");
        }
        return decision;
    }

    private static object Report(PlacementInputs input, PlacementDecision decision) => new
    {
        VerifierVersion,
        Qualified = decision.Passed,
        decision.Failures,
        input.PlanSha256,
        input.QualificationSha256,
        input.Trust.CoverageSha256,
        input.Trust.FixtureSha256,
        input.Trust.CoreSha256,
        input.Trust.ProfilerSha256,
        input.Plan.Runtime,
        input.Plan.OperatingSystem,
        input.Plan.Architecture,
        input.Plan.Frequency,
        input.Plan.ChunkCapacity,
        input.Plan.Repetitions,
        input.Plan.Warmup,
        input.Plan.Frames,
        input.Plan.Gates,
        ManagedGrowthBytesPerFrame = PlacementQualificationCheck.ManagedBytesPerFrame,
        SamePolicySha256 = input.SamePolicyReceipt?.Sha256,
        TunedArchetypesSha256 = input.TunedArchetypesReceipt?.Sha256,
        TunedPolicy = input.Qualification.Baseline,
        TrainingPlacement = decision.TrainingWinner?.Placement,
        TrainingPolicy = decision.TrainingWinner?.Policy,
        Traces = input.Traces.Select(item => new { item.File, item.Sha256, item.Trace.Header, item.Trace.PackedLayouts,
            item.Trace.LowerBound, item.Trace.PeakRetained }),
        BaselineTuning = decision.BaselineTuning.Select(Details),
        Training = decision.Training.Select(Details),
        Holdout = decision.Holdout.Select(Details)
    };

    private static object Details(PlacementTrialResult result) => new
    {
        result.Trial.Scenario,
        result.Trial.ControlKind,
        result.Trial.Placement,
        result.Trial.Policy,
        result.Trial.ControlPolicy,
        result.Trial.PairedFrameRatio,
        result.Trial.PairedTailRatio,
        result.Trial.PairedDenseRatio,
        result.Trial.PairedStartupRatio,
        result.Trial.StateMatches,
        result.Pairs,
        result.Failures
    };

    private static string Markdown(PlacementInputs input, PlacementDecision decision)
    {
        var builder = new StringBuilder().AppendLine("# Packed placement qualification").AppendLine();
        builder.AppendLine(decision.Passed ? "Qualified on both archetype controls." : "No profile qualified.");
        if (decision.TrainingWinner is { } winner)
            builder.AppendLine().AppendLine($"Training selection `{string.Join(", ", winner.Placement)}` at {winner.Policy.Target} bytes.");
        foreach (var failure in decision.Failures) builder.AppendLine().AppendLine($"- {failure}");
        builder.AppendLine().AppendLine($"Plan SHA-256 `{input.PlanSha256}`. Raw qualification SHA-256 `{input.QualificationSha256}`.");
        builder.AppendLine("Ratios are packed candidate / archetype control. Memory figures are fixture frame-sampled peaks.");
        builder.AppendLine().AppendLine("| Trial set | Scenario | Control | Packed types | Target bytes | p50 ratio | p99 ratio | Dense p95 ratio | Startup ratio | Peak bytes (control / candidate) | Result |");
        builder.AppendLine("| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        Add("Archetype tuning", decision.BaselineTuning);
        Add("Training", decision.Training);
        Add("Holdout", decision.Holdout);
        builder.AppendLine().AppendLine("| Training trace | World format | Mandatory peak bytes | Retained peak bytes |");
        builder.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var trace in input.Traces)
            builder.AppendLine($"| {trace.File} | {trace.Trace.Header.SchemaVersion} | {trace.Trace.LowerBound.Total} | {trace.Trace.PeakRetained} |");
        return builder.ToString();

        void Add(string phase, IEnumerable<PlacementTrialResult> trials)
        {
            foreach (var result in trials)
            {
                var trial = result.Trial;
                var types = trial.Placement.Length == 0 ? "—" : string.Join(", ", trial.Placement);
                var outcome = result.Passed ? "Passed" : string.Join(" ", result.Failures);
                builder.AppendLine(FormattableString.Invariant($"| {phase} | {trial.Scenario.Name} | {trial.ControlKind} | {types} | {trial.Policy.Target} | {trial.PairedFrameRatio:F3} | {trial.PairedTailRatio:F3} | {trial.PairedDenseRatio:F3} | {trial.PairedStartupRatio:F3} | {result.Pairs.Max(pair => pair.BaselinePeak)} / {result.Pairs.Max(pair => pair.CandidatePeak)} | {outcome} |"));
            }
        }
    }

    private static void WriteOutput(string output, Action<string> write)
    {
        if (Path.Exists(output)) throw new InvalidDataException("Placement output requires a new directory.");
        var staging = output + "." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            write(staging);
            Directory.Move(staging, output);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
