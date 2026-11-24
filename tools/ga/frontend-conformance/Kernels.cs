using System.Runtime.CompilerServices;
using SKECS.GA;
using SKECS.Numerics;

namespace FrontendFixture;

public struct Point
{
    public int X;
    public int Y;
}

public struct Packet
{
    public Point Point;
    public bool Ready;
    public uint Tag;
    public Fixed Offset;

    public static Packet operator +(Packet packet, int amount) => new()
    {
        Point = new Point { X = checked(packet.Point.X + amount), Y = packet.Point.Y },
        Tag = packet.Tag,
        Ready = packet.Ready,
        Offset = packet.Offset
    };

    public readonly Packet Transformed => new()
    {
        Point = new Point { X = unchecked(Point.X - 5), Y = unchecked(Point.Y + 17) },
        Ready = !Ready,
        Tag = Tag ^ 0x11223344u,
        Offset = Offset
    };

    public static Packet operator -(Packet value) => new()
    {
        Point = new Point { X = unchecked(-value.Point.X), Y = unchecked(-value.Point.Y) },
        Ready = value.Ready,
        Tag = value.Tag,
        Offset = value.Offset
    };

    public static explicit operator Point(Packet value) => new() { X = value.Point.Y, Y = value.Point.X };
    public static explicit operator checked Point(Packet source) => new()
    {
        X = checked(-source.Point.Y),
        Y = source.Point.X
    };
}

public struct Balance
{
    public int Units;
    public uint Ticket;

    public static implicit operator WideBalance(Balance old) => new()
    {
        Units = checked(old.Units + 101),
        Ticket = old.Ticket
    };

    public static implicit operator Balance(WideBalance result) => new()
    {
        Units = result.Units / result.Divisor,
        Ticket = result.Ticket
    };
}

public struct WideBalance
{
    public int Units;
    public int Divisor;
    public uint Ticket;

    public static implicit operator WideBalance(int original) => new()
    {
        Units = checked(original + 301),
        Ticket = 0x71438765u
    };

    public static implicit operator int(WideBalance adjusted) => adjusted.Units / adjusted.Divisor;
}

public struct Adjustment
{
    public int Units;
    public int Divisor;

    public static WideBalance operator +(WideBalance original, Adjustment amount) => new()
    {
        Units = unchecked(original.Units + amount.Units + 17),
        Divisor = amount.Divisor,
        Ticket = original.Ticket ^ 0x80000000u
    };

    public static WideBalance operator checked +(WideBalance left, Adjustment amount) => new()
    {
        Units = checked(left.Units + amount.Units),
        Divisor = amount.Divisor,
        Ticket = left.Ticket ^ 0x40000000u
    };
}

[InlineArray(4)]
public struct Slots
{
    private int _element;
}

[InlineArray(4)]
public struct Packets
{
    private Packet _element;
}

public struct ReadonlyMeta
{
    public int Code;
    public readonly uint Tag;

    public ReadonlyMeta(int code, uint tag)
    {
        Code = code;
        Tag = tag;
    }
}

public struct ReadonlyPacket
{
    public readonly ReadonlyMeta Meta;
    public int Counter;

    public ReadonlyPacket(int code, uint tag, int counter)
    {
        Meta = new ReadonlyMeta(code, tag);
        Counter = counter;
    }
}

[InlineArray(4)]
public struct ReadonlyPackets
{
    private ReadonlyPacket _element;
}

public struct State
{
    public Packet Current;
    public Packet Mirror;
    public Point Point;
    public Balance Balance;
    public Slots Slots;
    public Packets Packets;
    public ReadonlyPackets ReadonlyPackets;
    public int Result;
}

public struct Input
{
    public ReadonlyPacket Replacement;
    public int Left;
    public int Right;
    public int Stop;
    public bool Flag;
    public uint Tag;
    public Fixed Offset;
}

[SecsSystem(SystemId = "fixtures.frontend.copies")]
public partial struct CopiesKernel
{
    public void Execute(ref State state, in Input input)
    {
        Packet zero = new();
        Packet blank = default;
        Packet next = new Packet
        {
            Point = new Point { Y = input.Right, X = input.Left },
            Offset = input.Offset,
            Tag = input.Tag,
            Ready = input.Flag
        };
        Packet copy = state.Current;
        copy.Point.X = blank.Point.Y;
        state.Mirror = copy;
        copy.Point.Y = input.Left;
        state.Result = unchecked(copy.Point.Y + state.Mirror.Point.Y);
        state.Point = state.Current.Point;
        state.Current = next;
        next.Point.X = zero.Point.X;
        state.Mirror.Point.Y = next.Point.X;
    }
}

[SecsSystem(SystemId = "fixtures.frontend.initializers")]
public partial struct InitializerKernel
{
    public void Execute(ref State state, in Input input)
    {
        state.Result = 919;
        state.Current = new Packet
        {
            Point = new Point
            {
                Y = 144 / input.Right,
                X = checked(input.Left + 19)
            },
            Ready = input.Flag
        };
        state.Result = 960 / input.Stop;
    }
}

[SecsSystem(SystemId = "fixtures.frontend.members")]
public partial struct MembersKernel
{
    public void Execute(ref State state, in Input input)
    {
        Packet projected = Make(96 / input.Right, input.Left, input.Offset).Transformed;
        state.Current = projected;
        state.Mirror = -projected;
        if (input.Flag)
            state.Point = checked((Point)projected);
        else
            state.Point = unchecked((Point)projected);
        state.Result = 240 / input.Stop;
    }

    private static Packet Make(int x, int y, Fixed offset) => new()
    {
        Point = new Point { X = x, Y = y },
        Ready = true,
        Tag = 0x9065abcd,
        Offset = offset
    };
}

[SecsSystem(SystemId = "fixtures.frontend.named")]
public partial struct NamedKernel
{
    public void Execute(ref State state, in Input input)
    {
        state.Current = Mix(
            second: Make(48 / input.Right, input.Left),
            first: Make(checked(input.Left + 1), input.Right));
        state.Result = checked(input.Stop + 1009);
    }

    private static Point Make(int x, int y) => new() { X = x, Y = y };

    private static Packet Mix(Point first, Point second)
    {
        Point copy = second;
        second.X = first.Y;
        return new Packet
        {
            Point = new Point { X = unchecked(first.X * 7 + copy.X), Y = unchecked(second.X * 13 + copy.Y) },
            Ready = second.X != copy.X
        };
    }
}

[SecsSystem(SystemId = "fixtures.frontend.flow")]
public partial struct FlowKernel
{
    public void Execute(ref State state, in Input input)
    {
        state.Mirror = input.Flag ? Select(state.Current, input.Stop) : default;
        state.Result = 720 / input.Right;
    }

    private static Packet Select(Packet initial, int stop)
    {
        Packet result;
        if (initial.Ready)
            result = initial;
        else
            result = new Packet { Point = new Point { X = initial.Point.X } };
        for (int i = 0; i < 4; i++)
        {
            if (i == stop)
                return result;
            result.Point.X = checked(result.Point.X + 23);
            result.Point.Y++;
        }
        return new Packet { Point = result.Point, Ready = true, Tag = initial.Tag, Offset = initial.Offset };
    }
}

[SecsSystem(SystemId = "fixtures.frontend.compound")]
public partial struct CompoundKernel
{
    public void Execute(ref State state, in Input input)
    {
        Balance local = state.Balance;
        if (input.Flag)
            checked { state.Balance += Measure(input); }
        else
            unchecked { local += Measure(input); }
        if (!input.Flag)
            state.Balance = local;
        state.Result = checked(input.Left - 303);
    }

    private static Adjustment Measure(Input input) => new()
    {
        Units = 480 / input.Right,
        Divisor = input.Stop
    };
}

[SecsSystem(SystemId = "fixtures.frontend.indexed-compound")]
public partial struct IndexedCompoundKernel
{
    public void Execute(ref State state, in Input input)
    {
        checked { state.Slots[12 / input.Left] += Shift(input.Right, input.Stop); }
        state.Result = checked(input.Left + 949);
    }

    private static Adjustment Shift(int divisor, int resultDivisor) => new()
    {
        Units = 840 / divisor,
        Divisor = resultDivisor
    };
}

[SecsSystem(SystemId = "fixtures.frontend.indexed-value")]
public partial struct IndexedValueKernel
{
    public void Execute(ref State state, in Input input)
    {
        if (input.Flag)
            state.Packets[14 / input.Left] += 840 / input.Right;
        else
            state.Packets[20 / input.Left] = new Packet
            {
                Point = new Point { X = 700 / input.Right, Y = input.Left },
                Tag = input.Tag,
                Offset = input.Offset,
                Ready = true
            };

        state.Mirror = state.Packets[input.Stop];
        state.Result = checked(input.Left + 9490);
    }
}

[SecsSystem(SystemId = "fixtures.frontend.indexed-readonly-read-first")]
public partial struct IndexedReadonlyReadFirstKernel
{
    public void Execute(ref State state, in Input input)
    {
        int before = state.ReadonlyPackets[input.Left].Meta.Code;
        state.ReadonlyPackets[input.Left] = input.Replacement;
        int after = state.ReadonlyPackets[input.Left].Meta.Code;
        state.Result = checked(before + after);
    }
}

[SecsSystem(SystemId = "fixtures.frontend.indexed-readonly-store-first")]
public partial struct IndexedReadonlyStoreFirstKernel
{
    public void Execute(ref State state, in Input input)
    {
        state.ReadonlyPackets[input.Right] = input.Replacement;
        state.Result = state.ReadonlyPackets[input.Right].Meta.Code;
        state.Result += 907 / input.Stop;
    }
}
