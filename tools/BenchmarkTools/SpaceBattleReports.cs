using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using static SKECS.BenchmarkTools.BattleData;

namespace SKECS.BenchmarkTools;

public static class SpaceBattleReports
{
    public static void Run(string root, string directory, bool verifyTicks = true, bool render = true, string? outputDirectory = null)
    {
        directory = DirectoryUnderDocs(root, directory);
        var manifest = Load(Path.Combine(directory, "manifest.json"));
        Require(manifest["Format"].L() == 3 && manifest["Completed"]!.GetValue<bool>(), "Current campaign requires completed Format 3");
        Require(manifest["Profile"].S() == "high" && manifest["Profiles"] == null && manifest["PrimaryProfile"] == null, "Current campaign must have only the high profile");
        var frameworks = manifest["FrameworkOrder"].A().Select(n => n.S()).ToArray();
        Require(frameworks.SequenceEqual(new[] { "skecs", "lite", "classic", "morpeh", "static" }) || frameworks.SequenceEqual(new[] { "secs", "lite", "classic", "morpeh", "static" }), "Expected exactly five ECS implementations");
        var names = new[] { frameworks[0] == "skecs" ? "SKECS" : "SECS", "LeoECSLite", "LeoECS Classic", "Morpeh", "StaticEcs 2.2.9" };
        Equal(manifest["Workload"], JsonNode.Parse("{\"Preset\":\"game\",\"Seed\":7331,\"ShipsPerTeam\":96,\"GameplayChurnMultiplier\":2}"), "Wrong real-game high workload");
        Require(manifest["Ticks"].L() == Ticks && manifest["WarmupTicks"].L() == 600 && manifest["Preset"].S() == "game", "Wrong campaign tick range/preset");
        Require((IsHash(manifest["SourceRevision"].S(), 40) || manifest["SourceRevision"].S() == "source-tree") && manifest["SourceHashes"].O().Count > 0, "Missing source provenance");
        var compressed = manifest["TickCsvHashes"] != null;
        var expected = new HashSet<string>(StringComparer.Ordinal) { "hardware.json" };
        foreach (var framework in frameworks) { expected.Add("high-" + framework + ".json"); expected.Add("high-" + framework + (compressed ? ".ticks.csv.gz" : ".ticks.csv")); }
        Require(expected.SetEquals(manifest["Files"].O().Select(p => p.Key)), "Current manifest must list exactly five high cases");
        Require(expected.Where(s => s.EndsWith(".json", StringComparison.Ordinal)).ToHashSet().SetEquals(Directory.GetFiles(directory, "*.json").Select(Path.GetFileName).Where(n => n != "manifest.json")!), "Expected exactly five result JSON files and hardware.json");
        var rawExpected = frameworks.Select(f => "high-" + f + ".ticks.csv").ToHashSet();
        if (compressed) Require(rawExpected.SetEquals(manifest["TickCsvHashes"].O().Select(p => p.Key)), "Incomplete decompressed tick provenance");
        var raw = Directory.GetFiles(directory, "*.ticks.csv").Select(Path.GetFileName).ToArray();
        Require(raw.Length == 0 || rawExpected.SetEquals(raw!), "Extra tick CSV files");
        Require(Directory.GetFiles(directory, "*.ticks.csv.gz").Select(Path.GetFileName).All(n => expected.Contains(n!)), "Extra tick archives");
        Hashes(directory, manifest, verifyTicks);
        var hardware = Load(Path.Combine(directory, "hardware.json"));
        var runs = new List<JsonNode>(); JsonNode? condition = null, provenance = null, canonical = null, initialState = null; string? tickTrace = null;
        for (var f = 0; f < frameworks.Length; f++)
        {
            var label = "high-" + frameworks[f]; var run = Load(Path.Combine(directory, label + ".json")); var metadata = run["Metadata"]!;
            Require(metadata["Framework"].S() == names[f], label + ": wrong framework");
            foreach (var (key, value) in new[] { ("Ticks", Ticks), ("WarmupTicks", 600), ("TargetTickRate", 60), ("SimulatedSeconds", 1800) }) Require(metadata[key].L() == value, label + ": wrong " + key);
            Require(metadata["Realtime"]!.GetValue<bool>() == false && metadata["TieredCompilation"].S() == "0" && metadata["Preset"].S() == "game", label + ": wrong runtime/preset");
            Close(metadata["BudgetMs"].D(), Budget, label + ".BudgetMs");
            Require(!metadata.O().Any(p => p.Key.Contains("synthetic", StringComparison.OrdinalIgnoreCase)), label + ": synthetic metadata forbidden");
            var options = metadata["Options"]!;
            foreach (var (key, value) in new[] { ("Seed", 7331), ("ShipsPerTeam", 96), ("CarriersPerTeam", 3), ("FleetComposition", 1), ("Deployment", 1), ("ChurnMultiplier", 2) }) Require(options[key].L() == value, label + ": wrong " + key);
            Require(options["EndlessReinforcements"]!.GetValue<bool>() && !options["ReplenishCarriers"]!.GetValue<bool>(), label + ": wrong game preset");
            foreach (var (key, value) in new[] { ("Revision", "mounted-armament-v7"), ("TacticalRevision", "layered-squads-v3"), ("PhysicsRevision", "circle-kernel-v1"), ("DamageRevision", "layered-weapon-damage-v1") }) Require(metadata["GameplayRules"]![key].S() == value, label + ": stale gameplay rules");
            var currentCondition = Select(metadata, "Machine", "Runtime", "Os", "Options", "GameplayRules", "Collision");
            condition ??= currentCondition; provenance ??= run["Provenance"]!.DeepClone();
            Equal(condition, currentCondition, label + ": mixed campaign conditions"); Equal(provenance, run["Provenance"], label + ": mixed source/binary provenance");
            Require(run["Provenance"]!["Binaries"].O().Count > 0 && run["Provenance"]!["Binaries"].O().All(p => IsHash(p.Value.S(), 64)), label + ": malformed binary provenance");
            foreach (var (source, revision) in new[] { ("Lite", "5053141d7d663c6b1855c8409ac93dfac807b239"), ("Classic", "1213c31c9ef8325d48e74a52b345c4db242d038b"), ("Morpeh", "9f6369384792a01d74df24efd35b08ae313e4f12"), ("StaticEcs", "23b1cf36d89497e8a126d38459d688e2b0a5dea3") }) Require(run["Provenance"]!["Sources"]![source].S() == revision, label + ": unverified framework release " + source);
            Require(IsHash(run["Provenance"]!["Sources"]!["StaticPack"].S(), 40), label + ": malformed source revision");
            Require(metadata["Machine"].S() == hardware["Machine"].S(), label + ": hardware host mismatch");
            if (manifest["BinaryHashes"] is JsonObject binaryHashes) foreach (var pair in run["Provenance"]!["Binaries"].O()) Require(BinaryMatches(binaryHashes, pair.Key, pair.Value.S()), label + ": binary differs from manifest " + pair.Key);
            Summary(run["Timing"]!, Ticks, label); StageSummary(run["Stages"]!, Ticks, label);
            Summary(run["HudTiming"]!, Ticks, label + ".HudTiming");
            var hud = run["HudWindows"].A(); Require(hud.Count == 180, label + ": incomplete HUD windows");
            for (var i = 0; i < hud.Count; i++) { var w = hud[i]!; Bounds(w, i * 600 + 1, (i + 1) * 600, label); Summary(w["Timing"]!, 600, label); StageSummary(w["Stages"]!, 600, label); foreach (var key in Counts) Require(double.IsFinite(w["Mean" + key].D()) && w["Mean" + key].D() >= 0, label + ": invalid HUD count"); }
            Close(hud.Average(w => w!["Timing"]!["MeanMs"].D()), run["HudTiming"]!["MeanMs"].D(), label + ": HUD overall mean");
            for (var i = 0; i < Stages.Length; i++) Close(hud.Average(w => w!["Stages"]![i]!["Timing"]!["MeanMs"].D()), run["Stages"]![i]!["Timing"]!["MeanMs"].D(), label + ": HUD stage mean");
            foreach (var (key, first, last) in new[] { ("First600", 1, 600), ("First2000", 1, 2000), ("RemainderAfter2000", 2001, Ticks) })
            {
                var phase = run["OpeningPhases"]![key]!; Bounds(phase, first, last, label + "." + key); Summary(phase["Timing"]!, last - first + 1, label); StageSummary(phase["Stages"]!, last - first + 1, label);
                if (first == 1) { Summary(phase["HudTiming"]!, last, label); foreach (var count in Counts) Require(double.IsFinite(phase["Mean" + count].D()) && phase["Mean" + count].D() >= 0, label + ": invalid opening population"); }
            }
            var first600 = run["OpeningPhases"]!["First600"]!;
            foreach (var key in Counts) Close(first600["Mean" + key].D(), hud[0]!["Mean" + key].D(), label + ": First600 count");
            Close(first600["HudTiming"]!["MeanMs"].D(), hud[0]!["Timing"]!["MeanMs"].D(), label + ": First600 HUD");
            var initial = run["InitialWindow"]!; Bounds(initial, 1, 2000, label); Summary(initial["Timing"]!, 2000, label); StageSummary(initial["Stages"]!, 2000, label); Checksum(initial["Checksum"].S(), label);
            var earlyState = Select(initial, ["Checksum", "Counters", .. Counts]); initialState ??= earlyState; Equal(initialState, earlyState, label + ": initial state divergence");
            var windows = run["Windows"].A(); var points = run["Checkpoints"].A(); Require(windows.Count == 30 && points.Count == 30, label + ": incomplete windows/checkpoints");
            for (var i = 0; i < 30; i++) { var w = windows[i]!; Bounds(w, i * 3600 + 1, (i + 1) * 3600, label); Require(points[i]!["Tick"].L() == (i + 1) * 3600, label + ": wrong checkpoint"); Checksum(points[i]!["Checksum"].S(), label); Summary(w["Timing"]!, 3600, label); Require(w["Store"]!["Entities"].L() == Counts.Sum(k => w[k].L()), label + ": store census mismatch"); }
            Equal(points[29]!["Checksum"], run["Checksum"], label + ": final checksum mismatch"); Equal(windows[29]!["Counters"], run["Counters"], label + ": final counters mismatch");
            Require(windows.Sum(w => w!["AllocatedBytes"].L()) == run["AllocatedBytes"].L(), label + ": allocations mismatch"); Close(windows.Average(w => w!["Timing"]!["MeanMs"].D()), run["Timing"]!["MeanMs"].D(), label + ": minute mean");
            var state = Select(run, "Checkpoints", "Checksum", "Counters", "Tactics");
            state["Windows"] = new JsonArray(windows.Select(w => (JsonNode?)Select(w!, ["StartTick", "EndTick", "Counters", .. Counts])).ToArray());
            state["HudCounts"] = new JsonArray(hud.Select(w => (JsonNode?)Select(w!, ["StartTick", "EndTick", .. Counts.Select(c => "Mean" + c)])).ToArray());
            canonical ??= state; Equal(canonical, state, label + ": port divergence");
            if (verifyTicks) { var trace = ValidateTicks(directory, label, run); tickTrace ??= trace; Require(tickTrace == trace, label + ": per-tick gameplay trace divergence"); }
            runs.Add(run);
        }
        if (render) Render(root, directory, DirectoryUnderDocs(root, outputDirectory ?? directory), frameworks, names, runs, manifest, hardware, verifyTicks);
    }

    private static JsonObject Select(JsonNode node, params string[] keys) { var result = new JsonObject(); foreach (var key in keys) result[key] = node[key]!.DeepClone(); return result; }
    private static void Bounds(JsonNode node, int start, int end, string label) => Require(node["StartTick"].L() == start && node["EndTick"].L() == end, label + ": noncontiguous tick windows");
    private static void Checksum(string checksum, string label) => Require(checksum.Length == 16 && checksum.All(c => "0123456789ABCDEF".Contains(c)), label + ": malformed checksum");
    private static bool IsHash(string text, int length) => text.Length == length && text.All(Uri.IsHexDigit);
    private static string PeakStore(JsonArray windows, string key)
    {
        var values = windows.Where(w => w!["Store"]![key] != null).Select(w => w!["Store"]![key].D()).ToArray();
        return values.Length == 0 ? "n/a" : F(values.Max() / 1048576, 2);
    }

    private static string ValidateTicks(string directory, string stem, JsonNode run)
    {
        var samples = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var key in new[] { "tickMs", "hudMs" }.Concat(Stages).Concat(Counts.Select(k => k.ToLowerInvariant()))) samples[key] = new List<double>(Ticks);
        var allocation = new List<long>(Ticks); var gc = new List<long[]>(Ticks); using var trace = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var index = 0;
        foreach (var row in Csv(directory, stem))
        {
            index++; Require(index <= Ticks && row.Integer("tick") == index, stem + ": missing/duplicate tick"); Close(row.Number("simulationSeconds"), index / 60.0, stem + ": simulation clock");
            var gameplay = new[] { "ships", "projectiles", "debris", "particles", "turrets", "adds", "removes" }.Select(key => row.Integer(key)).ToArray(); Require(gameplay.All(n => n >= 0), stem + ": negative gameplay value");
            trace.AppendData(Encoding.ASCII.GetBytes(string.Join(",", gameplay) + "\n"));
            foreach (var pair in samples) pair.Value.Add(row.Number(pair.Key));
            allocation.Add(row.Integer("allocatedBytes")); Require(allocation[^1] >= 0, stem + ": negative allocation");
            var currentGc = Enumerable.Range(0, 3).Select(g => row.Integer("gen" + g)).ToArray(); Require(currentGc.All(v => v >= 0) && (gc.Count == 0 || currentGc.Zip(gc[^1]).All(p => p.First >= p.Second)), stem + ": invalid GC sample"); gc.Add(currentGc);
            if (index % 600 == 0)
            {
                var window = run["HudWindows"]![index / 600 - 1]!; Stats(window["Timing"]!, samples["hudMs"], index - 600, 600, stem + ": HUD window"); StageStats(window, samples, index - 600, 600, stem);
                foreach (var key in Counts) Close(window["Mean" + key].D(), samples[key.ToLowerInvariant()].Skip(index - 600).Average(), stem + ": HUD Mean" + key);
            }
            if (index % 3600 == 0)
            {
                var window = run["Windows"]![index / 3600 - 1]!; Stats(window["Timing"]!, samples["tickMs"], index - 3600, 3600, stem + ": minute window");
                Require(allocation.Skip(index - 3600).Sum() == window["AllocatedBytes"].L(), stem + ": window allocations mismatch");
                foreach (var key in Counts) Require(row.Integer(key.ToLowerInvariant()) == window[key].L(), stem + ": entity census mismatch");
                Require(row.Integer("adds") == window["Counters"]!["StructuralAdds"].L() && row.Integer("removes") == window["Counters"]!["StructuralRemoves"].L(), stem + ": counter mismatch");
            }
            if (index == 2000) { foreach (var key in Counts) Require(row.Integer(key.ToLowerInvariant()) == run["InitialWindow"]![key].L(), stem + ": initial census mismatch"); }
        }
        Require(index == Ticks, stem + ": incomplete CSV"); Stats(run["Timing"]!, samples["tickMs"], 0, Ticks, stem); Stats(run["HudTiming"]!, samples["hudMs"], 0, Ticks, stem); StageStats(run, samples, 0, Ticks, stem);
        Require(allocation.Sum() == run["AllocatedBytes"].L(), stem + ": total allocations mismatch");
        Stats(run["InitialWindow"]!["Timing"]!, samples["tickMs"], 0, 2000, stem); StageStats(run["InitialWindow"]!, samples, 0, 2000, stem);
        foreach (var (key, begin, count) in new[] { ("First600", 0, 600), ("First2000", 0, 2000), ("RemainderAfter2000", 2000, Ticks - 2000) })
        {
            var phase = run["OpeningPhases"]![key]!; Stats(phase["Timing"]!, samples["tickMs"], begin, count, stem + ": " + key); StageStats(phase, samples, begin, count, stem);
            if (begin == 0) { Stats(phase["HudTiming"]!, samples["hudMs"], begin, count, stem); foreach (var entity in Counts) Close(phase["Mean" + entity].D(), samples[entity.ToLowerInvariant()].Take(count).Average(), stem + ": opening mean"); }
        }
        for (var i = 0; i < 30; i++)
        {
            var end = (i + 1) * 3600 - 1; var checkpoint = run["Windows"]![i]!["Gc"].A(); Require(checkpoint.Count == 3, stem + ": invalid GC checkpoint");
            for (var g = 0; g < 3; g++) { var upper = end + 1 < Ticks ? gc[end + 1][g] : gc[0][g] + run["GcCollections"]![g].L(); Require(gc[end][g] <= checkpoint[g].L() && checkpoint[g].L() <= upper, stem + ": GC checkpoint outside sample interval"); }
        }
        return Convert.ToHexString(trace.GetHashAndReset());
    }
    private static void StageStats(JsonNode node, Dictionary<string, List<double>> samples, int begin, int count, string label) { foreach (var stage in node["Stages"].A()) Stats(stage!["Timing"]!, samples[stage["Name"].S()], begin, count, label + ": " + stage["Name"].S()); }

    private static void Render(string root, string dataDirectory, string directory, string[] frameworks, string[] names, List<JsonNode> runs, JsonNode manifest, JsonNode hardware, bool verified)
    {
        Directory.CreateDirectory(directory);
        var canonical = Path.GetFullPath(directory).Equals(Path.GetFullPath(Path.Combine(root, "docs", "space-battle", "current")), StringComparison.OrdinalIgnoreCase);
        var docs = canonical ? Path.Combine(root, "docs", "space-battle") : directory;
        var relative = Path.GetRelativePath(docs, directory).Replace('\\', '/');
        var csv = new List<string> { "profile,framework,opening600MeanMs,opening2000MeanMs,remainderMeanMs,meanMs,p95Ms,p99Ms,maxMs,budgetMisses,managedBytesPerTick,totalTickSeconds,checksum" };
        for (var f = 0; f < runs.Count; f++) { var r = runs[f]; csv.Add(string.Join(",", new[] { "high", frameworks[f] }.Concat(new[] { r["OpeningPhases"]!["First600"]!["Timing"]!["MeanMs"].D(), r["OpeningPhases"]!["First2000"]!["Timing"]!["MeanMs"].D(), r["OpeningPhases"]!["RemainderAfter2000"]!["Timing"]!["MeanMs"].D(), r["Timing"]!["MeanMs"].D(), r["Timing"]!["P95Ms"].D(), r["Timing"]!["P99Ms"].D(), r["Timing"]!["MaxMs"].D(), r["Timing"]!["BudgetMisses"].D(), r["AllocatedBytes"].D() / Ticks, r["Timing"]!["MeanMs"].D() * Ticks / 1000 }.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))).Append(r["Checksum"].S()))); }
        Write(Path.Combine(directory, "summary.csv"), csv);
        foreach (var metric in new[] { "MeanMs", "P99Ms" }) SvgCharts.Bars(Path.Combine(directory, metric + ".svg"), "SpaceBattle high: " + metric, names, runs.Select(r => r["Timing"]![metric].D()).ToArray(), "ms");
        var openingLabels = new List<string>(); var openingValues = new List<double>();
        for (var f = 0; f < 5; f++) foreach (var phase in new[] { "First600", "First2000", "RemainderAfter2000", "Full" }) { openingLabels.Add(names[f] + " " + phase); openingValues.Add(phase == "Full" ? runs[f]["Timing"]!["MeanMs"].D() : runs[f]["OpeningPhases"]![phase]!["Timing"]!["MeanMs"].D()); }
        SvgCharts.Bars(Path.Combine(directory, "InitialWindows.svg"), "Opening windows, remainder and full run", openingLabels, openingValues, "ms");
        var worst = Enumerable.Range(0, 180).MaxBy(i => runs.Max(r => r["HudWindows"]![i]!["Timing"]!["MeanMs"].D()));
        foreach (var ru in new[] { false, true })
        {
            string T(string en, string russian) => ru ? russian : en;
            var lines = new List<string> { ru ? "[English](REPORT.md) | Русский" : "English | [Русский](REPORT.ru.md)", "", "# " + T("Space Battle benchmarks — real-game high", "Бенчмарки Space Battle — игровой high"), "", $"Preset: `game`; profile: `high`; {manifest["StartedAt"].S()} — {manifest["FinishedAt"].S()}.", "", T("Five ECS implementations, 108,000 measured ticks each and 600 separate warmup ticks. Seed 7331, 96 escorts and 3 carriers per team; gameplay churn multiplier 2; endless reinforcements enabled.", "Пять ECS, по 108 000 измеренных тиков и 600 отдельных тиков прогрева. Seed 7331, 96 эскортов и 3 носителя на сторону; ChurnMultiplier 2; бесконечные подкрепления включены."), "", "## " + T("Validation and method", "Проверка и методика"), "", verified ? T("Validation passed: 540,000 raw ticks, archive/decompressed/input hashes, provenance, timings, stages, allocations, GC intervals, counters, census and per-tick gameplay parity.", "Проверка пройдена: 540 000 исходных тиков, хеши архивов/CSV/входов, происхождение, время, стадии, аллокации, интервалы GC, счётчики, census и потиковая игровая трасса.") : T("Published JSON and input/archive hashes validated. Raw tick samples were not verified in this run.", "Опубликованные JSON и хеши входов/архивов проверены. Потиковые выборки в этом запуске не проверялись."), "", T("Outer tick includes gameplay, physics and ECS commits; HUD and stage timers cover narrower scopes. Rendering, snapshots and report generation are excluded. One run per ECS describes this machine and does not establish a stable ranking or confidence interval. Managed allocation excludes native ECS storage.", "Внешний тик включает игровые системы, физику и ECS commit; HUD и стадии имеют более узкие границы. Рендеринг, snapshots и отчёт исключены. Один прогон описывает эту машину и не устанавливает устойчивый рейтинг или доверительный интервал. Managed allocation не включает нативное хранилище ECS."), "", "## " + T("Framework revisions used", "Ревизии фреймворков"), "" };
            Header(lines, "ECS", T("Source revision", "Ревизия"), T("Version", "Версия"));
            var pins = runs[0]["Provenance"]!["Sources"]!;
            var revisions = new[] { manifest["SourceRevision"].S(), pins["Lite"].S(), pins["Classic"].S(), pins["Morpeh"].S(), pins["StaticEcs"].S() };
            var versions = new[] { frameworks[0] == "skecs" ? "0.2.0" : "0.1.0", "2024.5.22", "2023.6.22", "2024.1.1", "2.2.9" };
            for (var f = 0; f < 5; f++) lines.Add(Table([names[f], "`" + revisions[f] + "`", versions[f]]));
            lines.AddRange(["", $"StaticPack: `{pins["StaticPack"].S()}`.", "", "## " + T("Tick time", "Время тика"), ""]);
            Header(lines, "ECS", "Mean ms", "P95 ms", "P99 ms", "Max ms", ">16.67 ms", "Managed B/tick", "Total outer s");
            for (var f = 0; f < 5; f++) { var r = runs[f]; lines.Add(Table(new[] { names[f] }.Concat(new[] { "MeanMs", "P95Ms", "P99Ms", "MaxMs" }.Select(k => F(r["Timing"]![k].D()))).Concat([r["Timing"]!["BudgetMisses"].L().ToString(), F(r["AllocatedBytes"].D() / Ticks, 1), F(r["Timing"]!["MeanMs"].D() * Ticks / 1000, 6)]))); }
            lines.AddRange(["", $"![Mean tick time]({relative}/MeanMs.svg)", "", $"![P99 tick time]({relative}/P99Ms.svg)", "", "## " + T("Opening windows", "Окна начала прогона"), "", T("The first 600 and 2,000 windows are cumulative; the remainder is disjoint.", "Окна 1–600 и 1–2000 накопительные; остаток с ними не пересекается."), ""]);
            Header(lines, "ECS", "1–600 ms", "1–2000 ms", "2001–108000 ms", "Full ms");
            for (var f = 0; f < 5; f++) lines.Add(Table(new[] { names[f] }.Concat(new[] { "First600", "First2000", "RemainderAfter2000" }.Select(k => F(runs[f]["OpeningPhases"]![k]!["Timing"]!["MeanMs"].D()))).Append(F(runs[f]["Timing"]!["MeanMs"].D()))));
            lines.AddRange(["", $"![Opening windows]({relative}/InitialWindows.svg)", "", $"[Summary CSV]({relative}/summary.csv)", "", "## " + T("Full-run stage means", "Средние стадий за полный прогон"), ""]);
            Header(lines, ["Stage", .. names]);
            for (var stage = 0; stage < Stages.Length; stage++) lines.Add(Table(new[] { Stages[stage] }.Concat(runs.Select(r => F(r["Stages"]![stage]!["Timing"]!["MeanMs"].D())))));
            lines.Add(Table(new[] { "Battle.LastTickMs" }.Concat(runs.Select(r => F(r["HudTiming"]!["MeanMs"].D())))));
            lines.Add(Table(new[] { "Outer tick" }.Concat(runs.Select(r => F(r["Timing"]!["MeanMs"].D())))));
            lines.AddRange(["", "### " + names[0] + T(" opening stages", ": стадии начала"), ""]); Header(lines, "Stage", "1–600 ms", "1–2000 ms", "2001–108000 ms", "Full ms");
            for (var stage = 0; stage < Stages.Length; stage++) lines.Add(Table(new[] { Stages[stage] }.Concat(new[] { "First600", "First2000", "RemainderAfter2000" }.Select(k => F(runs[0]["OpeningPhases"]![k]!["Stages"]![stage]!["Timing"]!["MeanMs"].D()))).Append(F(runs[0]["Stages"]![stage]!["Timing"]!["MeanMs"].D()))));
            lines.AddRange(["", "## " + T("HUD windows: first, second and common worst", "Окна HUD: первое, второе и общее худшее"), "", T("The common worst window has the largest HUD mean among all five ports; each ECS uses that same tick range. Population means match across ports.", "Общее худшее окно выбрано по максимальному HUD Mean среди пяти портов. Границы общие для всех ECS. Средняя численность совпадает."), ""]);
            foreach (var (phase, index) in new[] { (T("First", "Первое"), 0), (T("Second", "Второе"), 1), (T("Worst", "Худшее"), worst) })
            {
                var w = runs[0]["HudWindows"]![index]!; lines.AddRange([$"### {phase}: {w["StartTick"]!.ToJsonString()}–{w["EndTick"]!.ToJsonString()}", ""]); Header(lines, ["HUD / stage ms", .. names]);
                lines.Add(Table(new[] { "HUD total" }.Concat(runs.Select(r => F(r["HudWindows"]![index]!["Timing"]!["MeanMs"].D())))));
                for (var stage = 0; stage < Stages.Length; stage++) lines.Add(Table(new[] { Stages[stage] }.Concat(runs.Select(r => F(r["HudWindows"]![index]!["Stages"]![stage]!["Timing"]!["MeanMs"].D())))));
                lines.Add(""); Header(lines, Counts); lines.Add(Table(Counts.Select(k => F(w["Mean" + k].D(), 2)))); lines.Add("");
            }
            lines.AddRange(["## " + T("Gameplay parity, census and tactical counters", "Игровое совпадение, census и тактические счётчики"), "", $"Checksum: `{runs[0]["Checksum"].S()}`.", ""]);
            Header(lines, "Counter", "Value"); foreach (var pair in runs[0]["Counters"].O().Concat(runs[0]["Tactics"].O())) lines.Add(Table([pair.Key, pair.Value!.ToJsonString()]));
            lines.AddRange(["", "## " + T("Memory and store census", "Память и census хранилища"), "", T("Peak samples across 30 minute checkpoints; store entities include gameplay entities only. Process memory includes harness buffers.", "Пики по 30 минутным checkpoint; census содержит только игровые сущности. Память процесса включает буферы стенда."), ""]);
            Header(lines, "ECS", "Managed heap MiB", "Working set MiB", "Private MiB", "Native MiB", "Accessor MiB", "Entities final", "Archetypes final", "Chunks final");
            for (var f = 0; f < 5; f++) { var windows = runs[f]["Windows"].A(); var last = windows[29]!; lines.Add(Table(new[] { names[f] }.Concat(new[] { "ManagedHeapBytes", "WorkingSetBytes", "PrivateBytes" }.Select(k => F(windows.Max(w => w![k].D()) / 1048576, 2))).Concat(new[] { "NativeBytes", "AccessorBytes" }.Select(k => PeakStore(windows, k))).Concat(new[] { "Entities", "Archetypes", "Chunks" }.Select(k => last["Store"]![k]?.ToJsonString() ?? "n/a")))); }
            lines.AddRange(["", "### " + T("Minute population census", "Поминутная численность"), ""]); Header(lines, ["Tick", .. Counts]); foreach (var window in runs[0]["Windows"].A()) lines.Add(Table(new[] { window!["EndTick"]!.ToJsonString() }.Concat(Counts.Select(k => window[k]!.ToJsonString()))));
            lines.AddRange(["", "## " + T("Data provenance and reproduction", "Происхождение и воспроизведение"), "", $"Runtime: {runs[0]["Metadata"]!["Runtime"].S()}; OS: {runs[0]["Metadata"]!["Os"].S()}; tiered compilation `0`.", "", $"Base commit: `{manifest["SourceRevision"].S()}`. Source and loaded DLL hashes describe the measured working tree.", "", $"RAM: {F((hardware["TotalPhysicalMemoryBytes"] ?? hardware["AvailableMemoryBytes"]).D() / 1073741824, 2)} GiB.", "", T("Local lossless gzip archives retain the full tick CSV; archive and decompressed SHA256 hashes are recorded in the local manifest. Analysis reads gzip directly and requires only .NET 8.", "Полные CSV сохранены локально без потерь в gzip; SHA256 архива и распакованного файла записаны в локальном манифесте. Анализатор читает gzip напрямую и требует только .NET 8."), "", string.Empty, "", "```sh", "dotnet run --project tools/BenchmarkTools -- report space-battle --data artifacts/benchmarks/current/space-battle --output docs/space-battle/current", "```", "", $"[Desktop timing](DESKTOP_TIMING{(ru ? ".ru" : "")}.md) · [Architecture](ARCHITECTURE{(ru ? ".ru" : "")}.md) · [Historical finite battle](REPORT_2026-10-14_FINITE{(ru ? ".ru" : "")}.md)", ""]);
            lines = lines.Where(line => !line.Contains("[Manifest]", StringComparison.Ordinal) && !line.Contains("[hardware]", StringComparison.Ordinal) && !line.Contains(".ticks.csv", StringComparison.Ordinal) && !line.StartsWith("RAM:", StringComparison.Ordinal)).ToList();
            lines.Add($"{T("Local measurement inputs", "Локальные входы измерения")}: `{Path.GetRelativePath(root, dataDirectory).Replace('\\', '/')}`.");
            lines.Add($"{T("Base commit", "Базовый commit")}: `{manifest["SourceRevision"].S()}`.");
            lines.Add(hardware["TotalPhysicalMemoryBytes"] != null ? $"RAM: {F((hardware["TotalPhysicalMemoryBytes"] ?? hardware["AvailableMemoryBytes"]).D() / 1073741824, 2)} GiB." : $"{hardware["MemoryKind"]?.GetValue<string>() ?? "GC total available memory limit"}: {F(hardware["AvailableMemoryBytes"].D() / 1073741824, 2)} GiB.");
            var cpus = hardware["Cpu"] is JsonArray cpuArray ? cpuArray : new JsonArray(hardware["Cpu"]?.DeepClone());
            lines.Add("CPU: " + string.Join(", ", cpus.Select(cpu => cpu?["Name"]?.GetValue<string>() ?? "unknown")) + ".");
            ShadeTimingTables(lines, root, docs, ru);
            lines.InsertRange(8, [ReportColors.Legend(ru, Path.GetRelativePath(docs, Path.Combine(root, "docs", "benchmarks", "heatmap")).Replace('\\', '/'), Path.GetRelativePath(docs, Path.Combine(root, "docs", "benchmarks", ru ? "COLORS.ru.md" : "COLORS.md")).Replace('\\', '/')), ""]);
            Write(Path.Combine(docs, ru ? "REPORT.ru.md" : "REPORT.md"), lines);
        }
    }
}
