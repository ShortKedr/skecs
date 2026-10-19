using System.Text;
using System.Text.Json;
using SKECS.BenchmarkTools;

namespace SKECS.BenchmarkTools.Tests;

public sealed class GeneratorTests
{
    private const string Shared = "benchmarks/SKECS.Benchmarks.Workloads/";

    [Fact]
    public void NativeBackendsShareShapeMapping()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, BenchmarkCodeGenerator.Run(fixture.Root, false));
        var contract = fixture.Read(Shared + "StressContract.cs");
        var skecs = fixture.Read(Shared + "SkecsStressBackend.cs");
        var staticBackend = fixture.Read("Benchmarks.Local/StaticAdapters.cs");
        var others = fixture.Read("Benchmarks.Local/StressAdapters.cs");
        Assert.Contains("LayoutCount = 256", contract);
        for (var number = 1; number <= 8; number++)
        {
            var bit = 1 << (number - 1);
            Assert.Contains($"if ((shape & {bit}) != 0) _world.Add(entity, new StressTag{number}", skecs);
            Assert.Contains($"if ((shape & {bit}) != 0) e.Set<T{number}>();", staticBackend);
            Assert.Contains($"if ((shape & {bit}) != 0) entity.Get<WrappedTag{number}>()", others);
            Assert.Equal(2, others.Split($"if ((shape & {bit}) != 0) tag{number}.Add(entity)").Length - 1);
            foreach (var source in new[] { skecs, staticBackend, others }) Assert.Contains($"shape |= {bit};", source);
        }
    }

    [Fact]
    public void ShapeCountComesFromTheModel()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "Benchmarks.Local/CodeGen/benchmark-model.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"shapeBits\": 8", "\"shapeBits\": 4"));
        Assert.Equal(0, BenchmarkCodeGenerator.Run(fixture.Root, false));
        var contract = fixture.Read(Shared + "StressContract.cs");
        Assert.Contains("LayoutCount = 16", contract);
        Assert.Contains("struct StressTag4", contract);
        Assert.DoesNotContain("struct StressTag5", contract);
        var skecs = fixture.Read(Shared + "SkecsStressBackend.cs");
        Assert.Contains("if ((shape & 8) != 0) _world.Add(entity, new StressTag4", skecs);
        Assert.DoesNotContain("StressTag5", skecs);
    }

    [Fact]
    public void CatalogDefinesRegistriesAndEntryPoints()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, BenchmarkCodeGenerator.Run(fixture.Root, false));
        using var model = JsonDocument.Parse(fixture.Read("Benchmarks.Local/CodeGen/benchmark-model.json"));
        var registry = fixture.Read("Benchmarks.Local/BenchmarkRegistry.g.cs");
        foreach (var framework in model.RootElement.GetProperty("frameworks").EnumerateArray())
        foreach (var kind in new[] { "basic", "stress" })
            Assert.Contains($"new {framework.GetProperty(kind).GetString()}(n)", registry);
        foreach (var scenario in model.RootElement.GetProperty("basicScenarios").EnumerateArray())
            Assert.Contains($"public void {scenario.GetString()}()", fixture.Read("SKECS.Benchmarks/ComparisonBenchmarks.cs"));
        foreach (var scenario in model.RootElement.GetProperty("stressScenarios").EnumerateArray())
            Assert.Contains($"{scenario.GetString()}()", fixture.Read("SKECS.Benchmarks/StressBenchmarks.cs"));
        var before = fixture.GeneratedFiles().ToDictionary(path => path, File.ReadAllBytes);
        Assert.Equal(10, before.Count);
        Assert.Equal(0, BenchmarkCodeGenerator.Run(fixture.Root, false));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void CheckIsReadOnlyAndAcceptsCrLfButDetectsCorruptionAndMissingFiles()
    {
        using var fixture = new Fixture();
        Assert.Equal(1, BenchmarkCodeGenerator.Run(fixture.Root, true));
        Assert.Empty(fixture.GeneratedFiles());
        Assert.Equal(0, BenchmarkCodeGenerator.Run(fixture.Root, false));
        var files = fixture.GeneratedFiles();
        foreach (var path in files)
        {
            var bytes = File.ReadAllBytes(path);
            Assert.DoesNotContain((byte)'\r', bytes);
            Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
            Assert.Equal((byte)'\n', bytes[^1]);
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(File.ReadAllText(path).Replace("\n", "\r\n")));
        }
        var before = files.ToDictionary(path => path, File.ReadAllBytes);
        Assert.Equal(0, BenchmarkCodeGenerator.Run(fixture.Root, true));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        var target = files[0];
        File.AppendAllText(target, "// corrupted\r\n");
        var corrupt = File.ReadAllBytes(target);
        Assert.Equal(1, BenchmarkCodeGenerator.Run(fixture.Root, true));
        Assert.Equal(corrupt, File.ReadAllBytes(target));
        File.Delete(target);
        Assert.Equal(1, BenchmarkCodeGenerator.Run(fixture.Root, true));
        Assert.False(File.Exists(target));
        Assert.Equal(0, BenchmarkCodeGenerator.Run(fixture.Root, false));
        Assert.Equal(0, BenchmarkCodeGenerator.Run(fixture.Root, true));
        foreach (var path in files) Assert.DoesNotContain((byte)'\r', File.ReadAllBytes(path));
    }

    [Fact]
    public void UnknownTemplateTokensAreRejected()
    {
        using var fixture = new Fixture();
        File.AppendAllText(Path.Combine(fixture.Root, "Benchmarks.Local/CodeGen/Templates/Benchmarks.Local/Adapters.cs.in"), "\n{{TYPO}}\n");
        Assert.Throws<InvalidDataException>(() => BenchmarkCodeGenerator.Run(fixture.Root, false));
        Assert.Empty(fixture.GeneratedFiles());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("1.5")]
    public void InvalidShapeCountIsRejected(string bits)
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "Benchmarks.Local/CodeGen/benchmark-model.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"shapeBits\": 8", $"\"shapeBits\": {bits}"));
        Assert.Throws<InvalidDataException>(() => BenchmarkCodeGenerator.Run(fixture.Root, false));
        Assert.Empty(fixture.GeneratedFiles());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _temporaryParent;
        public string Root { get; }

        public Fixture()
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, "Benchmarks.Local", "CodeGen")))
                repository = repository.Parent;
            if (repository is null) throw new DirectoryNotFoundException("Cannot find benchmark generator inputs");
            _temporaryParent = Path.GetFullPath(Path.Combine(repository.FullName, "Benchmarks.Local", "tmp", "generator-tests"));
            Root = Path.Combine(_temporaryParent, Guid.NewGuid().ToString("N"));
            var source = Path.Combine(repository.FullName, "Benchmarks.Local", "CodeGen");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(path => !path.EndsWith(".md", StringComparison.Ordinal)))
            {
                var target = Path.Combine(Root, "Benchmarks.Local", "CodeGen", Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        public string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));
        public string[] GeneratedFiles() => Directory.GetFiles(Root, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(_temporaryParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to remove a fixture outside the project temporary directory");
            Directory.Delete(resolved, true);
        }
    }
}
