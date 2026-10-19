using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SKECS.BenchmarkTools;

/// <summary>Restores only byte-pinned runtime sources and licenses using read-only downloads.</summary>
public static class VendorRestore
{
    sealed record PinnedFile(string Relative, string Target, long Length, string Hash);
    static string Text(JsonNode component, string key) => component[key]?.GetValue<string>() ?? throw new InvalidDataException("Missing vendor field: " + key);
    static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    static string Relative(string value)
    {
        Require(!string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) && !value.Contains('\\') && !value.Contains(':') &&
            value.Split('/').All(part => part.Length > 0 && part is not "." and not ".."), "Invalid vendor path: " + value);
        return value;
    }
    static (Uri Archive, string Prefix, PinnedFile[] Files) Describe(Workspace workspace, JsonNode component)
    {
        string id = Text(component, "id"), revision = Text(component, "revision");
        Require(Regex.IsMatch(id, "\\A[A-Za-z0-9_-]+\\z") && Regex.IsMatch(revision, "\\A[0-9a-fA-F]{40}\\z"), "Invalid pinned vendor identity/revision");
        var repository = new Uri(Text(component, "repository"), UriKind.Absolute);
        string[] segments = repository.AbsolutePath.Trim('/').Split('/');
        Require(repository.Scheme == "https" && repository.Host == "github.com" && repository.IsDefaultPort && repository.UserInfo.Length == 0 &&
            repository.Query.Length == 0 && repository.Fragment.Length == 0 && segments.Length == 2 && segments.All(s => Regex.IsMatch(s, "\\A[A-Za-z0-9_.-]+\\z") && s is not "." and not ".."), "Vendor repository must be a GitHub owner/repository HTTPS URL");
        string license = Relative(Text(component, "license_file")), source = Relative(Text(component, "source_root"));
        string prefix = $"SKECS.Benchmarks/Vendor/Sources/{id}/";
        var files = new List<PinnedFile>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var item in component["files"]?.AsArray() ?? throw new InvalidDataException("Missing pinned vendor files"))
        {
            string relative = Relative(Text(item!, "path")), hash = Text(item!, "sha256");
            long length = item!["bytes"]!.GetValue<long>();
            Require(seen.Add(relative), "Duplicate vendor manifest path: " + relative);
            Require(relative == license || relative.StartsWith(source + "/", StringComparison.Ordinal) && relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase), "Manifest may restore only runtime C# and the original license");
            Require(length >= 0 && length <= int.MaxValue && Regex.IsMatch(hash, "\\A[0-9a-fA-F]{64}\\z"), "Invalid pinned vendor length/hash");
            files.Add(new(relative, workspace.LocalPath(prefix + relative), length, hash));
        }
        Require(files.Count > 0 && files.Any(f => f.Relative == license), "Pinned vendor license is missing");
        return (new Uri($"https://codeload.github.com/{segments[0]}/{segments[1]}/zip/{revision}"), $"{segments[1]}-{revision}/", files.ToArray());
    }
    static bool Matches(string path, PinnedFile file) => new FileInfo(path).Length == file.Length && Workspace.Hash(path).Equals(file.Hash, StringComparison.OrdinalIgnoreCase);
    static void VerifyExisting(IEnumerable<PinnedFile> files)
    {
        foreach (var file in files)
            if (File.Exists(file.Target)) Require(Matches(file.Target, file), "Existing source differs from pinned manifest; refusing to overwrite: " + file.Target);
    }
    public static async Task RunAsync(string root)
    {
        var workspace = new Workspace(root);
        var manifest = Workspace.Read(workspace.LocalPath("SKECS.Benchmarks/Vendor/manifest.json"));
        VendorVerifier.ValidateManifest(manifest);
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SKECS-BenchmarkTools/1.0");
        foreach (var component in manifest["components"]!.AsArray())
        {
            var specification = Describe(workspace, component!);
            VerifyExisting(specification.Files);
            if (specification.Files.All(f => File.Exists(f.Target)))
            {
                Console.WriteLine("Verified: " + Text(component!, "name"));
                continue;
            }
            using var response = await client.GetAsync(specification.Archive, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync();
            RestoreArchive(root, component!, stream);
            Console.WriteLine("Restored verified runtime subset: " + Text(component!, "name"));
        }
    }
    /// <summary>Offline entry point. Validates every selected member before any updates; leaves unrelated ZIP entries untouched.</summary>
    public static void RestoreArchive(string root, JsonNode component, Stream zipStream)
    {
        var workspace = new Workspace(root);
        var specification = Describe(workspace, component);
        VerifyExisting(specification.Files);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        var staged = new List<(PinnedFile File, byte[] Bytes)>();
        foreach (var file in specification.Files)
        {
            string member = specification.Prefix + file.Relative;
            var entries = zip.Entries.Where(e => e.FullName == member).ToArray();
            Require(entries.Length == 1, "Missing or ambiguous pinned archive member: " + file.Relative);
            var entry = entries[0];
            Require(entry.Length == file.Length && ((entry.ExternalAttributes >> 16) & 0xf000) != 0xa000, "Archive member length/type mismatch: " + file.Relative);
            using var input = entry.Open(); using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            byte[] bytes = buffer.ToArray();
            Require(bytes.LongLength == file.Length && Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.Hash, StringComparison.OrdinalIgnoreCase), "Archive member hash mismatch: " + file.Relative);
            staged.Add((file, bytes));
        }
        VerifyExisting(specification.Files);
        var temporary = new List<(PinnedFile File, string Path)>();
        var installed = new List<PinnedFile>();
        try
        {
            foreach (var item in staged.Where(item => !File.Exists(item.File.Target)))
            {
                string target = workspace.LocalPath(item.File.Target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temporaryPath = workspace.LocalPath(target + "." + Guid.NewGuid().ToString("N") + ".tmp");
                temporary.Add((item.File, temporaryPath));
                using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                output.Write(item.Bytes); output.Flush(true);
            }
            foreach (var item in temporary)
            {
                string target = workspace.LocalPath(item.File.Target);
                File.Move(item.Path, target, overwrite: false);
                installed.Add(item.File);
            }
        }
        catch
        {
            foreach (var file in installed) if (File.Exists(file.Target) && Matches(file.Target, file)) File.Delete(workspace.LocalPath(file.Target));
            throw;
        }
        finally
        {
            foreach (var item in temporary) if (File.Exists(item.Path)) File.Delete(workspace.LocalPath(item.Path));
        }
    }
}
