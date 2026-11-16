using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SKECS.Serialization.Generator.Tests;

public sealed class WorldSchemaGeneratorTests
{
    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Append(typeof(World).Assembly.Location)
        .Append(typeof(CodecRegistry).Assembly.Location)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray();

    [Fact]
    public void EmitsOrderedCodeAndRoundTripsPrimitivesEnumEntityAndFixed()
    {
        const string source = """
            using System;
            using System.IO;
            using SKECS;
            using SKECS.Numerics;
            using SKECS.Serialization;

            namespace @class;

            public enum Kind : sbyte { Special = -5 }

            [WorldComponent(uint.MaxValue, Version = 2)]
            public struct @struct
            {
                [WorldField(10)] public Entity Target;
                [WorldField(7)] internal Kind @event;
                [WorldField(1)] public float Score;
                [WorldField(11)] public Fixed Fraction;
                [WorldIgnore] private int _cache;
                [field: WorldIgnore] public long CachedValue { get; set; }
            }

            [WorldComponent(9)]
            public struct Tag { }

            public static class Consumer
            {
                public static bool CheckpointRoundTrip(bool binary)
                {
                    var schema = global::SKECS.Serialization.Generated.WorldSchema.Create();
                    using var source = new World();
                    var target = source.CreateEntity();
                    const int floatBits = unchecked((int)0xffc01234u);
                    var entity = source.CreateEntity(new @struct
                    {
                        Target = target,
                        @event = Kind.Special,
                        Score = BitConverter.Int32BitsToSingle(floatBits),
                        Fraction = Fixed.FromRaw(long.MinValue + 17),
                        CachedValue = 999
                    });

                    using var stream = new MemoryStream();
                    if (binary) WorldSerializer.WriteBinary(source, stream, schema);
                    else WorldSerializer.WriteJson(source, stream, schema);
                    stream.Position = 0;
                    using var restored = binary ? WorldSerializer.ReadBinary(stream, schema) : WorldSerializer.ReadJson(stream, schema);
                    var result = restored.Get<@struct>(restored.BindEntity(entity.Id, entity.Version));
                    return restored.IsAlive(result.Target) && !source.IsAlive(result.Target) &&
                        result.@event == Kind.Special && BitConverter.SingleToInt32Bits(result.Score) == floatBits &&
                        result.Fraction.Raw == long.MinValue + 17 && result.CachedValue == 0;
                }
            }
            """;

        var (result, output) = Generate(source);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();
        Assert.True(generated.IndexOf("ComponentSchema(9u, 1u", StringComparison.Ordinal) <
                    generated.IndexOf("ComponentSchema(4294967295u, 2u", StringComparison.Ordinal));
        Assert.True(generated.IndexOf("FieldSchema(1u", StringComparison.Ordinal) <
                    generated.IndexOf("FieldSchema(7u", StringComparison.Ordinal));
        Assert.Contains("AsInt8()", generated);
        Assert.Contains("SetEntity(10u", generated);
        Assert.Contains("FieldSchema(11u, global::SKECS.FieldKind.Int64)", generated);
        Assert.Contains("FieldValue.From(value.@Fraction.Raw)", generated);
        Assert.Contains("Fixed.FromRaw(fields.Get(11u).AsInt64())", generated);
        Assert.DoesNotContain("_cache", generated);
        Assert.DoesNotContain("CachedValue", generated);

        using var assembly = new MemoryStream();
        var emit = output.Emit(assembly);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        assembly.Position = 0;
        var consumer = AssemblyLoadContext.Default.LoadFromStream(assembly).GetType("class.Consumer", throwOnError: true)!;
        var roundTrip = consumer.GetMethod("CheckpointRoundTrip")!;
        Assert.True((bool)roundTrip.Invoke(null, new object[] { true })!);
        Assert.True((bool)roundTrip.Invoke(null, new object[] { false })!);
    }

    [Theory]
    [InlineData("[WorldComponent(5)] public struct A { } [WorldComponent(5)] public struct B { }", "SKECSSER003", 2)]
    [InlineData("[WorldComponent(0)] public struct A { }", "SKECSSER002", 1)]
    [InlineData("[WorldComponent(1, Version = 0)] public struct A { }", "SKECSSER002", 1)]
    [InlineData("[WorldComponent(1)] public struct A { public int Value; }", "SKECSSER004", 1)]
    [InlineData("[WorldComponent(1)] public struct A { public int Value { get; set; } }", "SKECSSER004", 1)]
    [InlineData("[WorldComponent(1)] public struct A { [WorldField(0)] public int Value; }", "SKECSSER006", 1)]
    [InlineData("[WorldComponent(1)] public struct A { [WorldField(1)] public int X; [WorldField(1)] public int Y; }", "SKECSSER007", 2)]
    [InlineData("[WorldComponent(1)] public struct A { [WorldField(1), WorldIgnore] public int Value; }", "SKECSSER008", 1)]
    [InlineData("[WorldComponent(1)] public struct A { [WorldField(1)] public nint Value; }", "SKECSSER005", 1)]
    [InlineData("[WorldComponent(1)] public unsafe struct A { [WorldField(1)] public int* Value; }", "SKECSSER005", 1)]
    [InlineData("[WorldComponent(1)] public struct A { [WorldField(1)] public readonly int Value; }", "SKECSSER005", 1)]
    [InlineData("[WorldComponent(1)] public struct A { [WorldField(1)] private int Value; }", "SKECSSER005", 1)]
    [InlineData("[WorldComponent(1)] public struct A { [WorldField(1)] public B Value; } public struct B { public int N; }", "SKECSSER005", 1)]
    [InlineData("[WorldComponent(1), StructLayout(LayoutKind.Explicit)] public struct A { [FieldOffset(0), WorldField(1)] public int Value; }", "SKECSSER001", 1)]
    [InlineData("[WorldComponent(1), StructLayout((short)2)] public struct A { [FieldOffset(0), WorldField(1)] public int Value; }", "SKECSSER001", 1)]
    [InlineData("[WorldComponent(1)] file struct A { }", "SKECSSER001", 1)]
    public void DiagnosesInvalidSchemas(string source, string code, int count)
    {
        var (result, _) = Generate("using SKECS.Serialization; using System.Runtime.InteropServices; " + source);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(code, diagnostic.Id));
        Assert.Equal(count, result.Diagnostics.Length);
        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
    }

    private static (GeneratorDriverRunResult, Compilation) Generate(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);
        var compilation = CSharpCompilation.Create("TestSchema", new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var generator = new WorldSchemaGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(new[] { generator }, parseOptions: parseOptions);
        var run = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (run.GetRunResult(), output);
    }
}
