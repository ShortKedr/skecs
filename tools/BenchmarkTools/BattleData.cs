using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SKECS.BenchmarkTools;

internal static class BattleData
{
    internal const int Ticks = 108000;
    internal const double Budget = 1000.0 / 60;
    internal static readonly string[] Stages = ["collect", "ai", "thermal-inventory", "steering", "weapons", "carriers", "physics", "hits", "lifecycle"];
    internal static readonly string[] Counts = ["Ships", "Projectiles", "Debris", "Particles", "Turrets"];
    internal static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    internal static double D(this JsonNode? value) => value!.GetValue<double>();
    internal static long L(this JsonNode? value) => value!.GetValue<long>();
    internal static string S(this JsonNode? value) => value!.GetValue<string>();
    internal static JsonArray A(this JsonNode? value) => value!.AsArray();
    internal static JsonObject O(this JsonNode? value) => value!.AsObject();
    internal static string F(double value, int decimals = 4) => value.ToString("F" + decimals, CultureInfo.InvariantCulture);
    internal static JsonNode Load(string path)
    {
        var text = File.ReadAllText(path);
        using var document = JsonDocument.Parse(text);
        CheckDuplicates(document.RootElement, path);
        return JsonNode.Parse(text)!;
    }
    private static void CheckDuplicates(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) { Require(names.Add(property.Name), path + ": duplicate key " + property.Name); CheckDuplicates(property.Value, path); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) CheckDuplicates(child, path);
    }
    internal static void Equal(JsonNode? a, JsonNode? b, string label) => Require(JsonNode.DeepEquals(a, b), label);
    internal static void Close(double actual, double expected, string label) => Require(double.IsFinite(actual) && double.IsFinite(expected) && Math.Abs(actual - expected) <= Math.Max(1e-9, 1e-8 * Math.Max(Math.Abs(actual), Math.Abs(expected))), $"{label}: {actual:R} differs from {expected:R}");
    internal static string DirectoryUnderDocs(string root, string directory)
    {
        var docs = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.IsPathRooted(directory) ? directory : Path.Combine(root, directory));
        Require(path.StartsWith(docs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Benchmark paths must stay inside the project");
        return path;
    }
    internal static string InputPath(string directory, string name)
    {
        Require(name.Length > 0 && name == Path.GetFileName(name) && !name.Contains('/') && !name.Contains('\\'), "Invalid manifest filename: " + name);
        return Path.Combine(directory, name);
    }
    internal static string Hash(string path, bool decompress = false)
    {
        using var file = File.OpenRead(path);
        using Stream stream = decompress ? new GZipStream(file, CompressionMode.Decompress) : file;
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    internal static bool HashEquals(string actual, string expected) => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    internal static void Hashes(string directory, JsonNode manifest, bool verifyTicks = true)
    {
        foreach (var pair in manifest["Files"].O())
        {
            var path = InputPath(directory, pair.Key);
            if (!File.Exists(path)) { Require(!verifyTicks && pair.Key.EndsWith(".ticks.csv", StringComparison.Ordinal), "Missing input: " + pair.Key); continue; }
            Require(HashEquals(Hash(path), pair.Value.S()), "Input hash mismatch: " + pair.Key);
        }
        if (manifest["TickCsvHashes"] is JsonObject hashes)
            foreach (var pair in hashes)
                Require(HashEquals(Hash(InputPath(directory, pair.Key + ".gz"), true), pair.Value.S()), "Decompressed tick input hash mismatch: " + pair.Key);
    }
    internal static bool BinaryMatches(JsonObject hashes, string name, string expected, string? project = null)
    {
        var matches = hashes.Where(pair =>
        {
            var path = pair.Key.Replace('\\', '/');
            return (path == name || path.EndsWith("/" + name, StringComparison.Ordinal)) &&
                   (project == null || path.StartsWith(project + "/", StringComparison.Ordinal));
        }).Select(pair => pair.Value.S()).ToArray();
        return matches.Length > 0 && matches.All(value => HashEquals(value, expected));
    }
    internal static void Summary(JsonNode stats, int count, string label)
    {
        foreach (var key in new[] { "MeanMs", "StddevMs", "MedianMs", "P95Ms", "P99Ms", "MaxMs", "BudgetMissPercent" }) Require(double.IsFinite(stats[key].D()) && stats[key].D() >= 0, label + ": invalid " + key);
        Require(stats["MedianMs"].D() <= stats["P95Ms"].D() && stats["P95Ms"].D() <= stats["P99Ms"].D() && stats["P99Ms"].D() <= stats["MaxMs"].D() && stats["MeanMs"].D() <= stats["MaxMs"].D(), label + ": inconsistent timing summary");
        Require(stats["BudgetMisses"].L() >= 0 && stats["BudgetMisses"].L() <= count, label + ": invalid BudgetMisses");
        Close(stats["BudgetMissPercent"].D(), stats["BudgetMisses"].D() * 100 / count, label);
    }
    internal static void Stats(JsonNode stats, IReadOnlyList<double> values, int begin, int count, string label)
    {
        Require(count > 0, label + ": empty samples");
        var ordered = new double[count];
        for (var i = 0; i < count; i++) { var value = values[begin + i]; Require(double.IsFinite(value) && value >= 0, label + ": invalid timing"); ordered[i] = value; }
        Array.Sort(ordered);
        double sum = 0, squares = 0;
        foreach (var value in ordered) { sum += value; squares += value * value; }
        Close(stats["MeanMs"].D(), sum / count, label + ".MeanMs");
        Close(stats["StddevMs"].D(), count > 1 ? Math.Sqrt(Math.Max(0, (squares - sum * sum / count) / (count - 1))) : 0, label + ".StddevMs");
        foreach (var (key, q) in new[] { ("MedianMs", .5), ("P95Ms", .95), ("P99Ms", .99), ("MaxMs", 1.0) }) Close(stats[key].D(), ordered[(int)Math.Ceiling((count - 1) * q)], label + "." + key);
        var misses = ordered.Count(value => value > Budget);
        Close(stats["BudgetMisses"].D(), misses, label + ".BudgetMisses");
        Close(stats["BudgetMissPercent"].D(), misses * 100.0 / count, label + ".BudgetMissPercent");
    }
    internal static void StageSummary(JsonNode stages, int count, string label)
    {
        Require(stages.A().Select(s => s!["Name"].S()).SequenceEqual(Stages), label + ": wrong/missing/duplicate stages");
        foreach (var stage in stages.A()) Summary(stage!["Timing"]!, count, label + "." + stage["Name"].S());
    }
    internal static IEnumerable<Dictionary<string, string>> Csv(string directory, string stem)
    {
        var raw = Path.Combine(directory, stem + ".ticks.csv");
        var path = File.Exists(raw + ".gz") ? raw + ".gz" : raw;
        using var file = File.OpenRead(path);
        using Stream input = path.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(file, CompressionMode.Decompress) : file;
        using var reader = new StreamReader(input, Encoding.UTF8, true);
        using var records = CsvRecords(reader).GetEnumerator();
        Require(records.MoveNext(), path + ": missing header");
        var header = records.Current;
        Require(header.Distinct(StringComparer.Ordinal).Count() == header.Length && !header.Any(h => h.Contains("synthetic", StringComparison.OrdinalIgnoreCase)), path + ": invalid CSV columns");
        while (records.MoveNext())
        {
            Require(records.Current.Length == header.Length, path + ": malformed CSV row");
            yield return header.Zip(records.Current).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal);
        }
    }
    // RFC 4180 fields, including escaped quotes, embedded commas and line breaks.
    internal static IEnumerable<string[]> CsvRecords(TextReader reader)
    {
        var fields = new List<string>(); var field = new StringBuilder(); bool quoted = false, afterQuote = false, touched = false;
        while (reader.Read() is var number && number >= 0)
        {
            var c = (char)number; touched = true;
            if (quoted) { if (c == '"') { if (reader.Peek() == '"') { reader.Read(); field.Append('"'); } else { quoted = false; afterQuote = true; } } else field.Append(c); continue; }
            if (c == '"') { Require(field.Length == 0 && !afterQuote, "Malformed CSV quoting"); quoted = true; continue; }
            if (c == ',' || c == '\n' || c == '\r')
            {
                fields.Add(field.ToString()); field.Clear(); afterQuote = false;
                if (c == ',') continue;
                if (c == '\r' && reader.Peek() == '\n') reader.Read();
                yield return fields.ToArray(); fields.Clear(); touched = false; continue;
            }
            Require(!afterQuote, "Unexpected data after CSV quote"); field.Append(c);
        }
        Require(!quoted, "Unterminated CSV quote");
        if (touched || fields.Count > 0) { fields.Add(field.ToString()); yield return fields.ToArray(); }
    }
    internal static double Number(this Dictionary<string, string> row, string key) => double.Parse(row[key], NumberStyles.Float, CultureInfo.InvariantCulture);
    internal static long Integer(this Dictionary<string, string> row, string key) => long.Parse(row[key], NumberStyles.Integer, CultureInfo.InvariantCulture);
    internal static string Table(IEnumerable<string> cells) => "| " + string.Join(" | ", cells) + " |";
    internal static void Header(List<string> lines, params string[] cells) { lines.Add(Table(cells)); lines.Add(Table(cells.Select(_ => "---"))); }
    internal static void ShadeTimingTables(List<string> lines, string root, string reportDirectory, bool ru)
    {
        var assets = Path.GetRelativePath(reportDirectory, Path.Combine(root, "docs", "benchmarks", "heatmap")).Replace('\\', '/');
        for (var index = 0; index < lines.Count - 2; index++)
        {
            if (!lines[index].StartsWith("| ", StringComparison.Ordinal) || !lines[index + 1].StartsWith("| ---", StringComparison.Ordinal)) continue;
            string[] Cells(string line) => line.Split('|').Skip(1).SkipLast(1).Select(s => s.Trim()).ToArray();
            var header = Cells(lines[index]); var first = index + 2; var end = first;
            while (end < lines.Count && lines[end].StartsWith("| ", StringComparison.Ordinal)) end++;
            var byRow = header.Length == 6 && (header[0] == "Stage" || header[0] == "HUD / stage ms" || header[0] == "Tick");
            var byColumn = header[0] == "ECS" && header.Length == 8 && header[1] == "Mean ms";
            if (!byRow && !byColumn) continue;
            if (byRow)
            {
                for (var row = first; row < end; row++)
                {
                    var cells = Cells(lines[row]); var numbers = cells.Skip(1).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                    var levels = ReportColors.Levels(numbers);
                    for (var col = 1; col < cells.Length; col++) cells[col] = ReportColors.Marker(levels[col - 1], ru, assets) + " " + cells[col];
                    lines[row] = Table(cells);
                }
            }
            else
            {
                var rows = Enumerable.Range(first, end - first).Select(i => Cells(lines[i])).ToArray();
                for (var col = 1; col < header.Length; col++)
                {
                    var levels = ReportColors.Levels(rows.Select(cells => double.Parse(cells[col], CultureInfo.InvariantCulture)).ToArray());
                    for (var row = 0; row < rows.Length; row++) rows[row][col] = ReportColors.Marker(levels[row], ru, assets) + " " + rows[row][col];
                }
                for (var row = 0; row < rows.Length; row++) lines[first + row] = Table(rows[row]);
            }
            index = end - 1;
        }
    }
    internal static void Write(string path, IEnumerable<string> lines) => File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
}
