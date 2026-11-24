using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SKECS.GA.Analysis;

internal static class GaFixedArrayAccess
{
    internal static bool TryGet(IOperation? operation, Compilation compilation, out IOperation storage,
        out IOperation index, out bool readOnly)
    {
        if (operation is IInvocationOperation { TargetMethod: { IsStatic: true, Arity: 0, Name: "At" or "Read" } method } call &&
            call.Arguments.Length == 2 &&
            call.Arguments[0] is { Parameter.Ordinal: 0 } first && call.Arguments[1] is { Parameter.Ordinal: 1 } second &&
            method.ContainingType.OriginalDefinition is { MetadataName: "FixedArray16`1" } definition &&
            definition.ContainingAssembly.Identity.Name == "SKECS.GA" &&
            definition.DeclaringSyntaxReferences.Length == 0 &&
            SymbolEqualityComparer.Default.Equals(definition, compilation.GetTypeByMetadataName("SKECS.GA.FixedArray16`1")) &&
            SymbolEqualityComparer.Default.Equals(definition.ContainingAssembly,
                compilation.GetTypeByMetadataName("SKECS.GA.SecsSystemAttribute")?.ContainingAssembly))
        {
            storage = first.Value;
            index = second.Value;
            readOnly = method.ReturnsByRefReadonly;
            return true;
        }

        storage = index = null!;
        readOnly = false;
        return false;
    }
}
