using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SKECS.BenchmarkTools;

namespace SKECS.BenchmarkTools.Tests;

public sealed class VendorRestoreTests
{
    const string Revision = "0123456789012345678901234567890123456789";
    const string Prefix = "demo-" + Revision + "/";
    static readonly byte[] License = Encoding.UTF8.GetBytes("original upstream license\r\n");
    static readonly byte[] Code = Encoding.UTF8.GetBytes("public class PinnedRuntime {}\n");
    static JsonNode Component() => new JsonObject
    {
        ["id"] = "demo", ["name"] = "Demo", ["repository"] = "https://github.com/upstream/demo", ["revision"] = Revision,
        ["license_file"] = "LICENSE.md", ["source_root"] = "src", ["files"] = new JsonArray(Record("LICENSE.md", License), Record("src/Runtime.cs", Code))
    };
    static JsonObject Record(string path, byte[] bytes) => new() { ["path"] = path, ["bytes"] = bytes.LongLength, ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)) };
    static MemoryStream Archive(string? mutation = null)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add("LICENSE.md", License);
            if (mutation != "missing") Add("src/Runtime.cs", mutation == "hash" ? Enumerable.Repeat((byte)'X', Code.Length).ToArray() : Code);
            if (mutation == "ambiguous") Add("src/Runtime.cs", Code);
            Add("README.md", Encoding.UTF8.GetBytes("Must not be distributed"));
            Add("tools/Tool.cs", Encoding.UTF8.GetBytes("Unselected upstream tool"));
            void Add(string relative, byte[] bytes) { using var member = zip.CreateEntry(Prefix + relative).Open(); member.Write(bytes); }
        }
        stream.Position = 0;
        return stream;
    }
    [Fact]
    public void RestoresOnlyPinnedRuntimeAndLicenseBytesAndIsIdempotent()
    {
        using var fixture = new Fixture(); using var zip = Archive();
        VendorRestore.RestoreArchive(fixture.Root, Component(), zip);
        string vendor = Path.Combine(fixture.Root, "Benchmarks.Local/vendor/demo");
        Assert.Equal(License, File.ReadAllBytes(Path.Combine(vendor, "LICENSE.md")));
        Assert.Equal(Code, File.ReadAllBytes(Path.Combine(vendor, "src/Runtime.cs")));
        Assert.Equal(2, Directory.EnumerateFiles(vendor, "*", SearchOption.AllDirectories).Count());
        using var again = Archive();
        VendorRestore.RestoreArchive(fixture.Root, Component(), again);
        Assert.Equal(2, Directory.EnumerateFiles(vendor, "*", SearchOption.AllDirectories).Count());
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("hash")]
    public void RejectsInvalidArchiveBeforeWritingAnyPinnedFile(string mutation)
    {
        using var fixture = new Fixture(); using var zip = Archive(mutation);
        Assert.Throws<InvalidDataException>(() => VendorRestore.RestoreArchive(fixture.Root, Component(), zip));
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }
    [Fact]
    public void PreservesLocalEditsAndRejectsManifestTraversal()
    {
        using var fixture = new Fixture();
        string target = Path.Combine(fixture.Root, "Benchmarks.Local/vendor/demo/LICENSE.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, "local edit");
        using (var zip = Archive()) Assert.Throws<InvalidDataException>(() => VendorRestore.RestoreArchive(fixture.Root, Component(), zip));
        Assert.Equal("local edit", File.ReadAllText(target));
        Assert.Single(Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories));
        var component = Component(); component["files"]![0]!["path"] = "../escape.cs";
        using var traversal = Archive();
        Assert.Throws<InvalidDataException>(() => VendorRestore.RestoreArchive(fixture.Root, component, traversal));
        Assert.Single(Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }
    sealed class Fixture : IDisposable
    {
        readonly string parent;
        public string Root { get; }
        public Fixture()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SKECS.sln"))) directory = directory.Parent;
            if (directory == null) throw new DirectoryNotFoundException("Workspace root missing");
            parent = Path.Combine(directory.FullName, "Benchmarks.Local/tmp/vendor-restore-tests");
            Root = Path.Combine(parent, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Root);
        }
        public void Dispose()
        {
            if (!Path.GetFullPath(Root).StartsWith(Path.GetFullPath(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture path");
            Directory.Delete(Root, true);
        }
    }
}
