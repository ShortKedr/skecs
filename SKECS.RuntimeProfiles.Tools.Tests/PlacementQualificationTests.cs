using SKECS.RuntimeProfiles;

namespace SKECS.RuntimeProfiles.Tools.Tests;

public sealed class PlacementQualificationTests
{
    private const string SamePolicy = PlacementQualificationCheck.SamePolicy;
    private const string TunedArchetypes = PlacementQualificationCheck.TunedArchetypes;
    private const string Component = "Fixture.Effect";
    private static readonly string PlanHash = new('A', 64);
    private static readonly string CoverageHash = new('B', 64);
    private static readonly string ReportHash = new('C', 64);
    private static readonly PackedProfileEvidence Receipts = new(new string('D', 64), new string('E', 64));

    [Fact]
    public void PassingPlacementReproducesBothReceiptsAcrossTheWholeCorpus()
    {
        var input = Evidence();
        var result = PlacementQualificationCheck.Assess(input);

        Assert.True(result.Passed);
        Assert.Equal(TunedArchetypes, result.TrainingWinner?.ControlKind);
        Assert.Equal(new[] { Component }, result.TrainingWinner?.Placement);
        Assert.Equal(new[] { SamePolicy, TunedArchetypes }, result.Training.Select(trial => trial.Trial.ControlKind));
        Assert.Equal(2 * input.Plan.Holdout.Length, result.Holdout.Length);
        Assert.All(result.Training.Concat(result.Holdout), trial =>
        {
            Assert.True(trial.Passed);
            Assert.Equal(input.Plan.Repetitions, trial.Pairs.Length);
        });
    }

    [Fact]
    public void TruthfulTrainingFailureReportsBothControlsAndSelectsNothing()
    {
        var result = PlacementQualificationCheck.Assess(Evidence(failSamePolicyTraining: true));

        Assert.False(result.Passed);
        Assert.Null(result.TrainingWinner);
        Assert.Empty(result.Holdout);
        Assert.Contains("No placement passed both training controls.", result.Failures);
        Assert.Equal("No qualifying full-frame gain.", Assert.Single(result.Training.Single(trial =>
            trial.Trial.ControlKind == SamePolicy).Failures));
        Assert.True(result.Training.Single(trial => trial.Trial.ControlKind == TunedArchetypes).Passed);
    }

    [Fact]
    public void TruthfulHoldoutFailureKeepsTheTrainingWinnerAndBothOutcomesInTheReport()
    {
        var result = PlacementQualificationCheck.Assess(Evidence(failSamePolicyHoldout: true));

        Assert.False(result.Passed);
        Assert.Equal(TunedArchetypes, result.TrainingWinner?.ControlKind);
        Assert.All(result.Training, trial => Assert.True(trial.Passed));
        Assert.Equal("holdout-churn failed same-policy.", Assert.Single(result.Failures));
        var pair = result.Holdout.Where(trial => trial.Trial.Scenario.Name == "holdout-churn").ToArray();
        Assert.Contains("Full-frame p99 regression.", pair.Single(trial => trial.Trial.ControlKind == SamePolicy).Failures);
        Assert.True(pair.Single(trial => trial.Trial.ControlKind == TunedArchetypes).Passed);
    }

    [Theory]
    [InlineData("frame count", "sample counts")]
    [InlineData("hash", "state hashes")]
    [InlineData("repetitions", "missing paired repetitions")]
    [InlineData("stated verdict", "Reported trial verdict")]
    [InlineData("timings", "inconsistent or negative timings")]
    public void InconsistentRawTrialsAreRejected(string corruption, string reason)
    {
        var input = Evidence();
        var training = input.Qualification.Trials[0];
        var firstRun = training.CandidateRuns[0];
        var corrupted = corruption switch
        {
            "frame count" => WithFirstRun(training, firstRun with { Frame = firstRun.Frame[..^1] }),
            "hash" => WithFirstRun(training, firstRun with { Hashes = ["invalid", .. firstRun.Hashes.Skip(1)] }),
            "repetitions" => training with { CandidateRuns = training.CandidateRuns[..^1] },
            "stated verdict" => training with { PairedFrameRatio = 0.10 },
            "timings" => WithFirstRun(training, firstRun with { Dense = [firstRun.Frame[0] + 1, .. firstRun.Dense.Skip(1)] }),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        var altered = input with { Qualification = input.Qualification with { Trials = [corrupted, .. input.Qualification.Trials.Skip(1)] } };

        Assert.Contains(reason, Assert.Throws<InvalidDataException>(() => PlacementQualificationCheck.Assess(altered)).Message);
    }

    [Theory]
    [InlineData(0, "Both distinct archetype controls are required.")]
    [InlineData(2, "Holdout must cover only the training winner")]
    public void AControlCannotBeOmittedFromTrainingOrHoldout(int omitted, string reason)
    {
        var input = Evidence();
        var trials = input.Qualification.Trials.Where((_, index) => index != omitted).ToArray();
        var incomplete = input with { Qualification = input.Qualification with { Trials = trials } };

        Assert.Contains(reason, Assert.Throws<InvalidDataException>(() => PlacementQualificationCheck.Assess(incomplete)).Message);
    }

    [Fact]
    public void AReleaseClaimCannotOverrideAFailedControl()
    {
        var input = Evidence(failSamePolicyTraining: true);
        var failedCandidate = input.Qualification.Trials[0];
        var claimed = input with
        {
            Qualification = input.Qualification with
            {
                Selected = failedCandidate.Placement, SelectedPolicy = failedCandidate.Policy, PackedEvidence = Receipts
            }
        };

        Assert.Contains("claims a release", Assert.Throws<InvalidDataException>(() => PlacementQualificationCheck.Assess(claimed)).Message);
    }

    [Fact]
    public void AFailedSupplementalReplayRemainsAnHonestRefusal()
    {
        var input = Evidence();
        var replay = input.Qualification.HoldoutStates[0];
        var changed = (string[])replay.Candidate.Clone();
        changed[3] = new string('f', 64);
        var failed = input with
        {
            Qualification = input.Qualification with
            {
                Selected = null, SelectedPolicy = null, PackedEvidence = null,
                HoldoutStates = [replay with { Matched = false, Candidate = changed }, input.Qualification.HoldoutStates[1]]
            },
            SamePolicyReceipt = null,
            TunedArchetypesReceipt = null
        };

        var decision = PlacementQualificationCheck.Assess(failed);
        Assert.False(decision.Passed);
        Assert.Contains(decision.Failures, failure => failure.Contains("supplemental replay changed state", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(SamePolicy)]
    [InlineData(TunedArchetypes)]
    public void AReceiptMustReproduceTheRawDataForItsControl(string kind)
    {
        var input = Evidence();
        var control = (kind == SamePolicy ? input.SamePolicyReceipt : input.TunedArchetypesReceipt)!;
        var training = control.Receipt.Training;
        var changedRun = training.CandidateRuns[0] with { StartupManagedBytes = training.CandidateRuns[0].StartupManagedBytes + 1 };
        var forged = control with { Receipt = control.Receipt with { Training = WithFirstRun(training, changedRun) } };
        var altered = kind == SamePolicy ? input with { SamePolicyReceipt = forged } : input with { TunedArchetypesReceipt = forged };

        Assert.Contains($"The {kind} control receipt", Assert.Throws<InvalidDataException>(() => PlacementQualificationCheck.Assess(altered)).Message);
    }

    [Theory]
    [InlineData(SamePolicy)]
    [InlineData(TunedArchetypes)]
    public void APositiveVerdictRequiresBothRawReceipts(string kind)
    {
        var input = Evidence();
        var incomplete = kind == SamePolicy ? input with { SamePolicyReceipt = null } : input with { TunedArchetypesReceipt = null };
        Assert.Contains($"Missing {kind} control receipt", Assert.Throws<InvalidDataException>(() => PlacementQualificationCheck.Assess(incomplete)).Message);
    }

    [Fact]
    public void ReplacingTheThresholdsDoesNotCreateAQualifiedExperiment()
    {
        var input = Evidence();
        var altered = input with { Plan = input.Plan with { Gates = input.Plan.Gates with { MinimumGain = .01 } } };

        Assert.Contains("changed the reviewed gates", Assert.Throws<InvalidDataException>(() => PlacementQualificationCheck.Assess(altered)).Message);
    }

    private static PlacementInputs Evidence(bool failSamePolicyTraining = false, bool failSamePolicyHoldout = false)
    {
        var training = new PlacementScenario("training", 1024, 1973, false);
        var churn = new PlacementScenario("holdout-churn", 1536, 11939, false);
        var quiet = new PlacementScenario("holdout-quiet", 1536, 9137, true);
        var gates = new PlacementGates(.10, .15, .10, .15, .05);
        var plan = new PlacementPlan("adaptive-placement-fixture-v2", false, "Runtime", "OS", "X64", 8, 10_000_000,
            128, 3, 16, 128, gates, training, [churn, quiet], "Verify only the training winner on both controls.",
            new string('1', 64), new string('2', 64), new string('3', 64), CoverageHash, [SamePolicy, TunedArchetypes]);
        var initial = new PlacementPolicy(20_000, []);
        var tuned = new PlacementPolicy(16_000, []);
        var packed = new PlacementPolicy(12_000, [64]);
        PlacementTrial[] baselineTuning = [Trial(training, [], tuned, initial, "archetype-tuning", 100, 100, [])];
        var sameTraining = Trial(training, [Component], packed, packed, SamePolicy, failSamePolicyTraining ? 80 : 100, 80,
            failSamePolicyTraining ? ["No qualifying full-frame gain."] : []);
        var tunedTraining = Trial(training, [Component], packed, tuned, TunedArchetypes, 100, 80, []);
        PlacementTrial[] holdout = failSamePolicyTraining ? [] :
        [
            Trial(churn, [Component], packed, packed, SamePolicy, failSamePolicyHoldout ? 60 : 100, 80,
                failSamePolicyHoldout ? ["Full-frame median regression.", "Full-frame p99 regression."] : []),
            Trial(churn, [Component], packed, tuned, TunedArchetypes, 100, 80, []),
            Trial(quiet, [Component], packed, packed, SamePolicy, 100, 80, []),
            Trial(quiet, [Component], packed, tuned, TunedArchetypes, 100, 80, [])
        ];
        var passed = !failSamePolicyTraining && !failSamePolicyHoldout;
        var report = new PlacementQualification(plan.Runtime, plan.OperatingSystem, plan.Architecture, plan.ProcessorCount,
            plan.Frequency, plan.ChunkCapacity, plan.Repetitions, plan.Warmup, plan.Frames, gates, plan.Smoke,
            PlanHash, CoverageHash, passed ? Receipts : null, [new PlacementSignal(Component, 100, 1024, 64, .20, .40)],
            tuned, baselineTuning, passed ? [Component] : null, passed ? packed : null, [sameTraining, tunedTraining, .. holdout],
            [new PlacementHoldoutState(churn, [Component], true, Hashes(churn), Hashes(churn)),
                new PlacementHoldoutState(quiet, [Component], true, Hashes(quiet), Hashes(quiet))]);
        return new PlacementInputs(plan, PlanHash, report, ReportHash, [Component], [],
            passed ? Receipt(Receipts.SamePolicySha256, sameTraining) : null,
            passed ? Receipt(Receipts.TunedArchetypesSha256, tunedTraining) : null, null!);

        PlacementControlReceipt Receipt(string hash, PlacementTrial trial) => new(hash,
            new PlacementReceipt(trial.ControlKind, PlanHash, trial, holdout.Where(item => item.ControlKind == trial.ControlKind).ToArray()));
    }

    private static PlacementTrial Trial(PlacementScenario scenario, string[] placement, PlacementPolicy policy,
        PlacementPolicy control, string kind, long baselineTicks, long candidateTicks, string[] failures) =>
        new(scenario, placement, policy, control, kind, true, (double)candidateTicks / baselineTicks,
            (double)candidateTicks / baselineTicks, 1, 1, 3_000, failures,
            Enumerable.Range(0, 3).Select(_ => Run(scenario, baselineTicks, 5_000)).ToArray(),
            Enumerable.Range(0, 3).Select(_ => Run(scenario, candidateTicks, 3_000)).ToArray());

    private static PlacementRun Run(PlacementScenario scenario, long ticks, long peak) => new(20, 500,
        Enumerable.Repeat(ticks, 128).ToArray(), Enumerable.Repeat(10L, 128).ToArray(), Enumerable.Repeat(5L, 128).ToArray(),
        512, [0, 0, 0], peak, peak - 500, 2, Hashes(scenario));

    private static string[] Hashes(PlacementScenario scenario) => Enumerable.Range(0, 145)
        .Select(frame => (scenario.Seed + frame).ToString("X64", System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    private static PlacementTrial WithFirstRun(PlacementTrial trial, PlacementRun run) =>
        trial with { CandidateRuns = [run, .. trial.CandidateRuns.Skip(1)] };
}
