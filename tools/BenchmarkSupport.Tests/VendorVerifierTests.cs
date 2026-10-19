using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SKECS.BenchmarkTools;

namespace SKECS.BenchmarkTools.Tests;

public sealed class VendorVerifierTests
{
    const string Prefix = "SKECS.Benchmarks/Vendor/Sources/probe/";
    static readonly byte[] License = Encoding.UTF8.GetBytes("Original upstream license\r\n");
    static readonly byte[] Code = Encoding.UTF8.GetBytes("public class Runtime {}\n");
    static JsonObject Record(string path, byte[] bytes) => new()
    {
        ["path"] = path, ["bytes"] = bytes.LongLength, ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
    };
    static JsonNode Component() => new JsonObject
    {
        ["id"] = "probe", ["revision"] = new string('a', 40), ["source_root"] = "src", ["license_file"] = "LICENSE.md",
        ["files"] = new JsonArray(Record("LICENSE.md", License), Record("src/Runtime.cs", Code))
    };
    static string Root => Workspace.FindRoot();
    static JsonNode Manifest() => Workspace.Read(Path.Combine(Root, "SKECS.Benchmarks/Vendor/manifest.json"));

    [Fact]
    public void VerifiesAllAuditedBytesAndExactCurrentGitPublicationInventory()
        => Assert.Equal(143, VendorVerifier.Run(Root));

    [Theory]
    [InlineData("name", "Other ECS")]
    [InlineData("repository", "https://github.com/other/ecs")]
    [InlineData("revision", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("license", "MIT")]
    [InlineData("license_file", "src/EcsEntity.cs")]
    [InlineData("source_root", "tools")]
    public void RejectsManifestMetadataChangesEvenIfFileHashesAreUnchanged(string field, string replacement)
    {
        var manifest = Manifest();
        manifest["components"]![0]![field] = replacement;
        Assert.Contains("audited pin", Assert.Throws<InvalidDataException>(() => VendorVerifier.ValidateManifest(manifest)).Message);
    }
    [Fact]
    public void RequiresSupportedFormatDistributionAndAllDistinctAuditedDependencies()
    {
        var manifest = Manifest(); manifest["format"] = 2;
        Assert.Throws<InvalidDataException>(() => VendorVerifier.ValidateManifest(manifest));
        manifest = Manifest(); manifest["distribution"] = "full-upstream";
        Assert.Throws<InvalidDataException>(() => VendorVerifier.ValidateManifest(manifest));
        manifest = Manifest(); manifest["components"]!.AsArray().RemoveAt(4);
        Assert.Throws<InvalidDataException>(() => VendorVerifier.ValidateManifest(manifest));
        manifest = Manifest(); manifest["components"]!.AsArray().Add(manifest["components"]![0]!.DeepClone());
        Assert.Contains("Duplicate", Assert.Throws<InvalidDataException>(() => VendorVerifier.ValidateManifest(manifest)).Message);
    }
    [Fact]
    public void ChecksOriginalLicenseBytesAndRuntimeInventory()
    {
        using var fixture = new Fixture();
        Assert.Equal(new[] { Prefix + "LICENSE.md", Prefix + "src/Runtime.cs" }, VendorVerifier.VerifyComponent(fixture.Root, Component()).Order());
        File.WriteAllText(fixture.PathFor("LICENSE.md"), "Changed original license\r\n");
        Assert.Contains("Changed pinned", Assert.Throws<InvalidDataException>(() => VendorVerifier.VerifyComponent(fixture.Root, Component())).Message);
        File.WriteAllBytes(fixture.PathFor("LICENSE.md"), License);
        File.WriteAllText(fixture.PathFor("src/Unpinned.cs"), "Unselected runtime code");
        Assert.Contains("Unpinned vendor runtime", Assert.Throws<InvalidDataException>(() => VendorVerifier.VerifyComponent(fixture.Root, Component())).Message);
    }
    [Theory]
    [InlineData("../LICENSE.md")]
    [InlineData("/LICENSE.md")]
    [InlineData("src\\Runtime.cs")]
    [InlineData("src/../Runtime.cs")]
    [InlineData("src//Runtime.cs")]
    public void RejectsUnsafePaths(string path)
    {
        using var fixture = new Fixture(); var component = Component(); component["files"]![0]!["path"] = path;
        Assert.Contains("Unsafe manifest path", Assert.Throws<InvalidDataException>(() => VendorVerifier.VerifyComponent(fixture.Root, component)).Message);
    }
    [Fact]
    public void RequiresOriginalLicenseAndExactCaseWithoutDuplicateManifestFiles()
    {
        using var fixture = new Fixture();
        var component = Component(); component["files"]!.AsArray().RemoveAt(0);
        Assert.Contains("license is not included", Assert.Throws<InvalidDataException>(() => VendorVerifier.VerifyComponent(fixture.Root, component)).Message);
        component = Component(); component["files"]![1]!["path"] = "src/runtime.cs";
        Assert.Contains("wrong-case", Assert.Throws<InvalidDataException>(() => VendorVerifier.VerifyComponent(fixture.Root, component)).Message);
        component = Component(); component["files"]!.AsArray().Add(component["files"]![0]!.DeepClone());
        Assert.Contains("Duplicate", Assert.Throws<InvalidDataException>(() => VendorVerifier.VerifyComponent(fixture.Root, component)).Message);
    }
    [Fact]
    public void RejectsExtraPublishableNonSourceFilesAndMissingOrWrongCaseInventoryEntries()
    {
        string[] expected = [Prefix + "LICENSE.md", Prefix + "src/Runtime.cs"];
        VendorVerifier.VerifyPublicationInventory(expected, expected.Reverse());
        Assert.Throws<InvalidDataException>(() => VendorVerifier.VerifyPublicationInventory(expected, expected.Append(Prefix + "README.md")));
        Assert.Throws<InvalidDataException>(() => VendorVerifier.VerifyPublicationInventory(expected, expected.Take(1)));
        Assert.Throws<InvalidDataException>(() => VendorVerifier.VerifyPublicationInventory(expected, expected.Select(path => path.ToLowerInvariant())));
    }
    [Fact]
    public void GitInventoryIncludesUnstagedMovesAndSkipsDeletedIndexEntriesAndIgnoredFiles()
    {
        using var fixture = new Fixture();
        RunGit(fixture.Root, "init", "--quiet");
        File.WriteAllText(Path.Combine(fixture.Root, ".gitignore"), "ignored.txt\n");
        File.WriteAllText(fixture.PathFor("deleted.txt"), "formerly tracked");
        RunGit(fixture.Root, "add", "--", Prefix + "deleted.txt");
        File.Delete(fixture.PathFor("deleted.txt"));
        File.WriteAllText(fixture.PathFor("ignored.txt"), "not publishable");
        Assert.Equal(new[] { Prefix + "LICENSE.md", Prefix + "src/Runtime.cs" }, VendorVerifier.PublicationFiles(fixture.Root).Order());
    }
    static void RunGit(string root, params string[] arguments)
    {
        var bundled = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache/codex-runtimes/codex-primary-runtime/dependencies/native/git/cmd/git.exe");
        var start = new ProcessStartInfo(File.Exists(bundled) ? bundled : "git")
        {
            WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd(); process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }
    sealed class Fixture : IDisposable
    {
        readonly string parent;
        public string Root { get; }
        public Fixture()
        {
            parent = Path.Combine(VendorVerifierTests.Root, "artifacts/tooling/tmp/vendor-verifier-tests");
            Root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.GetDirectoryName(PathFor("src/Runtime.cs"))!);
            File.WriteAllBytes(PathFor("LICENSE.md"), License); File.WriteAllBytes(PathFor("src/Runtime.cs"), Code);
        }
        public string PathFor(string path) => Path.Combine(Root, Prefix, path);
        public void Dispose()
        {
            if (!Path.GetFullPath(Root).StartsWith(Path.GetFullPath(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture path");
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
    }
}
