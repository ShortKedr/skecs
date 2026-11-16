using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SKECS.Numerics;

namespace SKECS.GA.Tests;

public sealed class GaReadBufferTests
{
    private const string BufferAttribute = "[GaReadBuffer(typeof(Cell))]";

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void ReadsMixedNestedWordsFromOwnedProjection(int index, bool enabled)
    {
        var program = Compile(BufferAttribute, """
            row.Result = context.ReadBuffer<Cell>(row.Index).Value.Weight;
            row.Value = context.ReadBuffer<Cell>(row.Index).Value.Mass;
            row.Enabled = context.ReadBuffer<Cell>(row.Index).Value.Enabled;
            context.Emit(unchecked((int)context.ReadBuffer<Cell>(row.Index).Value.Mask));
            context.Emit(context.BufferLength<Cell>());
            """);
        Assert.Equal("global::Cell", program.BufferType);
        Assert.Equal(64, program.BufferSchema!.Length);
        Assert.Equal(5, program.BufferRowWords);
        Assert.Equal(new[] { "Value.Enabled", "Value.Mask", "Value.Mass", "Value.Weight" },
            program.BufferFields.Select(field => field.Path).Order(StringComparer.Ordinal));

        uint[] raw = Enumerable.Range(0, 3).SelectMany(element => PackBufferRow(program, element)).ToArray();
        var buffer = GaBufferPacket.FromWords(program.BufferSchema, program.BufferRowWords, 3, raw);
        Array.Clear(raw);
        uint[] row = PackRow(program, ("Index", [(uint)index]));
        int[] events = [-1, -1];
        Assert.Null(GaInterpreter.Execute(program, row, events, out int count, null, default, buffer));
        Assert.Equal(PackRow(program, ("Index", [(uint)index]), ("Result", [unchecked((uint)(-11 - index * 5))]),
            ("Value", [0x9171_5141u + (uint)index, 0xe7c7_a787u]), ("Enabled", [enabled ? 1u : 0u])), row);
        Assert.Equal(2, count);
        Assert.Equal(new[] { unchecked((int)(0xf123_4567u + (uint)index)), 3 }, events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void LengthOnlyBufferHasAHeaderWithoutElementWords(int length)
    {
        var program = Compile(BufferAttribute, "row.Result = context.BufferLength<Cell>();");
        Assert.Empty(program.BufferFields);
        Assert.Equal(0, program.BufferRowWords);
        var buffer = GaBufferPacket.FromWords(program.BufferSchema!, 0, length, []);
        Assert.Equal((uint)length, Assert.Single(buffer.Words.ToArray()));
        uint[] row = [100];
        Assert.Null(GaInterpreter.Execute(program, row, Span<int>.Empty, out _, null, default, buffer));
        Assert.Equal((uint)length, Assert.Single(row));
    }

    [Fact]
    public void CodecsCopyScalarSourceRows()
    {
        var program = Compile("[GaReadBuffer(typeof(uint))]",
            "row.Result = unchecked((int)context.ReadBuffer<uint>(row.Index));");
        Assert.Equal("", Assert.Single(program.BufferFields).Path);
        var codec = new GaBufferCodec<uint>(program.BufferSchema!, program.BufferRowWords,
            static (in uint value, Span<uint> words) => words[0] = value);
        uint[] original = [0xf000_1234, 0xffffffff];
        var buffer = codec.Capture(original);
        original[1] = 0;
        Assert.Equal(new uint[] { 2, 0xf000_1234, 0xffffffff }, buffer.Words.ToArray());
        uint[] row = PackRow(program, ("Index", [1]));
        Assert.Null(GaInterpreter.Execute(program, row, Span<int>.Empty, out _, null, default, buffer));
        Assert.Equal(PackRow(program, ("Index", [1]), ("Result", [0xffffffff])), row);
    }

    [Theory]
    [InlineData(-1, 1, 2, GaFaultKind.IndexOutOfRange, 1)]
    [InlineData(2, 1, 2, GaFaultKind.IndexOutOfRange, 1)]
    [InlineData(0, 1, 0, GaFaultKind.IndexOutOfRange, 1)]
    [InlineData(-1, 0, 0, GaFaultKind.DivideByZero, 0)]
    public void BoundsAndIndexFaultsAreOrderedWithoutCommittingWrites(int index, int divisor, int length,
        GaFaultKind kind, int expectedOrdinal)
    {
        var program = Compile(BufferAttribute, """
            row.Result += 13;
            context.Emit(row.Result);
            row.Result = context.ReadBuffer<Cell>(row.Index / row.Divisor).Value.Weight;
            """);
        var buffer = GaBufferPacket.FromWords(program.BufferSchema!, program.BufferRowWords, length, new uint[length]);
        uint[] before = PackRow(program, ("Index", [unchecked((uint)index)]), ("Divisor", [(uint)divisor]), ("Result", [100]));
        uint[] row = before.ToArray();
        int[] events = [-1];
        var fault = GaInterpreter.Execute(program, row, events, out int count, null, default, buffer);
        Assert.True(fault.HasValue);
        Assert.Equal(kind, fault.Value.Kind);
        Assert.Equal((ulong)expectedOrdinal, fault.Value.EvaluationOrdinal);
        Assert.Equal("BufferedFixture.cs", fault.Value.Source.File);
        Assert.Equal(before, row);
        Assert.Equal(0, count);
        Assert.Equal(-1, Assert.Single(events));
    }

    [Fact]
    public void WrongSchemaWidthAndMissingBindingAreRejected()
    {
        var program = Compile(BufferAttribute, "row.Result = context.ReadBuffer<Cell>(0).Value.Weight;");
        var sameWidthOtherSchema = GaBufferPacket.FromWords(new string('f', 64), 1, 1, [17]);
        var sameSchemaOtherWidth = GaBufferPacket.FromWords(program.BufferSchema!, 2, 1, [17, 0]);
        uint[] row = [100];
        Assert.Throws<ArgumentException>(() => GaInterpreter.Execute(program, row, Span<int>.Empty, out _, null, default, sameWidthOtherSchema));
        Assert.Throws<ArgumentException>(() => GaInterpreter.Execute(program, row, Span<int>.Empty, out _, null, default, sameSchemaOtherWidth));
        Assert.Throws<InvalidOperationException>(() => GaInterpreter.Execute(program, row));
        Assert.Throws<ArgumentException>(() => GaBufferPacket.FromWords(program.BufferSchema!, 1, 2, [17]));
        Assert.Equal(100u, Assert.Single(row));
    }

    [Theory]
    [InlineData("", "row.Result = context.ReadBuffer<Cell>(0).Value.Weight;")]
    [InlineData(BufferAttribute, "row.Result = context.BufferLength<int>();")]
    [InlineData("[GaReadBuffer(typeof(string))]", "row.Result = 0;")]
    [InlineData(BufferAttribute, "context.ReadBuffer<Cell>(0).Value.Weight = 0;")]
    [InlineData(BufferAttribute, "ref Cell alias = ref context.ReadBuffer<Cell>(0); row.Result = alias.Value.Weight;")]
    public void RejectsInvalidDeclarationsAndUses(string attribute, string body)
    {
        var result = Analyze(attribute, body);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static uint[] PackBufferRow(GaProgram program, int element) =>
        program.BufferFields.SelectMany(field => field.Path switch
        {
            "Value.Enabled" => new uint[] { element % 2 == 0 ? 1u : 0u },
            "Value.Weight" => new uint[] { unchecked((uint)(-11 - element * 5)) },
            "Value.Mask" => new uint[] { 0xf123_4567u + (uint)element },
            "Value.Mass" => new uint[] { 0x9171_5141u + (uint)element, 0xe7c7_a787u },
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
            public struct Cell { public CellValue Value; }
            public struct CellValue { public bool Enabled; public uint Mask; public int Weight; public Fixed Mass; }
            public struct Row { public int Index, Divisor, Result; public Fixed Value; public bool Enabled; }
            [SecsSystem]
            public partial struct BufferedFixture
            {
                {{attribute}}
                public void Execute(in SystemContext context, ref Row row)
                {
                    {{body}}
                }
            }
            """, path: "BufferedFixture.cs");
        string coreLibrary = typeof(object).Assembly.Location;
        string runtime = Path.Combine(Path.GetDirectoryName(coreLibrary)!, "System.Runtime.dll");
        var compilation = CSharpCompilation.Create("BufferedFixture", [tree],
            new[] { coreLibrary, runtime, typeof(World).Assembly.Location, typeof(Fixed).Assembly.Location,
                typeof(SystemContext).Assembly.Location }.Distinct(StringComparer.Ordinal)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var entry = (IMethodSymbol)compilation.GetTypeByMetadataName("BufferedFixture")!.GetMembers("Execute").Single();
        return GaCompiler.Compile(compilation, entry);
    }
}
