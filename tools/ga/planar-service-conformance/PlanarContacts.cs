using SKECS.Numerics;

internal static class PlanarContacts
{
    private static readonly Fixed ContactDistanceSquared = Fixed.FromInt32(4);

    internal static PlanarResult Solve(PlanarSample[] sortedSamples)
    {
        var rows = (PlanarSample[])sortedSamples.Clone();
        var pairs = new List<PlanarPair>();
        int impulses = 0;

        for (int first = 0; first < rows.Length; first++)
        {
            var left = rows[first];
            for (int second = first + 1; second < rows.Length; second++)
            {
                var right = rows[second];
                Fixed dx = right.X - left.X;
                Fixed dy = right.Y - left.Y;
                Fixed squaredDistance = dx * dx + dy * dy;
                if (squaredDistance > ContactDistanceSquared)
                    continue;

                pairs.Add(new PlanarPair(left.Slot, left.Generation, right.Slot, right.Generation));
                // A distance rounded to zero uses the same normal as a coincident pair.
                if (squaredDistance == Fixed.Zero)
                {
                    dx = Fixed.One;
                    dy = Fixed.Zero;
                    squaredDistance = Fixed.One;
                }

                Fixed dot = (left.Vx - right.Vx) * dx + (left.Vy - right.Vy) * dy;
                if (dot <= Fixed.Zero)
                    continue;

                Fixed scalar = dot / squaredDistance;
                Fixed transferX = scalar * dx;
                Fixed transferY = scalar * dy;
                left = left with { Vx = left.Vx - transferX, Vy = left.Vy - transferY };
                rows[second] = right with { Vx = right.Vx + transferX, Vy = right.Vy + transferY };
                impulses++;
            }
            rows[first] = left;
        }

        return new PlanarResult(rows, pairs.ToArray(), impulses);
    }
}
