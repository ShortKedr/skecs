using System.Globalization;

namespace SKECS.GA;

internal static class GaAdditionEmitter
{
    internal const string AdditionType = "global::SKECS.GA.GaComponentAddition";
    internal const string EventType = "global::SKECS.GA.GaTypedEvent";

    internal static void EmitTable(GaProgram program, string tableName, Action<string> line) =>
        EmitTable(program.AdditionComponents.Select(item => (item.Id, item.Fields, item.Offsets, item.WordCount, false)).ToArray(),
            AdditionType, "__GaDecodeAddition", $"private static readonly {AdditionType}[] {tableName} =", line);

    internal static void EmitEventTable(GaProgram program, string tableName, Action<string> line)
    {
        string declaration = program.ReductionTypeCount == 0
            ? $"private static readonly {EventType}[] {tableName} ="
            : $"public static {EventType}[] {tableName}({ReductionParameters(program)}) => new {EventType}[]";
        EmitTable(program.EventTypes.Select(item => (item.Id, item.Fields, item.Offsets, item.WordCount, item.IsContribution)).ToArray(),
            EventType, "__GaDecodeEvent", declaration, line);
    }

    internal static string ReductionParameters(GaProgram program) =>
        string.Join(", ", program.EventTypes.Select((item, index) => (item, index))
            .Where(entry => entry.item.IsContribution)
            .Select(entry => $"global::SKECS.DeterministicReduction<{entry.item.Id}> reduction{Number(entry.index)}"));

    internal static string ReductionArguments(GaProgram program) =>
        string.Join(", ", program.EventTypes.Select((item, index) => (item, index))
            .Where(entry => entry.item.IsContribution).Select(entry => $"reduction{Number(entry.index)}"));

    private static void EmitTable(
        (string Id, IReadOnlyList<GaAdditionField> Fields, int[] Offsets, int WordCount, bool IsContribution)[] items,
        string bindingType, string decoderPrefix, string declaration, Action<string> line)
    {
        line(declaration);
        line("{");
        for (int index = 0; index < items.Length; index++)
        {
            var component = items[index];
            string booleans = string.Join(", ", component.Fields
                .Select((field, fieldIndex) => (field, fieldIndex))
                .Where(item => item.field.Type == GaScalar.Bool)
                .Select(item => Number(component.Offsets[item.fieldIndex])));
            string binding = component.IsContribution ? "ForContribution" : "For";
            string channel = component.IsContribution ? $"reduction{Number(index)}, " : "";
            line($"    {bindingType}.{binding}<{component.Id}>({channel}{decoderPrefix}{Number(index)}, {Number(component.WordCount)}, [{booleans}]),");
        }
        line("};");

        for (int index = 0; index < items.Length; index++)
        {
            var component = items[index];
            line("");
            line($"private static {component.Id} {decoderPrefix}{Number(index)}(global::System.ReadOnlySpan<uint> words)");
            line("{");
            line($"    {component.Id} value = default;");
            for (int fieldIndex = 0; fieldIndex < component.Fields.Count; fieldIndex++)
            {
                var field = component.Fields[fieldIndex];
                string target = field.Path.Length == 0
                    ? "value"
                    : GaFieldPath.CSharpAccess("value", field.Path);
                int word = component.Offsets[fieldIndex];
                string first = $"words[{Number(word)}]";
                string bits = field.Type switch
                {
                    GaScalar.Bool => $"{first} != 0",
                    GaScalar.Int32 => $"unchecked((int){first})",
                    GaScalar.UInt32 => first,
                    GaScalar.Fixed => $"global::SKECS.Numerics.Fixed.FromRaw(unchecked((long)((ulong){first} | ((ulong)words[{Number(word + 1)}] << 32))))",
                    _ => throw new InvalidOperationException("Unknown GA packed payload field type.")
                };
                if (field.ClrEnumType != null)
                    bits = $"({field.ClrEnumType})({bits})";
                line($"    {target} = {bits};");
            }
            line("    return value;");
            line("}");
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
