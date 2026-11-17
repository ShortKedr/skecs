namespace SKECS.GA;

internal sealed class GaEventCapacityException(string message) : InvalidOperationException(message);

internal static class GaVerifier
{
    internal static (bool UsesFixed, bool UsesSnapshot, bool UsesCommands, bool UsesDomainOrder,
        int EventCapacity) Verify(GaProgram program)
    {
        bool usesFixed = program.Fields.Any(field => field.Type == GaScalar.Fixed) || program.Locals.Contains(GaScalar.Fixed);
        var snapshotLayout = program.SnapshotLayout;
        bool usesSnapshot = snapshotLayout is not null;
        Require(snapshotLayout is null || snapshotLayout.Fields.Length != 0);
        bool usesCommands = false;
        bool usesDomainOrder = false;
        Require(program.TickResourceType is null
            ? program.TickResourceFields.Count == 0
            : !string.IsNullOrWhiteSpace(program.TickResourceType));
        var resourcePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in program.TickResourceFields)
        {
            Require(field.Path is not null && (field.Path.Length == 0
                    ? program.TickResourceFields.Count == 1
                    : !string.IsNullOrWhiteSpace(field.Path) && !field.Path.Contains('[')) &&
                resourcePaths.Add(field.Path) && Enum.IsDefined(field.Type) &&
                (field.ClrEnumType is null || (field.Type is GaScalar.Int32 or GaScalar.UInt32 &&
                    !string.IsNullOrWhiteSpace(field.ClrEnumType))));
        }
        var declaredTickets = new HashSet<int>();
        Require(program.BufferType is null ? program.BufferFields.Count == 0 : !string.IsNullOrWhiteSpace(program.BufferType));
        var bufferPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in program.BufferFields)
        {
            Require(field.Path is not null && (field.Path.Length == 0 ? program.BufferFields.Count == 1 :
                    !string.IsNullOrWhiteSpace(field.Path) && !field.Path.Contains('[')) &&
                bufferPaths.Add(field.Path) && Enum.IsDefined(field.Type) &&
                (field.ClrEnumType is null || (field.Type is GaScalar.Int32 or GaScalar.UInt32 &&
                    !string.IsNullOrWhiteSpace(field.ClrEnumType))));
        }
        var arrayFields = new Dictionary<int, GaArrayField>();
        var arraySlots = new HashSet<int>();
        var components = new Dictionary<string, GaComponent>();
        foreach (var component in program.Components)
            Require(!string.IsNullOrWhiteSpace(component.Id) && Enum.IsDefined(component.Access) &&
                Enum.IsDefined(component.Origin) &&
                (component.Origin != GaComponentOrigin.Optional || component.Access == GaFieldAccess.Read) &&
                (component.Origin != GaComponentOrigin.Own ||
                    component.OwnSource.File is not null && component.OwnSource.Start >= 0 && component.OwnSource.Length > 0) &&
                components.TryAdd(component.Id, component));
        var fields = new HashSet<(string Component, string Path)>();
        foreach (var field in program.Fields)
            Require(components.TryGetValue(field.Component, out var component) && Enum.IsDefined(field.Access)
                && (component.Access == field.Access || field.Access == GaFieldAccess.Read)
                && !string.IsNullOrWhiteSpace(field.Path) && fields.Add((field.Component, field.Path))
                && Enum.IsDefined(field.Type));
        var removalComponents = new HashSet<string>(StringComparer.Ordinal);
        Require(program.RemovalComponents.Count <= GaOutputRecord.MaximumComponentRemovals);
        foreach (var component in program.RemovalComponents)
            Require(!string.IsNullOrWhiteSpace(component) && removalComponents.Add(component));
        var additionComponents = new HashSet<string>(StringComparer.Ordinal);
        Require(program.AdditionComponents.Count <= GaOutputRecord.MaximumComponentRemovals &&
            program.AdditionPayloadWords <= GaOutputRecord.MaximumAdditionPayloadWords);
        foreach (var component in program.AdditionComponents)
        {
            Require(!string.IsNullOrWhiteSpace(component.Id) && additionComponents.Add(component.Id));
            CheckPayloadFields(component.Fields, allowScalar: false);
        }
        var eventTypes = new HashSet<(string Id, bool IsContribution)>();
        Require(program.EventTypes.Count <= GaOutputRecord.MaximumEventTypes &&
            program.EventPayloadWords <= GaOutputRecord.MaximumEventPayloadWords);
        foreach (var type in program.EventTypes)
        {
            Require(!string.IsNullOrWhiteSpace(type.Id) && eventTypes.Add((type.Id, type.IsContribution)));
            CheckPayloadFields(type.Fields, allowScalar: true);
        }
        Require(program.Locals.All(Enum.IsDefined) && program.TicketLocalCount >= 0);
        int eventCapacity = CheckStatement(program.Body, new HashSet<int>(), new HashSet<int>());
        Require(declaredTickets.Count == program.TicketLocalCount &&
            arraySlots.Count == program.Fields.Count(field => field.Path.Contains('[')));
        if (eventCapacity > GaEventPacket.MaximumCapacityPerSource)
            throw new GaEventCapacityException($"An invocation may produce more than {GaEventPacket.MaximumCapacityPerSource.ToString(System.Globalization.CultureInfo.InvariantCulture)} events and commands.");
        int outputWords = GaOutputRecord.Words + program.OutputPayloadWords +
            (usesDomainOrder ? GaOutputRecord.DomainWordCount : 0);
        if ((long)eventCapacity * outputWords > Array.MaxLength)
            throw new GaEventCapacityException("The packed output of one invocation exceeds the supported buffer size.");
        return (usesFixed, usesSnapshot, usesCommands, usesDomainOrder, eventCapacity);

        void CheckSlot(GaSlot slot)
        {
            if (slot.IsField)
                Require((uint)slot.Index < program.Fields.Count && program.Fields[slot.Index].Type == slot.Type &&
                    !program.Fields[slot.Index].Path.Contains('['));
            else
                Require((uint)slot.Index < program.Locals.Count && program.Locals[slot.Index] == slot.Type);
        }

        void CheckArrayField(GaArrayField range, GaValue index, HashSet<int> assigned, bool writable)
        {
            Require(range.FirstField >= 0 && range.Length > 0 &&
                (long)range.FirstField + range.Length <= program.Fields.Count);
            var first = program.Fields[range.FirstField];
            Require(components[first.Component].Origin != GaComponentOrigin.Optional);
            Require(!writable || first.Access == GaFieldAccess.ReadWrite);
            if (arrayFields.TryGetValue(range.FirstField, out var verified))
            {
                Require(verified == range);
            }
            else
            {
                int token = first.Path.IndexOf("[0]", StringComparison.Ordinal);
                Require(token >= 0 && first.Path.IndexOf('[') == token && first.Path.IndexOf('[', token + 1) == -1 &&
                    first.Type == range.Type);
                var prefix = first.Path[..(token + 1)];
                var suffix = first.Path[(token + 2)..];
                for (int item = 0; item < range.Length; item++)
                {
                    int slot = range.FirstField + item;
                    var field = program.Fields[slot];
                    string expected = prefix + item.ToString(System.Globalization.CultureInfo.InvariantCulture) + suffix;
                    Require(field.Component == first.Component && field.Type == first.Type &&
                        field.Access == first.Access && field.ClrEnumType == first.ClrEnumType &&
                        field.Path == expected && arraySlots.Add(slot));
                }
                arrayFields.Add(range.FirstField, range);
            }
            CheckValue(index, assigned);
            Require(index.Type == GaScalar.Int32);
        }

        int CheckStatement(GaStatement statement, HashSet<int> assigned, HashSet<int> assignedTickets, bool localOnly = false)
        {
            if (localOnly)
                Require(statement is GaBlock or GaIf or GaLoop or GaStore { Slot.IsField: false });
            switch (statement)
            {
                case GaBlock block:
                    int blockCapacity = 0;
                    foreach (var child in block.Statements)
                        blockCapacity = Bound((long)blockCapacity + CheckStatement(child, assigned, assignedTickets, localOnly));
                    return blockCapacity;
                case GaStore store:
                    CheckSlot(store.Slot);
                    CheckValue(store.Value, assigned);
                    Require(store.Slot.Type == store.Value.Type);
                    if (store.Slot.IsField)
                        Require(program.Fields[store.Slot.Index].Access == GaFieldAccess.ReadWrite);
                    else
                        assigned.Add(store.Slot.Index);
                    return 0;
                case GaArrayStore indexed:
                    CheckArrayField(indexed.Field, indexed.Index, assigned, writable: true);
                    if (indexed.Previous is { } previous)
                    {
                        CheckSlot(previous);
                        Require(!previous.IsField && previous.Type == indexed.Field.Type && assigned.Add(previous.Index));
                    }
                    CheckValue(indexed.Value, assigned);
                    Require(indexed.Value.Type == indexed.Field.Type);
                    if (indexed.Previous is { } saved)
                        assigned.Remove(saved.Index);
                    return 0;
                case GaEmit emit:
                    CheckValue(emit.Value, assigned);
                    Require(emit.Value.Type == GaScalar.Int32);
                    return 1;
                case GaEmitTyped typed:
                    Require((uint)typed.EventTypeIndex < program.EventTypes.Count);
                    CheckPayload(program.EventTypes[typed.EventTypeIndex].Fields, typed.Values, assigned);
                    return 1;
                case GaSetDomainOrder order:
                    usesDomainOrder = true;
                    CheckValue(order.Primary, assigned);
                    CheckValue(order.Secondary, assigned);
                    Require(order.Primary.Type == GaScalar.Int32 && order.Secondary.Type == GaScalar.Int32);
                    return 0;
                case GaDestroy destroy:
                    usesCommands = true;
                    CheckValue(destroy.EntityId, assigned);
                    CheckValue(destroy.Generation, assigned);
                    Require(destroy.EntityId.Type == GaScalar.Int32 && destroy.Generation.Type == GaScalar.Int32);
                    return 1;
                case GaCreate create:
                    usesCommands = true;
                    if (create.TicketSlot != -1)
                    {
                        Require((uint)create.TicketSlot < (uint)program.TicketLocalCount &&
                            assignedTickets.Add(create.TicketSlot));
                        declaredTickets.Add(create.TicketSlot);
                    }
                    return 1;
                case GaDestroyTicket destroy:
                    usesCommands = true;
                    Require(assignedTickets.Contains(destroy.TicketSlot));
                    return 1;
                case GaRemove remove:
                    usesCommands = true;
                    Require((uint)remove.ComponentIndex < (uint)program.RemovalComponents.Count);
                    CheckValue(remove.EntityId, assigned);
                    CheckValue(remove.Generation, assigned);
                    Require(remove.EntityId.Type == GaScalar.Int32 && remove.Generation.Type == GaScalar.Int32);
                    return 1;
                case GaRemoveTicket remove:
                    usesCommands = true;
                    Require((uint)remove.ComponentIndex < (uint)program.RemovalComponents.Count &&
                        assignedTickets.Contains(remove.TicketSlot));
                    return 1;
                case GaAdd add:
                    usesCommands = true;
                    CheckValue(add.EntityId, assigned);
                    CheckValue(add.Generation, assigned);
                    Require(add.EntityId.Type == GaScalar.Int32 && add.Generation.Type == GaScalar.Int32);
                    CheckAddition(add.ComponentIndex, add.Values, assigned);
                    return 1;
                case GaAddTicket add:
                    usesCommands = true;
                    Require(assignedTickets.Contains(add.TicketSlot));
                    CheckAddition(add.ComponentIndex, add.Values, assigned);
                    return 1;
                case GaIf branch:
                    CheckValue(branch.Condition, assigned);
                    Require(branch.Condition.Type == GaScalar.Bool);
                    var thenAssigned = new HashSet<int>(assigned);
                    var elseAssigned = new HashSet<int>(assigned);
                    var thenTickets = new HashSet<int>(assignedTickets);
                    var elseTickets = new HashSet<int>(assignedTickets);
                    int thenCapacity = CheckStatement(branch.Then, thenAssigned, thenTickets, localOnly);
                    int elseCapacity = branch.Else == null ? 0 : CheckStatement(branch.Else, elseAssigned, elseTickets, localOnly);
                    thenAssigned.IntersectWith(elseAssigned);
                    assigned.UnionWith(thenAssigned);
                    thenTickets.IntersectWith(elseTickets);
                    assignedTickets.UnionWith(thenTickets);
                    return Math.Max(thenCapacity, elseCapacity);
                case GaLoop loop:
                    CheckValue(loop.Condition, assigned);
                    Require(loop.Condition.Type == GaScalar.Bool && loop.MaximumIterations >= 0);
                    var loopAssigned = new HashSet<int>(assigned);
                    var loopTickets = new HashSet<int>(assignedTickets);
                    int loopCapacity = CheckStatement(loop.Body, loopAssigned, loopTickets, localOnly) +
                        CheckStatement(loop.Increment, loopAssigned, loopTickets, localOnly);
                    return Bound((long)loopCapacity * loop.MaximumIterations);
                default:
                    throw new InvalidDataException("Unknown GA IR statement.");
            }
        }

        static int Bound(long capacity) => (int)Math.Min(capacity, GaEventPacket.MaximumCapacityPerSource + 1L);

        void CheckAddition(int componentIndex, IReadOnlyList<GaAdditionValue> values, HashSet<int> assigned)
        {
            Require((uint)componentIndex < (uint)program.AdditionComponents.Count);
            CheckPayload(program.AdditionComponents[componentIndex].Fields, values, assigned);
        }

        static void CheckPayloadFields(IReadOnlyList<GaAdditionField> fields, bool allowScalar)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in fields)
                Require((!string.IsNullOrWhiteSpace(field.Path) || allowScalar && field.Path == "" && fields.Count == 1) &&
                    paths.Add(field.Path) && Enum.IsDefined(field.Type) && (field.ClrEnumType is null ||
                        field.Type is GaScalar.Int32 or GaScalar.UInt32 && !string.IsNullOrWhiteSpace(field.ClrEnumType)));
        }

        void CheckPayload(IReadOnlyList<GaAdditionField> fields, IReadOnlyList<GaAdditionValue> values, HashSet<int> assigned)
        {
            var supplied = new HashSet<int>();
            foreach (var value in values)
            {
                Require((uint)value.FieldIndex < (uint)fields.Count && supplied.Add(value.FieldIndex));
                CheckValue(value.Value, assigned);
                Require(value.Value.Type == fields[value.FieldIndex].Type);
            }
        }

        void CheckValue(GaValue value, HashSet<int> assigned)
        {
            Require(Enum.IsDefined(value.Type));
            usesFixed |= value.Type == GaScalar.Fixed;
            switch (value)
            {
                case GaConstant constant:
                    Require(constant.Type switch
                    {
                        GaScalar.Bool => constant.Bits <= 1,
                        GaScalar.Fixed => true,
                        _ => constant.Bits <= uint.MaxValue
                    });
                    break;
                case GaLoad load:
                    CheckSlot(load.Slot);
                    Require(load.Slot.IsField
                        ? components[program.Fields[load.Slot.Index].Component].Origin != GaComponentOrigin.Optional
                        : assigned.Contains(load.Slot.Index));
                    break;
                case GaBoundValue bound:
                    CheckSlot(bound.Slot);
                    Require(!bound.Slot.IsField && bound.Slot.Type == bound.Value.Type &&
                        !assigned.Contains(bound.Slot.Index));
                    CheckValue(bound.Value, assigned);
                    Require(assigned.Add(bound.Slot.Index));
                    CheckValue(bound.Body, assigned);
                    assigned.Remove(bound.Slot.Index);
                    break;
                case GaStoredValue stored:
                    CheckSlot(stored.Slot);
                    Require(!stored.Slot.IsField && stored.Slot.Type == stored.Value.Type &&
                        assigned.Contains(stored.Slot.Index));
                    CheckValue(stored.Value, assigned);
                    CheckValue(stored.Body, assigned);
                    break;
                case GaBlockValue block:
                    var blockAssigned = new HashSet<int>(assigned);
                    CheckStatement(block.Body, blockAssigned, new HashSet<int>(), localOnly: true);
                    CheckValue(block.Result, blockAssigned);
                    break;
                case GaHasOwn present:
                    CheckOptional(present.ComponentIndex);
                    break;
                case GaResourceRead resource:
                    Require((uint)resource.FieldIndex < program.TickResourceFields.Count &&
                        program.TickResourceFields[resource.FieldIndex].Type == resource.Type);
                    break;
                case GaBufferCount count:
                    Require(program.BufferType is not null && count.Type == GaScalar.Int32);
                    break;
                case GaBufferRead buffer:
                    CheckValue(buffer.Index, assigned);
                    Require(program.BufferType is not null && (uint)buffer.FieldIndex < program.BufferFields.Count &&
                        buffer.Index.Type == GaScalar.Int32 && program.BufferFields[buffer.FieldIndex].Type == buffer.Type);
                    break;
                case GaTryRead copy:
                    var optional = CheckOptional(copy.ComponentIndex);
                    var destinations = new HashSet<int>();
                    foreach (var binding in copy.Copies)
                    {
                        CheckSlot(binding.Local);
                        CheckSlot(binding.Field);
                        Require(!binding.Local.IsField && binding.Field.IsField && binding.Local.Type == binding.Field.Type &&
                            assigned.Contains(binding.Local.Index) && destinations.Add(binding.Local.Index) &&
                            program.Fields[binding.Field.Index].Component == optional.Id);
                    }
                    break;
                case GaArrayRead indexed:
                    CheckArrayField(indexed.Field, indexed.Index, assigned, writable: false);
                    Require(indexed.Type == indexed.Field.Type);
                    break;
                case GaCast cast:
                    CheckValue(cast.Operand, assigned);
                    Require((cast.Type, cast.Operand.Type) is
                        (GaScalar.Int32, GaScalar.UInt32 or GaScalar.Fixed) or
                        (GaScalar.UInt32 or GaScalar.Fixed, GaScalar.Int32));
                    break;
                case GaUnary unary:
                    CheckValue(unary.Operand, assigned);
                    Require(unary.Type == unary.Operand.Type && (unary.Operator switch
                    {
                        GaUnaryOperator.Negate => unary.Type is GaScalar.Int32 or GaScalar.Fixed,
                        GaUnaryOperator.BitwiseNot => unary.Type is GaScalar.Int32 or GaScalar.UInt32 && !unary.Checked,
                        GaUnaryOperator.Not => unary.Type == GaScalar.Bool && !unary.Checked,
                        _ => false
                    }));
                    break;
                case GaBinary binary:
                    CheckValue(binary.Left, assigned);
                    CheckValue(binary.Right, assigned);
                    CheckBinary(binary);
                    break;
                case GaSelect selected:
                    CheckValue(selected.Condition, assigned);
                    CheckValue(selected.True, assigned);
                    CheckValue(selected.False, assigned);
                    Require(selected.Condition.Type == GaScalar.Bool &&
                        selected.True.Type == selected.Type && selected.False.Type == selected.Type);
                    break;
                case GaSnapshotRead lookup:
                    usesSnapshot = true;
                    CheckValue(lookup.EntityId, assigned);
                    CheckValue(lookup.Generation, assigned);
                    CheckValue(lookup.Fallback, assigned);
                    Require(lookup.EntityId.Type == GaScalar.Int32 && lookup.Generation.Type == GaScalar.Int32 &&
                        lookup.Fallback.Type == lookup.Type);
                    if (lookup.FieldIndex == -1)
                    {
                        Require(lookup.Type == GaScalar.Int32 &&
                            (snapshotLayout is null || snapshotLayout.Fields is [GaScalar.Int32]));
                    }
                    else
                    {
                        Require(snapshotLayout is not null && (uint)lookup.FieldIndex < (uint)snapshotLayout.Fields.Length &&
                            snapshotLayout.Fields[lookup.FieldIndex] == lookup.Type);
                    }
                    break;
                default:
                    throw new InvalidDataException("Unknown GA IR value.");
            }
        }

        GaComponent CheckOptional(int componentIndex)
        {
            Require((uint)componentIndex < program.Components.Count);
            var optional = program.Components[componentIndex];
            Require(optional.Origin == GaComponentOrigin.Optional);
            return optional;
        }
    }

    private static void CheckBinary(GaBinary op)
    {
        GaScalar operand = op.Left.Type;
        bool integer = operand is GaScalar.Int32 or GaScalar.UInt32;
        bool numeric = integer || operand == GaScalar.Fixed;
        bool homogeneous = op.Right.Type == operand;
        bool sameResult = op.Type == operand;
        bool boolResult = op.Type == GaScalar.Bool;
        bool valid = op.Operator switch
        {
            GaBinaryOperator.Add or GaBinaryOperator.Subtract or GaBinaryOperator.Multiply =>
                numeric && homogeneous && sameResult,
            GaBinaryOperator.Divide =>
                numeric && homogeneous && sameResult && !op.Checked,
            GaBinaryOperator.Remainder =>
                integer && homogeneous && sameResult && !op.Checked,
            GaBinaryOperator.BitwiseAnd or GaBinaryOperator.BitwiseOr or GaBinaryOperator.ExclusiveOr =>
                operand != GaScalar.Fixed && homogeneous && sameResult && !op.Checked,
            GaBinaryOperator.LeftShift or GaBinaryOperator.RightShift or GaBinaryOperator.UnsignedRightShift =>
                integer && sameResult && op.Right.Type == GaScalar.Int32 && !op.Checked,
            GaBinaryOperator.Equal or GaBinaryOperator.NotEqual => homogeneous && boolResult && !op.Checked,
            GaBinaryOperator.Less or GaBinaryOperator.LessOrEqual or GaBinaryOperator.Greater or GaBinaryOperator.GreaterOrEqual =>
                numeric && homogeneous && boolResult && !op.Checked,
            GaBinaryOperator.AndAlso or GaBinaryOperator.OrElse =>
                operand == GaScalar.Bool && homogeneous && boolResult && !op.Checked,
            _ => false
        };
        Require(valid);
    }

    private static void Require(bool condition)
    {
        if (!condition)
            throw new InvalidDataException("GA IR violates its type, access or definite assignment contract.");
    }
}
