namespace SKECS.BenchmarkTools;

public static class VendorVerifier
{
    public static int Run(string root)
    {
        var workspace = new Workspace(root);
        var manifest = Workspace.Read(workspace.PathFor("Benchmarks.Local/vendor-manifest.json"));
        int count = 0;
        foreach (var component in manifest["components"]!.AsArray())
        {
            var prefix = "Benchmarks.Local/vendor/" + component!["id"]!.GetValue<string>() + "/";
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in component["files"]!.AsArray())
            {
                var relative = record!["path"]!.GetValue<string>();
                var path = workspace.LocalPath(prefix + relative);
                if (!expected.Add(relative) || !File.Exists(path) || new FileInfo(path).Length != record["bytes"]!.GetValue<long>() || Workspace.Hash(path) != record["sha256"]!.GetValue<string>())
                    throw new InvalidDataException("Pinned vendor file differs: " + prefix + relative);
                count++;
            }
            var sources = workspace.PathFor(prefix + component["source_root"]!.GetValue<string>());
            foreach (var file in Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(workspace.PathFor(prefix), file).Replace('\\', '/');
                if (!expected.Contains(relative)) throw new InvalidDataException("Unpinned vendor runtime source: " + prefix + relative);
            }
        }
        Console.WriteLine($"PASS: {count} byte-identical pinned vendor files.");
        return count;
    }
}
