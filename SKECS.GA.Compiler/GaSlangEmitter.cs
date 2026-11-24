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
    public const int MaximumD3D12WorkgroupX = 1024;

    public static GaSlangModule EmitVulkan(GaProgram program) => EmitVulkan(program, GaSlangModule.ThreadsPerGroup);

    public static GaSlangModule EmitVulkan(GaProgram program, int workgroupX) => Emit(program, workgroupX, vulkan: true);

    public static GaSlangModule EmitWebGpu(GaProgram program, int workgroupX) =>
        Emit(program, workgroupX, vulkan: true, webGpu: true);

    internal static GaSlangModule EmitOpenGlEs(GaProgram program, int workgroupX) =>
        Emit(program, workgroupX, vulkan: true, openGlEs: true);

    internal static GaSlangModule EmitUnityCompute(GaProgram program, int workgroupX) =>
        Emit(program, workgroupX, vulkan: false, unityCompute: true);

    public static GaSlangModule EmitD3D12(GaProgram program) => EmitD3D12(program, GaSlangModule.ThreadsPerGroup);

    public static GaSlangModule EmitD3D12(GaProgram program, int workgroupX)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfGreaterThan(workgroupX, MaximumD3D12WorkgroupX);
#else
        if (workgroupX > MaximumD3D12WorkgroupX)
            throw new ArgumentOutOfRangeException(nameof(workgroupX), workgroupX, "The workgroup size exceeds the D3D12 limit.");
#endif
        return Emit(program, workgroupX, vulkan: false);
    }

    private static GaSlangModule Emit(GaProgram program, int workgroupX, bool vulkan, bool webGpu = false,
        bool openGlEs = false, bool unityCompute = false)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(program);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workgroupX);
#else
        if (program is null) throw new ArgumentNullException(nameof(program));
        if (workgroupX < 1)
            throw new ArgumentOutOfRangeException(nameof(workgroupX), workgroupX, "The workgroup size must be positive.");
#endif
        return new Emitter(program, webGpu, openGlEs, unityCompute).Emit(vulkan, workgroupX);
    }

    private sealed class Emitter(GaProgram program, bool webGpu, bool openGlEs, bool unityCompute)
    {
        private const string InvalidLayout = "0xffffffffu";
        private readonly bool _deferredFaultWrites = openGlEs || unityCompute;
        private string LayoutExit => _deferredFaultWrites ? $"return uint4({InvalidLayout}, 0u, 0u, 0u);" : "return;";
        private readonly StringBuilder _code = new();
        private readonly List<GaSourceSpan> _sources = new();
        private readonly HashSet<int> _snapshotKeyWrites = program.UsesSnapshot
            ? GaRowCodecEmitter.WrittenFields(program.Body).ToHashSet() : [];
        private readonly List<SnapshotOffset> _snapshotOffsets = new();
        private readonly bool _usesOptional = program.Components.Any(component => component.Origin == GaComponentOrigin.Optional);
        private int _indent;
        private int _temporary;
        private readonly int _eventCapacity = program.EventCapacity;
        private readonly bool _mixedOutputs = UsesPackedOutputs(program);
        private readonly int _recordWords = UsesPackedOutputs(program)
            ? checked(GaOutputRecord.Words + program.OutputPayloadWords +
                (program.UsesDomainOrder ? GaOutputRecord.DomainWordCount : 0))
            : 1;

        private static bool UsesPackedOutputs(GaProgram program) =>
            program.UsesCommands || program.UsesDomainOrder || program.EventTypes.Count != 0;

        internal GaSlangModule Emit(bool vulkan, int workgroupX)
        {
            if (unityCompute)
            {
                Line($"#pragma kernel {GaUnityComputeAbi.MainKernel}");
                Line($"#pragma kernel {GaUnityComputeAbi.ProbeKernel}");
                Line("");
                Line($"StructuredBuffer<uint> {GaUnityComputeAbi.InputBuffer};");
                Line($"RWStructuredBuffer<uint> {GaUnityComputeAbi.OutputBuffer};");
                Line($"RWStructuredBuffer<uint> {GaUnityComputeAbi.FaultBuffer};");
                Line($"uint {GaUnityComputeAbi.InputWords};");
                Line($"uint {GaUnityComputeAbi.OutputWords};");
                Line($"uint {GaUnityComputeAbi.FaultWords};");
                Line("");
            }
            if (webGpu && (program.UsesSnapshot || program.TickResourceWords != 0 || program.BufferSchema is not null))
            {
                // Slang 2025.6.1 does not lower StructuredBuffer.GetDimensions to WGSL.
                _code.Append(GaCompilerText.NormalizeLineEndings("""
                    uint gaWgslInputLength(StructuredBuffer<uint> words)
                    {
                        __intrinsic_asm "arrayLength(&$0)";
                    }
                    """)).Append("\n\n");
            }
            if (program.UsesFixed)
                _code.Append(GaFixedSlangLibrary.Load()).Append('\n');
            if (program.UsesSnapshot)
                _code.Append(ReadSnapshotLibrary()).Append('\n');
            if (vulkan)
            {
                Line("[[vk::binding(0, 0)]] StructuredBuffer<uint> gaInput;");
                Line("[[vk::binding(1, 0)]] RWStructuredBuffer<uint> gaOutput;");
                Line("[[vk::binding(2, 0)]] RWStructuredBuffer<uint> gaFaults;");
            }
            else if (!unityCompute)
            {
                Line("StructuredBuffer<uint> gaInput : register(t0);");
                Line("RWStructuredBuffer<uint> gaOutput : register(u0);");
                Line("RWStructuredBuffer<uint> gaFaults : register(u1);");
            }
            Line("");
            if (_deferredFaultWrites)
            {
                Line("uint4 gaExecute(uint3 id)");
                Open();
            }
            else
            {
                OpenEntryPoint(workgroupX);
            }
            int stride = Math.Max(1, program.WordCount);
            Line($"uint gaRow = id.x * {Word(stride)};");
            if (!_deferredFaultWrites)
                Line($"uint gaFault = id.x * {Word(GaSlangModule.FaultStride)};");
            Line("uint2 gaOrdinal = uint2(0u, 0u);");
            if (program.TickResourceWords != 0)
                TickResourceLayout(stride);
            if (program.BufferSchema is not null)
                BufferLayout(stride);
            if (program.UsesSnapshot)
                SnapshotLayout(stride);
            if (_eventCapacity != 0)
                EventLayout(stride);
            for (int i = 0; i < program.Components.Count; i++)
            {
                int presence = program.PresenceOffsets[i];
                if (presence < 0)
                    continue;
                Line($"uint {Presence(i)} = gaInput[1u + gaRow + {Word(presence)}];");
                var component = program.Components[i];
                if (component.Origin == GaComponentOrigin.Own)
                    FaultWhen($"{Presence(i)} == 0u", GaFaultKind.MissingOwnComponent, "gaOrdinal", component.OwnSource);
            }
            if (program.UsesDomainOrder)
            {
                Line("uint gaHasDomainOrder = 0u;");
                Line("uint gaDomainPrimary = 0u, gaDomainSecondary = 0u;");
            }
            for (int i = 0; i < program.Fields.Count; i++)
            {
                int offset = program.Offsets[i];
                var type = program.Fields[i].Type;
                string firstWord = $"gaInput[1u + gaRow + {Word(offset)}]";
                string value = type == GaScalar.Fixed
                    ? $"uint2({firstWord}, gaInput[1u + gaRow + {Word(offset + 1)}])"
                    : firstWord;
                Line($"{Type(type)} {Field(i)} = {value};");
            }
            for (int i = 0; i < program.Locals.Count; i++)
                Line($"{Type(program.Locals[i])} {Local(i)} = {Literal(program.Locals[i], 0)};");
            for (int i = 0; i < program.TicketLocalCount; i++)
                Line($"uint {Ticket(i)} = {Word(uint.MaxValue)};");
            Statement(program.Body);
            if (program.WordCount == 0)
                Line("gaOutput[gaRow] = 0u;");
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
            for (int i = 0; i < program.Components.Count; i++)
            {
                int presence = program.PresenceOffsets[i];
                if (presence >= 0)
                    Line($"gaOutput[gaRow + {Word(presence)}] = {Presence(i)};");
            }
            if (!_deferredFaultWrites)
                for (int i = 0; i < GaSlangModule.FaultStride; i++)
                    Line($"gaFaults[gaFault + {Word(i)}] = 0u;");
            if (_eventCapacity != 0)
                Line("gaOutput[gaEventRow] = gaEventCount;");
            if (_deferredFaultWrites)
                Line("return uint4(0u, 0u, 0u, 0u);");
            Close();
            if (_deferredFaultWrites)
            {
                // Keep fault-buffer writes outside the nested control flow translated for GLES.
                Line("");
                OpenEntryPoint(workgroupX);
                if (unityCompute)
                    GuardUnityDispatch(stride);
                Line("uint4 result = gaExecute(id);");
                if (program.TickResourceWords != 0 || program.BufferSchema is not null)
                    Line($"if (result.x == {InvalidLayout}) return;");
                Line($"uint gaFault = id.x * {Word(GaSlangModule.FaultStride)};");
                for (int i = 0; i < GaSlangModule.FaultStride; i++)
                    Line($"gaFaults[gaFault + {Word(i)}] = result.{"xyzw"[i]};");
                Close();
            }
            if (unityCompute)
                UnityStartupProbe(workgroupX);
            return new GaSlangModule(_code.ToString(), Array.AsReadOnly(_sources.ToArray()));
        }

        private void OpenEntryPoint(int workgroupX)
        {
            if (!unityCompute)
                Line("[shader(\"compute\")]");
            OpenKernel(workgroupX, unityCompute ? GaUnityComputeAbi.MainKernel : GaSlangModule.EntryPoint);
            if (!unityCompute)
                Line("if (id.x >= gaInput[0]) return;");
        }

        private void OpenKernel(int workgroupX, string name)
        {
            Line(FormattableString.Invariant($"[numthreads({workgroupX}, 1, 1)]"));
            Line($"void {name}(uint3 id : SV_DispatchThreadID)");
            Open();
        }

        private void GuardUnityDispatch(int stride)
        {
            Line("if (gaInputWords == 0u) return;");
            Line("uint gaRows = gaInput[0];");
            Line("if (id.x >= gaRows) return;");
            Line($"if (gaRows > (gaInputWords - 1u) / {Word(stride)}) return;");
            Line($"if (gaRows > gaFaultWords / {Word(GaSlangModule.FaultStride)}) return;");
            if (_eventCapacity == 0)
                Line($"if (gaRows > gaOutputWords / {Word(stride)}) return;");
            else
            {
                int outputRowWords = checked(stride + 1 + _eventCapacity * _recordWords);
                string header = Word(GaEventPacket.HeaderWords);
                Line($"if (gaOutputWords < {header} || gaRows > (gaOutputWords - {header}) / {Word(outputRowWords)}) return;");
            }
        }

        private void UnityStartupProbe(int workgroupX)
        {
            Line("");
            OpenKernel(workgroupX, GaUnityComputeAbi.ProbeKernel);
            Line("if (gaInputWords < 3u || gaOutputWords < 2u || gaFaultWords < 4u) return;");
            Line("if (id.x != 0u || gaInput[0] != 1u) return;");
            Line("gaOutput[0] = gaInput[1] ^ 0x9e3779b9u;");
            Line("gaOutput[1] = gaInput[2] + 1u;");
            Line("gaFaults[0] = 1u;");
            Line("gaFaults[1] = gaInput[1];");
            Line("gaFaults[2] = gaInput[2];");
            Line("gaFaults[3] = 0u;");
            Close();
        }

        private string ReadSnapshotLibrary()
        {
            using var stream = typeof(GaSlangEmitter).Assembly.GetManifestResourceStream("SKECS.GA.GaSnapshotV1.slang")
                ?? throw new InvalidOperationException("Missing Slang implementation of the GA snapshot format.");
            using var source = new StreamReader(stream);
            string library = GaCompilerText.NormalizeLineEndings(source.ReadToEnd());
            if (!webGpu && !unityCompute)
                return library;
            const string dimensions = "uint bufferWords, stride;\n    words.GetDimensions(bufferWords, stride);";
            if (!library.Contains(dimensions, StringComparison.Ordinal))
                throw new InvalidOperationException("Missing snapshot dimension query.");
            string length = unityCompute ? GaUnityComputeAbi.InputWords : "gaWgslInputLength(words)";
            return library.Replace(dimensions, $"uint bufferWords = {length};", StringComparison.Ordinal);
        }

        private void SnapshotLayout(int stride)
        {
            if (program.BufferSchema is not null)
            {
                Line($"uint gaSnapshotStart = gaBufferData + gaBufferCount * {Word(program.BufferRowWords)};");
            }
            else if (program.TickResourceWords != 0)
            {
                Line($"uint gaSnapshotStart = gaTickResourceStart + {Word(program.TickResourceWords)};");
            }
            else
            {
                Line($"uint gaSnapshotStart = 1u + gaInput[0] * {Word(stride)};");
                ReadInputLength();
            }
            Line("uint gaSnapshotWords = gaInputWords - min(gaSnapshotStart, gaInputWords);");
            Line("GaSnapshotViewV1 gaSnapshotView;");
            int valueWords = program.SnapshotLayout?.ValueWordCount ?? 1;
            Line($"bool gaSnapshotValid = gaTryOpenSnapshotV1(gaInput, gaSnapshotStart, gaSnapshotWords, {Word(valueWords)}, gaSnapshotView);");
        }

        private void TickResourceLayout(int stride)
        {
            GuardRowDimensions(stride);
            Line($"uint gaTickResourceStart = 1u + gaInput[0] * {Word(stride)};");
            string comparison = program.UsesSnapshot || program.BufferSchema is not null ? "<" : "!=";
            Line($"if (gaInputWords - gaTickResourceStart {comparison} {Word(program.TickResourceWords)}) {LayoutExit}");
            for (int i = 0; i < program.TickResourceFields.Count; i++)
            {
                int offset = program.TickResourceOffsets[i];
                var type = program.TickResourceFields[i].Type;
                string firstWord = $"gaInput[gaTickResourceStart + {Word(offset)}]";
                string value = type == GaScalar.Fixed
                    ? $"uint2({firstWord}, gaInput[gaTickResourceStart + {Word(offset + 1)}])"
                    : firstWord;
                Line($"{Type(type)} {TickField(i)} = {value};");
            }
        }

        private void ReadInputLength()
        {
            if (unityCompute)
                return;
            if (webGpu)
                Line("uint gaInputWords = gaWgslInputLength(gaInput);");
            else
            {
                Line("uint gaInputWords, gaInputStride;");
                Line("gaInput.GetDimensions(gaInputWords, gaInputStride);");
            }
        }

        private void GuardRowDimensions(int stride)
        {
            ReadInputLength();
            Line($"if (gaInputWords < 1u || gaInput[0] > (gaInputWords - 1u) / {Word(stride)}) {LayoutExit}");
        }

        private void BufferLayout(int stride)
        {
            if (program.TickResourceWords == 0)
                GuardRowDimensions(stride);
            string bufferStart = program.TickResourceWords == 0
                ? $"1u + gaInput[0] * {Word(stride)}"
                : $"gaTickResourceStart + {Word(program.TickResourceWords)}";
            Line($"uint gaBufferStart = {bufferStart};");
            Line($"if (gaBufferStart >= gaInputWords) {LayoutExit}");
            Line("uint gaBufferCount = gaInput[gaBufferStart];");
            Line($"if (gaBufferCount > 0x7fffffffu) {LayoutExit}");
            Line("uint gaBufferData = gaBufferStart + 1u;");
            string width = Word(program.BufferRowWords);
            if (program.BufferRowWords != 0)
                Line($"if (gaBufferCount > (gaInputWords - gaBufferData) / {width}) {LayoutExit}");
            if (!program.UsesSnapshot)
                Line($"if (gaBufferData + gaBufferCount * {width} != gaInputWords) {LayoutExit}");
        }

        private void EventLayout(int stride)
        {
            Line($"uint gaEvents = gaInput[0] * {Word(stride)};");
            Line("if (id.x == 0u)");
            Open();
            Line($"gaOutput[gaEvents] = {Word(GaEventPacket.Version)};");
            Line("gaOutput[gaEvents + 1u] = gaInput[0];");
            Line($"gaOutput[gaEvents + 2u] = {Word(_eventCapacity)};");
            Line($"gaOutput[gaEvents + 3u] = {Word(_recordWords)};");
            Close();
            Line($"uint gaEventRow = gaEvents + {Word(GaEventPacket.HeaderWords)} + id.x * {Word(checked(1 + _eventCapacity * _recordWords))};");
            Line("uint gaEventCount = 0u;");
            // Fault returns leave the row's event count zero.
            Line("gaOutput[gaEventRow] = 0u;");
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
                case GaArrayStore store:
                {
                    string index = CheckedArrayIndex(store.Field, store.Index, store.Source);
                    if (store.Previous is { } previous)
                        ArraySelect(store.Field, index, element => $"{Slot(previous)} = {element};");
                    string value = Temp(Value(store.Value), Type(store.Field.Type));
                    ArraySelect(store.Field, index, element => $"{element} = {value};");
                    break;
                }
                case GaEmit emission:
                    if (_eventCapacity != 0)
                    {
                        string value = Value(emission.Value);
                        if (_mixedOutputs)
                            WriteOutput(GaOutputRecord.EmitInt32Kind, value, "0u");
                        else
                        {
                            Line($"gaOutput[gaEventRow + 1u + gaEventCount] = {value};");
                            Line("++gaEventCount;");
                        }
                    }
                    break;
                case GaEmitTyped emission:
                    if (_eventCapacity != 0)
                    {
                        var outputType = program.EventTypes[emission.EventTypeIndex];
                        uint kind = outputType.IsContribution ? GaOutputRecord.ContributeBase : GaOutputRecord.EmitTypedBase;
                        WritePayload(kind | (uint)emission.EventTypeIndex, "0u", "0u", outputType.Offsets, emission.Values);
                    }
                    break;
                case GaDestroy destroy:
                    if (_eventCapacity != 0)
                    {
                        string entityId = Temp(Value(destroy.EntityId));
                        string generation = Temp(Value(destroy.Generation));
                        WriteOutput(GaOutputRecord.DestroyEntityKind, entityId, generation);
                    }
                    break;
                case GaCreate create:
                    if (_eventCapacity != 0)
                    {
                        if (create.TicketSlot >= 0)
                            Line($"{Ticket(create.TicketSlot)} = gaEventCount;");
                        WriteOutput(GaOutputRecord.CreateEntityKind, "0u", "0u");
                    }
                    break;
                case GaDestroyTicket destroy:
                    if (_eventCapacity != 0)
                        WriteOutput(GaOutputRecord.DestroyTicketKind, Ticket(destroy.TicketSlot), "0u");
                    break;
                case GaRemove remove:
                    if (_eventCapacity != 0)
                    {
                        string entityId = Temp(Value(remove.EntityId));
                        string generation = Temp(Value(remove.Generation));
                        WriteOutput(GaOutputRecord.RemoveEntityBase | (uint)remove.ComponentIndex, entityId, generation);
                    }
                    break;
                case GaRemoveTicket remove:
                    if (_eventCapacity != 0)
                        WriteOutput(GaOutputRecord.RemoveTicketBase | (uint)remove.ComponentIndex, Ticket(remove.TicketSlot), "0u");
                    break;
                case GaAdd add:
                    if (_eventCapacity != 0)
                    {
                        string entityId = Temp(Value(add.EntityId));
                        string generation = Temp(Value(add.Generation));
                        WritePayload(GaOutputRecord.AddEntityBase | (uint)add.ComponentIndex, entityId, generation,
                            program.AdditionComponents[add.ComponentIndex].Offsets, add.Values);
                    }
                    break;
                case GaAddTicket add:
                    if (_eventCapacity != 0)
                        WritePayload(GaOutputRecord.AddTicketBase | (uint)add.ComponentIndex, Ticket(add.TicketSlot), "0u",
                            program.AdditionComponents[add.ComponentIndex].Offsets, add.Values);
                    break;
                case GaSetDomainOrder order:
                    Line($"gaDomainPrimary = {Value(order.Primary)};");
                    Line($"gaDomainSecondary = {Value(order.Secondary)};");
                    Line("gaHasDomainOrder = 1u;");
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

        private void WriteOutput(uint kind, string first, string second)
        {
            string offset = WriteOutputHeader(kind, first, second);
            ZeroOutputPayload(offset, 0, program.OutputPayloadWords);
            Line("++gaEventCount;");
        }

        private string WriteOutputHeader(uint kind, string first, string second)
        {
            string offset = Temp($"gaEventRow + 1u + gaEventCount * {Word(_recordWords)}");
            Line($"gaOutput[{offset}] = {Word(kind)};");
            Line($"gaOutput[{offset} + 1u] = {first};");
            Line($"gaOutput[{offset} + 2u] = {second};");
            if (program.UsesDomainOrder)
            {
                int domain = _recordWords - GaOutputRecord.DomainWordCount;
                Line($"gaOutput[{offset} + {Word(domain)}] = gaHasDomainOrder;");
                Line($"gaOutput[{offset} + {Word(domain + 1)}] = gaDomainPrimary;");
                Line($"gaOutput[{offset} + {Word(domain + 2)}] = gaDomainSecondary;");
            }
            return offset;
        }

        private void WritePayload(uint kind, string first, string second, int[] offsets,
            IReadOnlyList<GaAdditionValue> values)
        {
            string offset = WriteOutputHeader(kind, first, second);
            // ANGLE D3D11 on Radeon can drop a payload store that overlaps an earlier clearing loop.
            var populated = new bool[program.OutputPayloadWords];
            foreach (var value in values)
            {
                int payloadWord = offsets[value.FieldIndex];
                int word = GaOutputRecord.Words + payloadWord;
                populated[payloadWord] = true;
                string bits = Value(value.Value);
                if (value.Value.Type == GaScalar.Fixed)
                {
                    populated[payloadWord + 1] = true;
                    Line($"gaOutput[{offset} + {Word(word)}] = {bits}.x;");
                    Line($"gaOutput[{offset} + {Word(word + 1)}] = {bits}.y;");
                }
                else
                    Line($"gaOutput[{offset} + {Word(word)}] = {bits};");
            }
            for (int start = 0; start < populated.Length;)
            {
                if (populated[start])
                {
                    start++;
                    continue;
                }
                int end = start + 1;
                while (end < populated.Length && !populated[end])
                    end++;
                ZeroOutputPayload(offset, start, end);
                start = end;
            }
            Line("++gaEventCount;");
        }

        private void ZeroOutputPayload(string offset, int start, int end)
        {
            if (start == end)
                return;
            string index = Name();
            Line($"for (uint {index} = {Word(GaOutputRecord.Words + start)}; {index} < {Word(GaOutputRecord.Words + end)}; ++{index})");
            Line($"    gaOutput[{offset} + {index}] = 0u;");
        }

        private static string Ticket(int index) => "gaTicket" + index.ToString(CultureInfo.InvariantCulture);

        private string Value(GaValue value)
        {
            switch (value)
            {
                case GaConstant constant:
                    return Literal(constant.Type, constant.Bits);
                case GaLoad load:
                    return Slot(load.Slot);
                case GaBoundValue bound:
                    return StoreAndRead(bound.Slot, bound.Value, bound.Body);
                case GaStoredValue stored:
                    return StoreAndRead(stored.Slot, stored.Value, stored.Body);
                case GaBlockValue block:
                    Statement(block.Body);
                    return Temp(Value(block.Result), Type(block.Type));
                case GaArrayRead array:
                    string index = CheckedArrayIndex(array.Field, array.Index, array.Source);
                    string element = Temp(Literal(array.Type, 0), Type(array.Type));
                    ArraySelect(array.Field, index, field => $"{element} = {field};");
                    return element;
                case GaSnapshotRead read:
                    return SnapshotRead(read);
                case GaBufferCount:
                    return "gaBufferCount";
                case GaBufferRead read:
                    return BufferRead(read);
                case GaResourceRead read:
                    return TickField(read.FieldIndex);
                case GaHasOwn has:
                    return Presence(has.ComponentIndex);
                case GaTryRead read:
                    string present = Presence(read.ComponentIndex);
                    foreach (var copy in read.Copies)
                        Line($"{Slot(copy.Local)} = {present} != 0u ? {Slot(copy.Field)} : {Literal(copy.Local.Type, 0)};");
                    return present;
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

        private string StoreAndRead(GaSlot slot, GaValue value, GaValue body)
        {
            Line($"{Slot(slot)} = {Value(value)};");
            return Temp(Value(body), Type(body.Type));
        }

        private string SnapshotRead(GaSnapshotRead read)
        {
            string id = Temp(Value(read.EntityId));
            string generation = Temp(Value(read.Generation));
            string result = Temp(Value(read.Fallback), Type(read.Type));
            var key = SnapshotKey(read);
            string? cachedOffset = key is { } reusable
                ? _snapshotOffsets.FirstOrDefault(entry => entry.Key == reusable).Offset : null;
            string offset = cachedOffset ?? Name();
            if (cachedOffset is not null)
                Line($"if ({offset} != 0u)");
            else
            {
                if (key is not null)
                    Line($"uint {offset} = 0u;");
                Line("if (gaSnapshotValid)");
                Open();
                if (key is null)
                    Line($"uint {offset};");
                Line($"if (gaTryFindSnapshotV1(gaInput, gaSnapshotView, {id}, {generation}, {offset}))");
            }
            int word = read.FieldIndex < 0 ? 0 : program.SnapshotLayout!.GetWordOffset(read.FieldIndex);
            string firstWord = word == 0 ? $"gaInput[{offset}]" : $"gaInput[{offset} + {Word(word)}]";
            string value = read.Type == GaScalar.Fixed
                ? $"uint2({firstWord}, gaInput[{offset} + {Word(word + 1)}])"
                : firstWord;
            Line($"    {result} = {value};");
            if (cachedOffset is null)
            {
                Close();
                if (key is { } available)
                    _snapshotOffsets.Add(new SnapshotOffset(available, offset, _indent));
            }
            return result;
        }

        // The bound snapshot is immutable. Reuse an earlier offset only while its
        // shader variable is in scope and its key fields cannot change in this invocation.
        private (SnapshotOperand Id, SnapshotOperand Generation)? SnapshotKey(GaSnapshotRead read)
        {
            var id = SnapshotOperandFor(read.EntityId);
            var generation = SnapshotOperandFor(read.Generation);
            return id is { } first && generation is { } second ? (first, second) : null;
        }

        private SnapshotOperand? SnapshotOperandFor(GaValue value) => value switch
        {
            GaConstant constant => new SnapshotOperand(null, constant.Bits),
            GaLoad { Slot.IsField: true } load when !_snapshotKeyWrites.Contains(load.Slot.Index) =>
                new SnapshotOperand(load.Slot.Index, 0),
            _ => null
        };

        private readonly record struct SnapshotOperand(int? Field, ulong Constant);
        private readonly record struct SnapshotOffset((SnapshotOperand, SnapshotOperand) Key, string Offset, int Depth);

        private string CheckedArrayIndex(GaArrayField field, GaValue value, GaSourceSpan source)
            => CheckedIndex(value, Word(field.Length), source);

        private string CheckedIndex(GaValue value, string length, GaSourceSpan source)
        {
            string index = Temp(Value(value));
            FaultWhen($"{index} >= {length}", GaFaultKind.IndexOutOfRange, NextOrdinal(), source);
            return index;
        }

        private string BufferRead(GaBufferRead read)
        {
            string index = CheckedIndex(read.Index, "gaBufferCount", read.Source);
            int field = program.BufferOffsets[read.FieldIndex];
            string offset = Temp($"gaBufferData + {index} * {Word(program.BufferRowWords)} + {Word(field)}");
            string word = $"gaInput[{offset}]";
            string value = read.Type switch
            {
                GaScalar.Fixed => $"uint2({word}, gaInput[{offset} + 1u])",
                GaScalar.Bool => $"({word} != 0u ? 1u : 0u)",
                _ => word
            };
            return Temp(value, Type(read.Type));
        }

        private void ArraySelect(GaArrayField field, string index, Func<string, string> statement)
        {
            // Keep per-row state in named values across dynamic reads and writes.
            Line($"switch ({index})");
            Open();
            for (int element = 0; element < field.Length; element++)
                Line($"case {Word(element)}: {statement(Field(field.FirstField + element))} break;");
            Close();
        }

        private static bool MayStoreLocal(GaValue value) => value switch
        {
            GaStoredValue => true,
            GaBlockValue => true,
            GaBoundValue bound => MayStoreLocal(bound.Value) || MayStoreLocal(bound.Body),
            GaCast cast => MayStoreLocal(cast.Operand),
            GaUnary unary => MayStoreLocal(unary.Operand),
            GaBinary binary => MayStoreLocal(binary.Left) || MayStoreLocal(binary.Right),
            GaSelect select => MayStoreLocal(select.Condition) || MayStoreLocal(select.True) || MayStoreLocal(select.False),
            GaArrayRead array => MayStoreLocal(array.Index),
            GaBufferRead buffer => MayStoreLocal(buffer.Index),
            GaSnapshotRead read => MayStoreLocal(read.EntityId) || MayStoreLocal(read.Generation) || MayStoreLocal(read.Fallback),
            _ => false
        };

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

            if (_usesOptional || MayStoreLocal(binary.Right))
                left = Temp(left, Type(binary.Left.Type));
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
            if (_deferredFaultWrites)
                Line($"return uint4({Word((uint)kind)}, {ordinal}.x, {ordinal}.y, {Word(site)});");
            else
            {
                Line($"gaFaults[gaFault] = {Word((uint)kind)};");
                Line($"gaFaults[gaFault + 1u] = {ordinal}.x;");
                Line($"gaFaults[gaFault + 2u] = {ordinal}.y;");
                Line($"gaFaults[gaFault + 3u] = {Word(site)};");
                Line("return;");
            }
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
        private static string TickField(int index) => "tickField" + index.ToString(CultureInfo.InvariantCulture);
        private static string Local(int index) => "l" + index.ToString(CultureInfo.InvariantCulture);
        private static string Presence(int index) => "present" + index.ToString(CultureInfo.InvariantCulture);
        private static string Slot(GaSlot slot) => slot.IsField ? Field(slot.Index) : Local(slot.Index);
        private void Line(string text) => _code.Append(' ', _indent * 4).Append(text).Append('\n');
        private void Open() { Line("{"); _indent++; }
        private void Close()
        {
            _indent--;
            while (_snapshotOffsets.Count != 0 && _snapshotOffsets[^1].Depth > _indent)
                _snapshotOffsets.RemoveAt(_snapshotOffsets.Count - 1);
            Line("}");
        }
    }
}
