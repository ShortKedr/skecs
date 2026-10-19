using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SKECS.BenchmarkTools;

/// <summary>Validation of archived measurements; never runs or rebuilds benchmarks.</summary>
public static class MicroValidation
{
    public static readonly string[] Frameworks = ["SKECS", "LeoECS Lite", "LeoECS Classic", "Morpeh", "StaticEcs"];
    public static readonly string[] BaseScenarios = ["Create", "Iterate", "Churn", "Destroy"];
    public static readonly string[] StressScenarios = ["DenseIteration", "SparseIteration", "WideIteration", "RandomLookup", "PayloadChurn", "Recycle10Percent", "MixedFrames"];
    public static JsonElement Load(string path) => JsonDocument.Parse(File.ReadAllText(path).TrimStart('\uFEFF')).RootElement.Clone();
    public static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    public static string S(JsonElement value, string key) => value.GetProperty(key).GetString() ?? throw new InvalidDataException("Missing string: " + key);
    public static double Number(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var _) , "Measurement must be a number");
        double n = value.GetDouble();
        Require(double.IsFinite(n) && n >= 0, "Measurement must be finite and nonnegative");
        return n;
    }
    public static double N(JsonElement row, string key) => Number(row.GetProperty(key));
    public static int I(JsonElement row, string key)
    {
        Require(row.GetProperty(key).TryGetInt32(out int value), "Expected integer: " + key);
        return value;
    }
    public static string LocalPath(string root, string relative)
    {
        Require(!string.IsNullOrWhiteSpace(relative) && !Path.IsPathRooted(relative) && !relative.Contains(':') &&
            !relative.Split('/', '\\').Contains(".."), "Nonlocal provenance path: " + relative);
        string path = Path.GetFullPath(Path.Combine(root, relative));
        Require(path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "Path escapes campaign");
        return path;
    }
    public static string OutputDirectory(string root, string directory)
    {
        string full = Path.GetFullPath(directory, Path.GetFullPath(root));
        string relative = Path.GetRelativePath(Path.GetFullPath(root), full);
        Require(relative != ".", "Output directory must be below the project root");
        return LocalPath(root, relative);
    }
    static void Digest(string hash) => Require(Regex.IsMatch(hash, "\\A[0-9a-fA-F]{64}\\z"), "Invalid SHA-256");
    public static void VerifyHash(string path, string expected)
    {
        Digest(expected);
        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(expected, StringComparison.OrdinalIgnoreCase), "Input hash mismatch: " + path);
    }
    public static void Manifest(JsonElement data)
    {
        Require(I(data, "format") == 2 && !string.IsNullOrWhiteSpace(S(data, "sourceRevision")), "Incomplete campaign provenance");
        foreach (string field in new[] { "sourceHashes", "assemblyHashes" })
        {
            var properties = data.GetProperty(field).EnumerateObject().ToArray();
            Require(properties.Length > 0, "Empty " + field);
            foreach (var entry in properties) { LocalPath(Path.GetTempPath(), entry.Name); Digest(entry.Value.GetString()!); }
        }
    }
    public static bool Equal(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count() && a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var q) && Equal(p.Value, q)),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => Equal(p.First, p.Second)),
            JsonValueKind.Number => EqualNumber(a, b),
            JsonValueKind.String => a.GetString() == b.GetString(),
            _ => a.GetRawText() == b.GetRawText()
        };
    }
    static bool EqualNumber(JsonElement a, JsonElement b)
    {
        // Aggregation historically parsed exporter floating values as doubles.
        // Keep that contract, but never round integer counters through double.
        string x = a.GetRawText(), y = b.GetRawText();
        return x.IndexOfAny(['.', 'e', 'E']) < 0 && y.IndexOfAny(['.', 'e', 'E']) < 0
            ? BigInteger.Parse(x, CultureInfo.InvariantCulture) == BigInteger.Parse(y, CultureInfo.InvariantCulture)
            : a.GetDouble() == b.GetDouble();
    }
    public static void Close(double actual, double expected, string message, double absolute = 1e-9) =>
        Require(Math.Abs(actual - expected) <= Math.Max(absolute, 1e-12 * Math.Max(Math.Abs(actual), Math.Abs(expected))), message);
    public static double Deviation(double[] values)
    {
        double mean = values.Average();
        return values.Length == 1 ? 0 : Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1));
    }
    public static void Samples(JsonElement row, int count)
    {
        double[] samples = row.GetProperty("samplesNs").EnumerateArray().Select(Number).ToArray();
        Require(samples.Length == count && samples.All(x => x > 0), "Unexpected timing sample count or zero sample");
        Close(samples.Average(), N(row, "meanNs"), "Mean differs from raw samples");
        Close(Deviation(samples), N(row, "stddevNs"), "Deviation differs from raw samples");
        double[] allocation = row.GetProperty("samplesAllocatedBytes").EnumerateArray().Select(Number).ToArray();
        Require(allocation.Length == count, "Unexpected allocation sample count");
        Close(allocation.Average(), N(row, "allocatedBytes"), "Allocation mean differs from raw samples");
        Require(I(row, "repetitions") > 0, "Invalid repetitions");
    }
    public static Dictionary<(string Framework, string Scenario, int Count), JsonElement> Matrix(JsonElement data, bool stress)
    {
        var scenarios = stress ? StressScenarios : BaseScenarios;
        int[] counts = stress ? [100000, 1000000] : [1000, 100000];
        var expected = (from f in Frameworks from s in scenarios from n in counts select (f, s, n)).ToHashSet();
        var indexed = new Dictionary<(string, string, int), JsonElement>();
        foreach (var row in data.GetProperty("results").EnumerateArray())
        {
            string framework = S(row, "framework");
            if (framework == "SECS") framework = "SKECS";
            var key = (framework, S(row, "scenario"), I(row, "entityCount"));
            Require(expected.Contains(key) && indexed.TryAdd(key, row), "Unexpected or duplicate matrix case: " + key);
            Samples(row, stress ? 8 : 20);
        }
        Require(expected.SetEquals(indexed.Keys), "Matrix coverage mismatch");
        Require(S(data.GetProperty("metadata"), "tieredCompilation") == "0", "Tiering must be disabled");
        return indexed;
    }
    public static Dictionary<(string Operation, string Method, int Count), JsonElement> Api(JsonElement data)
    {
        var expected = new HashSet<(string, string, int)>();
        foreach (int n in new[] { 100000, 1000000 })
        {
            foreach (string method in new[] { "EmptyAddAdd", "CreateEntityComponents", "PreparedFactoryBatch" }) expected.Add(("Create", method, n));
            foreach (string method in new[] { "WorldGet", "CachedAccessorGet" }) expected.Add(("Lookup", method, n));
        }
        var indexed = new Dictionary<(string, string, int), JsonElement>();
        foreach (var row in data.GetProperty("results").EnumerateArray())
        {
            var key = (S(row, "operation"), S(row, "method"), I(row, "entityCount"));
            Require(expected.Contains(key) && indexed.TryAdd(key, row), "Unexpected or duplicate API case");
            Samples(row, 8); N(row, "storageNativeBytes");
            if (key.Item1 == "Create")
            {
                var native = row.GetProperty("samplesStorageNativeBytes").EnumerateArray().Select(Number).ToArray();
                Require(native.Length == 8, "Native storage sample count mismatch");
                Close(native.Average(), N(row, "storageNativeBytes"), "Native storage mean mismatch");
            }
        }
        Require(expected.SetEquals(indexed.Keys), "API coverage mismatch");
        Require(S(data.GetProperty("metadata"), "tieredCompilation") == "0", "API tiering must be disabled");
        return indexed;
    }
    public static void Storage(JsonElement data)
    {
        var metadata = data.GetProperty("metadata");
        Require(I(metadata, "passes") == 20 && I(metadata, "entityCount") == 100000 && I(metadata, "payloadBytes") == 256, "Storage scope mismatch");
        var rows = data.GetProperty("samples").EnumerateArray().ToArray();
        Require(rows.Select(r => I(r, "pass")).SequenceEqual(Enumerable.Range(0, 21)), "Storage pass coverage mismatch");
        foreach (var row in rows)
        {
            var archetypes = row.GetProperty("archetypes").EnumerateArray().ToArray();
            Require(archetypes.Sum(r => N(r, "nativeBytes")) == N(row, "nativeBytes"), "Storage byte total differs from archetypes");
            Require(archetypes.Sum(r => N(r, "chunkCount")) == N(row, "chunkCount"), "Storage chunk total differs from archetypes");
            foreach (var item in archetypes) Require(N(item, "nativeBytes") == N(item, "chunkCount") * N(item, "chunkBytes"), "Storage byte product mismatch");
        }
    }
    public readonly record struct BdnKey(string Type, string Method, int Count, int? Padding);
    public static BdnKey Key(JsonElement row)
    {
        var parameters = row.GetProperty("Parameters");
        var values = new Dictionary<string, int>();
        if (parameters.ValueKind == JsonValueKind.Object)
            foreach (var p in parameters.EnumerateObject()) values[p.Name] = int.Parse(p.Value.ToString(), CultureInfo.InvariantCulture);
        else foreach (Match m in Regex.Matches(parameters.ToString(), @"(EntityCount|Padding)\s*[=:]\s*(\d+)"))
            Require(values.TryAdd(m.Groups[1].Value, int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)), "Duplicate BDN parameter");
        Require(values.ContainsKey("EntityCount"), "BDN case has no EntityCount");
        return new(S(row, "Type").Split('.').Last(), S(row, "Method"), values["EntityCount"], values.TryGetValue("Padding", out int pvalue) ? pvalue : null);
    }
    public static HashSet<BdnKey> ExpectedCurrent()
    {
        var result = new HashSet<BdnKey>();
        void Add(string type, string[] methods, int[] counts) { foreach (var m in methods) foreach (int n in counts) result.Add(new(type, m, n, null)); }
        Add("EcsBenchmarks", ["LinearIteration", "AddRemoveStructuralChurn"], [100000]);
        Add("ComparisonBenchmarks", BaseScenarios, [1000, 100000]);
        Add("StressBenchmarks", StressScenarios, [100000, 1000000]);
        Add("OptimizedApiCreationBenchmarks", ["EmptyAddAdd", "CreateEntityComponents", "PreparedFactoryBatch"], [100000, 1000000]);
        Add("OptimizedApiLookupBenchmarks", ["WorldGet", "CachedAccessorGet"], [100000, 1000000]);
        return result;
    }
    public static HashSet<BdnKey> ExpectedLegacy(string root)
    {
        var names = Regex.Matches(File.ReadAllText(Path.Combine(root, "SKECS.Benchmarks/Suites/Extended/Benchmarks.cs")), @"public class (\w+)\s*:").Select(m => m.Groups[1].Value)
            .Concat(["DeleteEntity", "MultiSystems", "FilterMismatchSystems"]);
        return names.SelectMany(name => (name.StartsWith("SystemWith", StringComparison.Ordinal) ? new int?[] { 0, 10 } : [null])
            .Select(padding => new BdnKey(name, "Run", 100000, padding))).ToHashSet();
    }
    public static Dictionary<BdnKey, JsonElement> Bdn(JsonElement data, HashSet<BdnKey> expected)
    {
        var metadata = data.GetProperty("metadata");
        Require(S(metadata, "configuration") == "Release" && S(metadata, "job") == "Short" && S(metadata, "toolchain") == "InProcess", "Unexpected BDN job/configuration/toolchain");
        Require(I(metadata, "iterationCount") == 8 && I(metadata, "warmupCount") == 3, "Expected 8 actual / 3 warmup iterations");
        Require(!string.IsNullOrWhiteSpace(S(metadata, "runtime")), "Missing BDN runtime");
        var indexed = new Dictionary<BdnKey, JsonElement>();
        foreach (var row in data.GetProperty("Benchmarks").EnumerateArray())
        {
            var key = Key(row);
            Require(indexed.TryAdd(key, row), "Duplicate BDN case: " + key);
            var job = Regex.Match(S(row, "DisplayInfo"), @":\s*ShortRun\(([^)]*)\)");
            Require(job.Success, "Raw BDN case must use ShortRun");
            var settings = Regex.Matches(job.Groups[1].Value, @"(\w+)\s*=\s*([^, )]+)").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            foreach (var pair in new Dictionary<string, string> { ["Toolchain"] = "InProcessEmitToolchain", ["IterationCount"] = "8", ["LaunchCount"] = "1", ["WarmupCount"] = "3" })
                Require(settings.TryGetValue(pair.Key, out string? v) && v == pair.Value, "Unexpected raw BDN " + pair.Key);
            var measurements = row.GetProperty("Measurements").EnumerateArray().ToArray();
            var actual = measurements.Where(m => S(m, "IterationMode") == "Workload" && S(m, "IterationStage") == "Actual").ToArray();
            Require(actual.Length == 8, "Expected 8 actual samples");
            foreach (var item in actual) { Require(N(item, "Operations") > 0, "Zero measurement operations"); N(item, "Nanoseconds"); }
            var retainedItems = measurements.Where(m => S(m, "IterationMode") == "Workload" && S(m, "IterationStage") == "Result").ToArray();
            foreach (var item in retainedItems) Require(N(item, "Operations") > 0, "Zero retained operations");
            var retained = retainedItems.Select(m => N(m, "Nanoseconds") / N(m, "Operations")).ToArray();
            var stats = row.GetProperty("Statistics");
            int count = I(stats, "N");
            Require(count >= 1 && count <= 8 && retained.Length == count, "Retained BDN sample count mismatch");
            Require(stats.GetProperty("OriginalValues").EnumerateArray().Select(Number).SequenceEqual(retained), "BDN OriginalValues differ from Result measurements");
            Close(retained.Average(), N(stats, "Mean"), "BDN mean differs from retained measurements");
            Close(Deviation(retained), N(stats, stats.TryGetProperty("StandardDeviation", out _) ? "StandardDeviation" : "StdDev"), "BDN deviation differs from retained measurements");
        }
        Require(expected.SetEquals(indexed.Keys), "BDN coverage mismatch");
        return indexed;
    }
    public static void OriginalExports(string directory, JsonElement data, JsonElement manifest, string assemblyPath)
    {
        var metadata = data.GetProperty("metadata");
        Require(S(metadata, "sourceRevision") == S(manifest, "sourceRevision") && Equal(metadata.GetProperty("sourceHashes"), manifest.GetProperty("sourceHashes")), "Mixed BDN source snapshots");
        Require(S(metadata, "tieredCompilation") == "0", "BDN tiering must be disabled");
        Require(S(metadata, "assemblySha256").Equals(S(manifest.GetProperty("assemblyHashes"), assemblyPath), StringComparison.OrdinalIgnoreCase), "BDN assembly differs from campaign");
        var exported = new List<JsonElement>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in data.GetProperty("rawReports").EnumerateArray())
        {
            string relative = S(raw, "file");
            Require(paths.Add(relative), "Duplicate raw BDN export");
            string path = LocalPath(directory, relative);
            VerifyHash(path, S(raw, "sha256"));
            var original = Load(path);
            Require(S(original.GetProperty("HostEnvironmentInfo"), "RuntimeVersion") == S(metadata, "runtime"), "BDN runtime differs from original exporter");
            exported.AddRange(original.GetProperty("Benchmarks").EnumerateArray());
        }
        var aggregate = data.GetProperty("Benchmarks").EnumerateArray().ToArray();
        Require(exported.Count == aggregate.Length && exported.Zip(aggregate).All(p => Equal(p.First, p.Second)), "BDN aggregate differs from original full exporter files");
    }
}
