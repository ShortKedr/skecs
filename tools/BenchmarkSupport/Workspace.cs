using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace SKECS.BenchmarkTools;

public sealed class Workspace
{
    public string Root { get; }
    public Workspace(string root) => Root = Path.GetFullPath(root);
    public string PathFor(string relative) => Path.GetFullPath(relative, Root);
    public string LocalPath(string path)
    {
        var full = PathFor(path);
        var relative = Path.GetRelativePath(Root, full);
        if (relative is "." or ".." || Path.IsPathRooted(relative) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Output must stay inside this workspace: " + full);
        if (File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint))
            throw new ArgumentException("Output through a file link is not allowed: " + full);
        for (var directory = new DirectoryInfo(full);
             directory is not null && directory.FullName.Length > Root.Length; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new ArgumentException("Output through a directory link is not allowed: " + directory.FullName);
        return full;
    }
    public string ArtifactPath(string path)
    {
        var full = LocalPath(path);
        var relative = Path.GetRelativePath(PathFor("artifacts"), full);
        if (relative is "." or ".." || Path.IsPathRooted(relative) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Raw benchmark outputs must stay under ignored artifacts/: " + full);
        return full;
    }
    public static string FindRoot(string? supplied = null)
    {
        if (supplied is not null)
        {
            var full = Path.GetFullPath(supplied);
            if (!File.Exists(Path.Combine(full, "SKECS.sln"))) throw new ArgumentException("Workspace has no SKECS.sln.");
            return full;
        }
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "SKECS.sln"))) return directory.FullName;
        throw new ArgumentException("Run inside the SKECS workspace, or supply --root.");
    }
    public void ConfigureEnvironment()
    {
        var temporary = PathFor("artifacts/tooling/tmp");
        Directory.CreateDirectory(temporary);
        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["DOTNET_CLI_HOME"] = PathFor("artifacts/tooling/dotnet-home"),
            ["NUGET_PACKAGES"] = PathFor("artifacts/tooling/nuget-packages"),
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(temporary, "nuget-http"),
            ["TEMP"] = temporary, ["TMP"] = temporary, ["TMPDIR"] = temporary,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true",
            ["MSBuildEnableWorkloadResolver"] = "false", ["DOTNET_TieredCompilation"] = "0"
        }) Environment.SetEnvironmentVariable(name, value);
    }
    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public static JsonNode Read(string path) => JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty JSON: " + path);
    public static void Write(string path, JsonNode value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, value.ToJsonString(new() { WriteIndented = true }) + "\n", new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
    public SortedDictionary<string, string> Sources()
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string[] directories = ["SKECS", "SKECS.Benchmarks", "SpaceBattle.Core", "SpaceBattle.SKECS", "SpaceBattle.Adapters", "SpaceBattle.Benchmarks", "SpaceBattle.Game", "tools/BenchmarkSupport"];
        foreach (var directory in directories)
            if (Directory.Exists(PathFor(directory))) Visit(PathFor(directory));
        foreach (var file in new[] { "SKECS.sln", "Directory.Build.props", "Directory.Build.targets", "global.json", "NuGet.Config", "tools/BenchmarkMachineLabel.cs", "tools/BenchmarkMachineInfo.cs" })
            if (File.Exists(PathFor(file))) result[file] = Hash(PathFor(file));
        foreach (var component in Read(PathFor("SKECS.Benchmarks/Vendor/manifest.json"))["components"]!.AsArray())
            foreach (var file in component!["files"]!.AsArray())
            {
                var relative = $"SKECS.Benchmarks/Vendor/Sources/{component["id"]!.GetValue<string>()}/{file!["path"]!.GetValue<string>()}";
                result[relative] = Hash(PathFor(relative));
            }
        return result;

        void Visit(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                string relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
                bool json = relative.EndsWith("Vendor/manifest.json", StringComparison.Ordinal) || relative.Contains("/CodeGen/");
                if (relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                    relative.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) ||
                    relative.EndsWith(".cs.in", StringComparison.OrdinalIgnoreCase) || (json && relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                    result[relative] = Hash(file);
            }
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(child);
                if (name is "bin" or "obj" or "tmp" or "vendor" or "Sources" or ".git" or ".dotnet" or "node_modules" or "nuget-packages" or "python-packages" or "matplotlib-cache" or "__pycache__" or "baseline-bin" || name.StartsWith("bdn-", StringComparison.Ordinal) || name.EndsWith("-bdn-artifacts", StringComparison.Ordinal)) continue;
                if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)) continue;
                Visit(child);
            }
        }
    }
    public SortedDictionary<string, string> Binaries(IEnumerable<string> projects)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            var directory = PathFor(Path.Combine(Path.GetDirectoryName(project)!, "bin/Release/net8.0"));
            if (!Directory.Exists(directory)) throw new InvalidDataException("Build output missing: " + directory);
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
                    hashes[Path.GetRelativePath(Root, file).Replace('\\', '/')] = Hash(file);
        }
        return hashes;
    }
    public void AssertHashes(IReadOnlyDictionary<string, string> hashes)
    {
        foreach (var (file, digest) in hashes)
            if (!File.Exists(PathFor(file)) || Hash(PathFor(file)) != digest) throw new InvalidDataException("Campaign input changed: " + file);
    }
    public static JsonObject HashObject(IReadOnlyDictionary<string, string> values)
        => new(values.Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value))));
    public static string CpuName() => BenchmarkMachineInfo.CpuName;
}
