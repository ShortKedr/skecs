using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SKECS.GA.Tests;

public sealed class GaFixedArrayTests
{
    [Theory]
    [InlineData(0, 2, null)]
    [InlineData(15, 2, null)]
    [InlineData(-1, 0, GaFaultKind.IndexOutOfRange)]
    [InlineData(16, 0, GaFaultKind.IndexOutOfRange)]
    [InlineData(15, 0, GaFaultKind.DivideByZero)]
    public void PortableArrayRetainsCanonicalSchemaAndFaultOrder(int slot, int divisor, GaFaultKind? expectedFault)
    {
        var (native, _) = Compile(portable: false, readOnly: false,
            """
            row.State.Items[row.Index].Units += row.State.Items[0].Units / row.Divisor;
            row.Result = row.State.Items[row.Index].Units;
            """);
        var (portable, compilation) = Compile(portable: true, readOnly: false,
            """
            Slots.At(ref row.State.Items, row.Index).Units += Slots.Read(in row.State.Items, 0).Units / row.Divisor;
            row.Result = Slots.Read(in row.State.Items, row.Index).Units;
            """);

        var nativeBundle = GaUnityComputePackager.Create(native, "array-test/1");
        var portableBundle = GaUnityComputePackager.Create(portable, "array-test/1");
        Assert.Equal(nativeBundle.Manifest.Contract.ComponentSchemaDigest, portableBundle.Manifest.Contract.ComponentSchemaDigest);
        Assert.Equal(nativeBundle.Manifest.Contract.IrDigest, portableBundle.Manifest.Contract.IrDigest);
        Assert.NotEqual(nativeBundle.Manifest.Contract.SourceDigest, portableBundle.Manifest.Contract.SourceDigest);
        Assert.Equal(nativeBundle.Artifact.ToArray(), portableBundle.Artifact.ToArray());

        uint[] initial = native.Fields.Select(field => field.Path switch
        {
            "Index" => unchecked((uint)slot),
            "Divisor" => unchecked((uint)divisor),
            "State.Items[0].Units" => 42u,
            "State.Items[15].Units" => 3u,
            _ => 0u
        }).ToArray();
        var nativeWords = initial.ToArray();
        var portableWords = initial.ToArray();
        var nativeFault = GaInterpreter.Execute(native, nativeWords);
        var portableFault = GaInterpreter.Execute(portable, portableWords);
        Assert.Equal(expectedFault, portableFault?.Kind);
        Assert.Equal(nativeFault?.EvaluationOrdinal, portableFault?.EvaluationOrdinal);
        Assert.Equal(nativeWords, portableWords);
        VerifyGeneratedCode(portable, compilation);
    }

    [Fact]
    public void ReadOnlyQueryUsesTheSamePackedArrayLeaves()
    {
        var (native, _) = Compile(portable: false, readOnly: true,
            "context.Emit(row.State.Items[row.Index].Units);");
        var (portable, compilation) = Compile(portable: true, readOnly: true,
            "context.Emit(Slots.Read(in row.State.Items, row.Index).Units);");

        var expected = GaUnityComputePackager.Create(native, "array-test/1").Manifest.Contract;
        var actual = GaUnityComputePackager.Create(portable, "array-test/1").Manifest.Contract;
        Assert.Equal(expected.ComponentSchemaDigest, actual.ComponentSchemaDigest);
        Assert.Equal(expected.IrDigest, actual.IrDigest);
        VerifyGeneratedCode(portable, compilation);
    }

    [Fact]
    public void PortableAndNativeArrayReplaceTheSameWholeValueWithReadonlyLeaves()
    {
        const string item = "public struct Item { public readonly int Code; public int Units; }";
        var (native, _) = Compile(portable: false, readOnly: false,
            """
            row.State.Items[row.Index] = default;
            var value = row.State.Items[row.Index];
            row.Result = value.Code + value.Units;
            """, item);
        var (portable, _) = Compile(portable: true, readOnly: false,
            """
            Slots.At(ref row.State.Items, row.Index) = default;
            var value = Slots.Read(in row.State.Items, row.Index);
            row.Result = value.Code + value.Units;
            """, item);

        var expected = GaUnityComputePackager.Create(native, "array-test/1").Manifest.Contract;
        var actual = GaUnityComputePackager.Create(portable, "array-test/1").Manifest.Contract;
        Assert.Equal(expected.ComponentSchemaDigest, actual.ComponentSchemaDigest);
        Assert.Equal(expected.IrDigest, actual.IrDigest);
        Assert.Contains("Unsafe.AsRef(in ", GaCpuAdapterEmitter.EmitUnity(portable));
        Assert.Contains("Unsafe.AsRef(in ", GaRowCodecEmitter.EmitUnity(portable));
    }

    [Fact]
    public void StorageContainsSixteenIndependentInlineValues()
    {
        Assert.Equal(Unsafe.SizeOf<Pair>() * 16, Unsafe.SizeOf<FixedArray16<Pair>>());
        var storage = default(FixedArray16<Pair>);
        for (var i = 0; i < FixedArray16<Pair>.Length; i++)
            FixedArray16<Pair>.At(ref storage, i) = new Pair { First = i, Second = i * 7 };

        var copy = storage;
        FixedArray16<Pair>.At(ref copy, 15).Second = -1;
        for (var i = 0; i < FixedArray16<Pair>.Length; i++)
        {
            ref readonly var slot = ref FixedArray16<Pair>.Read(in storage, i);
            Assert.Equal(i, slot.First);
            Assert.Equal(i * 7, slot.Second);
        }
        Assert.Throws<IndexOutOfRangeException>(() => { _ = FixedArray16<Pair>.At(ref storage, -1); });
        Assert.Throws<IndexOutOfRangeException>(() => { _ = FixedArray16<Pair>.Read(in storage, 16); });
    }

    private struct Pair
    {
        public int First;
        public long Second;
    }

    private static void VerifyGeneratedCode(GaProgram program, CSharpCompilation compilation)
    {
        var parse = new CSharpParseOptions(LanguageVersion.CSharp9);
        var adapter = CSharpSyntaxTree.ParseText(GaCpuAdapterEmitter.EmitUnity(program), parse);
        var codec = CSharpSyntaxTree.ParseText(GaRowCodecEmitter.EmitUnity(program), parse);
        Assert.Empty(compilation.AddSyntaxTrees(adapter, codec).GetDiagnostics()
            .Where(item => item.Severity == DiagnosticSeverity.Error));
    }

    private static (GaProgram Program, CSharpCompilation Compilation) Compile(bool portable, bool readOnly, string body,
        string item = "public struct Item { public int Units; }")
    {
        string aliases = portable
            ? "using Slots = SKECS.GA.FixedArray16<global::Item>;"
            : "using System.Runtime.CompilerServices;";
        string declaration = portable ? "" : "[InlineArray(16)] public struct Slots { private Item first; }";
        string access = readOnly ? "in" : "ref";
        var parse = new CSharpParseOptions(portable ? LanguageVersion.CSharp9 : LanguageVersion.CSharp12);
        var tree = CSharpSyntaxTree.ParseText($$"""
            using SKECS.GA;
            {{aliases}}
            {{declaration}}
            {{item}}
            public struct Inventory { public Slots Items; }
            public struct Row { public Inventory State; public int Index, Divisor, Result; }
            [SecsSystem]
            public partial struct Fixture
            {
                public void Execute(in SystemContext context, {{access}} Row row)
                {
                    {{body}}
                }
            }
            """, parse, "PortableArrayFixture.cs");
        string runtime = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");
        var references = new[] { typeof(object).Assembly.Location, runtime, typeof(World).Assembly.Location,
            typeof(SystemContext).Assembly.Location }.Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("PortableArrayFixture", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var entry = (IMethodSymbol)compilation.GetTypeByMetadataName("Fixture")!.GetMembers("Execute").Single();
        var result = GaCompiler.Compile(compilation, entry);
        Assert.True(result.Program is not null, string.Join("\n", result.Diagnostics));
        return (result.Program!, compilation);
    }
}
