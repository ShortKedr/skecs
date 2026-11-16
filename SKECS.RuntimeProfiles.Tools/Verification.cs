namespace SKECS.RuntimeProfiles.Tools;

internal sealed record Metrics(double MeanTicks, long P50Ticks, long P95Ticks, long P99Ticks, long TrimP99Ticks,
    long StartupTicks, long StartupManagedBytes, long FirstFrameTicks, long ManagedBytes, long PeakRetained,
    long SteadyRetained, long Allocations, long Releases, int CyclingFrames)
{
    internal static Metrics From(RunResult run)
    {
        var cycling = 0;
        var previous = run.AfterWarmup;
        var previousReleased = false;
        foreach (var frame in run.Frames)
        {
            var released = frame.Memory.Releases > previous.Releases;
            if (frame.Memory.Allocations > previous.Allocations && (released || previousReleased)) cycling++;
            previous = frame.Memory;
            previousReleased = released;
        }
        return new(run.Frames.Average(frame => (double)frame.FrameTicks),
            Percentile(run.Frames.Select(frame => frame.FrameTicks), 0.50),
            Percentile(run.Frames.Select(frame => frame.FrameTicks), 0.95),
            Percentile(run.Frames.Select(frame => frame.FrameTicks), 0.99),
            Percentile(run.Frames.Select(frame => frame.TrimTicks), 0.99), run.StartupTicks, run.StartupManagedBytes,
            run.FirstFrameTicks, run.Frames.Sum(frame => frame.ManagedBytes),
            run.Diagnostics.PeakRetained, Percentile(run.Frames.Select(frame => frame.Memory.RetainedBytes), 0.50),
            run.Frames[^1].Memory.Allocations - run.AfterWarmup.Allocations,
            run.Frames[^1].Memory.Releases - run.AfterWarmup.Releases, cycling);
    }

    internal static long Percentile(IEnumerable<long> values, double rank)
    {
        var sorted = values.Order().ToArray();
        return sorted[(int)Math.Ceiling(sorted.Length * rank) - 1];
    }
}

internal sealed record PairReport(string BaselineSha256, string GenericSha256, string CandidateSha256,
    string? NoReserveSha256, Metrics Baseline, Metrics Generic, Metrics Candidate, Metrics? NoReserve);

internal readonly record struct Change(double MedianPercent, double MedianAbsoluteDeviationPercent)
{
    internal double ConservativeImprovement => -MedianPercent - MedianAbsoluteDeviationPercent;

    internal static Change Paired(IEnumerable<PairReport> pairs, Func<Metrics, double> metric, Func<PairReport, Metrics> reference,
        double absoluteAllowance = 0)
    {
        var changes = pairs.Select(pair =>
        {
            var baseline = metric(reference(pair));
            return 100 * (metric(pair.Candidate) - baseline - absoluteAllowance) / Math.Max(baseline, 1);
        }).ToArray();
        var median = Median(changes);
        return new(median, Median(changes.Select(value => Math.Abs(value - median))));
    }

    private static double Median(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        return 0.5 * (values[(values.Length - 1) / 2] + values[values.Length / 2]);
    }
}

internal sealed record Gains(double Allocations, double PeakRetained, double SteadyRetained, double MeanTicks, double P99Ticks)
{
    internal double Best => Math.Max(Math.Max(Allocations, PeakRetained), Math.Max(SteadyRetained, Math.Max(MeanTicks, P99Ticks)));

    internal static Gains Against(PairReport[] pairs, Func<PairReport, Metrics> reference)
    {
        double Gain(Func<Metrics, double> metric) => Math.Max(-100, Change.Paired(pairs, metric, reference).ConservativeImprovement);
        return new(Gain(metrics => metrics.Allocations), Gain(metrics => metrics.PeakRetained),
            Gain(metrics => metrics.SteadyRetained), Gain(metrics => metrics.MeanTicks), Gain(metrics => metrics.P99Ticks));
    }

    internal static Gains AgainstBoth(PairReport[] pairs)
    {
        var baseline = Against(pairs, pair => pair.Baseline);
        var generic = Against(pairs, pair => pair.Generic);
        return new(Math.Min(baseline.Allocations, generic.Allocations), Math.Min(baseline.PeakRetained, generic.PeakRetained),
            Math.Min(baseline.SteadyRetained, generic.SteadyRetained), Math.Min(baseline.MeanTicks, generic.MeanTicks),
            Math.Min(baseline.P99Ticks, generic.P99Ticks));
    }
}

internal sealed record CaseReport(string ScenarioId, PairReport[] Pairs, Gains Gains, string[] Failures);
internal sealed record CandidateReport(string CandidateId, Gains Gains, CaseReport[] Cases, string[] Failures);

internal static class Acceptance
{
    internal static CaseReport Assess(Corpus corpus, ScenarioTrace entry, Policy policy, long stopwatchFrequency,
        PairReport[] pairs, IEnumerable<string> stateFailures)
    {
        var failures = stateFailures.ToList();
        var gates = corpus.Gates;
        var scenario = entry.Scenario;
        if (policy.TargetRetainedBytes < entry.Trace.LowerBound.Total)
            failures.Add($"Target is below {entry.Trace.LowerBound.Total} bytes required at frame {entry.Trace.LowerBound.Frame}.");

        Regression("mean frame", metrics => metrics.MeanTicks,
            Corpus.IsDense(scenario) ? Math.Min(gates.DenseRegressionPercent, gates.FrameRegressionPercent) : gates.FrameRegressionPercent,
            pair => pair.Baseline);
        Regression("frame p99", metrics => metrics.P99Ticks, gates.P99RegressionPercent, pair => pair.Baseline);
        Regression("startup relative to generic", metrics => metrics.StartupTicks, gates.StartupRegressionPercent, pair => pair.Generic);
        Regression("managed bytes", metrics => metrics.ManagedBytes, gates.ManagedRegressionPercent, pair => pair.Baseline,
            (double)gates.ManagedRegressionBytesPerFrame * scenario.Frames);
        if (pairs.Any(pair => pair.Candidate.PeakRetained > gates.MaxRetainedBytes))
            failures.Add($"Observed retention exceeded {gates.MaxRetainedBytes} bytes.");
        if (pairs.Count(pair => Microseconds(pair.Candidate.TrimP99Ticks, stopwatchFrequency) > gates.MaxTrimP99Microseconds) > pairs.Length / 2)
            failures.Add($"Maintenance p99 exceeded {gates.MaxTrimP99Microseconds} µs in most repetitions.");
        if (pairs.Count(pair => pair.Candidate.CyclingFrames == scenario.Frames) > pairs.Length / 2)
            failures.Add("Release/reallocation cycled through the entire measured interval.");
        if (Corpus.IsDense(scenario) && pairs.Any(pair => pair.Candidate.Allocations > pair.Baseline.Allocations))
            failures.Add("Physical allocations increased during the measured dense control.");
        if (pairs[0].NoReserve != null)
            Regression("startup with reserve", metrics => metrics.StartupTicks, gates.StartupRegressionPercent, pair => pair.NoReserve!);
        return new(scenario.Id, pairs, Gains.AgainstBoth(pairs), failures.ToArray());

        void Regression(string label, Func<Metrics, double> metric, double ceiling, Func<PairReport, Metrics> reference,
            double absoluteAllowance = 0)
        {
            var change = Change.Paired(pairs, metric, reference, absoluteAllowance);
            if (change.MedianPercent - change.MedianAbsoluteDeviationPercent > ceiling)
                failures.Add(FormattableString.Invariant($"{label} regressed {change.MedianPercent:F2}% (paired MAD {change.MedianAbsoluteDeviationPercent:F2}%, ceiling {ceiling:F2}%)."));
        }
    }

    internal static CandidateReport Summarize(Corpus corpus, Candidate candidate, CaseReport[] reports, bool holdout)
    {
        var failures = reports.Where(report => report.Failures.Length > 0).Select(report => $"Failed {report.ScenarioId}.").ToList();
        var cases = reports.Select(report => (Report: report, Scenario: corpus.Scenarios.Single(scenario => scenario.Id == report.ScenarioId)))
            .Where(entry => !Corpus.IsDense(entry.Scenario)).ToArray();
        if (cases.Length == 0) failures.Add("The fold contains no churn workload.");
        var weight = cases.Sum(entry => entry.Scenario.Weight);
        double Weighted(Func<Gains, double> select) => weight == 0 ? 0 :
            cases.Sum(entry => select(entry.Report.Gains) * entry.Scenario.Weight) / weight;
        var gains = new Gains(Weighted(gain => gain.Allocations), Weighted(gain => gain.PeakRetained),
            Weighted(gain => gain.SteadyRetained), Weighted(gain => gain.MeanTicks), Weighted(gain => gain.P99Ticks));
        if (gains.Best < corpus.Gates.MinimumImprovementPercent)
            failures.Add($"No practical weighted improvement over both default and generic in {(holdout ? "holdout" : "training")}.");
        if (candidate.Policy.StartupReserveBytes > 0 && !cases.Any(entry =>
            Change.Paired(entry.Report.Pairs, metrics => metrics.FirstFrameTicks, pair => pair.NoReserve!).ConservativeImprovement >=
            corpus.Gates.MinimumImprovementPercent))
            failures.Add("Startup reserve did not improve the first-frame latency over the same policy without reserve.");
        return new(candidate.Id, gains, reports, failures.ToArray());
    }

    internal static double Microseconds(long ticks, long frequency) => ticks * 1_000_000.0 / frequency;
}
