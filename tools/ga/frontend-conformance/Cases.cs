using FrontendFixture;
using SKECS.GA;
using SKECS.Numerics;

internal delegate void Execute(ref State state, in Input input);

internal sealed record Kernel(string Type, string SystemId, Execute Execute, Case[] Cases);

internal sealed record Case(string Name, State State, Input Input, Fault? Fault = null);

internal sealed record Fault(GaFaultKind Kind, ulong Ordinal, string Expression);

internal static class Cases
{
    internal static Kernel[] Create()
    {
        State start = new()
        {
            Current = new Packet
            {
                Point = new Point { X = 318, Y = 573 },
                Ready = true,
                Tag = 0xf053dd11,
                Offset = Fixed.FromRaw(-0x5f020000b)
            },
            Mirror = new Packet
            {
                Point = new Point { X = -28, Y = 147 },
                Tag = 0xab528d31,
                Offset = Fixed.FromRaw(0x781feab12cd93)
            },
            Point = new Point { X = -12, Y = -3 },
            Balance = new Balance { Units = 203, Ticket = 0xb8a8b8a8 },
            Result = -17
        };
        start.Slots[0] = 63;
        start.Slots[1] = -286;
        start.Slots[2] = 9191;
        start.Slots[3] = -52;
        start.Packets[0] = start.Current;
        start.Packets[1] = start.Mirror;
        start.Packets[2] = start.Current with { Point = new Point { X = 300, Y = -10 } };
        start.Packets[3] = start.Mirror with { Ready = true };
        start.ReadonlyPackets[0] = new ReadonlyPacket(35, 0xff338822u, 108);
        start.ReadonlyPackets[1] = new ReadonlyPacket(36, 0x5ee32e6u, -34);

        Input settings = new()
        {
            Left = 7, Right = 4, Stop = 3, Flag = true, Tag = 0xff24875f,
            Offset = Fixed.FromRaw(long.MinValue + 731)
        };

        return
        [
            new(nameof(CopiesKernel), "fixtures.frontend.copies", new CopiesKernel().Execute,
            [
                Success("distinct leaves", settings),
                Success("zero input", default),
                Success("high word and signed extrema", settings with
                {
                    Left = int.MaxValue, Right = int.MinValue, Tag = uint.MaxValue, Offset = Fixed.FromRaw(long.MaxValue)
                }),
                Success("zero state", settings with { Flag = false }, default(State))
            ]),
            new(nameof(InitializerKernel), "fixtures.frontend.initializers", new InitializerKernel().Execute,
            [
                Success("unordered members and zero-fill", settings),
                Failure("initializer order with two faults", settings with { Right = 0, Left = int.MaxValue },
                    GaFaultKind.DivideByZero, 0, "144 / input.Right"),
                Failure("second nested initializer", settings with { Left = int.MaxValue, Stop = 0 },
                    GaFaultKind.Overflow, 1, "input.Left + 19"),
                Failure("writeback is withheld after initialized value", settings with { Stop = 0 },
                    GaFaultKind.DivideByZero, 2, "960 / input.Stop")
            ]),
            new(nameof(MembersKernel), "fixtures.frontend.members", new MembersKernel().Execute,
            [
                Success("checked conversion", settings),
                Success("unchecked conversion at signed minimum", settings with { Flag = false, Left = int.MaxValue - 16 }),
                Failure("fault in getter receiver construction", settings with { Right = 0, Left = int.MaxValue - 16 },
                    GaFaultKind.DivideByZero, 0, "96 / input.Right"),
                Failure("exact checked conversion on returned struct", settings with { Left = int.MaxValue - 16, Stop = 0 },
                    GaFaultKind.Overflow, 1, "-source.Point.Y"),
                Failure("getter receiver and checked conversion evaluated once", settings with { Stop = 0 },
                    GaFaultKind.DivideByZero, 2, "240 / input.Stop"),
                Failure("unchecked conversion skips checked body", settings with { Stop = 0, Flag = false },
                    GaFaultKind.DivideByZero, 1, "240 / input.Stop")
            ]),
            new(nameof(NamedKernel), "fixtures.frontend.named", new NamedKernel().Execute,
            [
                Success("out of declaration order", settings),
                Failure("first argument faults before competing checked argument", settings with { Right = 0, Left = int.MaxValue },
                    GaFaultKind.DivideByZero, 0, "48 / input.Right"),
                Failure("second argument follows complete first argument", settings with { Left = int.MaxValue },
                    GaFaultKind.Overflow, 1, "input.Left + 1"),
                Failure("each aggregate argument evaluates once", settings with { Stop = int.MaxValue },
                    GaFaultKind.Overflow, 2, "input.Stop + 1009")
            ]),
            new(nameof(FlowKernel), "fixtures.frontend.flow", new FlowKernel().Execute,
            [
                Success("default conditional arm", settings with { Flag = false }),
                Success("early return immediately", settings with { Stop = 0 }),
                Success("early return after two iterations", settings with { Stop = 2 }),
                Success("full loop and other initialization branch", settings with { Stop = 4 }, WithCurrent(17, false)),
                Failure("skipped aggregate helper", settings with { Flag = false, Right = 0 },
                    GaFaultKind.DivideByZero, 0, "720 / input.Right", WithCurrent(int.MaxValue, true)),
                Failure("initial loop iteration beats competing caller fault", settings with { Right = 0 },
                    GaFaultKind.Overflow, 0, "result.Point.X + 23", WithCurrent(int.MaxValue, true)),
                Failure("fault position counts completed loop evaluations", settings with { Right = 0 },
                    GaFaultKind.Overflow, 1, "result.Point.X + 23", WithCurrent(int.MaxValue - 23, false)),
                Failure("early return preserves caller evaluation order", settings with { Stop = 2, Right = 0 },
                    GaFaultKind.DivideByZero, 2, "720 / input.Right"),
                Failure("loop fallthrough preserves caller evaluation order", settings with { Stop = 4, Right = 0 },
                    GaFaultKind.DivideByZero, 4, "720 / input.Right")
            ]),
            new(nameof(CompoundKernel), "fixtures.frontend.compound", new CompoundKernel().Execute,
            [
                Success("checked component target", settings),
                Success("unchecked local target", settings with { Flag = false }),
                Success("unchecked overload wraps", settings with { Flag = false, Right = 1 }, WithBalance(int.MaxValue - 101)),
                Failure("incoming conversion precedes competing RHS fault", settings with { Right = 0 },
                    GaFaultKind.Overflow, 0, "old.Units + 101", WithBalance(int.MaxValue)),
                Failure("RHS precedes operator and outgoing conversion", settings with { Right = 0, Stop = 0 },
                    GaFaultKind.DivideByZero, 1, "480 / input.Right"),
                Failure("exact checked overload precedes outgoing conversion", settings with { Right = 1, Stop = 0 },
                    GaFaultKind.Overflow, 2, "left.Units + amount.Units", WithBalance(int.MaxValue - 101)),
                Failure("checked outgoing conversion", settings with { Stop = 0 },
                    GaFaultKind.DivideByZero, 3, "result.Units / result.Divisor"),
                Failure("unchecked outgoing conversion", settings with { Stop = 0, Flag = false },
                    GaFaultKind.DivideByZero, 2, "result.Units / result.Divisor"),
                Failure("all four checked phases execute once", settings with { Left = int.MinValue },
                    GaFaultKind.Overflow, 4, "input.Left - 303"),
                Failure("all three unchecked phases execute once", settings with { Left = int.MinValue, Flag = false },
                    GaFaultKind.Overflow, 3, "input.Left - 303")
            ]),
            new(nameof(IndexedCompoundKernel), "fixtures.frontend.indexed-compound", new IndexedCompoundKernel().Execute,
            [
                Success("write middle slot", settings with { Left = 6 }),
                Success("write last slot", settings with { Left = 4 }),
                Failure("target index calculation precedes RHS", settings with { Left = 0, Right = 0 },
                    GaFaultKind.DivideByZero, 0, "12 / input.Left"),
                Failure("target bounds precede RHS", settings with { Left = 3, Right = 0 },
                    GaFaultKind.IndexOutOfRange, 1, "state.Slots[12 / input.Left]"),
                Failure("incoming conversion follows target read", settings with { Left = 6, Right = 0 },
                    GaFaultKind.Overflow, 2, "original + 301", WithSlot(2, int.MaxValue)),
                Failure("indexed RHS follows incoming conversion", settings with { Left = 6, Right = 0 },
                    GaFaultKind.DivideByZero, 3, "840 / divisor"),
                Failure("indexed checked overload", settings with { Left = 6, Right = 1, Stop = 0 },
                    GaFaultKind.Overflow, 4, "left.Units + amount.Units", WithSlot(2, int.MaxValue - 301)),
                Failure("indexed outgoing conversion", settings with { Left = 6, Stop = 0 },
                    GaFaultKind.DivideByZero, 5, "adjusted.Units / adjusted.Divisor"),
                Failure("target index evaluates once", settings with { Left = int.MaxValue },
                    GaFaultKind.Overflow, 6, "input.Left + 949")
            ]),
            new(nameof(IndexedValueKernel), "fixtures.frontend.indexed-value", new IndexedValueKernel().Execute,
            [
                Success("compound reads old nested fields and copies updated element", settings with { Left = 6, Stop = 2 }),
                Success("plain overwrite preserves unrelated elements", settings with { Flag = false, Left = 10, Stop = 2 }),
                Failure("index expression faults before any bounds check", settings with { Left = 0, Right = 0 },
                    GaFaultKind.DivideByZero, 0, "14 / input.Left"),
                Failure("invalid compound target bounds before RHS", settings with { Left = 1, Right = 0 },
                    GaFaultKind.IndexOutOfRange, 1, "state.Packets[14 / input.Left]"),
                Failure("invalid plain target bounds before RHS", settings with { Flag = false, Left = 1, Right = 0 },
                    GaFaultKind.IndexOutOfRange, 1, "state.Packets[20 / input.Left]"),
                Failure("one compound target bounds check before RHS", settings with { Left = 6, Right = 0 },
                    GaFaultKind.DivideByZero, 2, "840 / input.Right"),
                Failure("overflow after scanning all old fields", settings with { Left = 6, Right = 1 },
                    GaFaultKind.Overflow, 3, "packet.Point.X + amount", WithPacketX(2, int.MaxValue)),
                Failure("one bounds check on subsequent whole read", settings with { Left = 6, Stop = -1 },
                    GaFaultKind.IndexOutOfRange, 4, "state.Packets[input.Stop]"),
                Failure("plain target and read each check once", settings with { Flag = false, Left = int.MaxValue, Stop = 0 },
                    GaFaultKind.Overflow, 4, "input.Left + 9490"),
                Failure("compound target and read each check once", settings with { Left = int.MaxValue, Stop = 0 },
                    GaFaultKind.Overflow, 5, "input.Left + 9490"),
            ]),
            new(nameof(IndexedReadonlyReadFirstKernel), "fixtures.frontend.indexed-readonly-read-first",
                new IndexedReadonlyReadFirstKernel().Execute,
            [
                Success("cached read-only range upgraded by whole write", settings with
                {
                    Left = 0, Replacement = new ReadonlyPacket(72, 0x91f390a1u, -16)
                }),
                Failure("fault after replacing readonly fields", settings with
                {
                    Left = 1, Replacement = new ReadonlyPacket(int.MaxValue, 13u, 36)
                }, GaFaultKind.Overflow, 3, "before + after")
            ]),
            new(nameof(IndexedReadonlyStoreFirstKernel), "fixtures.frontend.indexed-readonly-store-first",
                new IndexedReadonlyStoreFirstKernel().Execute,
            [
                Success("whole write declares shared range before readonly read", settings with
                {
                    Right = 1, Replacement = new ReadonlyPacket(-74, uint.MaxValue, 27)
                }),
                Failure("subsequent fault with no committed replacement", settings with
                {
                    Right = 0, Stop = 0, Replacement = new ReadonlyPacket(-12, 52u, 66)
                }, GaFaultKind.DivideByZero, 2, "907 / input.Stop")
            ])
        ];

        Case Success(string name, Input input, State? initial = null) => new(name, initial ?? start, input);
        Case Failure(string name, Input input, GaFaultKind kind, ulong ordinal, string expression, State? initial = null) =>
            new(name, initial ?? start, input, new Fault(kind, ordinal, expression));
        State WithCurrent(int x, bool ready) => start with
        {
            Current = start.Current with { Ready = ready, Point = start.Current.Point with { X = x } }
        };
        State WithBalance(int units) => start with { Balance = start.Balance with { Units = units } };
        State WithPacketX(int index, int value)
        {
            State state = start;
            state.Packets[index].Point.X = value;
            return state;
        }
        State WithSlot(int index, int value)
        {
            State state = start;
            state.Slots[index] = value;
            return state;
        }
    }
}
