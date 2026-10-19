using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json.Nodes;
using SKECS.BenchmarkTools;

namespace BenchmarkTools.Tests;

public sealed class SpaceBattleReportTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SKECS.sln")) && !Directory.Exists(Path.Combine(directory.FullName, "SpaceBattle.Core"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Project root not found");
    }
    private static string? Input(string kind)
    {
        var root = Root(); var local = Path.Combine(root, "artifacts", "benchmarks", "current", kind);
        var original = Path.Combine(root, "docs", "space-battle", kind == "desktop" ? "desktop-current" : "current");
        return File.Exists(Path.Combine(local, "manifest.json")) ? local : File.Exists(Path.Combine(original, "manifest.json")) ? original : null;
    }

    [Fact]
    public void RecordedFullBattleAndDesktopInputsPassWhenPresent()
    {
        if (Input("space-battle") is string battle) SpaceBattleReports.Run(Root(), battle, true, false);
        if (Input("desktop") is string desktop) DesktopReports.Run(Root(), desktop, false);
    }

    [Fact]
    public void DesktopEvidenceSupportsConsolidatedMeasurementHostWhenInputsPresent()
    {
        if (Input("desktop") is not string input) return;
        var root = Root(); var directory = Path.Combine(root, "artifacts", "benchmark-tools-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in Directory.EnumerateFiles(input))
                if (file.EndsWith(".json", StringComparison.Ordinal) || file.EndsWith(".gz", StringComparison.Ordinal)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            var manifestPath = Path.Combine(directory, "manifest.json"); var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            foreach (var name in new[] { "headless-warm-fast", "headless-warm-paced" })
            {
                var path = Path.Combine(directory, name + ".json"); var record = JsonNode.Parse(File.ReadAllText(path))!;
                var binaries = record["Provenance"]!["Binaries"]!.AsObject();
                if (binaries.Remove("SpaceBattle.Headless.dll", out var hash)) binaries["SpaceBattle.Benchmarks.dll"] = hash;
                File.WriteAllText(path, record.ToJsonString()); manifest["Files"]![name + ".json"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            }
            var hashes = manifest["BinaryHashes"]!.AsObject();
            foreach (var key in hashes.Select(p => p.Key).Where(k => k.StartsWith("SpaceBattle.Headless/", StringComparison.Ordinal)).ToArray())
            {
                var value = hashes[key]!.DeepClone(); hashes.Remove(key); hashes[key.Replace("SpaceBattle.Headless", "SpaceBattle.Benchmarks", StringComparison.Ordinal)] = value;
            }
            File.WriteAllText(manifestPath, manifest.ToJsonString());
            DesktopReports.Run(root, directory, false);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void IncompleteCampaignIsRejectedBeforeReadingSamples()
    {
        var root = Root(); var directory = Path.Combine(root, "artifacts", "benchmark-tools-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{\"Format\":3,\"Completed\":false}");
            Assert.Throws<InvalidDataException>(() => SpaceBattleReports.Run(root, directory, false, false));
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{\"Completed\":false,\"Ticks\":1200,\"SourceHashes\":{}}");
            Assert.Throws<InvalidDataException>(() => DesktopReports.Run(root, directory, false));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void DuplicateJsonKeysAreRejected()
    {
        var root = Root(); var directory = Path.Combine(root, "artifacts", "benchmark-tools-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{\"Format\":3,\"Completed\":false,\"Completed\":true}");
            Assert.Throws<InvalidDataException>(() => SpaceBattleReports.Run(root, directory, false, false));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void LowercaseRunnerHashesMatchUppercaseRuntimeHashesWhenInputsPresent()
    {
        var root = Root(); var directory = Path.Combine(root, "artifacts", "benchmark-tools-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            foreach (var kind in new[] { "space-battle", "desktop" })
            {
                if (Input(kind) is not string input) continue;
                var fixture = Path.Combine(directory, kind); Directory.CreateDirectory(fixture);
                foreach (var file in Directory.EnumerateFiles(input)) if (file.EndsWith(".json", StringComparison.Ordinal) || file.EndsWith(".gz", StringComparison.Ordinal)) File.Copy(file, Path.Combine(fixture, Path.GetFileName(file)));
                var manifestPath = Path.Combine(fixture, "manifest.json"); var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
                foreach (var field in new[] { "Files", "TickCsvHashes", "BinaryHashes", "SourceHashes" })
                    if (manifest[field] is JsonObject hashes) foreach (var key in hashes.Select(p => p.Key).ToArray()) hashes[key] = hashes[key]!.GetValue<string>().ToLowerInvariant();
                File.WriteAllText(manifestPath, manifest.ToJsonString());
                if (kind == "space-battle") SpaceBattleReports.Run(root, fixture, false, false); else DesktopReports.Run(root, fixture, false);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RehashedCounterAndBinaryCorruptionIsRejectedWhenRecordedInputsPresent()
    {
        if (Input("space-battle") is not string input) return;
        var root = Root(); var directory = Path.Combine(root, "artifacts", "benchmark-tools-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in Directory.EnumerateFiles(input)) if (file.EndsWith(".json", StringComparison.Ordinal) || file.EndsWith(".gz", StringComparison.Ordinal)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            var manifestPath = Path.Combine(directory, "manifest.json"); var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            var framework = manifest["FrameworkOrder"]![0]!.GetValue<string>(); var filename = "high-" + framework + ".json"; var path = Path.Combine(directory, filename);
            var original = File.ReadAllText(path); var run = JsonNode.Parse(original)!;
            run["Counters"]!["Shots"] = run["Counters"]!["Shots"]!.GetValue<long>() + 1;
            Save(); Assert.Throws<InvalidDataException>(() => SpaceBattleReports.Run(root, directory, false, false));
            run = JsonNode.Parse(original)!; var key = run["Provenance"]!["Binaries"]!.AsObject().First().Key; run["Provenance"]!["Binaries"]![key] = new string('0', 64);
            Save(); Assert.Throws<InvalidDataException>(() => SpaceBattleReports.Run(root, directory, false, false));
            void Save() { File.WriteAllText(path, run.ToJsonString()); manifest["Files"]![filename] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); File.WriteAllText(manifestPath, manifest.ToJsonString()); }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RehashedNegativeRawTickAndRenderedArtifactsAreCheckedWhenInputsPresent()
    {
        if (Input("space-battle") is not string input) return;
        var root = Root(); var directory = Path.Combine(root, "artifacts", "benchmark-tools-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in Directory.EnumerateFiles(input)) if (file.EndsWith(".json", StringComparison.Ordinal) || file.EndsWith(".gz", StringComparison.Ordinal)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            var output = Path.Combine(directory, "report"); SpaceBattleReports.Run(root, directory, true, true, output);
            Assert.True(File.Exists(Path.Combine(output, "REPORT.md"))); Assert.True(File.Exists(Path.Combine(output, "REPORT.ru.md")));
            Assert.True(File.Exists(Path.Combine(output, "summary.csv"))); Assert.Contains("<svg", File.ReadAllText(Path.Combine(output, "MeanMs.svg")));
            var report = File.ReadAllText(Path.Combine(output, "REPORT.md")); Assert.Contains("Memory and store census", report); Assert.Contains("IntentProjectiles", report); Assert.DoesNotContain("](report/manifest.json)", report);
            Assert.DoesNotContain(".ticks.csv", report); Assert.DoesNotContain("hardware.json", report); Assert.DoesNotContain("```powershell", report);
            if (Input("desktop") is string desktop)
            {
                var desktopOutput = Path.Combine(directory, "desktop-report"); DesktopReports.Run(root, desktop, true, desktopOutput);
                var desktopReport = File.ReadAllText(Path.Combine(desktopOutput, "DESKTOP_TIMING.md"));
                Assert.True(File.Exists(Path.Combine(desktopOutput, "DESKTOP_TIMING.ru.md"))); Assert.True(File.Exists(Path.Combine(desktopOutput, "StartupHud.svg")));
                Assert.DoesNotContain(".ticks.csv", desktopReport); Assert.DoesNotContain("manifest.json", desktopReport); Assert.Contains("rolling window", desktopReport);
            }
            var manifestPath = Path.Combine(directory, "manifest.json"); var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            var framework = manifest["FrameworkOrder"]![0]!.GetValue<string>(); var filename = "high-" + framework + ".ticks.csv"; var archive = Path.Combine(directory, filename + ".gz"); string csv;
            using (var file = File.OpenRead(archive)) using (var gz = new GZipStream(file, CompressionMode.Decompress)) using (var reader = new StreamReader(gz)) csv = reader.ReadToEnd();
            var rows = csv.Split('\n'); var header = rows[0].TrimEnd('\r').Split(','); var values = rows[1].TrimEnd('\r').Split(','); values[Array.IndexOf(header, "allocatedBytes")] = "-1"; rows[1] = string.Join(",", values); csv = string.Join("\n", rows);
            using (var file = File.Create(archive)) using (var gz = new GZipStream(file, CompressionLevel.Optimal)) using (var writer = new StreamWriter(gz)) writer.Write(csv);
            manifest["Files"]![filename + ".gz"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive)));
            using (var file = File.OpenRead(archive)) using (var gz = new GZipStream(file, CompressionMode.Decompress)) manifest["TickCsvHashes"]![filename] = Convert.ToHexString(SHA256.HashData(gz));
            File.WriteAllText(manifestPath, manifest.ToJsonString());
            Assert.Throws<InvalidDataException>(() => SpaceBattleReports.Run(root, directory, true, false));
        }
        finally { Directory.Delete(directory, true); }
    }
}
