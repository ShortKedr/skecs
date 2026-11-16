using System.Globalization;
using System.Text;

namespace SKECS.GA;

public sealed record GaSlangModule(string Source, IReadOnlyList<GaSourceSpan> FaultSources)
{
    public const string EntryPoint = "main";
    public const int ThreadsPerGroup = 64;
    public const int FaultStride = GaPackedDispatchResult.FaultStride;
}

public static class GaSlangEmitter
{
    public static GaSlangModule EmitVulkan(GaProgram program) => Emit(program, vulkan: true);

    public static GaSlangModule EmitD3D12(GaProgram program) => Emit(program, vulkan: false);

    private static GaSlangModule Emit(GaProgram program, bool vulkan)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.Fields.Count == 0)
            throw new ArgumentException("The packed graphics profile requires at least one component field.", nameof(program));
        return new Emitter().Emit(program, vulkan);
    }

    private sealed class Emitter
    {
        private readonly StringBuilder _code = new();
        private readonly List<GaSourceSpan> _sources = new();
        private int _indent;
        private int _temporary;

        internal GaSlangModule Emit(GaProgram program, bool vulkan)
        {
            if (program.UsesFixed)
                _code.Append(GaFixedSlangLibrary.Load()).Append('\n');
            if (vulkan)
            {
                Line("[[vk::binding(0, 0)]] StructuredBuffer<uint> gaInput;");
                Line("[[vk::binding(1, 0)]] RWStructuredBuffer<uint> gaOutput;");
                Line("[[vk::binding(2, 0)]] RWStructuredBuffer<uint> gaFaults;");
            }
            else
            {
                Line("StructuredBuffer<uint> gaInput : register(t0);");
                Line("RWStructuredBuffer<uint> gaOutput : register(u0);");
                Line("RWStructuredBuffer<uint> gaFaults : register(u1);");
            }
            Line("");
            Line("[shader(\"compute\")]");
            Line(FormattableString.Invariant($"[numthreads({GaSlangModule.ThreadsPerGroup}, 1, 1)]"));
            Line("void main(uint3 id : SV_DispatchThreadID)");
            Open();
            Line("if (id.x >= gaInput[0]) return;");
            int stride = program.WordCount;
            Line($"uint gaRow = id.x * {Word(stride)};");
            Line($"uint gaFault = id.x * {Word(GaSlangModule.FaultStride)};");
            Line("uint2 gaOrdinal = uint2(0u, 0u);");
            for (int i = 0; i < program.Fields.Count; i++)
            {
                int offset = program.Offsets[i];
                if (program.Fields[i].Type == GaScalar.Fixed)
                    Line($"uint2 {Field(i)} = uint2(gaInput[1u + gaRow + {Word(offset)}], gaInput[1u + gaRow + {Word(offset + 1)}]);");
                else
                    Line($"uint {Field(i)} = gaInput[1u + gaRow + {Word(offset)}];");
            }
            for (int i = 0; i < program.Locals.Count; i++)
                Line($"{Type(program.Locals[i])} {Local(i)} = {Literal(program.Locals[i], 0)};");
            Statement(program.Body);
            for (int i = 0; i < program.Fields.Count; i++)
            {
                int offset = program.Offsets[i];
                if (program.Fields[i].Type == GaScalar.Fixed)
                {
                    Line($"gaOutput[gaRow + {Word(offset)}] = {Field(i)}.x;");
                    Line($"gaOutput[gaRow + {Word(offset + 1)}] = {Field(i)}.y;");
                }
                else
                    Line($"gaOutput[gaRow + {Word(offset)}] = {Field(i)};");
            }
            for (int i = 0; i < GaSlangModule.FaultStride; i++)
                Line($"gaFaults[gaFault + {Word(i)}] = 0u;");
            Close();
            return new GaSlangModule(_code.ToString(), Array.AsReadOnly(_sources.ToArray()));
        }

        private void Statement(GaStatement statement)
        {
            switch (statement)
            {
                case GaBlock block:
                    foreach (var child in block.Statements)
                        Statement(child);
                    break;
                case GaStore assignment:
                    Line($"{Slot(assignment.Slot)} = {Value(assignment.Value)};");
                    break;
                case GaIf branch:
                    Line($"if ({Value(branch.Condition)} != 0u)");
                    Open();
                    Statement(branch.Then);
                    Close();
                    if (branch.Else != null)
                    {
                        Line("else");
                        Open();
                        Statement(branch.Else);
                        Close();
                    }
                    break;
                case GaLoop loop:
                    string counter = Temp("0u");
                    Line("while (true)");
                    Open();
                    Line($"if ({Value(loop.Condition)} == 0u) break;");
                    FaultWhen($"{counter} == {Word(loop.MaximumIterations)}", GaFaultKind.IterationLimit,
                        "gaOrdinal", loop.Condition.Source);
                    Statement(loop.Body);
                    Statement(loop.Increment);
                    Line($"++{counter};");
                    Close();
                    break;
                default:
                    throw new InvalidOperationException("Unknown GA IR statement.");
            }
        }

        private string Value(GaValue value)
        {
            switch (value)
            {
                case GaConstant constant:
                    return Literal(constant.Type, constant.Bits);
                case GaLoad load:
                    return Slot(load.Slot);
                case GaCast cast:
                    string bits = Value(cast.Operand);
                    if (cast.Type == GaScalar.Fixed)
                        return Temp($"gaFixedFromInt32({bits})", "uint2");
                    if (cast.Operand.Type == GaScalar.Fixed)
                        return Temp($"gaFixedToInt32({bits})");
                    if (cast.Checked && cast.Type != cast.Operand.Type)
                        FaultWhen($"{bits} >= 0x80000000u", GaFaultKind.Overflow, NextOrdinal(), cast.Source);
                    return bits;
                case GaUnary unary:
                    string operand = Value(unary.Operand);
                    if (unary.Type == GaScalar.Fixed)
                        return FixedArithmetic($"gaFixedNegate({operand})", unary.Source);
                    if (unary.Checked)
                        FaultWhen($"{operand} == 0x80000000u", GaFaultKind.Overflow, NextOrdinal(), unary.Source);
                    return Temp(unary.Operator switch
                    {
                        GaUnaryOperator.Not => $"{operand} ^ 1u",
                        GaUnaryOperator.BitwiseNot => $"~{operand}",
                        GaUnaryOperator.Negate => $"0u - {operand}",
                        _ => throw new InvalidOperationException()
                    });
                case GaSelect select:
                    string condition = Value(select.Condition);
                    string result = Temp(Literal(select.Type, 0), Type(select.Type));
                    Line($"if ({condition} != 0u)");
                    Open();
                    Line($"{result} = {Value(select.True)};");
                    Close();
                    Line("else");
                    Open();
                    Line($"{result} = {Value(select.False)};");
                    Close();
                    return result;
                case GaBinary binary:
                    return Binary(binary);
                default:
                    throw new InvalidOperationException("Unknown GA IR value.");
            }
        }

        private string Binary(GaBinary binary)
        {
            var op = binary.Operator;
            string left = Value(binary.Left);
            if (op is GaBinaryOperator.AndAlso or GaBinaryOperator.OrElse)
            {
                string result = Temp(left);
                Line($"if ({result} {(op == GaBinaryOperator.AndAlso ? "!=" : "==")} 0u)");
                Open();
                Line($"{result} = {Value(binary.Right)};");
                Close();
                return result;
            }

            string right = Value(binary.Right);
            if (binary.Left.Type == GaScalar.Fixed)
                return FixedBinary(binary, left, right);
            bool signed = binary.Left.Type == GaScalar.Int32;
            if (op is GaBinaryOperator.Divide or GaBinaryOperator.Remainder)
                return Quotient(binary, left, right, signed);

            string symbol = op switch
            {
                GaBinaryOperator.Add => "+",
                GaBinaryOperator.Subtract => "-",
                GaBinaryOperator.Multiply => "*",
                GaBinaryOperator.BitwiseAnd => "&",
                GaBinaryOperator.BitwiseOr => "|",
                GaBinaryOperator.ExclusiveOr => "^",
                GaBinaryOperator.LeftShift => "<<",
                GaBinaryOperator.RightShift or GaBinaryOperator.UnsignedRightShift => ">>",
                GaBinaryOperator.Equal => "==",
                GaBinaryOperator.NotEqual => "!=",
                GaBinaryOperator.Less => "<",
                GaBinaryOperator.LessOrEqual => "<=",
                GaBinaryOperator.Greater => ">",
                GaBinaryOperator.GreaterOrEqual => ">=",
                _ => throw new InvalidOperationException()
            };

            if (op is GaBinaryOperator.RightShift && signed)
                return Temp($"asuint(asint({left}) >> ({right} & 31u))");
            if (op is GaBinaryOperator.LeftShift or GaBinaryOperator.RightShift or GaBinaryOperator.UnsignedRightShift)
                return Temp($"{left} {symbol} ({right} & 31u)");
            if (op is GaBinaryOperator.Less or GaBinaryOperator.LessOrEqual or GaBinaryOperator.Greater or GaBinaryOperator.GreaterOrEqual)
                return Temp(signed ? $"(asint({left}) {symbol} asint({right})) ? 1u : 0u"
                    : $"({left} {symbol} {right}) ? 1u : 0u");
            if (op is GaBinaryOperator.Equal or GaBinaryOperator.NotEqual)
                return Temp($"({left} {symbol} {right}) ? 1u : 0u");

            string computed = Temp($"{left} {symbol} {right}");
            if (binary.Checked)
                CheckOverflow(binary, left, right, computed, signed);
            return computed;
        }

        private string FixedBinary(GaBinary binary, string left, string right)
        {
            return binary.Operator switch
            {
                GaBinaryOperator.Add => FixedArithmetic($"gaFixedAdd({left}, {right})", binary.Source),
                GaBinaryOperator.Subtract => FixedArithmetic($"gaFixedSubtract({left}, {right})", binary.Source),
                GaBinaryOperator.Multiply => FixedArithmetic($"gaFixedMultiply({left}, {right})", binary.Source),
                GaBinaryOperator.Divide => FixedArithmetic($"gaFixedDivide({left}, {right})", binary.Source, divide: true),
                GaBinaryOperator.Equal => Temp($"uint(gaWideEqual({left}, {right}))"),
                GaBinaryOperator.NotEqual => Temp($"uint(!gaWideEqual({left}, {right}))"),
                GaBinaryOperator.Less => Temp($"uint(gaFixedLess({left}, {right}))"),
                GaBinaryOperator.LessOrEqual => Temp($"uint(!gaFixedLess({right}, {left}))"),
                GaBinaryOperator.Greater => Temp($"uint(gaFixedLess({right}, {left}))"),
                GaBinaryOperator.GreaterOrEqual => Temp($"uint(!gaFixedLess({left}, {right}))"),
                _ => throw new InvalidOperationException("Unknown Fixed IR operator.")
            };
        }

        private string FixedArithmetic(string expression, GaSourceSpan span, bool divide = false)
        {
            string ordinal = NextOrdinal();
            string temporary = Temp(expression, "GaFixedResult");
            if (divide)
                FaultWhen($"{temporary}.fault == 2u", GaFaultKind.DivideByZero, ordinal, span);
            FaultWhen($"{temporary}.fault != 0u", GaFaultKind.Overflow, ordinal, span);
            return temporary + ".bits";
        }

        private string Quotient(GaBinary binary, string left, string right, bool signed)
        {
            string ordinal = NextOrdinal();
            FaultWhen($"{right} == 0u", GaFaultKind.DivideByZero, ordinal, binary.Source);
            string op = binary.Operator == GaBinaryOperator.Divide ? "/" : "%";
            if (!signed)
                return Temp($"{left} {op} {right}");

            FaultWhen($"{left} == 0x80000000u && {right} == 0xffffffffu", GaFaultKind.Overflow, ordinal, binary.Source);
            string magnitudeLeft = Magnitude(left);
            string magnitudeRight = Magnitude(right);
            string absolute = Temp($"{magnitudeLeft} {op} {magnitudeRight}");
            string sign = binary.Operator == GaBinaryOperator.Divide ? $"({left} ^ {right})" : left;
            return Temp($"{sign} >= 0x80000000u ? 0u - {absolute} : {absolute}");
        }

        private void CheckOverflow(GaBinary op, string left, string right, string result, bool signed)
        {
            string ordinal = NextOrdinal();
            if (op.Operator == GaBinaryOperator.Multiply)
            {
                string x = signed ? Magnitude(left) : left;
                string y = signed ? Magnitude(right) : right;
                string limit = signed ? $"(({left} ^ {right}) >= 0x80000000u ? 0x80000000u : 0x7fffffffu)" : "0xffffffffu";
                Line($"if ({y} != 0u)");
                Open();
                FaultWhen($"{x} > {limit} / {y}", GaFaultKind.Overflow, ordinal, op.Source);
                Close();
                return;
            }

            bool add = op.Operator == GaBinaryOperator.Add;
            string condition = (signed, add) switch
            {
                (true, true) => $"(({left} ^ {result}) & ({right} ^ {result})) >= 0x80000000u",
                (true, false) => $"(({left} ^ {result}) & ({left} ^ {right})) >= 0x80000000u",
                (false, true) => $"{result} < {left}",
                _ => $"{left} < {right}"
            };
            FaultWhen(condition, GaFaultKind.Overflow, ordinal, op.Source);
        }

        private string Magnitude(string value) => Temp($"{value} >= 0x80000000u ? 0u - {value} : {value}");

        private string NextOrdinal()
        {
            string name = Name();
            Line($"uint2 {name} = gaOrdinal;");
            Line("++gaOrdinal.x;");
            Line("if (gaOrdinal.x == 0u) ++gaOrdinal.y;");
            return name;
        }

        private void FaultWhen(string condition, GaFaultKind kind, string ordinal, GaSourceSpan span)
        {
            int site = _sources.Count;
            _sources.Add(span);
            Line($"if ({condition})");
            Open();
            Line($"gaFaults[gaFault] = {Word((uint)kind)};");
            Line($"gaFaults[gaFault + 1u] = {ordinal}.x;");
            Line($"gaFaults[gaFault + 2u] = {ordinal}.y;");
            Line($"gaFaults[gaFault + 3u] = {Word(site)};");
            Line("return;");
            Close();
        }

        private string Temp(string value, string type = "uint")
        {
            string name = Name();
            Line($"{type} {name} = {value};");
            return name;
        }

        private string Name() => "t" + (_temporary++).ToString(CultureInfo.InvariantCulture);
        private static string Word(int value) => Word((uint)value);
        private static string Word(uint value) => value.ToString(CultureInfo.InvariantCulture) + "u";
        private static string Type(GaScalar type) => type == GaScalar.Fixed ? "uint2" : "uint";
        private static string Literal(GaScalar type, ulong value) => type == GaScalar.Fixed
            ? $"uint2({Word(unchecked((uint)value))}, {Word((uint)(value >> 32))})"
            : Word(checked((uint)value));
        private static string Field(int index) => "f" + index.ToString(CultureInfo.InvariantCulture);
        private static string Local(int index) => "l" + index.ToString(CultureInfo.InvariantCulture);
        private static string Slot(GaSlot slot) => slot.IsField ? Field(slot.Index) : Local(slot.Index);
        private void Line(string text) => _code.Append(' ', _indent * 4).Append(text).Append('\n');
        private void Open() { Line("{"); _indent++; }
        private void Close() { _indent--; Line("}"); }
    }
}
