using SKECS.BenchmarkTools;

namespace SKECS.BenchmarkTools.Tests;

public sealed class RunnerTests
{
    [Fact]
    public void WorkspacePathsPreserveSpacesAndRejectEscapes()
    {
        using var fixture = new Fixture();
        var workspace = new Workspace(fixture.Root);
        Assert.Equal(Path.GetFullPath(Path.Combine(fixture.Root, "artifacts", "run with spaces")), workspace.LocalPath("artifacts/run with spaces"));
        Assert.Throws<ArgumentException>(() => workspace.LocalPath("."));
        Assert.Throws<ArgumentException>(() => workspace.LocalPath("../outside"));
        Assert.Throws<ArgumentException>(() => workspace.LocalPath(Path.GetFullPath(Path.Combine(fixture.Root, "../outside"))));
    }

    [Fact]
    public void SourceSnapshotIncludesRuntimeAndGeneratedInputsAndExcludesOutputs()
    {
        using var fixture = new Fixture();
        fixture.Write("SKECS.Benchmarks/Vendor/manifest.json", "{\"components\":[]}");
        fixture.Write("SKECS.Benchmarks/Comparison/ComparisonRunner.cs", "// measurement runner\n");
        fixture.Write("SKECS.Benchmarks/Workloads/Workload.cs", "// shared workload\n");
        fixture.Write("SKECS.Benchmarks/CodeGen/benchmark-model.json", "{}\n");
        fixture.Write("SKECS.Benchmarks/CodeGen/Templates/Adapter.cs.in", "// adapter template\n");
        fixture.Write("SKECS/SKECS.csproj", "<Project />\n");
        fixture.Write("SKECS.Benchmarks/bin/Release/net8.0/Generated.cs", "// output\n");
        fixture.Write("artifacts/tooling/tmp/work/Notes.cs", "// temporary\n");
        fixture.Write("artifacts/benchmarks/current/micro/result.json", "{}\n");
        var workspace = new Workspace(fixture.Root);
        var snapshot = workspace.Sources();
        Assert.Equal(6, snapshot.Count);
        Assert.Contains("SKECS.Benchmarks/Workloads/Workload.cs", snapshot.Keys);
        Assert.Contains("SKECS.Benchmarks/CodeGen/Templates/Adapter.cs.in", snapshot.Keys);
        Assert.Contains("SKECS.Benchmarks/CodeGen/benchmark-model.json", snapshot.Keys);
        workspace.AssertHashes(snapshot);
        fixture.Write("SKECS.Benchmarks/Workloads/Workload.cs", "// changed measurement operation\n");
        Assert.Throws<InvalidDataException>(() => workspace.AssertHashes(snapshot));
    }

    [Fact]
    public void BinarySnapshotCoversLibrariesAndRuntimeConfiguration()
    {
        using var fixture = new Fixture();
        string prefix = "SKECS.Benchmarks/bin/Release/net8.0/";
        fixture.Write(prefix + "SKECS.Benchmarks.dll", "binary fixture");
        fixture.Write(prefix + "SKECS.dll", "core fixture");
        fixture.Write(prefix + "SKECS.Benchmarks.deps.json", "{}\n");
        fixture.Write(prefix + "SKECS.Benchmarks.runtimeconfig.json", "{}\n");
        fixture.Write(prefix + "SKECS.Benchmarks.pdb", "debug output");
        var workspace = new Workspace(fixture.Root);
        var snapshot = workspace.Binaries(["SKECS.Benchmarks/SKECS.Benchmarks.csproj"]);
        Assert.Equal(4, snapshot.Count);
        workspace.AssertHashes(snapshot);
        fixture.Write(prefix + "SKECS.dll", "changed assembly");
        Assert.Throws<InvalidDataException>(() => workspace.AssertHashes(snapshot));
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
            parent = Path.Combine(directory.FullName, "artifacts/tooling/tmp/runner-tests");
            Root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }
        public void Write(string relative, string content)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        public void Dispose()
        {
            if (!Path.GetFullPath(Root).StartsWith(Path.GetFullPath(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture path");
            Directory.Delete(Root, true);
        }
    }
}
