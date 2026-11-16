using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AdaptivePlacementFixture.LegacyCoverage;

internal sealed record CoverageReport(string Format, string Scope, string Runtime, string Assembly, string AssemblySha256,
    bool Complete, IReadOnlyDictionary<string, string> TrustedModules, string[] Uncovered,
    LegacyUse[] LegacyUses, CandidateCoverage[] Candidates);
internal sealed record LegacyUse(string Assembly, string Component, string Consumer);
internal sealed record CandidateCoverage(string Component, bool PermittedByLegacyCoverage);

// Verifies the closed standalone fixture in metadata, including dormant branches. Unsupported
// dispatch or consumers outside the inspected and trusted assemblies disqualify every candidate.
internal static class LegacyAudit
{
    private static readonly ShapeProvider Shapes = new();
    private static readonly Dictionary<ushort, OpCode> Instructions = typeof(OpCodes)
        .GetFields(BindingFlags.Static | BindingFlags.Public)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => unchecked((ushort)opcode.Value));

    internal static CoverageReport Inspect(string path, string[] candidates)
    {
        using var file = File.OpenRead(path);
        using var image = new PEReader(file);
        var metadata = image.GetMetadataReader();
        var assembly = metadata.GetString(metadata.GetAssemblyDefinition().Name);
        var defined = metadata.TypeDefinitions.Select(handle => Shapes.GetTypeFromDefinition(metadata, handle, 0).Name)
            .ToHashSet(StringComparer.Ordinal);
        var incomplete = new SortedSet<string>(StringComparer.Ordinal);
        var legacy = new HashSet<LegacyUse>();
        var trusted = CheckReferences(path, metadata, incomplete);

        foreach (var name in candidates)
            if (!defined.Contains(name)) incomplete.Add($"Candidate is outside the audited assembly: {name}");

        foreach (var handle in metadata.MemberReferences)
        {
            var reference = metadata.GetMemberReference(handle);
            var declaring = ResolveType(metadata, reference.Parent);
            var member = metadata.GetString(reference.Name);
            if (IsDynamicBoundary(declaring, member)) incomplete.Add($"Dynamic dispatch: {declaring.Name}.{member}");
            if (declaring.Assembly == "SKECS" && IsLegacyQueryType(declaring.Name))
                Record(declaring.Arguments, $"{declaring.Name}.{member}", incomplete, legacy);
        }

        for (var row = 1; row <= metadata.GetTableRowCount(TableIndex.MethodSpec); row++)
        {
            var method = metadata.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(row));
            if (method.Method.Kind != HandleKind.MemberReference) continue;
            var reference = metadata.GetMemberReference((MemberReferenceHandle)method.Method);
            var declaring = ResolveType(metadata, reference.Parent);
            var member = metadata.GetString(reference.Name);
            if (declaring.Assembly == "SKECS" && IsLegacyMethod(declaring.Name, member))
                Record(method.DecodeSignature(Shapes, null), $"{declaring.Name}.{member}", incomplete, legacy);
        }

        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            var name = metadata.GetString(method.Name);
            if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0 ||
                (method.ImplAttributes & MethodImplAttributes.CodeTypeMask) is MethodImplAttributes.Native or MethodImplAttributes.Runtime)
                incomplete.Add($"Method without analyzable IL: {name}");
            if (method.RelativeVirtualAddress != 0 && HasIndirectCall(image.GetMethodBody(method.RelativeVirtualAddress).GetILReader()))
                incomplete.Add($"Indirect call: {name}");
        }

        var complete = incomplete.Count == 0;
        return new CoverageReport("skecs-fixture-legacy-coverage-v1", "Closed fixture assembly and trusted .NET/SKECS modules", RuntimeInformation.FrameworkDescription,
            assembly, Hash(path), complete, trusted, incomplete.ToArray(),
            legacy.OrderBy(use => use.Assembly).ThenBy(use => use.Component).ThenBy(use => use.Consumer).ToArray(),
            candidates.Select(name => new CandidateCoverage(name,
                complete && !legacy.Any(use => use.Assembly == assembly && use.Component == name))).ToArray());
    }

    private static SortedDictionary<string, string> CheckReferences(string path, MetadataReader metadata, SortedSet<string> incomplete)
    {
        var platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator)
            .Where(file => Path.GetDirectoryName(file) == RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar))
            .DistinctBy(file => Path.GetFileNameWithoutExtension(file), StringComparer.Ordinal)
            .ToDictionary(file => Path.GetFileNameWithoutExtension(file), StringComparer.Ordinal);
        var trusted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var handle in metadata.AssemblyReferences)
        {
            var reference = metadata.GetAssemblyReference(handle);
            var name = metadata.GetString(reference.Name);
            if (name is "SKECS" or "SKECS.Profiler")
            {
                var library = Path.Combine(Path.GetDirectoryName(path)!, name + ".dll");
                if (File.Exists(library) && AssemblyName.GetAssemblyName(library).Name == name)
                    trusted.Add(name, Hash(library));
                else
                    incomplete.Add($"Trusted SKECS binary missing: {name}");
            }
            else if (!platform.TryGetValue(name, out var framework) ||
                !metadata.GetBlobBytes(reference.PublicKeyOrToken).SequenceEqual(AssemblyName.GetAssemblyName(framework).GetPublicKeyToken() ?? []))
            {
                incomplete.Add($"Uninspected assembly reference: {name}");
            }
        }
        return trusted;
    }

    private static bool IsLegacyMethod(string type, string member) => (type, member) switch
    {
        ("SKECS.World", "Query") => true,
        ("SKECS.QueryBuilder", "With" or "Without") => true,
        ("SKECS.DeterministicQuery", "With" or "Without" or "Read" or "Write" or "ReadWrite") => true,
        _ => false,
    };

    private static bool IsLegacyQueryType(string name) => name.StartsWith("SKECS.Query`", StringComparison.Ordinal) ||
        name.StartsWith("SKECS.QueryChunkEnumerable`", StringComparison.Ordinal) ||
        name.StartsWith("SKECS.QueryChunkView`", StringComparison.Ordinal);

    private static bool IsDynamicBoundary(Shape type, string member) =>
        (type.Name.StartsWith("System.Reflection.", StringComparison.Ordinal) && !type.Name.EndsWith("Attribute", StringComparison.Ordinal)
            && (type.Name, member) != ("System.Reflection.MemberInfo", "get_Name")) ||
        type.Name.StartsWith("System.Runtime.Loader.", StringComparison.Ordinal) ||
        type.Name.StartsWith("System.Linq.Expressions.", StringComparison.Ordinal) ||
        (type.Name is "System.Activator" or "System.Runtime.InteropServices.NativeLibrary") ||
        (type.Name == "System.AppDomain" && member is "Load" or "CreateInstance" or "CreateInstanceAndUnwrap" or "ExecuteAssembly") ||
        (type.Name == "System.Runtime.InteropServices.Marshal" && member is "GetDelegateForFunctionPointer" or "GetFunctionPointerForDelegate") ||
        (type.Name == "System.Delegate" && member is "DynamicInvoke" or "CreateDelegate") ||
        (type.Name == "System.Type" && member is not ("GetTypeFromHandle" or "get_Name" or "get_FullName" or "EmptyTypes" or "op_Equality" or "op_Inequality"));

    private static void Record(ImmutableArray<Shape> arguments, string consumer, SortedSet<string> incomplete, HashSet<LegacyUse> uses)
    {
        if (arguments.IsDefaultOrEmpty || arguments.Any(type => !type.Known))
            incomplete.Add($"Open generic legacy consumer: {consumer}");
        foreach (var type in arguments)
            if (type.Known) uses.Add(new LegacyUse(type.Assembly, type.Name, consumer));
    }

    private static bool HasIndirectCall(BlobReader il)
    {
        while (il.RemainingBytes != 0)
        {
            ushort code = il.ReadByte();
            if (code == 0xfe) code = (ushort)(0xfe00 | il.ReadByte());
            if (!Instructions.TryGetValue(code, out var opcode)) throw new BadImageFormatException("Invalid IL opcode.");
            if (opcode == OpCodes.Calli) return true;
            var operands = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(il.ReadInt32() * 4),
                _ => 4,
            };
            if (operands < 0 || operands > il.RemainingBytes) throw new BadImageFormatException("Invalid IL operand.");
            il.Offset += operands;
        }
        return false;
    }

    private static Shape ResolveType(MetadataReader metadata, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeReference => Shapes.GetTypeFromReference(metadata, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeDefinition => Shapes.GetTypeFromDefinition(metadata, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeSpecification => Shapes.GetTypeFromSpecification(metadata, null, (TypeSpecificationHandle)handle, 0),
        _ => Shape.Unknown,
    };

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private readonly record struct Shape(string Assembly, string Name, ImmutableArray<Shape> Arguments, bool Known = true)
    {
        internal static Shape Unknown => new("", "", [], false);
    }

    private sealed class ShapeProvider : ISignatureTypeProvider<Shape, object?>
    {
        public Shape GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            var name = reader.GetString(type.Name);
            var parent = type.GetDeclaringType();
            if (!parent.IsNil)
            {
                var declaring = GetTypeFromDefinition(reader, parent, 0);
                return new Shape(declaring.Assembly, declaring.Name + "+" + name, []);
            }
            return new Shape(reader.GetString(reader.GetAssemblyDefinition().Name), Qualify(reader.GetString(type.Namespace), name), []);
        }

        public Shape GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            var name = reader.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                var declaring = GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, 0);
                return new Shape(declaring.Assembly, declaring.Name + "+" + name, [], declaring.Known);
            }
            if (type.ResolutionScope.Kind != HandleKind.AssemblyReference) return Shape.Unknown;
            var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope);
            return new Shape(reader.GetString(assembly.Name), Qualify(reader.GetString(type.Namespace), name), []);
        }

        public Shape GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public Shape GetGenericInstantiation(Shape genericType, ImmutableArray<Shape> typeArguments) => genericType with
        {
            Arguments = typeArguments,
            Known = genericType.Known && typeArguments.All(type => type.Known),
        };

        public Shape GetGenericMethodParameter(object? context, int index) => Shape.Unknown;
        public Shape GetGenericTypeParameter(object? context, int index) => Shape.Unknown;
        public Shape GetArrayType(Shape elementType, ArrayShape shape) => Shape.Unknown;
        public Shape GetSZArrayType(Shape elementType) => Shape.Unknown;
        public Shape GetByReferenceType(Shape elementType) => Shape.Unknown;
        public Shape GetPointerType(Shape elementType) => Shape.Unknown;
        public Shape GetPinnedType(Shape elementType) => Shape.Unknown;
        public Shape GetFunctionPointerType(MethodSignature<Shape> signature) => Shape.Unknown;
        public Shape GetModifiedType(Shape modifier, Shape unmodifiedType, bool isRequired) => unmodifiedType;
        public Shape GetPrimitiveType(PrimitiveTypeCode typeCode) => new("System.Private.CoreLib", "System." + typeCode, []);
        private static string Qualify(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
    }
}
