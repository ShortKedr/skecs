using SKECS.BenchmarkTools;

namespace BenchmarkTools.Tests;

public sealed class WorkspaceTests
{
    [Fact]
    public void PathsRejectRootAndTraversal()
    {
        string root = Workspace.FindRoot();
        var workspace = new Workspace(root);
        Assert.Throws<ArgumentException>(() => workspace.LocalPath(root));
        Assert.Throws<ArgumentException>(() => workspace.LocalPath("../outside/result.json"));
        Assert.Equal(Path.Combine(root, "artifacts", "result.json"), workspace.LocalPath("artifacts/result.json"));
        Assert.Throws<ArgumentException>(() => workspace.ArtifactPath("docs/benchmarks/raw.json"));
        Assert.Throws<ArgumentException>(() => workspace.ArtifactPath("artifacts/../docs/raw.json"));
        Assert.Equal(Path.Combine(root, "artifacts", "result.json"), workspace.ArtifactPath("artifacts/result.json"));
    }

    [Fact]
    public void SourceInventoryContainsSharedNativeSourcesAndVendorWithoutArtifacts()
    {
        var workspace = new Workspace(Workspace.FindRoot());
        var sources = workspace.Sources();
        Assert.Contains("SKECS.Benchmarks/Workloads/StressContract.cs", sources.Keys);
        Assert.Contains(sources.Keys, p => p.StartsWith("SKECS.Benchmarks/Vendor/Sources/StaticEcs/Src/", StringComparison.Ordinal));
        Assert.DoesNotContain(sources.Keys, p => p.Contains("/tmp/") || p.Contains("/bin/") || p.Contains("/obj/") || p.Contains("/python-packages/"));
    }

    [Fact]
    public void VendorInventoryAndPortableCpuMetadataAreAvailable()
    {
        Assert.True(VendorVerifier.Run(Workspace.FindRoot()) > 100);
        Assert.False(string.IsNullOrWhiteSpace(Workspace.CpuName()));
    }
}
