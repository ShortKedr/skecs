using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SKECS.Profiler;

namespace SKECS.RuntimeProfiles.Tools;

internal sealed record PlacementTrust(string Format, string PlanSha256, string QualificationSha256, string CoverageSha256,
    string FixtureSha256, string CoreSha256, string ProfilerSha256, PlacementTrustedTrace[] Traces,
    PackedProfileComponent[] Components, string WorkloadId, string TargetId, string SchemaFingerprint);

internal sealed record PlacementTrustedTrace(string File, string Sha256);
internal sealed record PlacementTrace(string File, string Sha256, ProfilerTrace Trace);
internal sealed record PlacementControlReceipt(string Sha256, PlacementReceipt Receipt);
internal sealed record PlacementReceipt(string ControlKind, string PlanSha256, PlacementTrial Training,
    PlacementTrial[] Holdout);

internal sealed record PlacementScenario(string Name, int Entities, int Seed, bool Quiet);
internal sealed record PlacementPolicy(long Target, int[] StartupSizes);
internal sealed record PlacementGates(
    [property: JsonPropertyName("frameRegression")] double FrameRegression,
    [property: JsonPropertyName("tailRegression")] double TailRegression,
    [property: JsonPropertyName("denseRegression")] double DenseRegression,
    [property: JsonPropertyName("startupRegression")] double StartupRegression,
    [property: JsonPropertyName("minimumGain")] double MinimumGain);

internal sealed record PlacementPlan(string Format, bool Smoke, string Runtime, string OperatingSystem, string Architecture,
    int ProcessorCount, long Frequency, int ChunkCapacity, int Repetitions, int Warmup, int Frames, PlacementGates Gates,
    PlacementScenario Training, PlacementScenario[] Holdout, string HoldoutRule, string CoreSha256, string ProfilerSha256,
    string FixtureSha256, string? CoverageSha256, string[] Controls);

internal sealed record PlacementQualification(string Runtime, string OperatingSystem, string Architecture, int ProcessorCount,
    long Frequency, int ChunkCapacity, int Repetitions, int Warmup, int Frames, PlacementGates Gates, bool Smoke,
    string PlanSha256, string? CoverageSha256, PackedProfileEvidence? PackedEvidence, PlacementSignal[] Screening,
    PlacementPolicy Baseline, PlacementTrial[] BaselineTuning, string[]? Selected, PlacementPolicy? SelectedPolicy,
    PlacementTrial[] Trials, PlacementHoldoutState[] HoldoutStates);

internal sealed record PlacementSignal(string Name, long Transitions, long AvoidableNeighborBytes, long EstimatedIndexBytes,
    double MeanDensity, double PeakDensity);

internal sealed record PlacementHoldoutState([property: JsonPropertyName("scenario")] PlacementScenario Scenario, string[] Types,
    [property: JsonPropertyName("matched")] bool Matched, string[] Control, string[] Candidate);

internal sealed record PlacementTrial(PlacementScenario Scenario, string[] Placement, PlacementPolicy Policy,
    PlacementPolicy ControlPolicy, string ControlKind, bool StateMatches, double PairedFrameRatio, double PairedTailRatio,
    double PairedDenseRatio, double PairedStartupRatio, long CandidatePeakBytes, string[] Rejections,
    PlacementRun[] BaselineRuns, PlacementRun[] CandidateRuns);

internal sealed record PlacementRun(long StartupTicks, long StartupManagedBytes, long[] Frame, long[] Dense, long[] Adaptive,
    long ManagedBytes, int[] GcCollections, long PeakBytes, long FinalBytes, long NativeAllocations, string[] Hashes);

internal sealed record PlacementInputs(PlacementPlan Plan, string PlanSha256, PlacementQualification Qualification,
    string QualificationSha256, string[] Coverage, PlacementTrace[] Traces, PlacementControlReceipt? SamePolicyReceipt,
    PlacementControlReceipt? TunedArchetypesReceipt, PlacementTrust Trust)
{
    internal const string SamePolicy = "same-policy";
    internal const string TunedArchetypes = "tuned-archetypes";
    internal const string ArchetypeTuning = "archetype-tuning";
    private const string FixtureName = "AdaptivePlacementFixture";
    private const string FixtureWorkload = "adaptive-placement-fixture-v1";
    private const string PlanFile = "experiment-plan.json";
    private const string QualificationFile = "qualification.json";
    private const string CoverageFile = "legacy-coverage.json";
    private const string SamePolicyFile = "same-policy-receipt.json";
    private const string TunedArchetypesFile = "tuned-archetypes-receipt.json";
    private static readonly JsonSerializerOptions FixtureJson = JsonOptions(camelCase: false);
    private static readonly JsonSerializerOptions TrustJson = JsonOptions(camelCase: true);
    private static readonly string[] NullablePlanFields = [nameof(PlacementPlan.CoverageSha256)];
    private static readonly string[] NullableReportFields = [nameof(PlacementQualification.CoverageSha256),
        nameof(PlacementQualification.PackedEvidence), nameof(PlacementQualification.Selected), nameof(PlacementQualification.SelectedPolicy)];

    internal static PlacementInputs Read(string directory, string trustPath)
    {
        var trust = ReadJson<PlacementTrust>(File.ReadAllBytes(trustPath), TrustJson, []);
        Require(trust.Format == "skecs-placement-trust-v1" && trust.WorkloadId == FixtureWorkload && !string.IsNullOrWhiteSpace(trust.TargetId),
            "Packed profiles require the trusted manifest format, qualified workload and target identity.");
        RequireSha256(trust.SchemaFingerprint, "trusted profile schema");
        RequireSha256(trust.FixtureSha256, "trusted fixture");
        RequireHash(trust.CoreSha256, Artifacts.HashFile(typeof(World).Assembly.Location), "currently loaded core");
        RequireHash(trust.ProfilerSha256, Artifacts.HashFile(typeof(ProfilerSession).Assembly.Location), "currently loaded profiler");

        var (plan, planSha) = ReadPinned<PlacementPlan>(directory, PlanFile, trust.PlanSha256, NullablePlanFields);
        VerifyPlan(plan, trust);
        var (report, reportSha) = ReadPinned<PlacementQualification>(directory, QualificationFile, trust.QualificationSha256, NullableReportFields);
        VerifyReportMetadata(plan, planSha, report);

        var permitted = ReadCoverage(directory, plan, trust);
        var traces = ReadTraces(directory, plan, trust, permitted);
        var evidence = report.PackedEvidence;
        if (evidence == null)
        {
            Require(!File.Exists(Path.Combine(directory, SamePolicyFile)) && !File.Exists(Path.Combine(directory, TunedArchetypesFile)),
                "Qualification without packed evidence has unreferenced control receipts.");
            return new(plan, planSha, report, reportSha, permitted, traces, null, null, trust);
        }

        Require(!ShaEquals(evidence.SamePolicySha256, evidence.TunedArchetypesSha256), "The two control receipts must differ.");
        var same = ReadControl(directory, SamePolicyFile, evidence.SamePolicySha256, SamePolicy, planSha);
        var tuned = ReadControl(directory, TunedArchetypesFile, evidence.TunedArchetypesSha256, TunedArchetypes, planSha);
        return new(plan, planSha, report, reportSha, permitted, traces, same, tuned, trust);
    }

    private static void VerifyPlan(PlacementPlan plan, PlacementTrust trust)
    {
        Require(plan.Format == "adaptive-placement-fixture-v2" && plan.Controls.SequenceEqual([SamePolicy, TunedArchetypes]),
            "Unknown placement fixture format or control order.");
        Require(plan.ChunkCapacity > 0 && plan.Repetitions > 0 && plan.Warmup > 0 && plan.Frames > 0 &&
            (long)plan.Warmup + plan.Frames < int.MaxValue, "Invalid placement fixture dimensions.");
        Require(plan.Runtime == RuntimeInformation.FrameworkDescription && plan.OperatingSystem == RuntimeInformation.OSDescription &&
            plan.Architecture == RuntimeInformation.ProcessArchitecture.ToString() && plan.ProcessorCount == Environment.ProcessorCount &&
            plan.Frequency == Stopwatch.Frequency, "Placement fixture environment differs from the verification environment.");
        RequireHash(trust.CoreSha256, plan.CoreSha256, "plan core");
        RequireHash(trust.ProfilerSha256, plan.ProfilerSha256, "plan profiler");
        RequireHash(trust.FixtureSha256, plan.FixtureSha256, "plan fixture");
        RequireHash(trust.CoverageSha256, plan.CoverageSha256, "plan coverage");
    }

    private static void VerifyReportMetadata(PlacementPlan plan, string planSha, PlacementQualification report)
    {
        RequireHash(planSha, report.PlanSha256, "qualification plan");
        RequireHash(plan.CoverageSha256!, report.CoverageSha256, "qualification coverage");
        Require(report.Runtime == plan.Runtime && report.OperatingSystem == plan.OperatingSystem && report.Architecture == plan.Architecture &&
            report.ProcessorCount == plan.ProcessorCount && report.Frequency == plan.Frequency && report.ChunkCapacity == plan.ChunkCapacity &&
            report.Repetitions == plan.Repetitions && report.Warmup == plan.Warmup && report.Frames == plan.Frames &&
            report.Gates == plan.Gates && report.Smoke == plan.Smoke, "Qualification metadata differs from the predeclared plan.");
    }

    private static string[] ReadCoverage(string directory, PlacementPlan plan, PlacementTrust trust)
    {
        var (coverage, _) = ReadPinned<CoverageReport>(directory, CoverageFile, trust.CoverageSha256, []);
        Require(coverage.Format == "skecs-fixture-legacy-coverage-v1" && coverage.Assembly == FixtureName &&
            !string.IsNullOrWhiteSpace(coverage.Scope) && coverage.Runtime == plan.Runtime && coverage.Complete && coverage.Uncovered.Length == 0,
            "Legacy coverage is incomplete or belongs to another fixture.");
        RequireHash(trust.FixtureSha256, coverage.AssemblySha256, "coverage fixture");
        RequireHash(trust.CoreSha256, coverage.TrustedModules.GetValueOrDefault("SKECS"), "coverage core");
        RequireHash(trust.ProfilerSha256, coverage.TrustedModules.GetValueOrDefault("SKECS.Profiler"), "coverage profiler");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var permitted = new List<string>();
        foreach (var candidate in coverage.Candidates)
        {
            Require(!string.IsNullOrWhiteSpace(candidate.Component) && seen.Add(candidate.Component),
                "Legacy coverage contains an invalid or repeated component.");
            if (!candidate.PermittedByLegacyCoverage) continue;
            Require(!coverage.LegacyUses.Any(use => use.Assembly == FixtureName && use.Component == candidate.Component),
                $"Legacy coverage permits a component with a legacy use: {candidate.Component}.");
            permitted.Add(candidate.Component);
        }
        return permitted.ToArray();
    }

    private static PlacementTrace[] ReadTraces(string directory, PlacementPlan plan, PlacementTrust trust, string[] permitted)
    {
        var bindings = new Dictionary<string, PackedProfileComponent>(StringComparer.Ordinal);
        foreach (var component in trust.Components)
        {
            Require(component.Assembly == FixtureName && !string.IsNullOrWhiteSpace(component.Type) && component.Size > 0 &&
                component.Alignment == Math.Min(16, component.Size & -component.Size) && permitted.Contains(component.Type, StringComparer.Ordinal) &&
                bindings.TryAdd(component.Type, component), "Invalid, repeated or legacy-ineligible trusted component binding.");
            RequireSha256(component.SchemaFingerprint, $"trusted schema for {component.Type}");
        }

        var traces = new List<PlacementTrace>();
        var filenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var placements = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in trust.Traces)
        {
            Require(source.File.EndsWith(".xml", StringComparison.Ordinal) && source.File.Length > 4 &&
                source.File.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_') &&
                filenames.Add(source.File), "Invalid or repeated trusted trace filename.");
            var bytes = File.ReadAllBytes(Path.Combine(directory, source.File));
            var sha = Artifacts.Hash(bytes);
            RequireHash(source.Sha256, sha, source.File);
            using var stream = new MemoryStream(bytes, writable: false);
            var trace = TraceReader.Read(stream);
            Require(trace.Header.SchemaVersion is 2 or 3 && trace.Header.Scenario == plan.Training.Name &&
                trace.Header.WorkloadId == FixtureWorkload && trace.Header.Runtime == plan.Runtime && trace.Header.Os == plan.OperatingSystem &&
                trace.Header.Architecture == plan.Architecture && trace.Header.StopwatchFrequency == plan.Frequency &&
                trace.ChunkCapacity == plan.ChunkCapacity && trace.Frames == plan.Warmup + plan.Frames,
                $"Trace {source.File} does not match the pinned training plan.");

            var selected = trace.PackedLayouts.Select(layout => layout.Components.Single()).ToArray();
            Require((trace.Header.SchemaVersion == 2) == (selected.Length == 0),
                $"Trace {source.File} has an inconsistent packed placement and schema version.");
            foreach (var type in selected)
            {
                Require(bindings.TryGetValue(type.Type, out var binding) && binding.Assembly == type.Assembly &&
                    binding.Size == type.Size && binding.Alignment == type.Alignment,
                    $"Trace {source.File} has no matching trusted type geometry for {type.Type}.");
            }
            var key = string.Join('\n', selected.Select(type => type.Type).Order(StringComparer.Ordinal));
            Require(placements.Add(key), $"Trace {source.File} repeats a training placement.");
            traces.Add(new(source.File, sha, trace));
        }
        Require(placements.Contains(string.Empty), "Placement evidence is missing the pinned archetype training trace.");
        return traces.ToArray();
    }

    private static PlacementControlReceipt ReadControl(string directory, string filename, string expectedSha, string kind, string planSha)
    {
        var (receipt, sha) = ReadPinned<PlacementReceipt>(directory, filename, expectedSha, []);
        Require(receipt.ControlKind == kind, $"The pinned control role differs in {filename}.");
        RequireHash(planSha, receipt.PlanSha256, $"{filename} plan");
        return new(sha, receipt);
    }

    private static (T Value, string Sha256) ReadPinned<T>(string directory, string filename, string expectedSha, string[] nullableFields)
    {
        var bytes = File.ReadAllBytes(Path.Combine(directory, filename));
        var sha = Artifacts.Hash(bytes);
        RequireHash(expectedSha, sha, filename);
        return (ReadJson<T>(bytes, FixtureJson, nullableFields), sha);
    }

    private static JsonSerializerOptions JsonOptions(bool camelCase) => new()
    {
        PropertyNamingPolicy = camelCase ? JsonNamingPolicy.CamelCase : null,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { info =>
            {
                if (info.Kind == JsonTypeInfoKind.Object)
                    foreach (var property in info.Properties) property.IsRequired = true;
            } }
        }
    };

    private static T ReadJson<T>(byte[] bytes, JsonSerializerOptions options, string[] nullableFields)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Require(root.ValueKind == JsonValueKind.Object, "Expected a JSON evidence object.");
        CheckJson(root, nullableFields);
        return root.Deserialize<T>(options) ?? throw new InvalidDataException("Missing JSON evidence object.");
    }

    private static void CheckJson(JsonElement value, string[] nullableFields)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray())
            {
                Require(child.ValueKind != JsonValueKind.Null, "Evidence arrays cannot contain null entries.");
                CheckJson(child, nullableFields);
            }
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            Require(names.Add(property.Name), $"Duplicate JSON property {property.Name}.");
            Require(property.Value.ValueKind != JsonValueKind.Null || nullableFields.Contains(property.Name, StringComparer.Ordinal),
                $"Missing value in JSON property {property.Name}.");
            CheckJson(property.Value, nullableFields);
        }
    }

    private static void RequireHash(string expected, string? actual, string artifact)
    {
        RequireSha256(expected, artifact);
        Require(ShaEquals(expected, actual), $"The pinned SHA-256 differs for {artifact}.");
    }

    private static bool ShaEquals(string? expected, string? actual) => string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private static void RequireSha256(string value, string artifact) =>
        Require(value.Length == 64 && value.All(Uri.IsHexDigit), $"Invalid pinned SHA-256 for {artifact}.");

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private sealed record CoverageReport(string Format, string Scope, string Runtime, string Assembly, string AssemblySha256,
        bool Complete, Dictionary<string, string> TrustedModules, string[] Uncovered, LegacyUse[] LegacyUses, CandidateCoverage[] Candidates);
    private sealed record LegacyUse(string Assembly, string Component, string Consumer);
    private sealed record CandidateCoverage(string Component, bool PermittedByLegacyCoverage);
}
