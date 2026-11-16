using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SKECS.GA.Tests;

public sealed class GaInlineArrayTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, 3)]
    public void NestedInventoryAndSquadBoundsShareNextState(int inventoryIndex, int squadIndex)
    {
        var program = Compile("""
            row.State.Items[row.Position].Quantity += row.Members[row.Other];
            row.Members[row.Other]++;
            row.Flag = row.State.Items[row.Position].Available;
            row.ReadBack = row.State.Items[row.Position].Quantity;
            """);
        Assert.Equal(16, program.Fields.Count(field => field.Path.EndsWith(".Quantity")));
        Assert.Equal(16, program.Fields.Count(field => field.Path.EndsWith(".Available")));
        Assert.Equal(4, program.Fields.Count(field => field.Path.StartsWith("Members[")));
        Assert.Contains(program.Fields, field => field.Path == "State.Items[15].Available" && field.Type == GaScalar.Bool);
        Assert.Contains(program.Fields, field => field.Path == "Members[3]" && field.Type == GaScalar.Int32);

        var inventory = Enumerable.Range(0, 16).Select(i => i - 21).ToArray();
        var squad = new[] { 1, -7, 10, 23 };
        int previous = squad[squadIndex];
        var values = new Dictionary<string, uint>
        {
            ["Position"] = (uint)inventoryIndex,
            ["Other"] = (uint)squadIndex,
            ["Flag"] = 0,
            ["ReadBack"] = 0
        };
        for (int i = 0; i < 16; i++)
        {
            values[$"State.Items[{i}].Quantity"] = unchecked((uint)inventory[i]);
            values[$"State.Items[{i}].Available"] = (uint)(i & 1);
        }
        for (int i = 0; i < 4; i++)
            values[$"Members[{i}]"] = unchecked((uint)squad[i]);
        uint[] row = Pack(program, values);

        Assert.Null(GaInterpreter.Execute(program, row));
        inventory[inventoryIndex] += previous;
        squad[squadIndex]++;
        values[$"State.Items[{inventoryIndex}].Quantity"] = unchecked((uint)inventory[inventoryIndex]);
        values[$"Members[{squadIndex}]"] = unchecked((uint)squad[squadIndex]);
        values["Flag"] = (uint)(inventoryIndex & 1);
        values["ReadBack"] = unchecked((uint)inventory[inventoryIndex]);
        Assert.Equal(Pack(program, values), row);
    }

    [Theory]
    [InlineData(-1, GaFaultKind.IndexOutOfRange, 0)]
    [InlineData(4, GaFaultKind.IndexOutOfRange, 0)]
    [InlineData(int.MaxValue, GaFaultKind.IndexOutOfRange, 0)]
    [InlineData(3, GaFaultKind.DivideByZero, 1)]
    public void InvalidTargetPreemptsRhsAndDiscardsStagedWritesAndEvents(int position, GaFaultKind expectedKind, int expectedOrdinal)
    {
        var program = Compile("""
            row.ReadBack = 75;
            context.Emit(20);
            row.Members[row.Position] = 42 / row.Divisor;
            """);
        uint[] row = Pack(program, new Dictionary<string, uint>
        {
            ["Position"] = unchecked((uint)position),
            ["ReadBack"] = 11
        });
        uint[] initial = row.ToArray();
        int[] events = [91];

        var fault = GaInterpreter.Execute(program, row, events, out int count);
        Assert.NotNull(fault);
        Assert.Equal(expectedKind, fault.Value.Kind);
        Assert.Equal((ulong)expectedOrdinal, fault.Value.EvaluationOrdinal);
        Assert.Equal(initial, row);
        Assert.Equal(0, count);
        Assert.Equal(91, events[0]);
    }

    [Fact]
    public void CompoundTargetEvaluatesIndexOnceBeforeReadingRhs()
    {
        var program = Compile("""
            row.Members[row.Position / row.Divisor] += row.State.Items[row.Other].Quantity;
            row.ReadBack = row.Members[row.BadIndex];
            """);
        uint[] row = Pack(program, new Dictionary<string, uint>
        {
            ["Position"] = 6,
            ["Divisor"] = 2,
            ["Other"] = 15,
            ["BadIndex"] = 4
        });
        uint[] initial = row.ToArray();

        var fault = GaInterpreter.Execute(program, row);
        Assert.NotNull(fault);
        Assert.Equal(GaFaultKind.IndexOutOfRange, fault.Value.Kind);
        Assert.Equal(3ul, fault.Value.EvaluationOrdinal);
        Assert.Equal(initial, row);
    }

    [Fact]
    public void ReadsIndexedLeavesFromReadonlyComponent()
    {
        var program = Compile("context.Emit(row.State.Items[row.Position].Quantity + row.Members[row.Other]);", readOnly: true);
        Assert.All(program.Fields, field => Assert.Equal(GaFieldAccess.Read, field.Access));
        uint[] row = Pack(program, new Dictionary<string, uint>
        {
            ["Position"] = 15,
            ["Other"] = 3,
            ["State.Items[15].Quantity"] = unchecked((uint)-54),
            ["Members[3]"] = 21
        });
        uint[] initial = row.ToArray();
        int[] events = new int[1];
        Assert.Null(GaInterpreter.Execute(program, row, events, out int count));
        Assert.Equal(1, count);
        Assert.Equal(-33, events[0]);
        Assert.Equal(initial, row);
    }

    [Fact]
    public void ConstantAndDynamicIndicesShareOneLeafRange()
    {
        var program = Compile("""
            row.State.Items[0].Quantity = 31;
            row.ReadBack = row.State.Items[row.Position].Quantity;
            """);
        Assert.Equal(16, program.Fields.Count(field => field.Path.EndsWith(".Quantity")));
        var original = new Dictionary<string, uint> { ["State.Items[0].Quantity"] = 11 };
        uint[] row = Pack(program, original);
        Assert.Null(GaInterpreter.Execute(program, row));
        original["State.Items[0].Quantity"] = 31;
        original["ReadBack"] = 31;
        Assert.Equal(Pack(program, original), row);
    }

    [Fact]
    public void RejectsDirectBackingFieldThatAliasesAnIndexedLeaf()
    {
        var result = Analyze("""
            row.State.Items.Backing.Quantity = 123;
            row.ReadBack = row.State.Items[row.Position].Quantity;
            """, readOnly: false);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "GA0004" &&
            diagnostic.GetMessage().Contains("through index 0", StringComparison.Ordinal));
    }

    private static uint[] Pack(GaProgram program, IReadOnlyDictionary<string, uint> fields) =>
        program.Fields.Select(field => fields.TryGetValue(field.Path, out uint word) ? word : 0u).ToArray();

    private static GaProgram Compile(string body, bool readOnly = false)
    {
        var result = Analyze(body, readOnly);
        Assert.True(result.Program is not null, string.Join("\n", result.Diagnostics));
        return result.Program!;
    }

    private static GaCompilationResult Analyze(string body, bool readOnly)
    {
        string access = readOnly ? "in" : "ref";
        var tree = CSharpSyntaxTree.ParseText($$"""
            using System.Runtime.CompilerServices;
            using SKECS.GA;
            [InlineArray(16)]
            public struct InventoryItems { public Item Backing; }
            [InlineArray(4)]
            public struct SquadMembers { private int _member; }
            public struct Item { public int Quantity; public bool Available; }
            public struct Inventory { public InventoryItems Items; }
            public struct Row
            {
                public Inventory State;
                public SquadMembers Members;
                public int Position, Other, Divisor, BadIndex, ReadBack;
                public bool Flag;
            }
            [SecsSystem]
            public partial struct Fixture
            {
                public void Execute(in SystemContext context, {{access}} Row row)
                {
                    {{body}}
                }
            }
            """, path: "InlineArrayFixture.cs");
        string runtime = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");
        var compilation = CSharpCompilation.Create("InlineArrayFixture", [tree],
            new[] { typeof(object).Assembly.Location, runtime, typeof(World).Assembly.Location, typeof(SystemContext).Assembly.Location }
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var entry = (IMethodSymbol)compilation.GetTypeByMetadataName("Fixture")!.GetMembers("Execute").Single();
        return GaCompiler.Compile(compilation, entry);
    }
}
