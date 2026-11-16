namespace Skecs.Agentic.Tests;

public sealed class LocalWorkspaceTests
{
    [Fact]
    public void ResolvesStrictPortableRelativePathsUnderRoot()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "agentic-workspace"));
        var workspace = new LocalWorkspace(root);

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "artifacts", "run-1", "result.json")),
            workspace.PathFor("artifacts/run-1/result.json"));
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "README.md")), workspace.PathFor("README.md"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("./file")]
    [InlineData("../outside")]
    [InlineData("a/../outside")]
    [InlineData("a//b")]
    [InlineData("a/")]
    [InlineData("/rooted")]
    [InlineData("C:/drive")]
    [InlineData("name:stream")]
    [InlineData("a\\b")]
    [InlineData("trailing.")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("PRN.log")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9.csv")]
    [InlineData("bad space")]
    [InlineData("é")]
    public void RejectsInvalidPortableRelativePaths(string relative)
    {
        var workspace = new LocalWorkspace(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "agentic-workspace")));

        Assert.Throws<ArgumentException>(() => workspace.PathFor(relative));
    }

    [Fact]
    public void RejectsPathsThatTraverseAnExistingReparsePoint()
    {
        string root = FindRepositoryRoot();
        string parent = Path.Combine(root, "artifacts", "tooling", "tmp", "agentic-workspace-tests");
        string fixture = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(fixture, "linked"), outside);
            var workspace = new LocalWorkspace(fixture);

            Assert.Throws<ArgumentException>(() => workspace.PathFor("linked/file.txt"));
        }
        finally
        {
            Directory.Delete(fixture, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SKECS.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Workspace root missing.");
    }
}
