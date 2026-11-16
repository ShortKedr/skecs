using SKECS.GA;
using SKECS.Numerics;

public struct SpatialBounds
{
    public Fixed Left, Bottom, Right, Top;
}

public struct SpatialMember
{
    public int Id, Generation, Token, Priority;
    public SpatialBounds Bounds;
}

public struct TurretProbe
{
    public int ParentId, ParentGeneration;
    public Fixed OffsetX, OffsetY, FallbackX, FallbackY, HalfX, HalfY;
    public int FirstCandidate, Candidates;
    public int SelectedId, SelectedGeneration, ExactCount;
    public uint ExactSet;
    public Fixed WorldX, WorldY;
}

public struct SpatialReport
{
    public int ParentId, ParentGeneration;
    public bool ParentAvailable;
    public Fixed WorldX, WorldY;
    public int BufferRows, CandidateCount, DuplicateCount, StaleCount, ExactCount;
    public uint CandidateStamp;
    // The unique result enumerates set bits in ascending token order.
    public uint ExactSet;
    public int SelectedId, SelectedGeneration, SelectedPriority;
    public Fixed Score;
}

[SecsSystem(SystemId = "spatial-parent")]
public partial struct SpatialKernel
{
    public const int CandidateLimit = 32;

    [GaReadBuffer(typeof(SpatialMember))]
    [GaSnapshot(GaScalar.Bool, GaScalar.Bool, GaScalar.Fixed, GaScalar.Fixed)]
    public void Execute(in SystemContext context, ref TurretProbe turret)
    {
        bool parent = context.ReadSnapshot<bool>(turret.ParentId, turret.ParentGeneration, 1, false);
        Fixed zero = Fixed.FromRaw(0L);
        Fixed x = turret.FallbackX;
        Fixed y = turret.FallbackY;
        if (parent)
        {
            x = context.ReadSnapshot<Fixed>(turret.ParentId, turret.ParentGeneration, 2, zero) + turret.OffsetX;
            y = context.ReadSnapshot<Fixed>(turret.ParentId, turret.ParentGeneration, 3, zero) + turret.OffsetY;
        }
        Fixed left = x - turret.HalfX;
        Fixed right = x + turret.HalfX;
        Fixed bottom = y - turret.HalfY;
        Fixed top = y + turret.HalfY;
        uint candidateStamp = 2166136261u;
        int duplicates = 0;
        int stale = 0;
        int hits = 0;
        uint exact = 0u;
        int previousId = -1;
        int previousGeneration = 0;
        int bestId = -1;
        int bestGeneration = 0;
        int bestPriority = 0;
        Fixed bestScore = zero;

        // Candidate rows are ordered by (Id, Generation) with repeated cell memberships retained.
        for (int i = 0; i < CandidateLimit; i++)
        {
            if (i < turret.Candidates)
            {
                int index = turret.FirstCandidate + i;
                int id = context.ReadBuffer<SpatialMember>(index).Id;
                int generation = context.ReadBuffer<SpatialMember>(index).Generation;
                candidateStamp = unchecked((candidateStamp * 16777619u) ^ (uint)id ^ ((uint)generation << 16));
                bool first = id != previousId || previousGeneration != generation;
                previousId = id;
                previousGeneration = generation;
                if (!first)
                    duplicates++;
                else if (!context.ReadSnapshot<bool>(id, generation, 0, false))
                    stale++;
                else
                {
                    Fixed minX = context.ReadBuffer<SpatialMember>(index).Bounds.Left;
                    Fixed minY = context.ReadBuffer<SpatialMember>(index).Bounds.Bottom;
                    Fixed maxX = context.ReadBuffer<SpatialMember>(index).Bounds.Right;
                    Fixed maxY = context.ReadBuffer<SpatialMember>(index).Bounds.Top;
                    if (left <= maxX && minX <= right && bottom <= maxY && minY <= top)
                    {
                        hits++;
                        int token = context.ReadBuffer<SpatialMember>(index).Token;
                        exact |= 1u << token;
                        Fixed dx = x < minX ? minX - x : x > maxX ? x - maxX : zero;
                        Fixed dy = y < minY ? minY - y : y > maxY ? y - maxY : zero;
                        Fixed score = dx + dy;
                        int priority = context.ReadBuffer<SpatialMember>(index).Priority;
                        if (bestId < 0 || score < bestScore || score == bestScore &&
                            (priority < bestPriority || priority == bestPriority &&
                                (id < bestId || id == bestId && generation < bestGeneration)))
                        {
                            bestScore = score;
                            bestId = id;
                            bestGeneration = generation;
                            bestPriority = priority;
                        }
                    }
                }
            }
        }
        turret.WorldX = x;
        turret.WorldY = y;
        turret.ExactSet = exact;
        turret.ExactCount = hits;
        turret.SelectedId = bestId;
        turret.SelectedGeneration = bestGeneration;
        context.Emit(new SpatialReport
        {
            ParentId = turret.ParentId, ParentGeneration = turret.ParentGeneration, ParentAvailable = parent, WorldX = x, WorldY = y,
            BufferRows = context.BufferLength<SpatialMember>(), CandidateCount = turret.Candidates,
            DuplicateCount = duplicates, StaleCount = stale, CandidateStamp = candidateStamp,
            ExactSet = exact, ExactCount = hits, SelectedId = bestId, SelectedGeneration = bestGeneration, SelectedPriority = bestPriority, Score = bestScore
        });
    }
}
