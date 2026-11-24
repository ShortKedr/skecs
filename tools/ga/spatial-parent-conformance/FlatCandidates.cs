#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SKECS.Numerics;

internal readonly struct RawBounds : IEquatable<RawBounds>
{
    internal RawBounds(long left, long bottom, long right, long top) =>
        (Left, Bottom, Right, Top) = (left, bottom, right, top);

    internal long Left { get; }
    internal long Bottom { get; }
    internal long Right { get; }
    internal long Top { get; }

    public bool Equals(RawBounds other) =>
        Left == other.Left && Bottom == other.Bottom && Right == other.Right && Top == other.Top;
    public override bool Equals(object? obj) => obj is RawBounds other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Left, Bottom, Right, Top);

    internal SpatialBounds ToComponent() => new()
    {
        Left = Fixed.FromRaw(Left), Bottom = Fixed.FromRaw(Bottom),
        Right = Fixed.FromRaw(Right), Top = Fixed.FromRaw(Top)
    };

    internal static RawBounds FromComponent(SpatialBounds value) =>
        new(value.Left.Raw, value.Bottom.Raw, value.Right.Raw, value.Top.Raw);
}

// The grid is a candidate-only projection. Large, outside, and over-capacity rows stay in a full-scan list.
internal sealed class FlatCandidates
{
    internal const long Unit = 4_294_967_296L;
    private const long Min = -8 * Unit;
    private const long Max = 8 * Unit;
    private const long CellWidth = 4 * Unit;
    private const int AxisCells = 4;
    private const int GridBudget = 9;
    private readonly List<SpatialMember>[] _cells =
        Enumerable.Range(0, AxisCells * AxisCells).Select(_ => new List<SpatialMember>()).ToArray();
    private readonly List<SpatialMember> _fallback = new();
    private int _cellEntries;
    private bool _sealed;

    internal int Outside { get; private set; }
    internal int Large { get; private set; }
    internal int Full { get; private set; }

    internal void Clear()
    {
        foreach (var cell in _cells)
            cell.Clear();
        _fallback.Clear();
        _cellEntries = 0;
        Outside = 0;
        Large = 0;
        Full = 0;
        _sealed = false;
    }

    internal void Add(SpatialMember member)
    {
        _sealed = false;
        var box = RawBounds.FromComponent(member.Bounds);
        if (box.Left > box.Right || box.Bottom > box.Top)
            throw new InvalidDataException("Invalid spatial member bounds.");
        if (Wide(box.Left, box.Right) || Wide(box.Bottom, box.Top))
            Large++;
        else if (box.Left < Min || box.Bottom < Min || box.Right > Max || box.Top > Max)
            Outside++;
        else
        {
            var (firstX, firstY, lastX, lastY) = CellRange(box);
            int entries = (lastX - firstX + 1) * (lastY - firstY + 1);
            if (_cellEntries + entries <= GridBudget)
            {
                for (int y = firstY; y <= lastY; y++)
                for (int x = firstX; x <= lastX; x++)
                    _cells[y * AxisCells + x].Add(member);
                _cellEntries += entries;
                return;
            }
            Full++;
        }
        _fallback.Add(member);
    }

    internal void Seal() => _sealed = true;

    internal SpatialMember[] Gather(RawBounds query, SpatialMember? retainedStale)
    {
        if (!_sealed)
            throw new InvalidOperationException("Spatial index changed since its last seal.");
        var result = new List<SpatialMember>();
        if (query.Right >= Min && query.Top >= Min && query.Left <= Max && query.Bottom <= Max)
        {
            var (firstX, firstY, lastX, lastY) = CellRange(query);
            for (int y = firstY; y <= lastY; y++)
            for (int x = firstX; x <= lastX; x++)
                result.AddRange(_cells[y * AxisCells + x]);
        }
        result.AddRange(_fallback);
        if (retainedStale is { } old)
            result.Add(old);
        return result.OrderBy(member => member.Id).ThenBy(member => member.Generation).ToArray();
    }

    private static (int FirstX, int FirstY, int LastX, int LastY) CellRange(RawBounds box) =>
        (Cell(box.Left), Cell(box.Bottom), Cell(box.Right), Cell(box.Top));

    private static bool Wide(long minimum, long maximum) =>
        minimum <= long.MaxValue - 2 * CellWidth && maximum > minimum + 2 * CellWidth;

    // Endpoints are inclusive. A box ending exactly on an internal cell edge occupies the cell on either side.
    private static int Cell(long coordinate) =>
        Math.Min(AxisCells - 1, (int)((Math.Clamp(coordinate, Min, Max) - Min) / CellWidth));
}
