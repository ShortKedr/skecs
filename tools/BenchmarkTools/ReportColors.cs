using System.Globalization;

namespace SKECS.BenchmarkTools;

/// <summary>The fixed 5/15/30 percent effect scale; inputs retain unrounded precision.</summary>
public static class ReportColors
{
    public static readonly string[] Names = ["best", "strong", "good", "fair", "middle", "weak", "poor", "bad", "worst"];
    public static readonly string[] Palette = ["#2e9b50", "#59b35d", "#91cb69", "#c7df78", "#f2d85c", "#efad46", "#e87c3f", "#dc513c", "#c93637"];
    public const string EqualColor = "#8b949e";
    public static string[] Levels(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return values.Select(_ => "equal").ToArray();
        MicroValidation.Require(values.All(v => double.IsFinite(v) && v >= 0), "Invalid comparison value");
        double best = values.Min();
        double[] lag = values.Select(v => best == 0 ? v == 0 ? 0 : double.PositiveInfinity : Math.Max(0, (v / best - 1) * 100)).ToArray();
        if (lag.All(v => v <= 5)) return values.Select(_ => "equal").ToArray();
        return lag.Select(v => v <= 5 ? Names[0] : v >= 30 ? Names[8] : Names[Math.Clamp((int)Math.Round(v <= 15 ? (v - 5) * .4 : 4 + (v - 15) * 4 / 15, MidpointRounding.AwayFromZero), 1, 7)]).ToArray();
    }
    public static string Color(string name) { int i = Array.IndexOf(Names, name); return i < 0 ? EqualColor : Palette[i]; }
    public static string Marker(string name, bool ru, string assets) => $"![{(ru ? name == "equal" ? "Нейтрально" : name == "best" ? "Отставание до 5%" : name == "worst" ? "Отставание от 30%" : "Отставание между 5% и 30%" : name == "equal" ? "Neutral" : name == "best" ? "Lag up to 5%" : name == "worst" ? "Lag from 30%" : "Lag between 5% and 30%")}]({assets}/{name}.svg)";
    public static string[] Shade(double[] values, Func<double, string> format, bool ru, string assets) => Levels(values).Zip(values).Select(p => Marker(p.First, ru, assets) + " " + format(p.Second)).ToArray();
    public static string Legend(bool ru, string assets, string details) => ru
        ? $"### Цветовая шкала\n\n{Marker("best", true, assets)} до 5% · {Marker("middle", true, assets)} 15% · {Marker("worst", true, assets)} от 30% · {Marker("equal", true, assets)} нейтрально.\n\nЦвет показывает отставание от лучшего значения в сопоставимой группе: зелёный → жёлтый → красный. Если вся группа укладывается в 5%, она серая. Цвет описывает размер различия, а не статистическую значимость. [Правила сравнения]({details})."
        : $"### Colour scale\n\n{Marker("best", false, assets)} up to 5% · {Marker("middle", false, assets)} 15% · {Marker("worst", false, assets)} from 30% · {Marker("equal", false, assets)} neutral.\n\nColour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules]({details}).";
}
