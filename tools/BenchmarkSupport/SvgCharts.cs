using System.Globalization;
using System.Security;
using System.Text;

namespace SKECS.BenchmarkTools;

public static class SvgCharts
{
    public static void Bars(string path, string title, IReadOnlyList<string> labels,
        IReadOnlyList<double> values, string unit)
    {
        if (labels.Count != values.Count || values.Count == 0 || values.Any(v => !double.IsFinite(v) || v < 0))
            throw new InvalidDataException("Chart needs matching nonnegative finite observations.");
        const int width = 1000, left = 240, right = 110, top = 90, row = 44;
        int height = top + row * values.Count + 55;
        double maximum = values.Max();
        if (maximum == 0) maximum = 1;
        var levels = ReportColors.Levels(values);
        var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" role=\"img\">\n");
        svg.AppendLine($"<title>{Escape(title)}</title><rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        svg.AppendLine("<g font-family=\"system-ui, sans-serif\" fill=\"#17202a\">");
        svg.AppendLine($"<text x=\"24\" y=\"32\" font-size=\"22\" font-weight=\"bold\">{Escape(title)}</text>");
        svg.AppendLine($"<text x=\"24\" y=\"58\" font-size=\"14\">{Escape(unit)} · lower is better · colour shows relative lag, not statistical significance</text>");
        for (int i = 0; i < values.Count; i++)
        {
            string color = ReportColors.Color(levels[i]);
            double length = values[i] / maximum * (width - left - right);
            int y = top + i * row;
            svg.AppendLine($"<text x=\"{left - 12}\" y=\"{y + 21}\" text-anchor=\"end\" font-size=\"14\">{Escape(labels[i])}</text>");
            svg.AppendLine($"<rect x=\"{left}\" y=\"{y}\" width=\"{N(length)}\" height=\"30\" fill=\"{color}\"/>");
            svg.AppendLine($"<text x=\"{N(left + length + 8)}\" y=\"{y + 21}\" font-size=\"14\">{N(values[i])}</text>");
        }
        svg.AppendLine("</g></svg>");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, svg.ToString(), new UTF8Encoding(false));
    }

    private static string N(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string Escape(string value) => SecurityElement.Escape(value) ?? "";
}
