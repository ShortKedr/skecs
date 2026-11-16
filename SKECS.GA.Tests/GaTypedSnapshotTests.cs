using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SKECS.Numerics;

namespace SKECS.GA.Tests;

public sealed class GaTypedSnapshotTests
{
    private const string MixedSchema =
        "[GaSnapshot(GaScalar.Bool, GaScalar.Fixed, GaScalar.UInt32, GaScalar.Int32)]";
    private const string ReadMixed = """
        value.Flag = context.ReadSnapshot<bool>(value.Id, value.Generation, 0, false);
        value.Amount = context.ReadSnapshot<Fixed>(value.Id, value.Generation, 1, Fixed.FromRaw(0x1122334455667788L));
        value.Tag = context.ReadSnapshot<uint>(value.Id, value.Generation, 1 + 1, 0xFEDCBA98u);
        value.Count = context.ReadSnapshot<int>(value.Id, value.Generation, 3, -50);
        """;

    [Fact]
    public void ReadsMixedSnapshotFieldsAndFallsBackForStaleOrMissingEntities()
    {
        var program = RequireProgram(MixedSchema, ReadMixed);
        Assert.True(program.UsesSnapshot);
        Assert.True(program.UsesFixed);
        Assert.Equal("bfui", program.SnapshotLayout!.Schema);
        Assert.Equal(5, program.SnapshotLayout.ValueWordCount);
        Assert.Equal(new[] { "Flag", "Id", "Generation", "Amount", "Tag", "Count" },
            program.Fields.Select(field => field.Path));

        using var world = new World();
        var original = world.CreateEntity(new Signal());
        world.DestroyEntity(original);
        var target = world.CreateEntity(new Signal(true, Fixed.FromRaw(unchecked((long)0xFEDCBA9876543210UL)),
            0x89ABCDEFu, -31));
        var missing = world.CreateEntity(new Marker());
        Assert.Equal(original.Id, target.Id);
        Assert.NotEqual(original.Version, target.Version);

        var packet = GaSnapshotPacket.Capture<Signal>(world, program.SnapshotLayout,
            static (in Signal signal, Span<uint> words) =>
            {
                ulong raw = unchecked((ulong)signal.Amount.Raw);
                words[0] = signal.Flag ? 1u : 0u;
                words[1] = unchecked((uint)raw);
                words[2] = (uint)(raw >> 32);
                words[3] = signal.Tag;
                words[4] = unchecked((uint)signal.Count);
            });

        uint[] live = Read(target);
        Assert.Equal(new uint[] { 1, 0x76543210, 0xFEDCBA98, 0x89ABCDEF, unchecked((uint)-31) },
            new[] { live[0], live[3], live[4], live[5], live[6] });
        foreach (var entity in new[] { original, missing })
        {
            uint[] absent = Read(entity);
            Assert.Equal(new uint[] { 0, 0x55667788, 0x11223344, 0xFEDCBA98, unchecked((uint)-50) },
                new[] { absent[0], absent[3], absent[4], absent[5], absent[6] });
        }

        uint[] Read(Entity entity)
        {
            uint[] row = [0, (uint)entity.Id, (uint)entity.Version, 0, 0, 0, 0];
            Assert.Null(GaInterpreter.Execute(program, row, [], out _, packet));
            return row;
        }
    }

    [Fact]
    public void RejectsEqualWidthSnapshotWithDifferentFieldTypes()
    {
        var program = RequireProgram(MixedSchema, ReadMixed);
        var wrongLayout = new GaSnapshotLayout([GaScalar.Bool, GaScalar.Fixed, GaScalar.Int32, GaScalar.UInt32]);
        Assert.Equal(program.SnapshotLayout!.ValueWordCount, wrongLayout.ValueWordCount);
        var packet = GaSnapshotPacket.FromRows(wrongLayout, [new(11, 3)], [1, 10, 20, 30, 40]);
        uint[] original = [0, 11, 3, 7, 8, 9, 10];
        uint[] row = original.ToArray();
        var error = Assert.Throws<ArgumentException>(() => GaInterpreter.Execute(program, row, [], out _, packet));
        Assert.Contains("bfiu", error.Message);
        Assert.Contains("bfui", error.Message);
        Assert.Equal(original, row);
    }

    [Fact]
    public void EvaluatesTypedFallbackEvenWhenEntityAndGenerationMatch()
    {
        var program = RequireProgram("[GaSnapshot(GaScalar.Int32)]",
            "value.Count = context.ReadSnapshot<int>(11, 3, 0, 100 / value.Count);");
        var packet = GaSnapshotPacket.FromRows(program.SnapshotLayout!, [new(11, 3)], [55]);
        uint[] row = [0];
        var fault = GaInterpreter.Execute(program, row, [], out _, packet);
        Assert.Equal(GaFaultKind.DivideByZero, fault!.Value.Kind);
        Assert.Equal(0ul, fault.Value.EvaluationOrdinal);
        Assert.Equal(0u, row[0]);
    }

    [Theory]
    [InlineData("""
        value.Flag = context.ReadSnapshot<bool>(value.Id, value.Generation, 0, false);
        if (value.Flag)
            value.Amount = context.ReadSnapshot<Fixed>(value.Id, value.Generation, 1, Fixed.FromRaw(0L));
        """, 1)]
    [InlineData("""
        value.Flag = context.ReadSnapshot<bool>(value.Id, value.Generation, 0, false);
        value.Generation++;
        value.Amount = context.ReadSnapshot<Fixed>(value.Id, value.Generation, 1, Fixed.FromRaw(0L));
        """, 2)]
    [InlineData("""
        if (value.Flag)
            value.Amount = context.ReadSnapshot<Fixed>(value.Id, value.Generation, 1, Fixed.FromRaw(0L));
        else
            value.Count = context.ReadSnapshot<int>(value.Id, value.Generation, 3, -1);
        """, 2)]
    public void SlangReusesSnapshotOffsetsOnlyForImmutableKeysInScope(string statements, int lookups)
    {
        var program = RequireProgram(MixedSchema, statements);
        var slang = GaSlangEmitter.EmitVulkan(program).Source;
        Assert.Equal(lookups, slang.Split('\n').Count(line =>
            line.Contains("gaTryFindSnapshotV1(gaInput", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(MixedSchema, "value.Count = context.ReadSnapshot<int>(11, 3, value.Id, 0);")]
    [InlineData(MixedSchema, "value.Count = context.ReadSnapshot<int>(11, 3, 2, 0);")]
    [InlineData(MixedSchema, "value.Count = context.ReadSnapshot<int>(11, 3, 4, 0);")]
    [InlineData(MixedSchema, "value.Count = context.ReadSnapshot<int>(11, 3, -1, 0);")]
    [InlineData(MixedSchema, "value.Count = context.ReadSnapshotInt32(11, 3, 0);")]
    [InlineData("", "value.Count = context.ReadSnapshot<int>(11, 3, 0, 0);")]
    [InlineData("[GaSnapshot()]", "value.Count = 1;")]
    [InlineData("[GaSnapshot((GaScalar)99)]", "value.Count = 1;")]
    public void RejectsInvalidTypedSnapshotDeclarationsAndLookups(string layout, string statement)
    {
        var result = Compile(layout, statement);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "GA0004");
    }

    private static GaProgram RequireProgram(string layout, string statements)
    {
        var result = Compile(layout, statements);
        Assert.True(result.Program is not null, string.Join("\n", result.Diagnostics));
        return result.Program!;
    }

    private static GaCompilationResult Compile(string layout, string statements)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            using SKECS.GA;
            using SKECS.Numerics;
            public struct Row
            {
                public int Id, Generation, Count;
                public bool Flag;
                public Fixed Amount;
                public uint Tag;
            }
            [SecsSystem]
            public partial struct Observe
            {
                {{layout}}
                public void Execute(in SystemContext context, ref Row value)
                {
                    {{statements}}
                }
            }
            """, path: "TypedSnapshotFixture.cs");
        string runtime = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");
        var compilation = CSharpCompilation.Create("TypedSnapshotFixture", [tree],
            new[] { typeof(object).Assembly.Location, runtime, typeof(SystemContext).Assembly.Location, typeof(Fixed).Assembly.Location }
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var entry = (IMethodSymbol)compilation.GetTypeByMetadataName("Observe")!.GetMembers("Execute").Single();
        return GaCompiler.Compile(compilation, entry);
    }

    private readonly record struct Signal(bool Flag, Fixed Amount, uint Tag, int Count);
    private readonly struct Marker;
}
