using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace AdaptivePlacementFixture;

internal sealed record CoverageInput(string Sha256, HashSet<string> Permitted)
{
    internal static CoverageInput Read(string path, string fixtureSha256, string coreSha256, string profilerSha256)
    {
        var bytes = File.ReadAllBytes(path);
        using var report = JsonDocument.Parse(bytes);
        var root = report.RootElement;
        var modules = root.GetProperty("TrustedModules");
        if (root.GetProperty("Format").GetString() != "skecs-fixture-legacy-coverage-v1" ||
            !root.GetProperty("Complete").GetBoolean() ||
            root.GetProperty("Runtime").GetString() != RuntimeInformation.FrameworkDescription ||
            root.GetProperty("Assembly").GetString() != "AdaptivePlacementFixture" ||
            root.GetProperty("AssemblySha256").GetString() != fixtureSha256 ||
            modules.GetProperty("SKECS").GetString() != coreSha256 ||
            modules.GetProperty("SKECS.Profiler").GetString() != profilerSha256)
            throw new InvalidDataException("Legacy coverage is incomplete or does not match this fixture and its runtime modules.");

        var permitted = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in root.GetProperty("Candidates").EnumerateArray())
        {
            var name = candidate.GetProperty("Component").GetString();
            if (string.IsNullOrEmpty(name) || !seen.Add(name))
                throw new InvalidDataException("Legacy coverage contains an invalid or duplicate component identity.");
            if (candidate.GetProperty("PermittedByLegacyCoverage").GetBoolean()) permitted.Add(name);
        }
        return new CoverageInput(Convert.ToHexString(SHA256.HashData(bytes)), permitted);
    }
}
