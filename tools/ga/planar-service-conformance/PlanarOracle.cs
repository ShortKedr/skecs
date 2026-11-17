using System.Numerics;
using SKECS.Numerics;

internal static class PlanarOracle
{
    private const long Scale = 1L << 32;
    private const long ContactDistanceSquared = 4 * Scale;

    internal static PlanarSample[] Integrate(PlanarSample[] sortedSamples, Fixed delta)
    {
        var result = new PlanarSample[sortedSamples.Length];
        for (int index = 0; index < sortedSamples.Length; index++)
        {
            var row = sortedSamples[index];
            result[index] = row with
            {
                X = Fixed.FromRaw(checked(row.X.Raw + Multiply(row.Vx.Raw, delta.Raw))),
                Y = Fixed.FromRaw(checked(row.Y.Raw + Multiply(row.Vy.Raw, delta.Raw)))
            };
        }
        return result;
    }

    internal static PlanarResult Solve(PlanarSample[] integrated)
    {
        // Contact geometry stays fixed while impulses update velocities in slot order.
        var contacts = new List<(int Left, int Right, long X, long Y, long Square)>();
        for (int left = 0; left < integrated.Length; left++)
        {
            for (int right = left + 1; right < integrated.Length; right++)
            {
                long x = checked(integrated[right].X.Raw - integrated[left].X.Raw);
                long y = checked(integrated[right].Y.Raw - integrated[left].Y.Raw);
                long square = checked(Multiply(x, x) + Multiply(y, y));
                if (square <= ContactDistanceSquared)
                    contacts.Add((left, right, x, y, square));
            }
        }

        var rows = (PlanarSample[])integrated.Clone();
        var pairs = new PlanarPair[contacts.Count];
        int impulses = 0;
        for (int index = 0; index < contacts.Count; index++)
        {
            var (left, right, x, y, square) = contacts[index];
            var first = rows[left];
            var second = rows[right];
            pairs[index] = new(first.Slot, first.Generation, second.Slot, second.Generation);
            if (square == 0)
                (x, y, square) = (Scale, 0, Scale);

            long relativeX = checked(first.Vx.Raw - second.Vx.Raw);
            long relativeY = checked(first.Vy.Raw - second.Vy.Raw);
            long dot = checked(Multiply(relativeX, x) + Multiply(relativeY, y));
            if (dot <= 0)
                continue;

            long scalar = Divide(dot, square);
            long transferX = Multiply(scalar, x);
            long transferY = Multiply(scalar, y);
            rows[left] = first with
            {
                Vx = Fixed.FromRaw(checked(first.Vx.Raw - transferX)),
                Vy = Fixed.FromRaw(checked(first.Vy.Raw - transferY))
            };
            rows[right] = second with
            {
                Vx = Fixed.FromRaw(checked(second.Vx.Raw + transferX)),
                Vy = Fixed.FromRaw(checked(second.Vy.Raw + transferY))
            };
            impulses++;
        }

        return new(rows, pairs, impulses);
    }

    internal static void VerifyArithmetic()
    {
        if (Multiply(1, Scale / 2) != 0 || Multiply(3, Scale / 2) != 2 ||
            Multiply(-3, Scale / 2) != -2 || Multiply(long.MinValue, Scale) != long.MinValue ||
            Divide(1, 2 * Scale) != 0 || Divide(3, 2 * Scale) != 2 ||
            Divide(3, -2 * Scale) != -2 || Divide(long.MinValue, Scale) != long.MinValue)
            throw new InvalidOperationException("The planar oracle failed its Q32.32 arithmetic checks.");
    }

    private static long Multiply(long left, long right) => Round((BigInteger)left * right, Scale);

    private static long Divide(long left, long right) => Round((BigInteger)left * Scale, right);

    private static long Round(BigInteger numerator, BigInteger denominator)
    {
        BigInteger divisor = BigInteger.Abs(denominator);
        BigInteger quotient = BigInteger.DivRem(BigInteger.Abs(numerator), divisor, out var remainder);
        int half = (2 * remainder).CompareTo(divisor);
        if (half > 0 || half == 0 && !quotient.IsEven)
            quotient++;
        return (long)(numerator.Sign == denominator.Sign ? quotient : -quotient);
    }
}
