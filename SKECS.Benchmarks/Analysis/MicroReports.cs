using System.Globalization;
using System.Text;
using System.Text.Json;
using static SKECS.BenchmarkTools.MicroValidation;

namespace SKECS.BenchmarkTools;

public static class MicroReports
{
    static string RecordedKey(JsonElement hashes, params string[] candidates)
        => candidates.FirstOrDefault(candidate => hashes.TryGetProperty(candidate, out _))
            ?? throw new InvalidDataException("Missing recorded provenance: " + string.Join(" or ", candidates));
    static string MeasurementKey(JsonElement manifest) => RecordedKey(manifest.GetProperty("assemblyHashes"),
        "SKECS.Benchmarks/bin/Release/net8.0/SKECS.Benchmarks.dll", "Benchmarks.Local/bin/Release/net8.0/Comparison.dll");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string Link(string directory, string root, string target) => Path.GetRelativePath(directory, Path.Combine(root, target)).Replace('\\', '/');
    static string Table(IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
    {
        var h = headers.ToArray();
        return string.Join("\n", new[] { "| " + string.Join(" | ", h) + " |", "| " + string.Join(" | ", h.Select(_ => "---")) + " |" }.Concat(rows.Select(r => "| " + string.Join(" | ", r) + " |")));
    }
    static string Count(int n) => n.ToString("N0", Inv);
    static string Ms(double ns) => (ns / 1e6).ToString("F6", Inv);
    static string Cell(double ns)
    {
        double ms = ns / 1e6;
        return ms.ToString(ms < .01 ? "F6" : ms < 1 ? "F4" : "F3", Inv);
    }
    static void Write(string directory, IReadOnlyDictionary<string, string> outputs)
    {
        foreach (var output in outputs) File.WriteAllText(Path.Combine(directory, output.Key), output.Value, new UTF8Encoding(false));
    }
    static string MatrixTable(Dictionary<(string Framework, string Scenario, int Count), JsonElement> data, bool ru, string assets)
    {
        var rows = data.Keys.Select(k => (k.Scenario, k.Count)).Distinct().OrderBy(k => k.Count).ThenBy(k => k.Scenario, StringComparer.Ordinal)
            .Select(k => new[] { k.Scenario, Count(k.Count) }.Concat(ReportColors.Shade(Frameworks.Select(f => N(data[(f, k.Scenario, k.Count)], "meanNs")).ToArray(), Cell, ru, assets)));
        return Table(new[] { "Scenario", "N" }.Concat(Frameworks), rows);
    }
    public static void Comparison(string root, string directory, bool render = true, string? outputDirectory = null)
    {
        root = Path.GetFullPath(root); directory = OutputDirectory(root, directory);
        string output = outputDirectory == null ? directory : OutputDirectory(root, outputDirectory);
        var campaign = Load(Path.Combine(directory, "campaign.json")); Manifest(campaign);
        VerifyHash(Path.Combine(directory, "base.json"), S(campaign, "baseInputSha256"));
        VerifyHash(Path.Combine(directory, "stress.json"), S(campaign, "stressInputSha256"));
        var baseData = Load(Path.Combine(directory, "base.json")); var stressData = Load(Path.Combine(directory, "stress.json"));
        var basis = Matrix(baseData, false); var stress = Matrix(stressData, true);
        var bm = baseData.GetProperty("metadata"); var sm = stressData.GetProperty("metadata");
        Require(I(bm, "sampleCount") == 20 && I(sm, "sampleCount") == 8, "Metadata sample count mismatch");
        Require(I(sm, "layouts") == 256 && I(sm, "payloadBytes") == 256 && I(sm, "hotStride") == 100 && I(sm, "mixedFrames") == 120, "Stress workload scope mismatch");
        Require(S(bm.GetProperty("revisions"), "SKECS") == S(campaign, "sourceRevision") && S(sm.GetProperty("versions"), "SKECS") == S(campaign, "sourceRevision"), "Mixed campaign source revisions");
        Require(S(bm, "runtime") == S(sm, "runtime"), "Mixed runtimes");
        var identities = baseData.GetProperty("results").EnumerateArray().Concat(stressData.GetProperty("results").EnumerateArray()).Select(r => S(r, "framework")).Where(f => f is "SECS" or "SKECS").Distinct().ToArray();
        Require(identities.Length == 1, "Campaign must measure one SECS/SKECS identity");
        var matrix = campaign.GetProperty("matrix"); Require(I(matrix, "base") == 40 && I(matrix, "stress") == 70 && I(matrix, "frameworks") == 5, "Campaign matrix mismatch");
        var assemblyHashes = campaign.GetProperty("assemblyHashes");
        // Older combined campaigns also include the BDN SKECS.Benchmarks assembly.
        // Select the host named by the recorded comparison metadata, never by today's layout.
        string measurement = assemblyHashes.TryGetProperty("Benchmarks.Local/bin/Release/net8.0/Comparison.dll", out _)
            ? "Benchmarks.Local/bin/Release/net8.0/Comparison.dll" : MeasurementKey(campaign);
        Require(S(campaign, "measurementAssemblySha256").Equals(S(assemblyHashes, measurement), StringComparison.OrdinalIgnoreCase), "Comparison assembly provenance mismatch");
        Require(S(campaign, "skecsAssemblySha256").Equals(S(assemblyHashes, Path.GetDirectoryName(measurement)!.Replace('\\', '/') + "/SKECS.dll"), StringComparison.OrdinalIgnoreCase), "SKECS assembly provenance mismatch");
        string vendorPath = Path.Combine(root, "SKECS.Benchmarks/Vendor/manifest.json");
        VerifyHash(vendorPath, S(campaign.GetProperty("sourceHashes"), RecordedKey(campaign.GetProperty("sourceHashes"), "SKECS.Benchmarks/Vendor/manifest.json", "Benchmarks.Local/vendor-manifest.json")));
        var vendor = Load(vendorPath).GetProperty("components").EnumerateArray().ToDictionary(c => S(c, "name"), c => S(c, "revision"));
        foreach (var pair in new Dictionary<string, string> { ["classic"] = "LeoECS Classic", ["lite"] = "LeoECS Lite", ["morpeh"] = "Morpeh", ["staticEcs"] = "StaticEcs", ["staticPack"] = "StaticPack" })
        {
            Require(S(bm.GetProperty("revisions"), pair.Key) == vendor[pair.Value] && S(sm.GetProperty("revisions"), pair.Key) == vendor[pair.Value], "Framework revision differs from measured vendor manifest: " + pair.Key);
        }
        if (!render) return;
        string assets = Link(output, root, "docs/benchmarks/heatmap");
        string pinTable = Table(["Framework", "Source revision"], Frameworks.Select(f => new[] { f, "`" + (f == "SKECS" ? S(campaign, "sourceRevision") : vendor[f]) + "`" }));
        string Render(bool ru)
        {
            string overview = Link(output, root, ru ? "SKECS.Benchmarks/README.ru.md" : "SKECS.Benchmarks/README.md");
            string harness = Link(output, root, ru ? "SKECS.Benchmarks/README.ru.md" : "SKECS.Benchmarks/README.md");
            string artifact = Path.GetRelativePath(root, directory).Replace('\\', '/');
            string date = S(campaign, "started")[..10];
            string environment = ru
                ? $"{S(bm, "runtime")}, {S(bm, "architecture")}, {S(campaign, "cpu")}, {I(campaign, "logicalProcessors")} логических процессоров. Release; один поток; tiered compilation отключён. `{S(campaign, "sourceRevision")}`; рабочее дерево изменено: `{campaign.GetProperty("workingTreeModified")}`."
                : $"{S(bm, "runtime")}, {S(bm, "architecture")}, {S(campaign, "cpu")}, {I(campaign, "logicalProcessors")} logical processors. Release; one thread; tiered compilation off. `{S(campaign, "sourceRevision")}`; working tree modified: `{campaign.GetProperty("workingTreeModified")}`.";
            var parts = new List<string>
            {
                ru ? "[English](REPORT.md) | Русский" : "English | [Русский](REPORT.ru.md)",
                ru ? "# Сравнение ECS — " + date : "# ECS comparison — " + date,
                ru ? $"[Обзор бенчмарков]({overview}) | [Раннер и воспроизведение]({harness})" : $"[Benchmark overview]({overview}) | [Harness and reproduction]({harness})",
                ru ? "[Полные наборы SKECS и замеры API](SUPPLEMENT.ru.md): 34 актуальных и 51 legacy-кейс BenchmarkDotNet, создание/аксессоры и диагностика сохранённого хранилища." : "[Complete SKECS suites and API measurements](SUPPLEMENT.md) include 34 current and 51 legacy BenchmarkDotNet cases, creation/accessor and retained-storage diagnostics.",
                ru ? "Все пять фреймворков измерены заново в одной последовательной кампании: 40 базовых и 70 стресс-кейсов. Ячейки — среднее время полной операции в миллисекундах; меньше лучше. Цвет сравнивает только одинаковые сценарии и число сущностей, показывает величину эффекта, а не статистическую значимость." : "All five frameworks were measured afresh in one sequential campaign: 40 base cases and 70 stress cases. Cells are mean milliseconds per complete operation; lower is better. Colour compares only the same scenario and entity count, and describes effect size, not statistical significance.",
                (ru ? "Среда: " : "Environment: ") + environment,
                ru ? $"Локальные артефакты `{artifact}`: `campaign.json` фиксирует измеренную ревизию и хеши; `base.json` и `stress.json` сохраняют все наблюдения. Хеши исходников и сборок описывают измеренный снимок." : $"Local artifacts in `{artifact}`: `campaign.json` pins the measured revision and hashes; `base.json` and `stress.json` retain every observation. Source and assembly hashes describe the measured snapshot.",
                ru ? "## Ревизии фреймворков в замерах" : "## Framework revisions used",
                ru ? "Для сторонних ECS указаны точные upstream-коммиты из проверенного по хешу `SKECS.Benchmarks/Vendor/manifest.json`, а не предполагаемые номера релизов. Строка SKECS указывает ревизию кампании." : "Third-party rows show exact upstream commits from the hash-verified `SKECS.Benchmarks/Vendor/manifest.json`, rather than guessed release numbers. The SKECS row records the campaign repository revision.",
                $"StaticEcs **{S(sm.GetProperty("versions"), "staticEcs")}**; StaticPack **{S(sm.GetProperty("versions"), "staticPack")}** (`{vendor["StaticPack"]}`). " + (ru ? "Манифест содержит SHA-256 каждого включённого upstream-файла." : "The manifest records SHA-256 for every included upstream source file."),
                pinTable,
                ru ? "## Измеренные средние" : "## Recorded means",
                MatrixTable(basis, ru, assets), MatrixTable(stress, ru, assets),
                ReportColors.Legend(ru, assets, Link(output, root, ru ? "docs/benchmarks/COLORS.ru.md" : "docs/benchmarks/COLORS.md")),
                ru ? "## Графики и интерпретация" : "## Charts and interpretation",
                "![Base comparison](comparison.svg)\n\n![Stress at 100,000 entities](stress-100k.svg)\n\n![Stress at 1,000,000 entities](stress-1m.svg)",
                ru ? "Погрешности показывают стандартное отклонение выборки, а не доверительный интервал. База сохраняет 20 замеров, стресс — 8. Подготовка, проверки, освобождение и принудительный GC исключены; естественные сборки и Morpeh Commit внутри операций включены. Create/Destroy используют свежие миры; остальные нагрузки — прогретое хранилище. MixedFrames — пакет 120 кадров, а не один кадр. Аллокации — управляемые выделения текущего потока, без нативной памяти." : "Error bars show sample standard deviation, not confidence intervals. Base timings retain 20 samples; stress retains 8. Setup, validation, disposal and forced GC are excluded; natural collections and Morpeh commits during operations are included. Create/Destroy use fresh worlds; other workloads reuse warmed storage. MixedFrames is a complete 120-frame batch, not one frame. Allocation fields measure managed thread allocations and exclude native memory.",
                ru ? "Перед таймингом базовая проверка прошла для пяти фреймворков при 1,000/100,000 сущностей. Стресс-проверка прошла при 257/4,097 сущностей: семь сценариев по три повтора, round-trip payload и устаревшие handles. Каждый стресс-кейс прошёл итоговую проверку всей популяции. Перед генерацией проверены точные матрицы, хеши входных данных, число замеров, среднее, стандартное отклонение и аллокации." : "Base validation passed for all five frameworks at 1,000/100,000 entities before timing. Stress validation passed at 257/4,097 entities: seven scenarios repeated three times, payload round-trip and retired handles. Each measured stress case passed final full-population validation. Exact matrices, input hashes, sample counts, means, standard deviations and allocations were checked before report generation.",
                ru ? "Это эквивалентные логические операции через выбранные адаптеры, а не одинаковое хранилище или лучшие возможные API. Результаты относятся к этой машине, runtime, снимкам и нагрузке. Они не устанавливают универсальный рейтинг ECS, Unity/IL2CPP-производительность, эффективность полной памяти или межплатформенный детерминизм. Близкие средние нельзя считать надёжной победой без дополнительных экспериментов." : "These are equivalent logical operations through selected adapters, not identical storage or each framework’s best possible API. Results apply to this machine, runtime, snapshots and workload. They do not establish a universal ECS ranking, Unity/IL2CPP performance, total-memory efficiency or cross-platform determinism. Close means should not be treated as reliable wins without further experiments.",

            };
            return string.Join("\n\n", parts) + "\n";
        }
        var outputs = new Dictionary<string, string> { ["REPORT.md"] = Render(false), ["REPORT.ru.md"] = Render(true) };
        Directory.CreateDirectory(output);
        if (render)
        {
            MicroCharts.Render(Path.Combine(output, "comparison.svg"), "ECS base comparison", basis, false);
            MicroCharts.Render(Path.Combine(output, "stress-100k.svg"), "ECS stress · 100,000 entities", stress, true, 100000);
            MicroCharts.Render(Path.Combine(output, "stress-1m.svg"), "ECS stress · 1,000,000 entities", stress, true, 1000000);
        }
        Write(output, outputs);
    }
    static string ApiTable(Dictionary<(string Operation, string Method, int Count), JsonElement> api, string operation, bool ru, string assets)
    {
        string[] methods = operation == "Create" ? ["EmptyAddAdd", "CreateEntityComponents", "PreparedFactoryBatch"] : ["WorldGet", "CachedAccessorGet"];
        return Table(new[] { "N" }.Concat(methods), new[] { 100000, 1000000 }.Select(n => new[] { Count(n) }.Concat(ReportColors.Shade(methods.Select(m => N(api[(operation, m, n)], "meanNs")).ToArray(), Ms, ru, assets))));
    }
    static string BdnTable(Dictionary<BdnKey, JsonElement> indexed, bool ru, string assets)
    {
        return Table(["Class", "Method", "N", "Padding", "Mean ms", "SD ms", "Actual / retained"],
            indexed.OrderBy(p => p.Key.Type, StringComparer.Ordinal).ThenBy(p => p.Key.Method, StringComparer.Ordinal).ThenBy(p => p.Key.Count).ThenBy(p => p.Key.Padding).Select(p =>
            {
                var stats = p.Value.GetProperty("Statistics");
                return new[] { p.Key.Type, p.Key.Method, Count(p.Key.Count), p.Key.Padding?.ToString(Inv) ?? "—", ReportColors.Marker("equal", ru, assets) + " " + Ms(N(stats, "Mean")), Ms(N(stats, stats.TryGetProperty("StandardDeviation", out _) ? "StandardDeviation" : "StdDev")), "8 / " + I(stats, "N") };
            }));
    }
    public static void Supplement(string root, string directory, bool render = true, string? outputDirectory = null)
    {
        root = Path.GetFullPath(root); directory = OutputDirectory(root, directory);
        string output = outputDirectory == null ? directory : OutputDirectory(root, outputDirectory);
        var manifest = Load(Path.Combine(directory, "supplement.json")); Manifest(manifest);
        Require(manifest.GetProperty("completed").ValueKind == JsonValueKind.True, "Incomplete supplemental campaign");
        string campaignPath = Path.Combine(directory, "campaign.json");
        if (File.Exists(campaignPath))
        {
            var campaign = Load(campaignPath); Manifest(campaign);
            Require(S(campaign, "sourceRevision") == S(manifest, "sourceRevision") && Equal(campaign.GetProperty("sourceHashes"), manifest.GetProperty("sourceHashes")), "Comparison and supplement source snapshots differ");
            foreach (var assembly in campaign.GetProperty("assemblyHashes").EnumerateObject()
                .Where(p => p.Name.StartsWith("Benchmarks.Local/bin/Release/net8.0/", StringComparison.Ordinal) || p.Name.StartsWith("SKECS.Benchmarks/bin/Release/net8.0/", StringComparison.Ordinal)))
                Require(manifest.GetProperty("assemblyHashes").TryGetProperty(assembly.Name, out var digest) && assembly.Value.GetString()!.Equals(digest.GetString(), StringComparison.OrdinalIgnoreCase), "Comparison and supplement assemblies differ: " + assembly.Name);
        }
        var inputs = manifest.GetProperty("inputHashes");
        Require(inputs.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(["optimized-api.json", "storage.json", "bdn-skecs.json", "bdn-legacy.json"]), "Incomplete supplemental inputs");
        foreach (var p in inputs.EnumerateObject()) VerifyHash(LocalPath(directory, p.Name), p.Value.GetString()!);
        var apiData = Load(Path.Combine(directory, "optimized-api.json")); var storage = Load(Path.Combine(directory, "storage.json"));
        var secsData = Load(Path.Combine(directory, "bdn-skecs.json")); var legacyData = Load(Path.Combine(directory, "bdn-legacy.json"));
        foreach (var data in new[] { apiData, storage }) Require(S(data.GetProperty("metadata"), "revision") == S(manifest, "sourceRevision"), "Mixed API/storage source revisions");
        Require(I(apiData.GetProperty("metadata"), "sampleCount") == 8, "API metadata sample count mismatch");
        Require(S(apiData.GetProperty("metadata"), "runtime") == S(storage.GetProperty("metadata"), "runtime"), "Mixed API/storage runtimes");
        string comparisonKey = manifest.GetProperty("assemblyHashes").TryGetProperty("Benchmarks.Local/bin/Release/net8.0/Comparison.dll", out _)
            ? "Benchmarks.Local/bin/Release/net8.0/Comparison.dll" : MeasurementKey(manifest);
        Require(S(manifest, "comparisonAssemblySha256").Equals(S(manifest.GetProperty("assemblyHashes"), comparisonKey), StringComparison.OrdinalIgnoreCase), "API assembly provenance mismatch");
        OriginalExports(directory, secsData, manifest, "SKECS.Benchmarks/bin/Release/net8.0/SKECS.Benchmarks.dll");
        OriginalExports(directory, legacyData, manifest, RecordedKey(manifest.GetProperty("assemblyHashes"), "SKECS.OtherBenchmarks/bin/Release/net8.0/SKECS.OtherBenchmarks.dll", "SKECS.Benchmarks/bin/Release/net8.0/SKECS.Benchmarks.dll"));
        var api = Api(apiData); Storage(storage);
        var secs = Bdn(secsData, ExpectedCurrent()); var legacy = Bdn(legacyData, ExpectedLegacy(root));
        Require(secs.Count == 34 && legacy.Count == 51, "Unexpected suite matrix size");
        var secsMetadata = secsData.GetProperty("metadata"); var legacyMetadata = legacyData.GetProperty("metadata");
        Require(S(secsMetadata, "runtime") == S(legacyMetadata, "runtime"), "Mixed BDN runtimes");
        if (!render) return;
        string assets = Link(output, root, "docs/benchmarks/heatmap");
        string Render(bool ru)
        {
            var parts = new List<string>
            {
                ru ? "[English](SUPPLEMENT.md) | Русский" : "English | [Русский](SUPPLEMENT.ru.md)",
                ru ? "# Дополнительные измерения SKECS" : "# Supplemental SKECS measurements",
                ru ? "Значения относятся к измеренным исходникам и этой машине. Полные операции измерены в миллисекундах. Это отдельные нагрузки; таблицы не создают общего рейтинга API и систем." : "These measurements apply to the measured sources and this machine. Timings are milliseconds per complete operation. These are separate workloads; tables do not form an overall ranking of APIs or systems.",
                (ru ? "Локальные сырые данные: " : "Local raw artifacts: ") + $"`{Path.GetRelativePath(root, directory).Replace('\\', '/')}`: `optimized-api.json`, `storage.json`, `bdn-skecs.json`, `bdn-legacy.json`, `bdn-raw/`.",
                ru ? "Локальный `supplement.json` фиксирует ревизию, исходники, сборки и хеши входных данных; исходные BDN full-json проверяются по сохранённым байтам и содержимому. [Сравнение пяти ECS](REPORT.ru.md) — отдельная общая нагрузка." : "Local `supplement.json` records revision, source/assembly provenance and input hashes; original BDN full-json exports are verified by saved bytes and content. The [five-ECS comparison](REPORT.md) uses a separate shared workload.",
                "## API",
                ru ? "Восемь замеров на кейс. Создание: новый мир за повтор; подготовка, проверка, освобождение и принудительный GC вне таймера. Lookup использует прогретый мир и общий seeded порядок. Цвет сравнивает методы одной операции при одинаковом N по исходной точности; он не доказывает статистическую значимость." : "Eight samples per case. Creation uses a fresh world per repetition; setup, validation, disposal and forced GC are outside timing. Lookup uses warmed storage and a shared seeded order. Colour compares only methods of the same operation at the same N using raw precision; it does not establish statistical significance.",
                "### Create", ApiTable(api, "Create", ru, assets), "### Lookup", ApiTable(api, "Lookup", ru, assets),
                Table(["Operation", "Method", "N", "SD ms", "Managed allocated bytes", "Retained native bytes"], api.OrderBy(p => p.Key.Operation, StringComparer.Ordinal).ThenBy(p => p.Key.Method, StringComparer.Ordinal).ThenBy(p => p.Key.Count).Select(p => new[] { p.Key.Operation, p.Key.Method, Count(p.Key.Count), Ms(N(p.Value, "stddevNs")), N(p.Value, "allocatedBytes").ToString("F3", Inv), N(p.Value, "storageNativeBytes").ToString("G17", Inv) })),
                ReportColors.Legend(ru, assets, Link(output, root, ru ? "docs/benchmarks/COLORS.ru.md" : "docs/benchmarks/COLORS.md")),
                "## Storage",
                ru ? "Не измерение времени: сохранённые нативные колонковые буферы после добавления/удаления payload 256 bytes, N=100000. Управляемые массивы, заголовки и overhead allocator исключены; это не полная память процесса." : "Untimed retained native column buffers after adding/removing a 256-byte payload, N=100000. Managed arrays, headers and allocator overhead are excluded; these are not total process memory.",
                Table(["Pass", "Chunks", "Native bytes"], storage.GetProperty("samples").EnumerateArray().Select(r => new[] { I(r, "pass").ToString(Inv), N(r, "chunkCount").ToString(Inv), N(r, "nativeBytes").ToString(Inv) })),
                "## BenchmarkDotNet",
                Table(["Suite", "Runtime", "Configuration", "Toolchain"], new[] { ("SKECS", secsMetadata), ("Legacy", legacyMetadata) }.Select(p => new[] { p.Item1, S(p.Item2, "runtime"), S(p.Item2, "configuration"), S(p.Item2, "toolchain") })),
                ru ? "Release, Short, InProcess: явно заданы 3 warmup и 8 actual итераций, вместо стандартных 3 actual у Short. Actual / retained отделяет собранные замеры от Statistics после обработки выбросов. Setup/cleanup вне операций; естественный GC внутри включён. InProcess использует общий процесс без изоляции child runtime. Allocation diagnostics не измеряют нативные колонки." : "Release, Short, InProcess: explicitly 3 warmup and 8 actual iterations, rather than Short’s default 3 actual iterations. Actual / retained distinguishes collected observations from Statistics after outlier processing. Setup/cleanup are outside operations; natural GC within operations is included. InProcess shares the host process without child-runtime isolation. Allocation diagnostics exclude native columns.",
                ru ? "34 кейса SKECS и 51 legacy-кейс проверены по точным ключам. Разные операции не ранжируются: все ячейки нейтральны. MixedFrames — пакет 120 кадров. Padding 10 меняет долю совпадающих сущностей и не сравнивается с Padding 0 как ускорение одной нагрузки." : "Exact keys cover 34 SKECS and 51 legacy cases. Different operations are not ranked: all cells are neutral. MixedFrames is a 120-frame batch. Padding 10 changes the fraction of matching entities and is not compared with Padding 0 as a speedup of one workload.",
                ru ? "Mutable-кейсы выполняют одну операцию за итерацию для корректного reset. Для многих BDN сообщает MinIterationTime ниже рекомендуемых 100 ms: восемь коротких наблюдений имеют ограниченную точность. Числа не заменяют длинный default job и общую матрицу пяти ECS." : "Mutable cases invoke one operation per iteration to preserve reset semantics. Many receive BDN MinIterationTime warnings below its recommended 100 ms: eight short observations have limited precision. These values do not replace a longer default job or the shared five-ECS matrix.",
                "### SKECS: 34", BdnTable(secs, ru, assets), "### Legacy: 51", BdnTable(legacy, ru, assets)
            };
            return string.Join("\n\n", parts) + "\n";
        }
        var outputs = new Dictionary<string, string> { ["SUPPLEMENT.md"] = Render(false), ["SUPPLEMENT.ru.md"] = Render(true) };
        Directory.CreateDirectory(output);
        Write(output, outputs);
    }
}
