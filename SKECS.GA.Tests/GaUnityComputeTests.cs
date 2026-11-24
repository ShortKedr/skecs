using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SKECS.Numerics;

namespace SKECS.GA.Tests;

public sealed class GaUnityComputeTests
{
    [Fact]
    public void UnityBundleRetainsPackedLayoutAndOrderedFaultSources()
    {
        var (_, program) = Compile();
        var module = GaUnityComputeEmitter.Emit(program, 32);
        var native = GaSlangEmitter.EmitD3D12(program, 32);
        Assert.Equal(native.FaultSources.ToArray(), module.FaultSources.ToArray());
        Assert.StartsWith("#pragma kernel main\n#pragma kernel gaStartupProbe\n", module.Source);
        Assert.Contains("[numthreads(32, 1, 1)]\nvoid main", module.Source);
        Assert.Contains("[numthreads(32, 1, 1)]\nvoid gaStartupProbe", module.Source);
        Assert.DoesNotContain("[shader(", module.Source);
        Assert.DoesNotContain("GetDimensions(", module.Source);
        Assert.DoesNotContain("register(", module.Source);
        string body = module.Source[module.Source.IndexOf("uint4 gaExecute(", StringComparison.Ordinal)..
            module.Source.IndexOf("[numthreads(", StringComparison.Ordinal)];
        Assert.Contains("while (true)", body);
        Assert.Contains("return uint4(", body);
        Assert.DoesNotContain("gaFaults[", body);
        Assert.Contains("gaFaults[gaFault + 3u] = result.w;", module.Source);

        var bundle = GaUnityComputePackager.Create(program, "unity-fixture/1", 32, "6000.3.19f1");
        var rebuilt = GaUnityComputePackager.Create(program, "unity-fixture/1", 32, "6000.3.19f1");
        Assert.Equal(bundle.ContentId, rebuilt.ContentId);
        Assert.NotEqual(bundle.ContentId, GaUnityComputePackager.Create(program, "unity-fixture/1", 64, "6000.3.19f1").ContentId);
        Assert.NotEqual(bundle.ContentId, GaUnityComputePackager.Create(program, "unity-fixture/1", 32, "6000.3.20f1").ContentId);
        var manifest = GaArtifactManifest.Parse(bundle.Manifest.GetCanonicalBytes());
        Assert.Equal(program.SourceDigest, manifest.Contract.SourceDigest);
        Assert.Equal(GaArtifactFormat.UnityCompute, manifest.Toolchain.Format);
        Assert.Equal(GaComputeProfiles.WideFixedEmulated, manifest.Requirements.ComputeProfile);
        Assert.Equal((32, 1, 1), (manifest.Requirements.WorkgroupX, manifest.Requirements.WorkgroupY,
            manifest.Requirements.WorkgroupZ));
        Assert.Equal(module.Source, Encoding.UTF8.GetString(bundle.Artifact));
        Assert.Equal(module.FaultSources.ToArray(), bundle.DecodeSourceMap().ToArray());
        Assert.Equal(6, manifest.DeclaredBindings.Count);
        Assert.All(manifest.DeclaredBindings, binding =>
        {
            Assert.Equal(GaBindingNamespace.UnityComputeProperty, binding.Namespace);
            Assert.Equal(binding.Index < 3 ? 4 : 0, binding.Layout!.ElementStride);
        });
        using var layout = JsonDocument.Parse(bundle.Reflection.ToArray());
        Assert.Equal(GaUnityComputeAbi.ProbeKernel, layout.RootElement.GetProperty("probeKernel").GetString());
        Assert.Equal(program.WordCount, layout.RootElement.GetProperty("wordsPerRow").GetInt32());
    }

    [Fact]
    public void UnityFallbackAndCodecCompileAsCSharp9IncludingTypedPayloads()
    {
        var (compilation, program) = Compile();
        const string nativeOffsets = "[0]";
        Assert.Contains(nativeOffsets, GaCpuAdapterEmitter.Emit(program));
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
        string adapter = GaCpuAdapterEmitter.EmitUnity(program);
        string codec = GaRowCodecEmitter.EmitUnity(program);
        Assert.DoesNotContain("ThrowIfNull(", adapter);
        Assert.Contains("new int[] { 0 }", adapter);
        Assert.Contains("new int[] { 0 }", codec);
        var diagnostics = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(adapter, parseOptions),
            CSharpSyntaxTree.ParseText(codec, parseOptions)).GetDiagnostics();
        Assert.Empty(diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void RegenerationSkipsOnlyErrorsInThePreviousGeneratedTrees()
    {
        var (compilation, program) = Compile();
        var original = compilation.SyntaxTrees.Single();
        var options = (CSharpParseOptions)original.Options;
        var edited = CSharpSyntaxTree.ParseText(original.ToString().Replace("Divisor", "Denominator"), options, original.FilePath);
        var staleAdapter = CSharpSyntaxTree.ParseText(GaCpuAdapterEmitter.EmitUnity(program), options, "Owned.cpu.g.cs");
        var staleCodec = CSharpSyntaxTree.ParseText(GaRowCodecEmitter.EmitUnity(program), options, "Owned.row.g.cs");
        SyntaxTree[] previous = [staleAdapter, staleCodec];
        var withStaleCode = compilation.ReplaceSyntaxTree(original, edited).AddSyntaxTrees(previous);
        var rejected = GaCompiler.Compile(withStaleCode, Entry(withStaleCode));
        Assert.Null(rejected.Program);
        Assert.All(rejected.Diagnostics, error => Assert.Contains(error.Location.SourceTree, previous));

        var refreshed = GaCompiler.CompileForRegeneration(withStaleCode, Entry(withStaleCode), previous);
        Assert.NotNull(refreshed.Program);
        Assert.NotEqual(program.SourceDigest, refreshed.Program.SourceDigest);
        var replacementTrees = withStaleCode.RemoveSyntaxTrees(previous).AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(GaCpuAdapterEmitter.EmitUnity(refreshed.Program), options, staleAdapter.FilePath),
            CSharpSyntaxTree.ParseText(GaRowCodecEmitter.EmitUnity(refreshed.Program), options, staleCodec.FilePath));
        Assert.DoesNotContain(replacementTrees.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var authoredError = CSharpSyntaxTree.ParseText("public class Authored { public MissingType Value; }", options,
            staleCodec.FilePath);
        var withAuthoredError = withStaleCode.AddSyntaxTrees(authoredError);
        var stillRejected = GaCompiler.CompileForRegeneration(withAuthoredError, Entry(withAuthoredError), previous);
        Assert.Null(stillRejected.Program);
        Assert.Same(authoredError, Assert.Single(stillRejected.Diagnostics).Location.SourceTree);
    }

    [Fact]
    public void NativeEmitterRangeFailuresPreserveBclDetails()
    {
        var (_, program) = Compile();
        const int workgroupX = GaSlangEmitter.MaximumD3D12WorkgroupX + 1;
        var expectedUpper = Assert.Throws<ArgumentOutOfRangeException>(
            () => ArgumentOutOfRangeException.ThrowIfGreaterThan(workgroupX, GaSlangEmitter.MaximumD3D12WorkgroupX));
        var upper = Assert.Throws<ArgumentOutOfRangeException>(() => GaSlangEmitter.EmitD3D12(program, workgroupX));
        Assert.Equal(expectedUpper.Message, upper.Message);
        Assert.Equal(workgroupX, upper.ActualValue);

        int minimumFailure = 0;
        var expectedLower = Assert.Throws<ArgumentOutOfRangeException>(
            () => ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumFailure, nameof(workgroupX)));
        var lower = Assert.Throws<ArgumentOutOfRangeException>(() => GaSlangEmitter.EmitVulkan(program, minimumFailure));
        Assert.Equal(expectedLower.Message, lower.Message);
        Assert.Equal(minimumFailure, lower.ActualValue);
        Assert.Throws<ArgumentNullException>(nameof(program), () => GaSlangEmitter.EmitVulkan(null!, minimumFailure));
    }

    private static (CSharpCompilation Compilation, GaProgram Program) Compile()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            using SKECS.GA;
            using SKECS.Numerics;

            public struct Row { public int Result, Divisor; public Fixed Amount, Factor; }
            public struct Event { public bool Enabled; public int Result; }

            [SecsSystem(SystemId = "unity-test")]
            public partial struct SystemUnderTest
            {
                public void Execute(in SystemContext context, ref Row row)
                {
                    for (int outer = 0; outer < 2; ++outer)
                    {
                        for (int inner = 0; inner < 2; ++inner)
                            row.Result = checked(row.Result + 17 / row.Divisor);
                    }
                    row.Amount = row.Amount / row.Factor;
                    context.Emit(new Event { Enabled = true, Result = row.Result });
                }
            }
            """, new CSharpParseOptions(LanguageVersion.CSharp9), path: "Fixture.cs");
        string coreLibrary = typeof(object).Assembly.Location;
        string runtime = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.Runtime.dll");
        var references = new[] { coreLibrary, runtime, typeof(SystemContext).Assembly.Location, typeof(Fixed).Assembly.Location }
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("UnityShaderFixture", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = GaCompiler.Compile(compilation, Entry(compilation));
        Assert.True(result.Program is not null, string.Join("\n", result.Diagnostics));
        return (compilation, result.Program!);
    }

    private static IMethodSymbol Entry(CSharpCompilation compilation) =>
        (IMethodSymbol)compilation.GetTypeByMetadataName("SystemUnderTest")!.GetMembers("Execute").Single();
}
