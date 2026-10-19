using System.Text.Json.Nodes;
using System.Security.Cryptography;
using SKECS.BenchmarkTools;

namespace SKECS.BenchmarkTools.Tests;

public sealed class CompositionReportTests
{
    [Fact]
    public void SyntheticCatalogueRendersEveryNumericSectionInBothLanguages()
    {
        WithFixture((root, input, output) =>
        {
            CompositionReports.Run(root, input, output);
            foreach (var filename in new[] { "COMPOSITION.md", "COMPOSITION.ru.md" })
            {
                var report = File.ReadAllText(Path.Combine(output, filename));
                foreach (var field in new[] { "EntityTicks", "MeanOfPerTickMeans", "LogicalPayloadBytes", "AddsDuringSimulation", "QueryBindings", "EntitySharePercent", "ComponentCount", "NativeComponentCountsVerified", "Transform + Motion" }) Assert.Contains(field, report);
                Assert.DoesNotContain("](low.json)", report); Assert.DoesNotContain("```powershell", report);
            }
        });
    }

    [Theory]
    [InlineData("type")]
    [InlineData("signature")]
    [InlineData("group")]
    public void InconsistentCensusIsRejected(string corruption)
    {
        WithFixture((root, input, output) =>
        {
            var path = Path.Combine(input, "nominal.json"); var record = JsonNode.Parse(File.ReadAllText(path))!;
            if (corruption == "type") record["Components"]![0]!["Final"] = 5;
            if (corruption == "signature") record["Compositions"]![0]!["Components"]![1] = "Transform";
            if (corruption == "group") record["Groups"]![0]!["ComponentTicks"] = 8;
            File.WriteAllText(path, record.ToJsonString()); Assert.Throws<InvalidDataException>(() => CompositionReports.Run(root, input, output, false));
        });
    }

    [Fact]
    public void RawCensusIsCheckedBeyondItsHash()
    {
        WithFixture((root, input, output) =>
        {
            var path = Path.Combine(input, "low.json"); var record = JsonNode.Parse(File.ReadAllText(path))!; var csv = Path.Combine(input, "low.ticks.csv");
            var header = "tick,entities,components,Transform,Motion,UnclassifiedEntities,UnclassifiedComponents,CountWith2\n";
            File.WriteAllText(csv, header + "1,1,2,1,1,1,2,1\n2,1,2,1,1,1,2,1\n"); Save();
            CompositionReports.Run(root, input, output, false);
            File.WriteAllText(csv, header + "1,1,2,1,1,1,2,1\n2,1,3,1,1,1,2,1\n"); Save();
            Assert.Throws<InvalidDataException>(() => CompositionReports.Run(root, input, output, false));
            void Save() { record["TickCsvSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(csv))).ToLowerInvariant(); File.WriteAllText(path, record.ToJsonString()); }
        });
    }

    private static void WithFixture(Action<string, string, string> action)
    {
        var root = Workspace.FindRoot(); var directory = Path.Combine(root, "artifacts", "benchmark-tools-tests", Guid.NewGuid().ToString("N")); var input = Path.Combine(directory, "composition"); var output = Path.Combine(directory, "reports"); Directory.CreateDirectory(input);
        try
        {
            foreach (var profile in new[] { "low", "nominal", "high" })
            {
                var components = new JsonArray(); foreach (var name in new[] { "Transform", "Motion" }) components.Add(new JsonObject { ["Name"] = name, ["LogicalPayloadBytes"] = 8, ["Initial"] = 1, ["Final"] = 1, ["MeanActive"] = 1, ["MinActive"] = 1, ["MaxActive"] = 1, ["EntityTicks"] = 2, ["EntityPresencePercent"] = 100, ["AddsDuringSimulation"] = 0, ["RemovesDuringSimulation"] = 0, ["GetCalls"] = 2, ["HasCalls"] = 0, ["QueryBindings"] = 2 });
                var record = new JsonObject
                {
                    ["SchemaVersion"] = 1, ["Profile"] = profile, ["Ticks"] = 2, ["SimulatedSeconds"] = 2.0 / 60, ["Scope"] = "Synthetic census fixture", ["Checksum"] = "0123456789ABCDEF", ["Config"] = new JsonObject { ["TickRate"] = 60, ["Seed"] = 7331 },
                    ["Summary"] = new JsonObject { ["EntityTicks"] = 2, ["ComponentTicks"] = 4, ["MeanEntities"] = 1, ["MeanComponents"] = 2, ["MeanComponentsPerEntity"] = 2, ["MeanOfPerTickMeans"] = 2, ["MinEntities"] = 1, ["MaxEntities"] = 1, ["MinComponents"] = 2, ["MaxComponents"] = 2, ["ObservedCompositions"] = 1 },
                    ["Components"] = components,
                    ["Groups"] = new JsonArray(new JsonObject { ["Name"] = "Unclassified", ["EntityTicks"] = 2, ["ComponentTicks"] = 4, ["MeanEntities"] = 1, ["MeanComponentsPerEntity"] = 2, ["MinEntities"] = 1, ["MaxEntities"] = 1, ["EntitySharePercent"] = 100 }),
                    ["ComponentCountHistogram"] = new JsonArray(new JsonObject { ["Components"] = 2, ["EntityTicks"] = 2, ["Percent"] = 100 }),
                    ["Compositions"] = new JsonArray(new JsonObject { ["Components"] = new JsonArray("Transform", "Motion"), ["ComponentCount"] = 2, ["EntityTicks"] = 2, ["Percent"] = 100 }),
                    ["Checkpoints"] = new JsonArray(new JsonObject { ["Tick"] = 2, ["Checksum"] = "0123456789ABCDEF", ["NativeComponentCountsVerified"] = true })
                };
                File.WriteAllText(Path.Combine(input, profile + ".json"), record.ToJsonString());
            }
            action(root, input, output);
        }
        finally { Directory.Delete(directory, true); }
    }
}
