using Microsoft.CodeAnalysis;

namespace SKECS.GA.Tests;

public sealed partial class CompilerTests
{
    private const string ReadonlyInlineArrayTypes = """
        using System.Runtime.CompilerServices;
        public struct Item
        {
            public readonly int Code;
            public int Count;
        }
        [InlineArray(2)] public struct Items { private Item element; }
        public struct Inventory { public Items Values; }
        public struct ReadonlyInventory { public readonly Items Values; }
        """;

    [Fact]
    public void StandaloneInlineArrayReadonlyReadKeepsReadAccess()
    {
        var result = Analyze("""
            public void Execute(ref Position output, ref Inventory input, in Velocity row)
            {
                output.Point.X = input.Values[row.X].Code;
            }
            """, ReadonlyInlineArrayTypes);
        Assert.True(result.Program != null, string.Join("\n", result.Diagnostics));
        Assert.All(result.Program!.Fields.Where(field => field.Path.EndsWith(".Code", StringComparison.Ordinal)),
            field => Assert.Equal(GaFieldAccess.Read, field.Access));
        Assert.Equal(2, result.Program.Fields.Count(field => field.Path.EndsWith(".Code", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("Inventory", "state.Values[row.X].Code = 42;")]
    [InlineData("ReadonlyInventory", "state.Values[row.X] = default;")]
    public void ReadonlyInlineArrayStorageStillRejectsIndividualOrReceiverWrites(string type, string statement)
    {
        var result = Analyze($$"""
            public void Execute(ref {{type}} state, in Velocity row)
            {
                {{statement}}
            }
            """, ReadonlyInlineArrayTypes);
        Assert.Null(result.Program);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
