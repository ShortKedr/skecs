using System.Globalization;

namespace SKECS.GA;

internal static class GaAdditionEmitter
{
    internal const string AdditionType = "global::SKECS.GA.GaComponentAddition";

    internal static void EmitTable(GaProgram program, string tableName, Action<string> line)
    {
        line($"private static readonly {AdditionType}[] {tableName} =");
        line("{");
        for (int index = 0; index < program.AdditionComponents.Count; index++)
        {
            var component = program.AdditionComponents[index];
            string booleans = string.Join(", ", component.Fields
                .Select((field, fieldIndex) => (field, fieldIndex))
                .Where(item => item.field.Type == GaScalar.Bool)
                .Select(item => Number(component.Offsets[item.fieldIndex])));
            line($"    {AdditionType}.For<{component.Id}>(__GaDecodeAddition{Number(index)}, {Number(component.WordCount)}, [{booleans}]),");
        }
        line("};");

        for (int index = 0; index < program.AdditionComponents.Count; index++)
        {
            var component = program.AdditionComponents[index];
            line("");
            line($"private static {component.Id} __GaDecodeAddition{Number(index)}(global::System.ReadOnlySpan<uint> words)");
            line("{");
            line($"    {component.Id} value = default;");
            for (int fieldIndex = 0; fieldIndex < component.Fields.Count; fieldIndex++)
            {
                var field = component.Fields[fieldIndex];
                string path = string.Join(".", field.Path.Split('.').Select(segment => "@" + segment));
                int word = component.Offsets[fieldIndex];
                string first = $"words[{Number(word)}]";
                string bits = field.Type switch
                {
                    GaScalar.Bool => $"{first} != 0",
                    GaScalar.Int32 => $"unchecked((int){first})",
                    GaScalar.UInt32 => first,
                    GaScalar.Fixed => $"global::SKECS.Numerics.Fixed.FromRaw(unchecked((long)((ulong){first} | ((ulong)words[{Number(word + 1)}] << 32))))",
                    _ => throw new InvalidOperationException("Unknown GA addition field type.")
                };
                if (field.ClrEnumType != null)
                    bits = $"({field.ClrEnumType})({bits})";
                line($"    value.{path} = {bits};");
            }
            line("    return value;");
            line("}");
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
