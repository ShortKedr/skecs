using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SKECS.RuntimeProfiles.Tools;

internal sealed record Scenario([property: JsonRequired] string Id, [property: JsonRequired] string Workload,
    [property: JsonRequired] int Seed, [property: JsonRequired] int Entities, [property: JsonRequired] int WarmupFrames,
    [property: JsonRequired] int Frames, [property: JsonRequired] bool Holdout, [property: JsonRequired] double Weight);

internal sealed record Gates([property: JsonRequired] double FrameRegressionPercent,
    [property: JsonRequired] double P99RegressionPercent, [property: JsonRequired] double DenseRegressionPercent,
    [property: JsonRequired] double StartupRegressionPercent, [property: JsonRequired] double ManagedRegressionPercent,
    [property: JsonRequired] double MinimumImprovementPercent, [property: JsonRequired] double KneePercent,
    [property: JsonRequired] double MaxTrimP99Microseconds, long? MaxRetainedBytes, long ManagedRegressionBytesPerFrame = 0);

internal sealed record Corpus(int SchemaVersion, string WorkloadId, string TargetId, int ChunkCapacity,
    int EventCapacity, int Repetitions, long GenericTargetRetainedBytes, Gates Gates, Scenario[] Scenarios)
{
    internal void Validate()
    {
        Require(SchemaVersion == 1 && !string.IsNullOrWhiteSpace(WorkloadId) && !string.IsNullOrWhiteSpace(TargetId) &&
            ChunkCapacity > 0 && EventCapacity > 0 && Repetitions >= 3 && GenericTargetRetainedBytes > 0,
            "Invalid corpus version, identity, capacity, generic target or repetition count (minimum three).");
        Require(Gates is not null && ValidPercent(Gates.FrameRegressionPercent) && ValidPercent(Gates.P99RegressionPercent) &&
            ValidPercent(Gates.DenseRegressionPercent) && ValidPercent(Gates.StartupRegressionPercent) &&
            ValidPercent(Gates.ManagedRegressionPercent) && Gates.MinimumImprovementPercent > 0 &&
            ValidPercent(Gates.MinimumImprovementPercent) && Gates.KneePercent > 0 && ValidPercent(Gates.KneePercent) &&
            double.IsFinite(Gates.MaxTrimP99Microseconds) && Gates.MaxTrimP99Microseconds > 0 &&
            (Gates.MaxRetainedBytes is null or > 0) && Gates.ManagedRegressionBytesPerFrame >= 0, "Invalid acceptance thresholds.");
        Require(Scenarios is not null && Scenarios.Length > 0 && Scenarios.All(scenario => scenario is not null &&
            scenario.Id is { Length: > 0 and <= 64 } && scenario.Id.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') &&
            Workload.Supports(scenario.Workload) && scenario.Entities > 0 && scenario.WarmupFrames > 0 &&
            scenario.Frames > 0 && (long)scenario.WarmupFrames + scenario.Frames <= int.MaxValue &&
            double.IsFinite(scenario.Weight) && scenario.Weight > 0 && scenario.Weight <= 1), "Invalid scenario in corpus.");
        Require(Scenarios is not null && Scenarios.Select(scenario => scenario.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Scenarios.Length &&
            Scenarios.Select(scenario => (scenario.Workload, scenario.Entities, scenario.Seed)).Distinct().Count() == Scenarios.Length &&
            Scenarios.Any(scenario => scenario.Holdout && !IsDense(scenario)) &&
            Scenarios.Any(scenario => !scenario.Holdout && !IsDense(scenario)) &&
            new[] { "for-each", "chunks", "get", "set" }.All(workload => Scenarios.Any(scenario => !scenario.Holdout && scenario.Workload == workload)),
            "Provide unique cases, separate holdout inputs, a training churn case and all four training dense controls.");
    }

    internal static bool IsDense(Scenario scenario) => scenario.Workload is "for-each" or "chunks" or "get" or "set";

    private static bool ValidPercent(double value) => double.IsFinite(value) && value is >= 0 and <= 100;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}

internal sealed record RunEnvironment(string BuildId, string ProfilerBuildId, string Runtime, string Os, string Architecture, string Cpu,
    int LogicalProcessors, bool ServerGc, long StopwatchFrequency)
{
    internal static string AssemblyPath => typeof(RunEnvironment).Assembly.Location;
    private static readonly string CurrentBuildId = Artifacts.HashFile(AssemblyPath);

    internal static RunEnvironment Current()
    {
        var system = ProfileIdentity.Current(CurrentBuildId, "environment", CurrentBuildId, "environment", 1);
        var cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (OperatingSystem.IsLinux() && File.Exists("/proc/cpuinfo"))
        {
            var model = File.ReadLines("/proc/cpuinfo").Where(line => line.StartsWith("model name", StringComparison.Ordinal) ||
                line.StartsWith("flags", StringComparison.Ordinal)).Distinct().Order(StringComparer.Ordinal);
            cpu = string.Join("; ", model);
        }
        var profilerBuild = typeof(SKECS.Profiler.ProfilerSession).Assembly.ManifestModule.ModuleVersionId.ToString("D");
        return new(CurrentBuildId, profilerBuild, system.Runtime, system.Os, system.Architecture,
            string.IsNullOrWhiteSpace(cpu) ? "unspecified (see targetId)" : cpu,
            Environment.ProcessorCount, System.Runtime.GCSettings.IsServerGC, Stopwatch.Frequency);
    }
}

internal static class Artifacts
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    internal static T Read<T>(string path) => Read<T>(File.ReadAllBytes(path));

    internal static T Read<T>(byte[] bytes)
    {
        // Duplicate properties are ambiguous when the same artifact is checked by independent consumers.
        using var document = JsonDocument.Parse(bytes);
        CheckObjectNames(document.RootElement);
        return document.Deserialize<T>(Options) ?? throw new InvalidDataException("Empty JSON artifact.");
    }

    private static void CheckObjectNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckObjectNames(item);
        if (element.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate JSON property {property.Name}.");
            CheckObjectNames(property.Value);
        }
    }

    internal static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
    internal static void Write<T>(string path, T value) => File.WriteAllBytes(path, Serialize(value));
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
