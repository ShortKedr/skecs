using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SKECS.Determinism;

internal static class Program
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private static int Main(string[] args)
    {
        try
        {
            switch (args)
            {
                case ["run", var fixtures, var output]:
                    Run(new DirectoryInfo(fixtures), output);
                    return 0;
                case ["compare", var expected, var actual]:
                    Compare(expected, actual);
                    return 0;
                default:
                    Console.Error.WriteLine("Usage: SKECS.Determinism run <fixture-directory> <output-directory>");
                    Console.Error.WriteLine("       SKECS.Determinism compare <expected-run-directory> <actual-run-directory>");
                    return 2;
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void Run(DirectoryInfo inputs, string output)
    {
        var files = inputs.GetFiles("*.json").OrderBy(file => file.Name, StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
            throw new InvalidDataException($"No fixtures found in {inputs.FullName}.");

        var goldenPath = Path.Combine(inputs.Parent!.FullName, "golden.json");
        var golden = ReadJson<Golden>(goldenPath);
        if (golden.FormatVersion != 1 || golden.Fixtures is not { Count: > 0 })
            throw new InvalidDataException($"Invalid golden trace inventory in {goldenPath}.");
        var remainingGolden = new HashSet<string>(golden.Fixtures.Keys, StringComparer.Ordinal);
        Directory.CreateDirectory(output);
        if (Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidDataException($"Replay output directory must be empty: {output}");

        var records = new List<FixtureRecord>();
        foreach (var file in files)
        {
            var name = FixtureName(Path.GetFileNameWithoutExtension(file.Name));
            try
            {
                var bytes = File.ReadAllBytes(file.FullName);
                var fixture = Fixture.Read(bytes);
                var steps = golden.Fixtures.GetValueOrDefault(name);
                if (steps is not null && steps.Length != fixture.Operations.Length + 1)
                    throw new InvalidDataException("The golden trace must cover initial state and each operation.");

                WriteTrace(output, name, fixture, steps);
                remainingGolden.Remove(name);
                var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                records.Add(new FixtureRecord(name, digest, fixture.Operations.Length));
                Console.WriteLine($"{name}: {fixture.Operations.Length + 1} states match the managed oracle and D3 variant.");
            }
            catch (Exception error)
            {
                throw new InvalidDataException($"Fixture {file.FullName}: {error.Message}", error);
            }
        }
        if (remainingGolden.Count != 0)
            throw new InvalidDataException($"Missing fixtures for golden traces: {string.Join(", ", remainingGolden.Order())}.");

        var manifest = new RunManifest(1, OsName(), RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.Version.ToString(), Configuration, Environment.GetEnvironmentVariable("SKECS_REPLAY_COMMIT") ?? "unrecorded",
            Environment.GetEnvironmentVariable("SKECS_REPLAY_SDK") ?? "unrecorded", JitSettings(), records.ToArray());
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Fixture.Json) + "\n");
    }

    private static void WriteTrace(string output, string name, Fixture fixture, GoldenStep[]? golden)
    {
        using var writer = new StreamWriter(Path.Combine(output, name + ".jsonl")) { NewLine = "\n" };
        var alternateCapacity = fixture.Capacity == 3 ? 32 : 3;
        using var variant = Workload.Replay(fixture, new ReplayOptions(alternateCapacity, Diagnostics: true,
            WarmQueries: false, ReverseRegistration: true, UseChunks: true)).GetEnumerator();
        foreach (var step in Workload.Replay(fixture))
        {
            if (!variant.MoveNext())
                throw new InvalidDataException($"D3 variant ended before step {step.Step}.");
            var logicalDifference = Snapshot.Difference(step, variant.Current, logicalOnly: true);
            if (logicalDifference is not null)
                FailStep($"D3 step {step.Step} ({step.Operation}): {logicalDifference}", step, variant.Current);

            if (golden is not null)
            {
                var reference = golden[step.Step];
                var expected = step with { Result = reference.Result, Visits = reference.Visits, Entities = reference.Entities };
                ValidateShape(expected);
                var difference = Snapshot.Difference(expected, step);
                if (difference is not null)
                    FailStep($"Golden step {step.Step} ({step.Operation}): {difference}", expected, step);
            }
            writer.WriteLine(JsonSerializer.Serialize(step, Fixture.Json));
        }
        if (variant.MoveNext())
            throw new InvalidDataException("The D3 variant emitted an extra step.");
    }

    private static void Compare(string expectedDir, string actualDir)
    {
        var expected = ReadManifest(expectedDir);
        var actual = ReadManifest(actualDir);
        if (expected.Commit != actual.Commit || expected.Runtime != actual.Runtime ||
            expected.Sdk != actual.Sdk || !expected.Fixtures.SequenceEqual(actual.Fixtures) ||
            expected.Jit.Count != actual.Jit.Count ||
            expected.Jit.Any(pair => actual.Jit.GetValueOrDefault(pair.Key) != pair.Value))
            throw new InvalidDataException("Run metadata differ in commit, SDK, runtime, JIT settings or fixture inventory.");

        foreach (var input in expected.Fixtures)
        {
            using var left = File.OpenText(Path.Combine(expectedDir, input.Name + ".jsonl"));
            using var right = File.OpenText(Path.Combine(actualDir, input.Name + ".jsonl"));
            for (var i = 0; i <= input.Steps; i++)
            {
                var location = $"{input.Name}, step {i}";
                var a = ReadStep(left, location, i);
                var b = ReadStep(right, location, i);
                var difference = Snapshot.Difference(a, b);
                if (difference is not null)
                    FailStep($"{location} ({a.Operation}): {difference}", a, b);
                if (a.Hash != Snapshot.Hash(a.Entities) || b.Hash != Snapshot.Hash(b.Entities))
                    FailStep($"{location}: SHA-256 does not match the canonical state.", a, b);
            }
            if (left.ReadLine() is not null || right.ReadLine() is not null)
                throw new InvalidDataException($"{input.Name}: trace contains unexpected rows after step {input.Steps}.");
        }

        Console.WriteLine($"Compared {expected.Fixtures.Length} fixtures across two replay runs.");
    }

    private static ReplayStep ReadStep(StreamReader reader, string location, int index)
    {
        var line = reader.ReadLine() ?? throw new InvalidDataException($"{location}: trace is truncated.");
        var step = JsonSerializer.Deserialize<ReplayStep>(line, Fixture.Json)
            ?? throw new InvalidDataException($"{location}: missing trace step.");
        ValidateShape(step);
        if (step.Step != index || (index == 0) != (step.Operation == "initial"))
            throw new InvalidDataException($"{location}: unexpected step {step.Step} ({step.Operation}).");
        return step;
    }

    private static void ValidateShape(ReplayStep step)
    {
        if (string.IsNullOrEmpty(step.Operation) || step.Result is null || step.Visits is null ||
            step.Result.Any(value => value is null) || step.Visits.Any(value => value is null) ||
            step.Entities is null || step.Entities.Any(entity => entity is null || entity.Id < 0 || entity.Version <= 0) ||
            step.Entities.Select(entity => entity.Key).Distinct().Count() != step.Entities.Length ||
            step.Entities.Select(entity => entity.Id).Distinct().Count() != step.Entities.Length)
            throw new InvalidDataException($"Invalid trace structure at step {step.Step}.");
    }

    private static RunManifest ReadManifest(string directory)
    {
        var manifest = ReadJson<RunManifest>(Path.Combine(directory, "manifest.json"));
        if (manifest.FormatVersion != 1 || string.IsNullOrWhiteSpace(manifest.Commit) ||
            string.IsNullOrWhiteSpace(manifest.Sdk) || string.IsNullOrWhiteSpace(manifest.Runtime) ||
            manifest.Configuration is not ("Debug" or "Release") ||
            manifest.Jit is null || manifest.Fixtures is not { Length: > 0 })
            throw new InvalidDataException($"Invalid replay manifest in {directory}.");

        var names = new HashSet<string>(StringComparer.Ordinal) { "manifest.json" };
        foreach (var fixture in manifest.Fixtures)
        {
            if (fixture is null || fixture.Steps < 0 || fixture.InputSha256 is null ||
                !Regex.IsMatch(fixture.InputSha256, "\\A[0-9a-f]{64}\\z") || !names.Add(FixtureName(fixture.Name) + ".jsonl"))
                throw new InvalidDataException($"Invalid fixture inventory in {directory}.");
        }
        if (!names.SetEquals(new DirectoryInfo(directory).EnumerateFileSystemInfos().Select(entry => entry.Name)))
            throw new InvalidDataException($"Missing or unexpected trace files in {directory}.");
        return manifest;
    }

    private static T ReadJson<T>(string path) where T : class
        => JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Fixture.Json)
            ?? throw new InvalidDataException($"Missing JSON document in {path}.");

    private static string FixtureName(string name)
        => name is not null && Regex.IsMatch(name, "\\A[a-z0-9][a-z0-9-]*\\z") ? name
            : throw new InvalidDataException($"Invalid fixture name '{name}'.");

    private static string OsName() => OperatingSystem.IsWindows() ? "Windows"
        : OperatingSystem.IsLinux() ? "Linux"
        : OperatingSystem.IsMacOS() ? "macOS"
        : RuntimeInformation.OSDescription;

    private static SortedDictionary<string, string> JitSettings()
    {
        var settings = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in new[] { "TieredCompilation", "TieredPGO", "ReadyToRun", "JitMinOpts", "EnableHWIntrinsic" })
        {
            foreach (var prefix in new[] { "DOTNET_", "COMPlus_" })
            {
                var key = prefix + option;
                if (Environment.GetEnvironmentVariable(key) is { } value)
                    settings.Add(key, value);
            }
        }
        return settings;
    }

    private static void FailStep(string message, ReplayStep expected, ReplayStep actual)
        => throw new InvalidDataException($"{message}\nExpected: {JsonSerializer.Serialize(expected, Fixture.Json)}" +
            $"\nActual: {JsonSerializer.Serialize(actual, Fixture.Json)}");

    private sealed record FixtureRecord(string Name, string InputSha256, int Steps);
    private sealed record RunManifest(int FormatVersion, string Os, string Architecture, string Runtime,
        string Configuration, string Commit, string Sdk, SortedDictionary<string, string> Jit, FixtureRecord[] Fixtures);
    private sealed record Golden(int FormatVersion, Dictionary<string, GoldenStep[]> Fixtures);
    private sealed record GoldenStep(string[] Result, string[] Visits, EntityState[] Entities);
}
