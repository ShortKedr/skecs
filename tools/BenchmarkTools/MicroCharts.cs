using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace SKECS.BenchmarkTools;

/// <summary>Portable panelled SVGs with all framework values and sample-deviation error bars.</summary>
public static class MicroCharts
{
    static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    static string F(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
    public static void Render(string path, string title, Dictionary<(string Framework, string Scenario, int Count), JsonElement> data, bool stress, int? count = null)
    {
        var cases = data.Keys.Where(k => count == null || k.Count == count).Select(k => (k.Scenario, k.Count)).Distinct().OrderBy(k => k.Count).ThenBy(k => k.Scenario, StringComparer.Ordinal).ToArray();
        int rows = (cases.Length + 1) / 2;
        var root = new XElement(Svg + "svg", new XAttribute("viewBox", $"0 0 1200 {rows * 275 + 110}"), new XAttribute("role", "img"), new XElement(Svg + "title", title));
        root.Add(new XElement(Svg + "rect", new XAttribute("width", "100%"), new XAttribute("height", "100%"), new XAttribute("fill", "white")));
        void Text(double x, double y, string text, int size = 12) => root.Add(new XElement(Svg + "text", new XAttribute("x", F(x)), new XAttribute("y", F(y)), new XAttribute("font-family", "Arial, sans-serif"), new XAttribute("font-size", size), new XAttribute("fill", "#20252b"), text));
        void Line(double x1, double y1, double x2, double y2, string color = "#30343b") => root.Add(new XElement(Svg + "line", new XAttribute("x1", F(x1)), new XAttribute("y1", F(y1)), new XAttribute("x2", F(x2)), new XAttribute("y2", F(y2)), new XAttribute("stroke", color)));
        Text(24, 32, title, 21);
        for (int panel = 0; panel < cases.Length; panel++)
        {
            var key = cases[panel]; double x = 20 + panel % 2 * 600, y = 65 + panel / 2 * 275;
            double divisor = stress ? 1e6 : key.Count;
            double[] means = MicroValidation.Frameworks.Select(f => MicroValidation.N(data[(f, key.Scenario, key.Count)], "meanNs") / divisor).ToArray();
            double[] errors = MicroValidation.Frameworks.Select(f => MicroValidation.N(data[(f, key.Scenario, key.Count)], "stddevNs") / divisor).ToArray();
            string[] levels = ReportColors.Levels(means);
            double low = means.Zip(errors).Min(p => Math.Max(p.First - p.Second, p.First * .01)) * .8;
            double high = means.Zip(errors).Max(p => p.First + p.Second) * 1.8;
            double Scale(double value) => x + 125 + (Math.Log(Math.Max(low, value)) - Math.Log(low)) / (Math.Log(high) - Math.Log(low)) * 330;
            Text(x, y, $"{key.Scenario} · N={key.Count.ToString("N0", CultureInfo.InvariantCulture)}", 15);
            for (int tick = 0; tick < 4; tick++)
            {
                double value = Math.Exp(Math.Log(low) + (Math.Log(high) - Math.Log(low)) * tick / 3);
                double px = Scale(value); Line(px, y + 15, px, y + 194, "#e0e4e9"); Text(px, y + 213, value.ToString("G3", CultureInfo.InvariantCulture), 10);
            }
            for (int i = 0; i < means.Length; i++)
            {
                double py = y + 33 + i * 34, px = Scale(means[i]), left = x + 125;
                Text(x, py + 4, MicroValidation.Frameworks[i]);
                var bar = new XElement(Svg + "rect", new XAttribute("x", F(left)), new XAttribute("y", F(py - 10)), new XAttribute("width", F(Math.Max(.5, px - left))), new XAttribute("height", 20), new XAttribute("fill", ReportColors.Color(levels[i])),
                    new XElement(Svg + "title", $"{MicroValidation.Frameworks[i]}: {means[i].ToString("G17", CultureInfo.InvariantCulture)} ± {errors[i].ToString("G17", CultureInfo.InvariantCulture)}"));
                root.Add(bar);
                double lo = Scale(means[i] - errors[i]), hi = Scale(means[i] + errors[i]);
                Line(lo, py, hi, py); Line(lo, py - 4, lo, py + 4); Line(hi, py - 4, hi, py + 4);
                Text(hi + 5, py + 4, means[i].ToString("G4", CultureInfo.InvariantCulture), 11);
            }
            Text(x + 125, y + 237, stress ? "milliseconds / complete operation (log scale)" : "nanoseconds / entity (log scale)", 11);
        }
        Text(24, rows * 275 + 80, "Error bars: sample SD, not confidence intervals. Fixed lag scale: green ≤5%, yellow 15%, red ≥30%; grey ≤5% group.", 12);
        File.WriteAllText(path, root.ToString() + "\n", new System.Text.UTF8Encoding(false));
    }
}
