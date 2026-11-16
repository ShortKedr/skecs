using System.Text.Json;
using AdaptivePlacementFixture.LegacyCoverage;

if (args.Length < 2 || args.Skip(1).Distinct(StringComparer.Ordinal).Count() != args.Length - 1)
{
    Console.Error.WriteLine("Usage: AdaptivePlacementFixture.LegacyCoverage assembly.dll ComponentFullName [ComponentFullName ...]");
    return 2;
}

try
{
    var report = LegacyAudit.Inspect(Path.GetFullPath(args[0]), args.Skip(1).ToArray());
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return report.Candidates.All(candidate => candidate.PermittedByLegacyCoverage) ? 0 : 1;
}
catch (Exception error) when (error is IOException or BadImageFormatException or InvalidOperationException or OverflowException)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}
