using System.Runtime.InteropServices;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SpaceBattle;

// Software-side replay hashing. Cores only export state and never decide parity.
public sealed class ReplayHash(Action<List<uint>> copyState, BattleCounters? counters = null, AiCounters? tactics = null)
{
    public const string Revision = "native-cores-state-v1";
    private readonly List<uint> state = new(65536);

    public ulong Capture()
    {
        copyState(state);
        if (counters is not null) AppendCounters(counters);
        if (tactics is not null) AppendTactics(tactics);
        return Compute(CollectionsMarshal.AsSpan(state));
    }

    private void Add(long value) { state.Add(unchecked((uint)value)); state.Add(unchecked((uint)(value >> 32))); }
    private void AppendCounters(BattleCounters c)
    {
        Add(c.ShipsCreated);
        Add(c.ShipsDestroyed);
        Add(c.Shots);
        Add(c.Hits);
        Add(c.PlasmaShieldHits);
        Add(c.ShieldBreaks);
        Add(c.Overheats);
        Add(c.Dockings);
        Add(c.Repairs);
        Add(c.Builds);
        Add(c.ItemUses);
        Add(c.DebrisHits);
        Add(c.Explosions);
        Add(c.StructuralAdds);
        Add(c.StructuralRemoves);
        Add(c.AimDecisions);
        Add(c.ImpreciseAimDecisions);
        Add(c.FriendlyFireHits);
        Add(c.SelfHits);
        Add(c.HeavyShots);
        Add(c.TurretShots);
        Add(c.ReinforcementWaves);
        Add(c.ReinforcementShips);
        Add(c.ReinforcementPlacementChecks);
    }
    private void AppendTactics(AiCounters c)
    {
        Add(c.TargetSwitches);
        Add(c.TargetRetentions);
        Add(c.GroupOrders);
        Add(c.CoverOrders);
        Add(c.GroupChanges);
        Add(c.FormationReviews);
        Add(c.CompatibilityChecks);
        Add(c.ScoreEvaluations);
        Add(c.SpatialWork);
        Add(c.BudgetFallbacks);
        Add(c.IntentProjectiles);
    }

    public static string TraceDigest(ReadOnlySpan<ulong> hashes)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[8];
        foreach (var hash in hashes) { BinaryPrimitives.WriteUInt64LittleEndian(bytes, hash); digest.AppendData(bytes); }
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    public static ulong Compute(ReadOnlySpan<uint> state)
    {
        var hash = 14695981039346656037UL;
        foreach (var value in state) { hash ^= value; hash = unchecked(hash * 1099511628211UL); }
        return hash;
    }

    public static void RequireMatch(string framework, int tick, ulong expected, ulong actual)
    {
        if (actual != expected)
            throw new InvalidOperationException($"{framework}: replay divergence at tick {tick}: {actual:X16} != SKECS {expected:X16}");
    }
}
