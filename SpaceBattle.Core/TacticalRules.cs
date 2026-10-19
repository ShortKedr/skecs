namespace SpaceBattle;

public static class TacticalRules
{
    public const string Revision = "layered-squads-v3";
    public const int MinimumTargetLockTicks = 120, MaxSquadSize = 4;
    public const float FormationRange = 48, SquadBreakRange = 96, SwitchImprovement = 0.35f;
}

public sealed class AiCounters
{
    public long TargetSwitches, TargetRetentions, GroupOrders, CoverOrders;
    public long GroupChanges, FormationReviews, CompatibilityChecks, ScoreEvaluations, SpatialWork, BudgetFallbacks, IntentProjectiles;
}
