using System.Text.Json.Nodes;
using static SKECS.BenchmarkTools.BattleData;

namespace SKECS.BenchmarkTools;

/// <summary>Logical composition diagnostics, separate from performance measurements.</summary>
public static class CompositionReports
{
    private static readonly string[] Profiles = ["low", "nominal", "high"];

    public static void Run(string root, string dataDirectory, string? outputDirectory = null, bool render = true)
    {
        dataDirectory = DirectoryUnderDocs(root, dataDirectory);
        var runs = new List<JsonNode>();
        foreach (var profile in Profiles)
        {
            var run = Load(Path.Combine(dataDirectory, profile + ".json"));
            Validate(root, dataDirectory, profile, run); runs.Add(run);
        }
        if (!render) return;
        var output = DirectoryUnderDocs(root, outputDirectory ?? Path.Combine(root, "docs", "space-battle", "composition"));
        Directory.CreateDirectory(output);
        foreach (var ru in new[] { false, true })
        {
            string T(string en, string russian) => ru ? russian : en;
            var lines = new List<string>
            {
                ru ? "[English](COMPOSITION.md) | Русский" : "English | [Русский](COMPOSITION.ru.md)", "",
                "# " + T("Logical entity composition", "Логический состав сущностей"), "",
                T("These are separate diagnostic replays, not performance measurements. Values describe the recorded workload and physics revision; historical data do not describe current gameplay performance. The observer counts logical gameplay components, excluding adapter bookkeeping.", "Это отдельные диагностические replay, а не измерения производительности. Числа относятся к записанной нагрузке и ревизии физики; исторические данные не описывают производительность текущей игры. Наблюдатель считает логические игровые компоненты без служебных компонентов адаптеров."), "",
                T("An entity-tick means one live entity at the end of one simulation tick. Components per entity is ComponentTicks / EntityTicks, weighted by population and lifetime. MeanOfPerTickMeans is the unweighted mean of each tick's ratio. API calls and structural operations include short-lived objects that can disappear within a tick. Payload bytes exclude storage alignment and ECS/physics overhead.", "Entity-tick — одна живая сущность в конце одного тика. Компонентов на сущность = ComponentTicks / EntityTicks, со взвешиванием по популяции и времени жизни. MeanOfPerTickMeans — невзвешенное среднее отношений каждого тика. API и структурные операции учитывают краткоживущие объекты, исчезающие внутри тика. Payload bytes не включают выравнивание хранилища и накладные расходы ECS/физики."), "",
                T("All recorded numeric census, type, group, histogram and signature fields are reproduced below. Identifiers remain identical across languages. JSON conservation checks are mandatory; available raw CSV and source results are additionally checked against their recorded hashes and aggregates.", "Ниже приведены все записанные числовые поля census, типов, групп, гистограммы и сигнатур. Идентификаторы одинаковы в обоих языках. Проверки сохранения в JSON обязательны; доступные исходные CSV и результаты дополнительно сверяются с записанными хешами и агрегатами."), ""
            };
            for (var index = 0; index < Profiles.Length; index++)
            {
                var run = runs[index]; lines.AddRange(["## " + Profiles[index], "", $"Ticks: {run["Ticks"]!.ToJsonString()}; checksum: `{run["Checksum"].S()}`.", "", run["Scope"]?.GetValue<string>() ?? "", ""]);
                ObjectTable(lines, T("Census summary", "Сводка census"), run["Summary"]!);
                ArrayTable(lines, T("Components and API activity", "Компоненты и активность API"), run["Components"]!);
                ArrayTable(lines, T("Entity groups", "Семейства сущностей"), run["Groups"]!);
                ArrayTable(lines, T("Component-count histogram", "Распределение числа компонентов"), run["ComponentCountHistogram"]!);
                ArrayTable(lines, T("All observed signatures", "Все наблюдаемые составы"), run["Compositions"]!);
                ArrayTable(lines, T("Checkpoint phases", "Фазы checkpoint"), run["Checkpoints"]!);
                foreach (var pair in run.O().Where(p => p.Key.Contains("Phase", StringComparison.OrdinalIgnoreCase) || p.Key.Contains("Window", StringComparison.OrdinalIgnoreCase)))
                    if (pair.Value is JsonArray) ArrayTable(lines, pair.Key, pair.Value); else if (pair.Value != null) ObjectTable(lines, pair.Key, pair.Value);
                ObjectTable(lines, T("Recorded configuration", "Записанная конфигурация"), run["Config"]!);
                ObjectTable(lines, T("Recorded provenance", "Записанное происхождение"), new JsonObject
                {
                    ["SchemaVersion"] = run["SchemaVersion"]!.DeepClone(), ["SimulatedSeconds"] = run["SimulatedSeconds"]?.DeepClone(),
                    ["SourceResult"] = run["SourceResult"]?.DeepClone(), ["SourceSha256"] = run["SourceSha256"]?.DeepClone(),
                    ["TickCsvSha256"] = run["TickCsvSha256"]?.DeepClone(), ["ObserverSha256"] = run["ObserverSha256"]?.DeepClone(), ["BinaryHashes"] = run["BinaryHashes"]?.DeepClone()
                });
            }
            lines.AddRange(["## " + T("Reproduction", "Воспроизведение"), "", $"{T("Local input directory", "Локальная директория входов")}: `{Path.GetRelativePath(root, dataDirectory).Replace('\\', '/')}`.", "", "```sh", $"dotnet run --project tools/BenchmarkTools -- report composition --data \"{Path.GetRelativePath(root, dataDirectory).Replace('\\', '/')}\" --output \"{Path.GetRelativePath(root, output).Replace('\\', '/')}\"", "```"]);
            Write(Path.Combine(output, ru ? "COMPOSITION.ru.md" : "COMPOSITION.md"), lines);
        }
    }

    private static void Validate(string root, string directory, string profile, JsonNode run)
    {
        Require(run["SchemaVersion"].L() == 1 && run["Profile"].S() == profile && run["Ticks"].L() > 0, profile + ": invalid composition schema/profile/ticks");
        var ticks = run["Ticks"].L(); var summary = run["Summary"]!; var entityTicks = summary["EntityTicks"].D(); var componentTicks = summary["ComponentTicks"].D();
        Require(entityTicks > 0 && componentTicks >= 0, profile + ": invalid composition totals");
        NonnegativeNumbers(summary, profile + ".Summary");
        Close(summary["MeanEntities"].D(), entityTicks / ticks, profile + ": MeanEntities"); Close(summary["MeanComponents"].D(), componentTicks / ticks, profile + ": MeanComponents"); Close(summary["MeanComponentsPerEntity"].D(), componentTicks / entityTicks, profile + ": weighted mean");
        var components = run["Components"].A(); var groups = run["Groups"].A();
        Require(components.Count > 0 && groups.Count > 0, profile + ": empty catalogues"); Unique(components, "Name", profile); Unique(groups, "Name", profile);
        var names = components.Select(c => c!["Name"].S()).ToHashSet(StringComparer.Ordinal);
        foreach (var component in components)
        {
            var item = component!; var label = profile + "." + item["Name"].S(); NonnegativeNumbers(item, label);
            Require(item["Initial"].L() + item["AddsDuringSimulation"].L() - item["RemovesDuringSimulation"].L() == item["Final"].L(), label + ": structural conservation mismatch");
            Close(item["MeanActive"].D(), item["EntityTicks"].D() / ticks, label + ": MeanActive"); Close(item["EntityPresencePercent"].D(), 100 * item["EntityTicks"].D() / entityTicks, label + ": presence");
        }
        Close(components.Sum(c => c!["EntityTicks"].D()), componentTicks, profile + ": component totals");
        foreach (var group in groups)
        {
            var item = group!; var label = profile + "." + item["Name"].S(); NonnegativeNumbers(item, label);
            Close(item["MeanEntities"].D(), item["EntityTicks"].D() / ticks, label + ": mean population"); Close(item["EntitySharePercent"].D(), 100 * item["EntityTicks"].D() / entityTicks, label + ": group share");
            if (item["EntityTicks"].D() > 0) Close(item["MeanComponentsPerEntity"].D(), item["ComponentTicks"].D() / item["EntityTicks"].D(), label + ": mean components"); else Require(item["MeanComponentsPerEntity"] == null, label + ": empty group mean must be null");
        }
        Close(groups.Sum(g => g!["EntityTicks"].D()), entityTicks, profile + ": group entity conservation"); Close(groups.Sum(g => g!["ComponentTicks"].D()), componentTicks, profile + ": group component conservation");
        var histogram = run["ComponentCountHistogram"].A(); Unique(histogram, "Components", profile); foreach (var item in histogram) { NonnegativeNumbers(item!, profile); Close(item!["Percent"].D(), item["EntityTicks"].D() * 100 / entityTicks, profile + ": histogram share"); }
        Close(histogram.Sum(h => h!["EntityTicks"].D()), entityTicks, profile + ": histogram entity conservation"); Close(histogram.Sum(h => h!["EntityTicks"].D() * h["Components"].D()), componentTicks, profile + ": histogram component conservation");
        var signatures = run["Compositions"].A(); Require(signatures.Count == summary["ObservedCompositions"].L(), profile + ": signature count mismatch");
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var signature in signatures)
        {
            var item = signature!; NonnegativeNumbers(item, profile); var types = item["Components"].A().Select(n => n.S()).ToArray();
            Require(types.Length == item["ComponentCount"].L() && types.Distinct(StringComparer.Ordinal).Count() == types.Length && types.All(names.Contains), profile + ": invalid signature");
            Require(observed.Add(string.Join("\n", types.Order(StringComparer.Ordinal))), profile + ": duplicate signature"); Close(item["Percent"].D(), item["EntityTicks"].D() * 100 / entityTicks, profile + ": signature share");
        }
        Close(signatures.Sum(s => s!["EntityTicks"].D()), entityTicks, profile + ": signature entity conservation"); Close(signatures.Sum(s => s!["EntityTicks"].D() * s["ComponentCount"].D()), componentTicks, profile + ": signature component conservation");
        foreach (var component in components) Close(signatures.Where(s => s!["Components"].A().Any(n => n.S() == component!["Name"].S())).Sum(s => s!["EntityTicks"].D()), component!["EntityTicks"].D(), profile + ": signature type conservation");
        foreach (var bin in histogram) Close(signatures.Where(s => s!["ComponentCount"].L() == bin!["Components"].L()).Sum(s => s!["EntityTicks"].D()), bin!["EntityTicks"].D(), profile + ": signature histogram conservation");
        var points = run["Checkpoints"].A(); long previousTick = 0;
        foreach (var point in points) { Require(point!["Tick"].L() > previousTick && point["Tick"].L() <= ticks && point["NativeComponentCountsVerified"]!.GetValue<bool>(), profile + ": invalid native census checkpoint"); previousTick = point["Tick"].L(); }
        if (points.Count > 0 && previousTick == ticks) Equal(points[^1]!["Checksum"], run["Checksum"], profile + ": final checkpoint mismatch");
        var csv = Path.Combine(directory, profile + ".ticks.csv");
        if (File.Exists(csv) || File.Exists(csv + ".gz"))
        {
            var path = File.Exists(csv + ".gz") ? csv + ".gz" : csv;
            Require(HashEquals(Hash(path, path.EndsWith(".gz", StringComparison.Ordinal)), run["TickCsvSha256"].S()), profile + ": composition CSV hash mismatch");
            ValidateCsv(directory, profile, run);
        }
        if (run["SourceResult"] != null)
        {
            var source = DirectoryUnderDocs(root, Path.Combine(Directory.GetParent(directory)!.FullName, run["SourceResult"].S()));
            if (File.Exists(source))
            {
                Require(HashEquals(Hash(source), run["SourceSha256"].S()), profile + ": source result hash mismatch"); var original = Load(source);
                Equal(original["Metadata"]!["Options"], run["Config"], profile + ": source configuration mismatch"); Equal(original["Checksum"], run["Checksum"], profile + ": source checksum mismatch");
                foreach (var pair in run["BinaryHashes"].O()) Require(HashEquals(pair.Value.S(), original["Provenance"]!["Binaries"]![pair.Key].S()), profile + ": source binary mismatch");
                Close(components.Sum(c => c!["Initial"].D() + c["AddsDuringSimulation"].D()), original["Counters"]!["StructuralAdds"].D(), profile + ": total additions"); Close(components.Sum(c => c!["RemovesDuringSimulation"].D()), original["Counters"]!["StructuralRemoves"].D(), profile + ": total removals");
            }
        }
    }

    private static void ValidateCsv(string directory, string profile, JsonNode run)
    {
        var sums = new Dictionary<string, double>(); var minima = new Dictionary<string, double>(); var maxima = new Dictionary<string, double>(); Dictionary<string, string>? last = null; double ratios = 0; long tick = 0;
        var components = run["Components"].A(); var groups = run["Groups"].A();
        foreach (var row in Csv(directory, profile))
        {
            tick++; Require(row.Integer("tick") == tick && tick <= run["Ticks"].L(), profile + ": invalid composition trajectory");
            foreach (var pair in row.Where(p => p.Key != "tick")) { var value = row.Integer(pair.Key); Require(value >= 0, profile + ": negative census"); sums[pair.Key] = sums.GetValueOrDefault(pair.Key) + value; minima[pair.Key] = minima.TryGetValue(pair.Key, out var min) ? Math.Min(min, value) : value; maxima[pair.Key] = Math.Max(maxima.GetValueOrDefault(pair.Key), value); }
            Require(row.Integer("entities") > 0, profile + ": empty census");
            Close(components.Sum(c => row.Number(c!["Name"].S())), row.Number("components"), profile + ": per-tick type conservation");
            Close(groups.Sum(g => row.Number(g!["Name"].S() + "Entities")), row.Number("entities"), profile + ": per-tick group entities"); Close(groups.Sum(g => row.Number(g!["Name"].S() + "Components")), row.Number("components"), profile + ": per-tick group components");
            var bins = row.Keys.Where(k => k.StartsWith("CountWith", StringComparison.Ordinal)).ToArray(); Close(bins.Sum(row.Number), row.Number("entities"), profile + ": per-tick histogram entities"); Close(bins.Sum(k => int.Parse(k[9..], System.Globalization.CultureInfo.InvariantCulture) * row.Number(k)), row.Number("components"), profile + ": per-tick histogram components");
            ratios += row.Number("components") / row.Number("entities"); last = row;
        }
        Require(tick == run["Ticks"].L() && last != null, profile + ": incomplete composition CSV");
        var summary = run["Summary"]!; Close(summary["EntityTicks"].D(), sums["entities"], profile); Close(summary["ComponentTicks"].D(), sums["components"], profile); Close(summary["MeanOfPerTickMeans"].D(), ratios / tick, profile);
        foreach (var (key, column) in new[] { ("MinEntities", "entities"), ("MaxEntities", "entities"), ("MinComponents", "components"), ("MaxComponents", "components") }) Close(summary[key].D(), key.StartsWith("Min", StringComparison.Ordinal) ? minima[column] : maxima[column], profile + ": " + key);
        foreach (var component in components) { var item = component!; var name = item["Name"].S(); Close(item["EntityTicks"].D(), sums[name], profile); Close(item["MinActive"].D(), minima[name], profile); Close(item["MaxActive"].D(), maxima[name], profile); Close(item["Final"].D(), last!.Number(name), profile); }
        foreach (var group in groups) { var item = group!; var name = item["Name"].S(); Close(item["EntityTicks"].D(), sums[name + "Entities"], profile); Close(item["ComponentTicks"].D(), sums[name + "Components"], profile); Close(item["MinEntities"].D(), minima[name + "Entities"], profile); Close(item["MaxEntities"].D(), maxima[name + "Entities"], profile); }
        foreach (var bin in run["ComponentCountHistogram"].A()) Close(bin!["EntityTicks"].D(), sums["CountWith" + bin["Components"]!.ToJsonString()], profile + ": raw histogram");
    }

    private static void Unique(JsonArray rows, string key, string label) => Require(rows.Select(r => r![key]!.ToJsonString()).Distinct(StringComparer.Ordinal).Count() == rows.Count, label + ": duplicate " + key);
    private static void NonnegativeNumbers(JsonNode node, string label)
    {
        foreach (var pair in node.O()) if (pair.Value is JsonValue value && value.TryGetValue<double>(out var number)) Require(double.IsFinite(number) && number >= 0, label + ": invalid " + pair.Key);
    }
    private static string Cell(JsonNode? value) => value switch { null => "n/a", JsonArray array => string.Join(" + ", array.Select(Cell)), JsonValue item when item.TryGetValue<string>(out var text) => text.Replace("|", "\\|").Replace("\n", " "), _ => value.ToJsonString() };
    private static void ObjectTable(List<string> lines, string title, JsonNode node)
    {
        lines.AddRange(["### " + title, ""]); Header(lines, "Field", "Value");
        void Add(JsonNode? value, string prefix) { if (value is JsonObject obj) foreach (var pair in obj) Add(pair.Value, prefix.Length == 0 ? pair.Key : prefix + "." + pair.Key); else lines.Add(Table([prefix, Cell(value)])); }
        Add(node, ""); lines.Add("");
    }
    private static void ArrayTable(List<string> lines, string title, JsonNode node)
    {
        lines.AddRange(["### " + title, ""]); var rows = node.A();
        if (rows.Count == 0) { lines.Add("—"); lines.Add(""); return; }
        var columns = rows.SelectMany(r => r!.O().Select(p => p.Key)).Distinct(StringComparer.Ordinal).ToArray(); Header(lines, columns);
        foreach (var row in rows) lines.Add(Table(columns.Select(c => Cell(row![c])))); lines.Add("");
    }
}
