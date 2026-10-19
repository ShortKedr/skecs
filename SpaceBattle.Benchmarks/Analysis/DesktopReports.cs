using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using static SKECS.BenchmarkTools.BattleData;

namespace SKECS.BenchmarkTools;

public static class DesktopReports
{
    private static readonly string[] Cases = ["headless-warm-fast", "headless-warm-paced", "desktop-cold-fast-tiering0", "desktop-cold-fast-default", "desktop-cold-paced-default"];
    public static void Run(string root, string directory, bool render = true, string? outputDirectory = null)
    {
        directory = DirectoryUnderDocs(root, directory);
        var manifest = Load(Path.Combine(directory, "manifest.json"));
        var ticks = checked((int)manifest["Ticks"].L()); Require(manifest["Completed"]!.GetValue<bool>() && ticks >= 1200, "Incomplete desktop campaign");
        var legacy = manifest["SourceHashes"].O().Any(p => p.Key.Replace('\\', '/').StartsWith("SpaceBattle.SECS/", StringComparison.Ordinal));
        var product = legacy ? "SECS" : "SKECS";
        var dlls = new[] { "SpaceBattle.Core.dll", "SpaceBattle." + product + ".dll", product + ".dll" };
        var dllFields = new[] { "CoreDllSha256", legacy ? "SecsAdapterDllSha256" : "SkecsAdapterDllSha256", legacy ? "SecsDllSha256" : "SkecsDllSha256" };
        var compressed = manifest["TickCsvHashes"] != null;
        var expected = Cases.SelectMany(c => new[] { c + ".json", c + (compressed ? ".ticks.csv.gz" : ".ticks.csv") }).ToHashSet();
        Require(expected.SetEquals(manifest["Files"].O().Select(p => p.Key)), "Incomplete desktop input provenance");
        if (compressed) Require(Cases.Select(c => c + ".ticks.csv").ToHashSet().SetEquals(manifest["TickCsvHashes"].O().Select(p => p.Key)), "Incomplete desktop decompressed provenance");
        Hashes(directory, manifest);
        var records = new List<JsonNode>(); var samples = new List<Dictionary<string, List<double>>>();
        JsonNode? config = null, environment = null, gameplay = null; string? checksum = null; var populationTraces = new Dictionary<bool, string>(); string[]? binaries = null; var hostHashes = new Dictionary<string, string>();
        for (var c = 0; c < Cases.Length; c++)
        {
            var name = Cases[c]; var headless = c < 2; var record = Load(Path.Combine(directory, name + ".json")); var settings = headless ? record["Metadata"]!["Options"]! : record["Config"]!;
            if (manifest["Workload"] != null)
            {
                Equal(manifest["Workload"], JsonNode.Parse("{\"Preset\":\"game\",\"Profile\":\"nominal\",\"Seed\":7331,\"ShipsPerTeam\":96,\"GameplayChurnMultiplier\":1}"), "Wrong nominal study workload");
                Equal(settings, manifest["BattleConfig"], name + ": configuration differs from manifest");
                Require(settings["ChurnMultiplier"].L() == 1 && settings["Seed"].L() == 7331 && settings["ShipsPerTeam"].L() == 96, name + ": wrong study workload");
            }
            config ??= settings.DeepClone(); Equal(config, settings, name + ": mixed BattleConfig");
            var final = record[headless ? "Checksum" : "FinalBattleChecksum"].S(); checksum ??= final; Require(final == checksum, name + ": final state divergence");
            string[] currentBinaries; string runtime, machine, hostHash; JsonNode finalCounts;
            if (headless)
            {
                var m = record["Metadata"]!;
                Require(m["Ticks"].L() == ticks && m["Preset"].S() == "game" && m["WarmupTicks"].L() == 600 && m["TieredCompilation"].S() == "0", name + ": wrong headless warmup/tiering/workload");
                Require(m["Realtime"]!.GetValue<bool>() == (c == 1), name + ": wrong pacing");
                currentBinaries = dlls.Select(d => record["Provenance"]!["Binaries"]![d].S()).ToArray();
                runtime = m["Runtime"].S(); machine = m["Machine"].S();
                var hostBinaries = record["Provenance"]!["Binaries"].O();
                var headlessHosts = new[] { "SpaceBattle.Benchmarks.dll", "SpaceBattle.Headless.dll" }.Where(hostBinaries.ContainsKey).ToArray();
                Require(headlessHosts.Length == 1, name + ": expected exactly one measurement host assembly");
                hostHash = hostBinaries[headlessHosts[0]].S();
                finalCounts = new JsonObject(); foreach (var key in Counts) finalCounts[key] = record["Windows"].A()[^1]![key]!.DeepClone();
            }
            else
            {
                Require(record["SampleFrames"].L() == ticks && record["WarmupFrames"].L() == 0 && record["FirstMeasuredTick"].L() == 1 && record["LastMeasuredTick"].L() == ticks, name + ": wrong desktop tick range");
                Require(record["GlErrors"].A().Count == 0 && record["GlErrorOverflow"].L() == 0 && record["ValidationPass"]!.GetValue<bool>(), name + ": desktop GL validation failed");
                Require(record["FrameRateCap"].L() == (c == 4 ? 60 : 0), name + ": wrong desktop pacing");
                Require(c == 2 ? record["TieredCompilation"].S() == "0" : record["TieredCompilation"] == null, name + ": wrong tiering");
                Require(record["StepsPerFrame"].L() == 1 && !record["VSync"]!.GetValue<bool>(), name + ": wrong deterministic replay");
                currentBinaries = dllFields.Select(d => record[d].S()).ToArray(); runtime = record["Runtime"].S(); machine = record["Machine"].S(); hostHash = record["GameDllSha256"].S();
                finalCounts = new JsonObject(); foreach (var key in Counts) finalCounts[key] = record["FinalAliveEntities"]![key]!.DeepClone();
            }
            binaries ??= currentBinaries; Require(currentBinaries.SequenceEqual(binaries, StringComparer.OrdinalIgnoreCase), name + ": loaded assembly mismatch");
            var host = headless
                ? (record["Provenance"]!["Binaries"]!.AsObject().ContainsKey("SpaceBattle.Benchmarks.dll") ? "SpaceBattle.Benchmarks" : "SpaceBattle.Headless")
                : "SpaceBattle.Game";
            if (!hostHashes.TryGetValue(host, out var previousHash)) hostHashes[host] = hostHash; else Require(HashEquals(previousHash, hostHash), name + ": host assembly changed");
            var currentEnvironment = new JsonArray(runtime, machine); environment ??= currentEnvironment; Equal(environment, currentEnvironment, name + ": mixed runtime/host");
            var currentGameplay = new JsonObject { ["Counters"] = record["Counters"]!.DeepClone(), ["Tactics"] = record["Tactics"]!.DeepClone(), ["Counts"] = finalCounts }; gameplay ??= currentGameplay; Equal(gameplay, currentGameplay, name + ": gameplay counter/population divergence");
            var sample = new Dictionary<string, List<double>> { ["hud"] = new List<double>(ticks) }; foreach (var stage in Stages) sample[stage] = new List<double>(ticks);
            var index = 0; using var trace = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var row in Csv(directory, name))
            {
                index++; Require(index <= ticks && row.Integer("tick") == index, name + ": wrong CSV trajectory"); sample["hud"].Add(row.Number(headless ? "hudMs" : "hud_ms")); foreach (var stage in Stages) sample[stage].Add(row.Number(stage));
                var counts = Counts.Select(key => row.Integer(key.ToLowerInvariant())).ToArray(); Require(counts.All(v => v >= 0), name + ": negative population"); trace.AppendData(Encoding.ASCII.GetBytes(string.Join(",", counts) + "\n"));
                if (headless && index == ticks) for (var k = 0; k < Counts.Length; k++) Require(counts[k] == finalCounts[Counts[k]].L(), name + ": final raw census mismatch");
            }
            // Desktop CSV populations come from render snapshots, whose capture is
            // outside Step. Compare like hosts; final simulation census above is shared.
            var currentTrace = Convert.ToHexString(trace.GetHashAndReset());
            if (populationTraces.TryGetValue(headless, out var populationTrace)) Require(populationTrace == currentTrace, name + ": per-tick population divergence"); else populationTraces[headless] = currentTrace;
            Require(index == ticks, name + ": incomplete CSV");
            foreach (var pair in sample) Require(pair.Value.All(v => double.IsFinite(v) && v >= 0), name + ": invalid timing samples");
            var meanKey = headless ? "MeanMs" : "Mean";
            Close(sample["hud"].Average(), record[headless ? "HudTiming" : "HudTickMs"]![meanKey].D(), name + ": HUD mean mismatch");
            Require(record["HudWindows"].A().Count == ticks / 600, name + ": incomplete HUD windows");
            for (var i = 0; i < ticks / 600; i++)
            {
                var w = record["HudWindows"]![i]!; Require(w["StartTick"].L() == i * 600 + 1 && w["EndTick"].L() == Math.Min((i + 1) * 600, ticks), name + ": wrong HUD boundaries");
                Close(sample["hud"].Skip(i * 600).Take(600).Average(), w["Timing"]![meanKey].D(), name + ": HUD window mismatch");
                Require(w["Stages"].A().Select(s => s!["Name"].S()).SequenceEqual(Stages), name + ": missing stages");
                foreach (var stage in w["Stages"].A()) Close(sample[stage!["Name"].S()].Skip(i * 600).Take(600).Average(), stage["Timing"]![meanKey].D(), name + ": stage window mismatch");
            }
            records.Add(record); samples.Add(sample);
        }
        if (manifest["BinaryHashes"] is JsonObject hashes)
            foreach (var pair in hostHashes)
            {
                Require(BinaryMatches(hashes, pair.Key + ".dll", pair.Value, pair.Key), "Host binary differs from manifest");
                for (var i = 0; i < dlls.Length; i++) Require(BinaryMatches(hashes, dlls[i], binaries![i], pair.Key), "Shared binary differs from manifest: " + dlls[i]);
            }
        if (render) Render(root, directory, DirectoryUnderDocs(root, outputDirectory ?? directory), samples, checksum!, product);
    }

    private static double Hud(List<double> samples, int tick) { Require(tick >= 1 && tick <= samples.Count, "HUD tick outside samples"); return samples.Skip(Math.Max(0, tick - 600)).Take(Math.Min(600, tick)).Average(); }
    private static void Render(string root, string dataDirectory, string directory, List<Dictionary<string, List<double>>> samples, string checksum, string product)
    {
        Directory.CreateDirectory(directory);
        var canonical = Path.GetFullPath(directory).Equals(Path.GetFullPath(Path.Combine(root, "docs", "space-battle", "desktop-current")), StringComparison.OrdinalIgnoreCase);
        var docs = canonical ? Path.Combine(root, "docs", "space-battle") : directory;
        var relative = Path.GetRelativePath(docs, directory).Replace('\\', '/');
        foreach (var ru in new[] { false, true })
        {
            string T(string en, string russian) => ru ? russian : en;
            var labels = ru ? new[] { "Headless прогрет", "Headless прогрет / 60 Hz", "Desktop старт / tiering off", "Desktop старт / default", "Desktop старт / default 60 Hz" } : new[] { "Headless warm / fast", "Headless warm / 60 Hz", "Desktop cold / tiering off", "Desktop cold / default", "Desktop cold / default 60 Hz" };
            var lines = new List<string> { ru ? "[English](DESKTOP_TIMING.md) | Русский" : "English | [Русский](DESKTOP_TIMING.ru.md)", "", "# " + T("Why HUD Mean differs from headless", "Почему HUD Mean отличается от headless"), "", T($"Cold Release startup with tiering off: {F(Hud(samples[2]["hud"], 30), 3)} ms HUD Mean at tick 30 and {F(Hud(samples[2]["hud"], 1200), 3)} ms at ticks 601–1200. Default runtime: {F(Hud(samples[3]["hud"], 10), 3)} ms at tick 10 and {F(Hud(samples[3]["hud"], 1200), 3)} ms at ticks 601–1200.", $"Холодный Release с tiering off: {F(Hud(samples[2]["hud"], 30), 3)} ms HUD Mean к тику 30 и {F(Hud(samples[2]["hud"], 1200), 3)} ms в окне 601–1200. Обычный runtime: {F(Hud(samples[3]["hud"], 10), 3)} ms к тику 10 и {F(Hud(samples[3]["hud"], 1200), 3)} ms в окне 601–1200."), "", T($"Five independent Release processes replay the same nominal game battle: seed 7331, 96 escorts and 3 carriers per team, LightSwarm, Tactical, reinforcements enabled. Core, {product} and adapter hashes match; final checksums, counters, tactics and populations match.", $"Пять независимых Release-процессов повторяют один nominal game-бой: seed 7331, 96 эскортов и 3 носителя на сторону, LightSwarm, Tactical, подкрепления включены. Хеши Core, {product} и адаптера совпадают; итоговые checksum, счётчики, тактика и численность совпали."), "", $"Checksum: `{checksum}`.", "", T("Headless warms 600 separate ticks in another battle. Desktop starts without Step warmup, including initial JIT and lazy initialization. Default removes the DOTNET_TieredCompilation override; tiering0 disables tiered compilation. Paced desktop uses a 60 frame/s cap, exactly one Step/frame, VSync off. This is controlled replay; ordinary gameplay accumulator FPS is not measured. Processes run sequentially.", "Headless отдельно прогревает 600 тиков в другом экземпляре боя. Desktop начинается без прогрева Step, включая стартовые JIT и ленивую инициализацию. Default убирает DOTNET_TieredCompilation, tiering0 отключает tiered compilation. Paced desktop ограничен 60 кадрами/с, выполняет один Step на кадр, VSync выключен. Это контролируемый replay; FPS обычного игрового накопителя не измеряется. Процессы запускаются последовательно."), "", T("HUD Mean averages the last min(600, completed ticks) samples. Early 10/30/60-tick samples include startup; after 600 this is a rolling window. Capture, rendering and frame waiting are outside Battle.LastTickMs; their process load can affect the next Step.", "HUD Mean усредняет последние min(600, число тиков) выборок. Первые 10/30/60 тиков включают старт; после 600 это скользящее окно. Capture, рендеринг и ожидание кадра не входят в Battle.LastTickMs; их нагрузка может повлиять на следующий Step."), "", "## " + T("HUD Mean at tick, ms", "Mean HUD в момент тика, ms"), "" };
            Header(lines, ["Tick", .. labels]);
            foreach (var tick in new[] { 10, 30, 60, 120, 600, 1200 }) lines.Add(Table(new[] { tick.ToString() }.Concat(samples.Select(s => F(Hud(s["hud"], tick))))));
            foreach (var begin in new[] { 0, 600 })
            {
                lines.AddRange(["", $"## {T("Stage mean", "Среднее стадий")}: {begin + 1}–{begin + 600}, ms", ""]); Header(lines, ["Stage", .. labels]);
                foreach (var stage in Stages.Append("hud")) lines.Add(Table(new[] { stage == "hud" ? "Total HUD Step" : stage }.Concat(samples.Select(s => F(s[stage].Skip(begin).Take(600).Average())))));
            }
            lines.AddRange(["", T("This is one run per configuration. Meaningful comparison requires matching settings, runtime, tick ranges, warmup and timer scopes. These data do not establish the cause of a sustained value near 4 ms in a different battle phase. RawCsvPath stores a filename without a local user path.", "Это один запуск каждой конфигурации. Для сравнения должны совпадать настройки, runtime, диапазоны тиков, прогрев и границы таймера. Эти данные не устанавливают причину устойчивого значения около 4 ms в другой фазе боя. RawCsvPath содержит имя файла без локального пути пользователя."), "", $"[Full high-profile game run and all five ECS](REPORT{(ru ? ".ru" : "")}.md).", "", T("Raw CSV is retained locally as lossless gzip archives. The local manifest records archive and decompressed SHA256 hashes; analysis reads gzip directly with .NET 8.", "Полные CSV сохранены локально без потерь в gzip. Локальный манифест хранит SHA256 архивов и распакованных CSV; анализатор читает gzip напрямую с .NET 8."), "", string.Empty, "", "```sh", "dotnet run --project SpaceBattle.Benchmarks -- report desktop --data artifacts/benchmarks/current/desktop --output docs/space-battle/desktop-current", "```", ""]);
            lines = lines.Where(line => !line.Contains("[Manifest]", StringComparison.Ordinal) && !line.Contains(".ticks.csv", StringComparison.Ordinal)).ToList();
            lines.Add($"Checksum: `{checksum}`. {T("Local measurement inputs", "Локальные входы измерения")}: `{Path.GetRelativePath(root, dataDirectory).Replace('\\', '/')}`.");
            ShadeTimingTables(lines, root, docs, ru);
            lines.InsertRange(6, [ReportColors.Legend(ru, Path.GetRelativePath(docs, Path.Combine(root, "docs", "benchmarks", "heatmap")).Replace('\\', '/'), Path.GetRelativePath(docs, Path.Combine(root, "docs", "benchmarks", ru ? "COLORS.ru.md" : "COLORS.md")).Replace('\\', '/')), ""]);
            Write(Path.Combine(docs, ru ? "DESKTOP_TIMING.ru.md" : "DESKTOP_TIMING.md"), lines);
        }
        SvgCharts.Bars(Path.Combine(directory, "StartupHud.svg"), "HUD Mean at tick 30", Cases, samples.Select(s => Hud(s["hud"], 30)).ToArray(), "ms");
    }
}
