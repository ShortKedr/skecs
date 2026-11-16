using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Skecs.Agentic;

internal sealed class LocalWorkspace(string root)
{
    private static readonly Regex Segment = new(@"\A[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant);
    private static readonly Regex Device = new(@"\A(?:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|\z)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public const string Output = "artifacts/agentic";
    public const string BuildOutput = "artifacts/agentic-build";
    public static readonly string[] GitOptions = ["--no-pager", "--no-optional-locks", "-c", "core.fsmonitor=false"];
    public string Root { get; } = Path.GetFullPath(root);

    public static LocalWorkspace Find(string from)
    {
        for (var directory = new DirectoryInfo(from); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SKECS.sln")) &&
                Path.Exists(Path.Combine(directory.FullName, ".git")))
                return new(directory.FullName);
        throw new ArgumentException("Run from the local SKECS checkout.");
    }

    public string PathFor(string relative)
    {
        if (relative.Split('/').Any(part => !Segment.IsMatch(part) || part.EndsWith('.') || Device.IsMatch(part)))
            throw new ArgumentException("Expected a portable, workspace-relative path: " + relative);
        return CheckedPath(relative);
    }

    public string ReplayInput(string relative)
    {
        string full = PathFor(relative);
        if (!relative.StartsWith("artifacts/", StringComparison.Ordinal) || !Directory.Exists(full))
            throw new ArgumentException("Expected an existing replay directory under artifacts/: " + relative);
        foreach (string file in Directory.EnumerateFileSystemEntries(full))
            _ = PathFor(relative + "/" + Path.GetFileName(file));
        return relative;
    }

    public string CorpusInput(string relative)
    {
        string full = PathFor(relative);
        if (!relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw new ArgumentException("Expected a workspace-relative JSON corpus file: " + relative);
        return full;
    }

    public async Task<ReplayIdentity> ReplayIdentityAsync(string relative, CancellationToken cancellation)
    {
        string directory = PathFor(ReplayInput(relative));
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var names = Directory.EnumerateFileSystemEntries(directory).Select(path => Path.GetFileName(path))
            .Order(StringComparer.Ordinal).ToArray();
        foreach (string name in names)
        {
            cancellation.ThrowIfCancellationRequested();
            string path = PathFor(relative + "/" + name);
            if (!File.Exists(path))
                throw new ArgumentException("Expected regular files in replay directory: " + relative);
            digest.AppendData(Encoding.UTF8.GetBytes(name + "\0"));
            await using var input = File.OpenRead(path);
            digest.AppendData(await SHA256.HashDataAsync(input, cancellation));
        }
        return new(relative, names.Length, Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant());
    }

    public string CheckedPath(string relative)
    {
        string full = Path.GetFullPath(relative, Root);
        string local = Path.GetRelativePath(Root, full);
        if (local is "." or ".." || Path.IsPathRooted(local) ||
            local.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Path must stay inside the checkout: " + relative);

        string current = Root;
        foreach (string part in local.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new ArgumentException("Path through a link is not allowed: " + relative);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return full;
    }

    public Dictionary<string, string> EnvironmentForTools()
    {
        string temporary = PathFor("artifacts/tooling/tmp");
        Directory.CreateDirectory(temporary);
        return new()
        {
            ["DOTNET_CLI_HOME"] = PathFor("artifacts/tooling/dotnet-home"),
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["NUGET_PACKAGES"] = PathFor("artifacts/tooling/nuget-packages"),
            ["NUGET_HTTP_CACHE_PATH"] = PathFor("artifacts/tooling/nuget-http-cache"),
            ["NUGET_SCRATCH"] = temporary,
            ["TEMP"] = temporary,
            ["TMP"] = temporary,
            ["TMPDIR"] = temporary
        };
    }

    public async Task<FileStream> LockBuildAsync(CancellationToken cancellation)
    {
        Directory.CreateDirectory(PathFor(BuildOutput));
        string path = PathFor(BuildOutput + "/build.lock");
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try { return File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None); }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 11 or 32 or 33)
            {
                await Task.Delay(100, cancellation);
            }
        }
    }

    public async Task<SourceIdentity> SourceAsync(ProcessRunner runner, string runDirectory, string suffix, CancellationToken cancellation)
    {
        async Task<string> Git(string step, params string[] args)
        {
            var command = new Command(step + suffix, "git", [.. GitOptions, .. args]);
            var result = await runner.ExecuteAsync(command, cancellation);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"Source capture failed at {result.Name}. See {result.Stderr}.");
            return await File.ReadAllTextAsync(PathFor(result.Stdout), cancellation);
        }

        string head = (await Git("head", "rev-parse", "--verify", "HEAD")).Trim();
        string status = await Git("status", "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=all");
        string paths = await Git("files", "ls-files", "--cached", "--others", "--exclude-standard", "-z");
        var files = paths.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        // Hash file bytes directly so index flags, file stat caches and Git filters do not hide edits.
        string manifest = runDirectory + "/source" + suffix + ".sha256";
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var log = new StreamWriter(PathFor(manifest), append: false);
        int count = 0;
        foreach (string file in files)
        {
            cancellation.ThrowIfCancellationRequested();
            string path = CheckedPath(file);
            string hash;
            if (File.Exists(path))
            {
                await using var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation)).ToLowerInvariant();
            }
            else
                hash = "missing";
            string row = hash + " " + System.Text.Json.JsonSerializer.Serialize(file) + "\n";
            digest.AppendData(Encoding.UTF8.GetBytes(row));
            await log.WriteAsync(row.AsMemory(), cancellation);
            count++;
        }

        return new(head, status.Length != 0, Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(),
            count, manifest, runDirectory + "/status" + suffix + ".stdout.log");
    }
}

internal sealed record SourceIdentity(string Head, bool Dirty, string WorktreeSha256, int FileCount, string Manifest, string StatusLog);
internal sealed record ReplayIdentity(string Directory, int FileCount, string Sha256);
