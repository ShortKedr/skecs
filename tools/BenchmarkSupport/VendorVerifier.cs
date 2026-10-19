using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SKECS.BenchmarkTools;

/// <summary>Checks audited identities, original bytes, and the exact local Git publication inventory.</summary>
public static class VendorVerifier
{
    const string SourcePrefix = "SKECS.Benchmarks/Vendor/Sources/";
    sealed record Pin(string Id, string Name, string Repository, string Revision, string License, string SourceRoot);
    static readonly Pin[] Pins =
    [
        new("ecs", "LeoECS Classic", "Leopotam/ecs", "1213c31c9ef8325d48e74a52b345c4db242d038b", "MIT-Red OR proprietary", "src"),
        new("ecslite", "LeoECS Lite", "Leopotam/ecslite", "5053141d7d663c6b1855c8409ac93dfac807b239", "MIT-Red", "src"),
        new("morpeh", "Morpeh", "scellecs/morpeh", "9f6369384792a01d74df24efd35b08ae313e4f12", "MIT", "Scellecs.Morpeh/Core"),
        new("StaticEcs", "StaticEcs", "Felid-Force-Studios/StaticEcs", "23b1cf36d89497e8a126d38459d688e2b0a5dea3", "MIT", "Src"),
        new("StaticPack", "StaticPack", "Felid-Force-Studios/StaticPack", "7bfb6cf925901b9366dda884258d727a89fff76e", "MIT", "Src")
    ];
    static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    static string Text(JsonNode node, string key) => node[key]?.GetValue<string>() ?? throw new InvalidDataException("Missing vendor field: " + key);
    static string Relative(string value)
    {
        Require(!string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) && !value.Contains('\\') && !value.Contains(':') &&
            value.Split('/').All(part => part.Length > 0 && part is not "." and not ".."), "Unsafe manifest path: " + value);
        return value;
    }
    public static void ValidateManifest(JsonNode manifest)
    {
        Require(manifest["format"]?.GetValue<int>() == 1, "Unsupported vendor manifest format");
        Require(Text(manifest, "distribution") == "runtime-source-only", "Unsupported vendor distribution");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in manifest["components"]?.AsArray() ?? throw new InvalidDataException("Missing vendor components"))
        {
            Require(component is not null, "Missing vendor component");
            string id = Text(component!, "id");
            Require(ids.Add(id), "Duplicate vendor component: " + id);
            var pin = Pins.SingleOrDefault(pin => pin.Id == id);
            Require(pin is not null && Text(component!, "name") == pin.Name && Text(component!, "repository") == "https://github.com/" + pin.Repository &&
                Text(component!, "revision") == pin.Revision && Text(component!, "license") == pin.License && Text(component!, "source_root") == pin.SourceRoot &&
                Text(component!, "license_file") == "LICENSE.md", "Component metadata differs from audited pin: " + id);
        }
        Require(ids.SetEquals(Pins.Select(pin => pin.Id)), "Missing or unexpected vendored framework/dependency");
    }
    static string ExactPath(Workspace workspace, string relative)
    {
        string full;
        try { full = workspace.LocalPath(relative); }
        catch (ArgumentException error) { throw new InvalidDataException("Manifest path escapes its component: " + relative, error); }
        string current = workspace.Root;
        foreach (var part in relative.Split('/'))
        {
            Require(Directory.Exists(current) && Directory.EnumerateFileSystemEntries(current).Any(entry => Path.GetFileName(entry) == part), "Missing or wrong-case source: " + relative);
            current = Path.Combine(current, part);
        }
        return full;
    }
    public static HashSet<string> VerifyComponent(string root, JsonNode component)
    {
        var workspace = new Workspace(root);
        string id = Text(component, "id"), revision = Text(component, "revision");
        Require(Regex.IsMatch(id, "\\A[A-Za-z0-9_-]+\\z") && Regex.IsMatch(revision, "\\A[0-9a-f]{40}\\z"), "Invalid component identity or revision");
        string prefix = SourcePrefix + id + "/", source = Relative(Text(component, "source_root")), license = Relative(Text(component, "license_file"));
        var expected = new HashSet<string>(StringComparer.Ordinal);
        var portablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in component["files"]?.AsArray() ?? throw new InvalidDataException("Missing pinned vendor files"))
        {
            Require(record is not null, "Missing vendor file record");
            string relative = Relative(Text(record!, "path")), hash = Text(record!, "sha256");
            long length = record!["bytes"]?.GetValue<long>() ?? throw new InvalidDataException("Missing pinned vendor length");
            Require(portablePaths.Add(relative), "Duplicate manifest file: " + relative);
            Require(relative == license || relative.StartsWith(source + "/", StringComparison.Ordinal) && relative.EndsWith(".cs", StringComparison.Ordinal), "Manifest may publish only runtime C# and the original license");
            Require(length >= 0 && Regex.IsMatch(hash, "\\A[0-9a-f]{64}\\z"), "Invalid pinned vendor length/hash");
            var path = ExactPath(workspace, prefix + relative);
            Require(File.Exists(path) && new FileInfo(path).Length == length && Workspace.Hash(path) == hash, "Changed pinned upstream file: " + prefix + relative);
            expected.Add(prefix + relative);
        }
        Require(expected.Contains(prefix + license), "Original license is not included: " + id);
        string sources = workspace.LocalPath(prefix + source);
        if (Directory.Exists(sources))
            foreach (var file in Directory.EnumerateFiles(sources, "*", SearchOption.AllDirectories).Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                string relative = Path.GetRelativePath(workspace.Root, file).Replace('\\', '/');
                Require(expected.Contains(relative), "Unpinned vendor runtime source: " + relative);
            }
        return expected;
    }
    public static void VerifyPublicationInventory(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var wanted = expected.ToHashSet(StringComparer.Ordinal);
        var published = actual.ToHashSet(StringComparer.Ordinal);
        Require(wanted.SetEquals(published), "Publishable vendor subset differs from manifest; missing=" + string.Join(", ", wanted.Except(published).Order()) +
            "; extra=" + string.Join(", ", published.Except(wanted).Order()));
    }
    public static HashSet<string> PublicationFiles(string root)
    {
        var bundled = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache/codex-runtimes/codex-primary-runtime/dependencies/native/git/cmd/git.exe");
        var start = new ProcessStartInfo(File.Exists(bundled) ? bundled : "git")
        {
            WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        // Read-only: include new, nonignored files without requiring a staged migration.
        foreach (var argument in new[] { "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", SourcePrefix.TrimEnd('/') }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start local Git inventory");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Require(process.ExitCode == 0, "Cannot inspect local Git publication inventory: " + error.GetAwaiter().GetResult());
        // Deleted index entries are not publication candidates in the current worktree.
        return output.GetAwaiter().GetResult().Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => File.Exists(Path.Combine(root, path))).ToHashSet(StringComparer.Ordinal);
    }
    public static int Run(string root)
    {
        var workspace = new Workspace(root);
        var manifest = Workspace.Read(workspace.PathFor("SKECS.Benchmarks/Vendor/manifest.json"));
        ValidateManifest(manifest);
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in manifest["components"]!.AsArray()) expected.UnionWith(VerifyComponent(root, component!));
        VerifyPublicationInventory(expected, PublicationFiles(root));
        Console.WriteLine($"PASS: {Pins.Length} audited pinned components; {expected.Count} byte-identical sources/licenses; exact publishable subset.");
        return expected.Count;
    }
}
