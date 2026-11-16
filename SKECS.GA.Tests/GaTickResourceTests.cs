using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SKECS.Numerics;

namespace SKECS.GA.Tests;

public sealed class GaTickResourceTests
{
    private const string FrameAttribute = "[GaTickResource(typeof(Frame))]";
    private const string ReadFrame = """
        ref readonly var input = ref context.Resource<Frame>();
        row.Result = input.Config.Enabled ? row.Result + input.Config.Delta : row.Result - input.Config.Delta;
        row.Value = input.Config.Weight;
        context.Emit(unchecked((int)(input.Config.Mask ^ input.Seed)));
        """;

    [Theory]
    [InlineData(true, 9)]
    [InlineData(false, 31)]
    public void CopiesTypedTickInputIntoInterpreter(bool enabled, int expected)
    {
        var program = Compile(FrameAttribute, ReadFrame);
        Assert.Equal("global::Frame", program.TickResourceType);
        Assert.Equal(6, program.TickResourceWords);
        Assert.Equal(new[] { "Config.Delta", "Config.Enabled", "Config.Mask", "Config.Weight", "Seed" },
            program.TickResourceFields.Select(field => field.Path).Order(StringComparer.Ordinal));
        uint[] row = PackRow(program, ("Result", [20u]));
        uint[] tickResource = PackResources(program, enabled);
        int[] events = [-1];
        Assert.Null(GaInterpreter.Execute(program, row, events, out int count, null, tickResource));
        Assert.Equal(PackRow(program, ("Result", [(uint)expected]), ("Value", [0x9171_5141, 0xe7c7_a787])), row);
        Assert.Equal(1, count);
        Assert.Equal(unchecked((int)(0x97b3_c15du ^ 0x0175_5050u)), events[0]);
    }

    [Fact]
    public void ScalarTickInputCanBeConsumedDirectly()
    {
        var program = Compile("[GaTickResource(typeof(uint))]", """
            row.Result = unchecked((int)context.Resource<uint>());
            """);
        Assert.Equal(1, program.TickResourceWords);
        Assert.Equal("", Assert.Single(program.TickResourceFields).Path);
        uint[] row = new uint[program.WordCount];
        Assert.Null(GaInterpreter.Execute(program, row, Span<int>.Empty, out _, null, [0xf000_1284u]));
        Assert.Equal(0xf000_1284u, Assert.Single(row));
    }

    [Fact]
    public void DeclaredUnusedResourcePreservesTypeWithoutPayloadWords()
    {
        var program = Compile(FrameAttribute, "row.Result = 24;");
        Assert.Equal("global::Frame", program.TickResourceType);
        Assert.Empty(program.TickResourceFields);
        Assert.Equal(0, program.TickResourceWords);
        uint[] row = new uint[program.WordCount];
        Assert.Null(GaInterpreter.Execute(program, row));
        Assert.Equal(24u, Assert.Single(row));
    }

    [Fact]
    public void RejectsBadWidthAndNonCanonicalFlagsBeforeChangingRows()
    {
        var program = Compile(FrameAttribute, ReadFrame);
        uint[] before = PackRow(program, ("Result", [20u]));
        uint[] row = before.ToArray();
        uint[] words = PackResources(program, true);
        int[] events = [-1];
        Assert.Throws<ArgumentException>(() =>
            GaInterpreter.Execute(program, row, events, out _, null, words.AsSpan(0, words.Length - 1)));
        Assert.Throws<ArgumentException>(() =>
            GaInterpreter.Execute(program, row, events, out _, null, [.. words, 0u]));
        words = PackResources(program, true, booleanWord: 2);
        Assert.Throws<ArgumentException>(() =>
            GaInterpreter.Execute(program, row, events, out _, null, words));
        Assert.Equal(before, row);
        Assert.Equal(-1, Assert.Single(events));
    }

    [Fact]
    public void RejectsResourceWordsAliasingWritableRowOrEvents()
    {
        var program = Compile("[GaTickResource(typeof(int))]", """
            row.Result = context.Resource<int>();
            context.Emit(row.Result);
            """);
        uint[] row = [21u];
        uint[] resource = [47u];
        int[] events = [-1];
        Assert.Throws<ArgumentException>(() =>
            GaInterpreter.Execute(program, row, events, out _, null, row));
        Assert.Throws<ArgumentException>(() =>
            GaInterpreter.Execute(program, row, MemoryMarshal.Cast<uint, int>(resource.AsSpan()), out _, null, resource));
        Assert.Equal(21u, Assert.Single(row));
        Assert.Equal(47u, Assert.Single(resource));
        Assert.Equal(-1, Assert.Single(events));
    }

    [Theory]
    [InlineData("", "row.Result = context.Resource<Frame>().Config.Delta;")]
    [InlineData(FrameAttribute, "row.Result = context.Resource<Config>().Delta;")]
    [InlineData("[GaTickResource(typeof(string))]", "row.Result = 0;")]
    [InlineData(FrameAttribute, "context.Resource<Frame>().Config.Delta = 0;")]
    [InlineData(FrameAttribute, "ref Frame alias = ref context.Resource<Frame>(); row.Result = alias.Config.Delta;")]
    public void RejectsInvalidDeclarationsAndUses(string attribute, string body)
    {
        var result = Analyze(attribute, body);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static uint[] PackResources(GaProgram program, bool enabled, uint? booleanWord = null) =>
        program.TickResourceFields.SelectMany(field => field.Path switch
        {
            "Config.Enabled" => new uint[] { booleanWord ?? (enabled ? 1u : 0u) },
            "Config.Delta" => new uint[] { unchecked((uint)-11) },
            "Config.Mask" => new uint[] { 0x97b3_c15d },
            "Config.Weight" => new uint[] { 0x9171_5141, 0xe7c7_a787 },
            "Seed" => new uint[] { 0x0175_5050 },
            _ => throw new InvalidOperationException(field.Path)
        }).ToArray();

    private static uint[] PackRow(GaProgram program, params (string Path, uint[] Words)[] packed)
    {
        var values = packed.ToDictionary(item => item.Path, item => item.Words, StringComparer.Ordinal);
        return program.Fields.SelectMany(field => values.TryGetValue(field.Path, out var words)
            ? words : new uint[field.Type == GaScalar.Fixed ? 2 : 1]).ToArray();
    }

    private static GaProgram Compile(string attribute, string body)
    {
        var result = Analyze(attribute, body);
        Assert.True(result.Program is not null, string.Join("\n", result.Diagnostics));
        return result.Program!;
    }

    private static GaCompilationResult Analyze(string attribute, string body)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            using SKECS.GA;
            using SKECS.Numerics;
            public struct Frame { public Config Config; public uint Seed; }
            public struct Config { public bool Enabled; public uint Mask; public int Delta; public Fixed Weight; }
            public struct Row { public int Result; public Fixed Value; }
            [SecsSystem]
            public partial struct TickFixture
            {
                {{attribute}}
                public void Execute(in SystemContext context, ref Row row)
                {
                    {{body}}
                }
            }
            """, path: "TickFixture.cs");
        string coreLibrary = typeof(object).Assembly.Location;
        string runtime = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.Runtime.dll");
        var compilation = CSharpCompilation.Create("TickFixture", [tree],
            new[] { coreLibrary, runtime, typeof(World).Assembly.Location, typeof(Fixed).Assembly.Location,
                typeof(SystemContext).Assembly.Location }.Distinct(StringComparer.Ordinal)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var entry = (IMethodSymbol)compilation.GetTypeByMetadataName("TickFixture")!.GetMembers("Execute").Single();
        return GaCompiler.Compile(compilation, entry);
    }
}
