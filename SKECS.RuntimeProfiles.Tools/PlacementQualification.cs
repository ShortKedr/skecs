using System.Diagnostics.CodeAnalysis;

namespace SKECS.RuntimeProfiles.Tools;

internal sealed record PlacementPair(double FrameRatio, double TailRatio, double DenseRatio, double StartupRatio,
    long ManagedDelta, long BaselinePeak, long CandidatePeak, long BaselineNativeAllocations, long CandidateNativeAllocations);

internal sealed record PlacementTrialResult(PlacementTrial Trial, PlacementPair[] Pairs, string[] Failures)
{
    internal bool Passed => Failures.Length == 0;
}

internal sealed record PlacementDecision(PlacementTrialResult[] BaselineTuning, PlacementTrialResult[] Training,
    PlacementTrialResult[] Holdout, PlacementTrial? TrainingWinner, string[] Failures)
{
    internal bool Passed => TrainingWinner != null && Failures.Length == 0;
}

internal static class PlacementQualificationCheck
{
    internal const string SamePolicy = PlacementInputs.SamePolicy;
    internal const string TunedArchetypes = PlacementInputs.TunedArchetypes;
    private const string ArchetypeTuning = PlacementInputs.ArchetypeTuning;
    internal const long ManagedBytesPerFrame = 64;

    internal static PlacementDecision Assess(PlacementInputs input)
    {
        var plan = input.Plan;
        var report = input.Qualification;
        ValidateGates(plan);
        if (!plan.CoverageSha256!.Equals(report.CoverageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The placement report and plan disagree on coverage evidence.");

        var observedHashes = new Dictionary<PlacementScenario, string[]>();
        var tuning = report.BaselineTuning.Select(AssessTrial).ToArray();
        Require(tuning.Length > 0 && tuning.All(item => item.Trial.ControlKind == ArchetypeTuning &&
            item.Trial.Scenario == plan.Training && item.Trial.Placement.Length == 0), "Archetype tuning evidence is missing or misplaced.");
        var initialPolicy = tuning[0].Trial.ControlPolicy;
        Require(tuning.All(item => Same(item.Trial.ControlPolicy, initialPolicy)) &&
            tuning.Select(item => PolicyKey(item.Trial.Policy)).Distinct().Count() == tuning.Length &&
            tuning.All(item => !Same(item.Trial.Policy, initialPolicy)), "Archetype tuning did not compare unique policies against a common control.");
        var bestControl = tuning.Where(item => item.Passed).OrderBy(item => item.Trial.CandidatePeakBytes)
            .ThenBy(item => item.Trial.PairedFrameRatio).FirstOrDefault()?.Trial.Policy ?? initialPolicy;
        Require(Same(report.Baseline, bestControl), "The stated tuned archetype policy does not follow the training measurements.");

        var measured = report.Trials.Select(AssessTrial).ToArray();
        var training = measured.Where(item => item.Trial.Scenario == plan.Training).ToArray();
        var holdout = measured.Where(item => item.Trial.Scenario != plan.Training).ToArray();
        var eligible = new List<PlacementTrial>();
        foreach (var candidate in training.GroupBy(item => CandidateKey(item.Trial)))
        {
            var controls = CheckedControls(candidate.ToArray(), bestControl);
            if (controls.All(item => item.Passed)) eligible.Add(controls.Single(item => item.Trial.ControlKind == TunedArchetypes).Trial);
        }

        var winner = eligible.Where(candidate => !eligible.Any(subset => subset.Placement.Length < candidate.Placement.Length &&
                subset.Placement.All(candidate.Placement.Contains) && subset.CandidatePeakBytes <= candidate.CandidatePeakBytes &&
                subset.PairedFrameRatio <= candidate.PairedFrameRatio))
            .OrderBy(candidate => candidate.CandidatePeakBytes).ThenBy(candidate => candidate.PairedFrameRatio)
            .ThenBy(candidate => candidate.Placement.Length).FirstOrDefault();

        var failures = new List<string>();
        if (plan.Smoke) failures.Add("Smoke evidence is ineligible for profile emission.");
        if (winner == null)
        {
            Require(holdout.Length == 0, "Holdout trials require a candidate selected from training.");
            failures.Add("No placement passed both training controls.");
        }
        else
        {
            Require(holdout.Length == 2 * plan.Holdout.Length && holdout.All(item => CandidateKey(item.Trial) == CandidateKey(winner)),
                "Holdout must cover only the training winner with both controls for every case.");
            foreach (var scenario in plan.Holdout)
                foreach (var failed in CheckedControls(holdout.Where(item => item.Trial.Scenario == scenario).ToArray(), bestControl)
                    .Where(item => !item.Passed))
                    failures.Add($"{scenario.Name} failed {failed.Trial.ControlKind}.");
        }
        var placements = training.Select(item => PlacementKey(item.Trial.Placement)).ToHashSet(StringComparer.Ordinal);
        var replayed = new HashSet<(PlacementScenario Scenario, string Placement)>();
        foreach (var replay in report.HoldoutStates)
        {
            var placement = PlacementKey(replay.Types);
            Require(plan.Holdout.Contains(replay.Scenario) && placements.Contains(placement) &&
                replayed.Add((replay.Scenario, placement)), "Unexpected or repeated supplemental holdout replay.");
            ValidateHashes(plan, replay.Control);
            ValidateHashes(plan, replay.Candidate);
            if (observedHashes.TryGetValue(replay.Scenario, out var reference))
                Require(reference.SequenceEqual(replay.Control), "Supplemental holdout archetype state does not match its scenario.");
            else observedHashes.Add(replay.Scenario, replay.Control);
            var matched = replay.Control.SequenceEqual(replay.Candidate);
            Require(matched == replay.Matched, "Reported supplemental holdout verdict does not match its hashes.");
            if (!matched) failures.Add($"{replay.Scenario.Name} supplemental replay changed state or events for {string.Join(", ", replay.Types)}.");
        }
        Require((long)replayed.Count == (long)placements.Count * plan.Holdout.Length,
            "Supplemental holdout replay evidence is incomplete.");
        if (tuning.Concat(measured).Any(item => !item.Trial.StateMatches))
            failures.Add("A measured trial changed state or events.");

        if (failures.Count == 0)
        {
            Require(report.Selected != null && report.SelectedPolicy != null && report.PackedEvidence != null &&
                report.Selected.SequenceEqual(winner!.Placement) && Same(report.SelectedPolicy, winner.Policy),
                "The fixture winner or placement receipts do not match the recomputed decision.");
            ValidateReceipt(input.SamePolicyReceipt, SamePolicy);
            ValidateReceipt(input.TunedArchetypesReceipt, TunedArchetypes);
        }
        else
        {
            Require(report.Selected == null && report.SelectedPolicy == null && report.PackedEvidence == null &&
                input.SamePolicyReceipt == null && input.TunedArchetypesReceipt == null,
                "The fixture claims a release for a placement that failed qualification.");
        }
        return new PlacementDecision(tuning, training, holdout, winner, failures.ToArray());

        PlacementTrialResult AssessTrial(PlacementTrial trial)
        {
            Require(trial.Scenario == plan.Training || plan.Holdout.Contains(trial.Scenario), "Trial uses inputs outside the frozen corpus.");
            Require(trial.Placement != null && trial.Placement.Distinct(StringComparer.Ordinal).Count() == trial.Placement.Length &&
                trial.Placement.All(type => input.Coverage.Contains(type, StringComparer.Ordinal)), "A trial includes missing, duplicate or unapproved packed types.");
            Require(trial.ControlKind == ArchetypeTuning || trial.Placement.Length != 0, "A placement trial must select a packed component.");
            ValidatePolicy(trial.Policy, plan.ChunkCapacity);
            ValidatePolicy(trial.ControlPolicy, plan.ChunkCapacity);
            Require(trial.BaselineRuns?.Length == plan.Repetitions && trial.CandidateRuns?.Length == plan.Repetitions,
                "The trial is missing paired repetitions.");

            var pairs = new PlacementPair[plan.Repetitions];
            var problems = new HashSet<string>(StringComparer.Ordinal);
            var matches = true;
            for (var repetition = 0; repetition < pairs.Length; repetition++)
            {
                var baseline = trial.BaselineRuns[repetition];
                var candidate = trial.CandidateRuns[repetition];
                ValidateRun(plan, baseline);
                ValidateRun(plan, candidate);
                // Identical scenario inputs must replay consistently across the whole experiment, even on rejected policies.
                if (!observedHashes.TryGetValue(trial.Scenario, out var reference))
                    observedHashes.Add(trial.Scenario, reference = baseline.Hashes);
                Require(reference.SequenceEqual(baseline.Hashes), "Archetype state or events changed across repetitions for identical inputs.");
                var different = -1;
                for (var index = 0; index < reference.Length; index++)
                    if (reference[index] != candidate.Hashes[index]) { different = index; break; }
                if (different >= 0)
                {
                    matches = false;
                    problems.Add($"Full-state or event mismatch at hash index {different}.");
                }
                pairs[repetition] = new PlacementPair(
                    Ratio(Metrics.Percentile(candidate.Frame, .50), Metrics.Percentile(baseline.Frame, .50)),
                    Ratio(Metrics.Percentile(candidate.Frame, .99), Metrics.Percentile(baseline.Frame, .99)),
                    Ratio(Metrics.Percentile(candidate.Dense, .95), Metrics.Percentile(baseline.Dense, .95)),
                    Ratio(candidate.StartupTicks, baseline.StartupTicks), candidate.ManagedBytes - baseline.ManagedBytes,
                    baseline.PeakBytes, candidate.PeakBytes, baseline.NativeAllocations, candidate.NativeAllocations);
            }

            var frame = Median(pairs.Select(pair => pair.FrameRatio));
            var tail = Median(pairs.Select(pair => pair.TailRatio));
            var dense = Median(pairs.Select(pair => pair.DenseRatio));
            var startup = Median(pairs.Select(pair => pair.StartupRatio));
            if (!plan.Smoke)
            {
                if (frame > 1 + plan.Gates.FrameRegression) problems.Add("Full-frame median regression.");
                if (tail > 1 + plan.Gates.TailRegression) problems.Add("Full-frame p99 regression.");
                if (dense > 1 + plan.Gates.DenseRegression) problems.Add("Dense traversal regression.");
                if (startup > 1 + plan.Gates.StartupRegression) problems.Add("Startup regression.");
                if (Median(pairs.Select(pair => pair.ManagedDelta)) > plan.Frames * ManagedBytesPerFrame)
                    problems.Add("Managed frame allocation regression.");
                if (trial.Scenario == plan.Training && trial.Placement.Length != 0 && frame > 1 - plan.Gates.MinimumGain)
                    problems.Add("No qualifying full-frame gain.");
            }
            Require(trial.StateMatches == matches && trial.PairedFrameRatio == frame && trial.PairedTailRatio == tail &&
                trial.PairedDenseRatio == dense && trial.PairedStartupRatio == startup &&
                trial.CandidatePeakBytes == pairs.Max(pair => pair.CandidatePeak) && trial.Rejections != null &&
                problems.SetEquals(trial.Rejections) && trial.Rejections.Length == problems.Count,
                "Reported trial verdict or metric does not match the recorded repetitions.");
            return new PlacementTrialResult(trial, pairs, problems.Order(StringComparer.Ordinal).ToArray());
        }

        void ValidateReceipt(PlacementControlReceipt? control, string kind)
        {
            if (control == null) throw new InvalidDataException($"Missing {kind} control receipt.");
            var receipt = control.Receipt;
            var selected = training.Single(item => item.Trial.ControlKind == kind && CandidateKey(item.Trial) == CandidateKey(winner!)).Trial;
            var expectedHoldout = plan.Holdout.Select(scenario => holdout.Single(item => item.Trial.Scenario == scenario &&
                item.Trial.ControlKind == kind).Trial).ToArray();
            Require(receipt.ControlKind == kind && receipt.PlanSha256 == input.PlanSha256 &&
                Artifacts.Serialize(receipt.Training).AsSpan().SequenceEqual(Artifacts.Serialize(selected)) &&
                Artifacts.Serialize(receipt.Holdout).AsSpan().SequenceEqual(Artifacts.Serialize(expectedHoldout)),
                $"The {kind} control receipt does not reproduce the verified training and holdout data.");
        }
    }

    private static PlacementTrialResult[] CheckedControls(PlacementTrialResult[] group, PlacementPolicy tuned)
    {
        Require(group.Length == 2 && group.Count(item => item.Trial.ControlKind == SamePolicy) == 1 &&
            group.Count(item => item.Trial.ControlKind == TunedArchetypes) == 1, "Both distinct archetype controls are required.");
        foreach (var item in group)
        {
            var control = item.Trial.ControlKind == SamePolicy ? item.Trial.Policy : tuned;
            Require(Same(control, item.Trial.ControlPolicy), $"Incorrect archetype policy for {item.Trial.ControlKind}.");
        }
        return group;
    }

    private static void ValidateGates(PlacementPlan plan)
    {
        // These gates are the reviewed AdaptivePlacementFixture v2 contract. A different experiment needs its own version.
        Require(plan.Gates is { FrameRegression: .10, TailRegression: .15, DenseRegression: .10,
            StartupRegression: .15, MinimumGain: .05 } && plan.ChunkCapacity == 128 && plan.Warmup == 16 &&
            (plan.Smoke ? plan.Frames == 8 && plan.Repetitions == 1 : plan.Frames == 128 && plan.Repetitions == 3),
            "The placement plan changed the reviewed gates, warmup, frame count, repetitions or chunk capacity.");
        Require(plan.Holdout is { Length: > 0 } && plan.Holdout.Prepend(plan.Training)
            .Select(scenario => scenario.Name).Distinct(StringComparer.Ordinal).Count() == plan.Holdout.Length + 1,
            "The plan requires distinct training and holdout scenarios.");
    }

    private static void ValidateRun(PlacementPlan plan, PlacementRun run)
    {
        Require(run != null && run.StartupTicks > 0 && run.StartupManagedBytes >= 0 && run.ManagedBytes >= 0 &&
            run.PeakBytes >= run.FinalBytes && run.FinalBytes > 0 && run.NativeAllocations > 0 &&
            run.GcCollections is { Length: 3 } && run.GcCollections.All(count => count >= 0) &&
            run.Frame?.Length == plan.Frames && run.Dense?.Length == plan.Frames && run.Adaptive?.Length == plan.Frames,
            "Invalid placement run sample counts or memory metrics.");
        ValidateHashes(plan, run.Hashes);
        for (var index = 0; index < plan.Frames; index++)
            Require(run.Frame[index] > 0 && run.Dense[index] >= 0 && run.Adaptive[index] >= 0 &&
                run.Dense[index] <= run.Frame[index] && run.Adaptive[index] <= run.Frame[index] - run.Dense[index],
                "A placement frame has inconsistent or negative timings.");
    }

    private static void ValidateHashes(PlacementPlan plan, string[] hashes) =>
        Require(hashes?.Length == plan.Warmup + plan.Frames + 1 && hashes.All(hash => hash?.Length == 64 && hash.All(Uri.IsHexDigit)),
            "Invalid placement replay state hashes.");

    private static void ValidatePolicy(PlacementPolicy policy, int capacity)
    {
        Require(policy != null && policy.Target >= 0 && policy.StartupSizes != null &&
            policy.StartupSizes.All(bytes => bytes > 0) && policy.StartupSizes.SequenceEqual(policy.StartupSizes.Distinct().Order()) &&
            policy.StartupSizes.Sum(bytes => bytes + 8L * capacity) <= policy.Target,
            "Invalid placement policy or startup reserve exceeds its target.");
    }

    private static string CandidateKey(PlacementTrial trial) => PlacementKey(trial.Placement) + "|" + PolicyKey(trial.Policy);
    private static string PlacementKey(IEnumerable<string> types) => string.Join('\n', types.Order(StringComparer.Ordinal));
    private static string PolicyKey(PlacementPolicy policy) => FormattableString.Invariant($"{policy.Target}:{string.Join(',', policy.StartupSizes)}");
    private static bool Same(PlacementPolicy a, PlacementPolicy b) => a.Target == b.Target && a.StartupSizes.SequenceEqual(b.StartupSizes);
    private static double Ratio(long numerator, long denominator) => (double)numerator / Math.Max(1, denominator);
    private static T Median<T>(IEnumerable<T> values) => values.Order().ElementAt(values.Count() / 2);
    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
