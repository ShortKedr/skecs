using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using SKECS.BenchmarkTools;

namespace SKECS.BenchmarkTools.Tests;

public sealed class MicroReportsTests
{
    static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value);
    static object Samples(string framework = "SKECS", string scenario = "Create", int count = 1000, int samples = 20) => new
    {
        framework, scenario, entityCount = count, meanNs = 100.0, stddevNs = 0.0,
        samplesNs = Enumerable.Repeat(100.0, samples), allocatedBytes = 2.0,
        samplesAllocatedBytes = Enumerable.Repeat(2.0, samples), repetitions = 1
    };
    static JsonElement Matrix(bool stress = false) => Element(new
    {
        metadata = new { tieredCompilation = "0" },
        results = from framework in MicroValidation.Frameworks
                  from scenario in stress ? MicroValidation.StressScenarios : MicroValidation.BaseScenarios
                  from count in stress ? new[] { 100000, 1000000 } : [1000, 100000]
                  select Samples(framework, scenario, count, stress ? 8 : 20)
    });
    static JsonElement Bdn()
    {
        var values = new[] { 100.0, 102.0, 99.0, 101.0, 100.0, 101.0, 99.0 };
        return Element(new
        {
            metadata = new { configuration = "Release", job = "Short", toolchain = "InProcess", iterationCount = 8, warmupCount = 3, runtime = ".NET 8" },
            Benchmarks = MicroValidation.ExpectedCurrent().Select(k => new
            {
                Type = k.Type, Method = k.Method, Parameters = "EntityCount=" + k.Count,
                DisplayInfo = k.Type + "." + k.Method + ": ShortRun(Toolchain=InProcessEmitToolchain, IterationCount=8, LaunchCount=1, WarmupCount=3)",
                Measurements = Enumerable.Range(0, 8).Select(i => new { IterationMode = "Workload", IterationStage = "Actual", Operations = 2, Nanoseconds = 200.0 + i })
                    .Concat(values.Select(v => new { IterationMode = "Workload", IterationStage = "Result", Operations = 2, Nanoseconds = v * 2 })),
                Statistics = new { OriginalValues = values, N = values.Length, Mean = values.Average(), StandardDeviation = MicroValidation.Deviation(values) }
            })
        });
    }
    [Fact]
    public void ExactMatricesAndRetainedBdnSamplesValidate()
    {
        Assert.Equal(40, MicroValidation.Matrix(Matrix(), false).Count);
        Assert.Equal(70, MicroValidation.Matrix(Matrix(true), true).Count);
        var bdn = MicroValidation.Bdn(Bdn(), MicroValidation.ExpectedCurrent());
        Assert.Equal(34, bdn.Count);
        Assert.All(bdn.Values, row => Assert.Equal(7, row.GetProperty("Statistics").GetProperty("N").GetInt32()));
    }
    [Fact]
    public void ApiAndStorageValidateExactScopeAndNativeTotals()
    {
        var methods = new[] { ("Create", "EmptyAddAdd"), ("Create", "CreateEntityComponents"), ("Create", "PreparedFactoryBatch"), ("Lookup", "WorldGet"), ("Lookup", "CachedAccessorGet") };
        var api = Element(new
        {
            metadata = new { tieredCompilation = "0" },
            results = methods.SelectMany(m => new[] { 100000, 1000000 }.Select(n => new
            {
                operation = m.Item1, method = m.Item2, entityCount = n, meanNs = 100.0, stddevNs = 0.0,
                samplesNs = Enumerable.Repeat(100.0, 8), allocatedBytes = 0.0, samplesAllocatedBytes = new double[8], repetitions = 1,
                storageNativeBytes = 100.0, samplesStorageNativeBytes = Enumerable.Repeat(100.0, 8)
            }))
        });
        Assert.Equal(10, MicroValidation.Api(api).Count);
        var storage = Element(new
        {
            metadata = new { entityCount = 100000, passes = 20, payloadBytes = 256 },
            samples = Enumerable.Range(0, 21).Select(pass => new { pass, nativeBytes = 100, chunkCount = 2, archetypes = new[] { new { nativeBytes = 100, chunkCount = 2, chunkBytes = 50 } } })
        });
        MicroValidation.Storage(storage);
        var broken = JsonNode.Parse(storage.GetRawText())!;
        broken["samples"]![0]!["archetypes"]![0]!["chunkBytes"] = 51;
        Assert.Throws<InvalidDataException>(() => MicroValidation.Storage(Element(broken)));
    }
    [Theory]
    [InlineData("meanNs")]
    [InlineData("stddevNs")]
    [InlineData("allocatedBytes")]
    public void TamperedSampleSummariesAreRejected(string field)
    {
        var row = JsonNode.Parse(Element(Samples()).GetRawText())!;
        row[field] = 123456;
        Assert.Throws<InvalidDataException>(() => MicroValidation.Samples(Element(row), 20));
    }
    [Fact]
    public void DuplicatesAndMissingCasesAreRejected()
    {
        var data = JsonNode.Parse(Matrix().GetRawText())!;
        data["results"]!.AsArray().RemoveAt(0);
        Assert.Throws<InvalidDataException>(() => MicroValidation.Matrix(Element(data), false));
        data = JsonNode.Parse(Matrix().GetRawText())!;
        data["results"]!.AsArray().Add(data["results"]![0]!.DeepClone());
        Assert.Throws<InvalidDataException>(() => MicroValidation.Matrix(Element(data), false));
    }
    [Theory]
    [InlineData("job")]
    [InlineData("actual")]
    [InlineData("retained")]
    [InlineData("statistics")]
    public void BdnUsesActualDisplayInfoAndSeparateRetainedSamples(string mutation)
    {
        var data = JsonNode.Parse(Bdn().GetRawText())!;
        var row = data["Benchmarks"]![0]!;
        if (mutation == "job") row["DisplayInfo"] = row["DisplayInfo"]!.GetValue<string>().Replace("IterationCount=8", "IterationCount=3");
        if (mutation == "actual") row["Measurements"]!.AsArray().RemoveAt(0);
        if (mutation == "retained") row["Statistics"]!["OriginalValues"]![0] = 999;
        if (mutation == "statistics") row["Statistics"]!["Mean"] = 999;
        Assert.Throws<InvalidDataException>(() => MicroValidation.Bdn(Element(data), MicroValidation.ExpectedCurrent()));
    }
    [Fact]
    public void FixedEffectScaleAndSvgChartsRetainEveryCase()
    {
        Assert.Equal(new[] { "equal", "equal" }, ReportColors.Levels([100, 104]));
        Assert.Equal(new[] { "best", "middle", "worst" }, ReportColors.Levels([100, 115, 130]));
        Assert.Equal("strong", ReportColors.Levels([100, 105.0001])[1]);
        var repository = FindRoot();
        string directory = Path.Combine(repository, "artifacts/tooling/tmp/micro-chart-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "comparison.svg");
            MicroCharts.Render(path, "Base", MicroValidation.Matrix(Matrix(), false), false);
            var svg = XDocument.Load(path); XNamespace ns = "http://www.w3.org/2000/svg";
            Assert.Equal(40, svg.Descendants(ns + "rect").Count(e => e.Element(ns + "title") != null));
            Assert.Contains("sample SD", File.ReadAllText(path));
            SvgCharts.Bars(path, "Battle <timing>", ["Best", "15% lag", "30% lag"], [100, 115, 130], "ms");
            svg = XDocument.Load(path);
            Assert.Equal("Battle <timing>", svg.Root!.Element(ns + "title")!.Value);
            Assert.Equal(new[] { "#2e9b50", "#f2d85c", "#c93637" }, svg.Descendants(ns + "rect").Where(e => e.Attribute("height")?.Value == "30").Select(e => e.Attribute("fill")!.Value));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("../escape")]
    [InlineData("C:\\escape")]
    [InlineData("sub/../../escape")]
    [InlineData("sub\\..\\escape")]
    public void EscapingPathsAreRejected(string path) => Assert.Throws<InvalidDataException>(() => MicroValidation.LocalPath(FindRoot(), path));
    [Fact]
    public void SavedCampaignGeneratesBilingualReportsAndRejectsHashTamperingWhenArtifactsExist()
    {
        using var fixture = SavedFixture.Create();
        if (fixture == null) return; // Captured machine artifacts are deliberately not source controlled.
        MicroReports.Comparison(fixture.Root, fixture.Directory, true, fixture.Output);
        MicroReports.Supplement(fixture.Root, fixture.Directory, true, fixture.Output);
        foreach (string name in new[] { "REPORT.md", "REPORT.ru.md", "SUPPLEMENT.md", "SUPPLEMENT.ru.md", "comparison.svg", "stress-100k.svg", "stress-1m.svg" }) Assert.True(File.Exists(Path.Combine(fixture.Output, name)), name);
        string report = File.ReadAllText(Path.Combine(fixture.Output, "REPORT.md"));
        Assert.Contains("40 base cases and 70 stress cases", report);
        Assert.DoesNotContain("](base.json)", report);
        Assert.Contains("### Legacy: 51", File.ReadAllText(Path.Combine(fixture.Output, "SUPPLEMENT.md")));
        string original = File.ReadAllText(Path.Combine(fixture.Output, "REPORT.md"));
        File.AppendAllText(Path.Combine(fixture.Directory, "base.json"), " ");
        Assert.Throws<InvalidDataException>(() => MicroReports.Comparison(fixture.Root, fixture.Directory, true, fixture.Output));
        Assert.Equal(original, File.ReadAllText(Path.Combine(fixture.Output, "REPORT.md")));
    }
    [Fact]
    public void SavedRawExporterByteTamperingIsRejectedWhenArtifactsExist()
    {
        using var fixture = SavedFixture.Create();
        if (fixture == null) return;
        string raw = Directory.EnumerateFiles(Path.Combine(fixture.Directory, "bdn-raw"), "*.json", SearchOption.AllDirectories).First();
        File.AppendAllText(raw, " ");
        Assert.Throws<InvalidDataException>(() => MicroReports.Supplement(fixture.Root, fixture.Directory, false, fixture.Output));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "SUPPLEMENT.md")));
    }
    [Fact]
    public void SavedCampaignValidationOnlyDoesNotCreateOrChangeOutputsWhenArtifactsExist()
    {
        using var fixture = SavedFixture.Create();
        if (fixture == null) return;
        MicroReports.Comparison(fixture.Root, fixture.Directory, false, fixture.Output);
        MicroReports.Supplement(fixture.Root, fixture.Directory, false, fixture.Output);
        Assert.False(Directory.Exists(fixture.Output));
        Directory.CreateDirectory(fixture.Output);
        string sentinel = Path.Combine(fixture.Output, "REPORT.md");
        byte[] original = [0xef, 0xbb, 0xbf, 13, 10, 65, 66, 67];
        File.WriteAllBytes(sentinel, original);
        MicroReports.Comparison(fixture.Root, fixture.Directory, false, fixture.Output);
        MicroReports.Supplement(fixture.Root, fixture.Directory, false, fixture.Output);
        Assert.Equal(original, File.ReadAllBytes(sentinel));
        Assert.Equal(new[] { sentinel }, Directory.EnumerateFiles(fixture.Output).ToArray());
    }
    static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SKECS.Benchmarks/Vendor/manifest.json"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Project root missing");
    }
    sealed class SavedFixture : IDisposable
    {
        public string Root { get; } = FindRoot();
        public string Directory { get; }
        public string Output => Path.Combine(Directory, "published");
        readonly string parent;
        SavedFixture(string source)
        {
            parent = Path.Combine(Root, "artifacts/tooling/tmp/micro-report-tests");
            Directory = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            foreach (string file in System.IO.Directory.EnumerateFiles(source, "*.json", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Directory, Path.GetRelativePath(source, file));
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
        }
        public static SavedFixture? Create()
        {
            string root = FindRoot();
            string source = Path.Combine(root, "artifacts/benchmarks/current/micro");
            if (!File.Exists(Path.Combine(source, "campaign.json"))) source = Path.Combine(root, "docs/benchmarks/current");
            return File.Exists(Path.Combine(source, "campaign.json")) ? new SavedFixture(source) : null;
        }
        public void Dispose()
        {
            string resolved = Path.GetFullPath(Directory);
            if (!resolved.StartsWith(Path.GetFullPath(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture path");
            System.IO.Directory.Delete(resolved, true);
        }
    }
}
