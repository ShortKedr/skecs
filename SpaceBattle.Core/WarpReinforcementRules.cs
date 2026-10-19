using System.Numerics;

namespace SpaceBattle;

public static class WarpEffect
{
    public const int PortalKind = 4, ArrivalKind = 6, LastKind = 7, CloseTicks = 45, ArrivalTicks = 30;
    public static bool Foreground(int kind) => kind == HeavyImpactEffect.Kind || kind is >= ArrivalKind and <= LastKind;

    public static void Present(in Particle particle, int tick, out float radius, out float alpha)
    {
        var age = Math.Max(0, tick - particle.StartTick);
        if (particle.Kind < ArrivalKind)
        {
            var opening = Math.Clamp(age / 30f, 0, 1);
            var closing = Math.Clamp((particle.ExpiresTick - tick) / (float)CloseTicks, 0, 1);
            radius = particle.Size * (0.55f + 0.45f * opening) * closing;
            alpha = opening * closing * 0.7f * (0.9f + 0.1f * MathF.Sin(age * 0.12f));
        }
        else
        {
            var phase = Math.Clamp(age / (float)ArrivalTicks, 0, 1);
            radius = particle.Size * (0.5f + 0.7f * Math.Min(age / 8f, 1));
            alpha = (1 - phase) * (1 - phase) * 0.85f;
        }
    }
}

public static class WarpReinforcementRules
{
    public const string Revision = "corner-warp-v1";
    public static float ClassStrength(ShipClass kind) => kind switch
    {
        ShipClass.Interceptor => 0.75f, ShipClass.Fighter => 1, ShipClass.Bomber => 1.5f,
        ShipClass.Gunship => 2, ShipClass.Corvette => 3, ShipClass.Frigate => 4,
        ShipClass.Destroyer => 6, ShipClass.Support => 2,
        ShipClass.LightCarrier => 12, ShipClass.FleetCarrier => 18, _ => 24
    };
}

public readonly record struct ReinforcementStatus(int Wave, int Corner, int Composition, int PendingShips,
    int ArrivalTick, int NextSpawnTick, int EndTick, Vector2 Position, bool Active);
