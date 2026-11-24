using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SKECS.GA;

internal sealed partial class GaLowerer
{
    private Dictionary<string, GaSlot> CaptureArrayValue(ITypeSymbol type, IOperation operation,
        ArrayAccess access, List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)> bindings)
    {
        var fields = CreateAggregate(type, operation, bindings);
        var layout = ReadArrayLayout(operation, access);
        var indexed = AggregateFields(type, operation).Select(item => new GaArrayValueField(
            ArrayField(operation, layout, "." + item.Path, fields[item.Path].Type, item.Field.Type,
                writable: false, fieldReadOnly: item.ReadOnly), fields[item.Path])).ToArray();
        if (indexed.Length != 0)
            AddAggregateEffect(bindings, new GaArrayValueRead(indexed, LowerValue(access.Index), Source(access.Site)), operation);
        else
            throw Unsupported(operation, "Index a structure with at least one portable field.");
        return fields;
    }

    private GaStatement LowerArrayValueStore(IOperation target, ArrayAccess access, IOperation value,
        ICompoundAssignmentOperation? compound)
    {
        var type = ResolveHelperType(target.Type)!;
        var layout = ReadArrayLayout(target, access);
        var leaves = AggregateFields(type, target).ToArray();
        if (leaves.Length == 0)
            throw Unsupported(target, "Index a structure with at least one portable field.");
        var arrays = leaves.Select(item => ArrayField(target, layout, "." + item.Path, Scalar(item.Field.Type, target),
            item.Field.Type, writable: true, fieldReadOnly: item.ReadOnly, replaceValue: true)).ToArray();
        var index = LowerValue(access.Index);
        var bindings = new List<(GaSlot Slot, GaValue Value, GaSourceSpan Source)>();
        Dictionary<string, GaSlot> result;
        GaSlot[]? previous = null;
        if (compound == null)
        {
            result = CaptureAggregate(type, value, bindings);
        }
        else
        {
            previous = arrays.Select(array => AddLocal(array.Type)).ToArray();
            var previousFields = new Dictionary<string, GaSlot>(StringComparer.Ordinal);
            for (int i = 0; i < leaves.Length; i++)
                previousFields.Add(leaves[i].Path, previous[i]);
            result = CompoundOperand(compound, new BoundOperand(type, null, previousFields), bindings).Fields!;
        }
        var fields = arrays.Select((array, i) =>
            new GaArrayValueField(array, result[leaves[i].Path], leaves[i].ReadOnly)).ToArray();
        return new GaArrayValueStore(fields, index, previous, BindingStatements(bindings), Source(access.Site));
    }
}
