namespace SKECS.RuntimeProfiles.Tools.Tests;

public sealed class VerificationTests
{
    [Fact]
    public void PairingUsesTheNoiseAndImprovementWithinEachPair()
    {
        var runs = new[] { Pair(100, 90, 85), Pair(1000, 900, 850), Pair(200, 180, 200) };
        var gain = Change.Paired(runs, metrics => metrics.MeanTicks, pair => pair.Baseline);
        Assert.Equal(-15, gain.MedianPercent);
        Assert.Equal(0, gain.MedianAbsoluteDeviationPercent);
        Assert.Equal(15, gain.ConservativeImprovement);
        Assert.InRange(Gains.AgainstBoth(runs).MeanTicks, 5.55, 5.56);
    }

    [Fact]
    public void EveryFoldNeedsItsOwnPracticalImprovement()
    {
        var scenario = new Scenario("holdout", "sizes", 2, 10, 10, 10, true, 1);
        var corpus = new Corpus(1, "workload", "target", 16, 256, 3, 1000,
            new Gates(5, 5, 5, 5, 5, 5, 2, 100, null), [scenario]);
        var candidate = new Candidate("candidate", Policy.Generic(1000), default);
        var pairs = Enumerable.Repeat(Pair(100, 80, 80), 3).ToArray();
        var reports = new[] { new CaseReport(scenario.Id, pairs, Gains.AgainstBoth(pairs), []) };
        Assert.Contains(Acceptance.Summarize(corpus, candidate, reports, holdout: true).Failures,
            failure => failure.Contains("No practical weighted improvement", StringComparison.Ordinal));
    }

    private static PairReport Pair(long baseline, long generic, long candidate) =>
        new("off", "generic", "candidate", null, Measurement(baseline), Measurement(generic), Measurement(candidate), null);

    private static Metrics Measurement(long ticks) => new(ticks, ticks, ticks, ticks, 0, 1, 0, 1, 0, 100, 100, 0, 0, 0);
}
