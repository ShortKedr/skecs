namespace SpaceBattle;

public static class ArmamentRules
{
    public const string Revision = "mounted-armament-v7";
    public const float TurretTurnRate = 5, TurretAimTolerance = 0.08f, MainAimTolerance = 0.12f;
    public const int TurretDecisionTicks = 30, RecoilTicks = 9;

    public static int InitialHeavyChargeTicks(Weapon weapon, float churn, uint seed, int serial)
    {
        var cooldown = Math.Max(1, (int)(weapon.Cooldown / churn));
        var minimum = (cooldown + 1) / 2;
        // A separate stream staggers births without changing fleet placement or shot spread.
        var rng = new RandomStream(seed ^ unchecked((uint)serial * 0x9E3779B9u) ^ 0xD1B54A35u);
        return minimum + (int)(rng.Next() % (uint)(cooldown - minimum + 1));
    }
}
