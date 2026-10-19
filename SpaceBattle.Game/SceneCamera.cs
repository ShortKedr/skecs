using System.Numerics;

namespace SpaceBattle.Game;

internal readonly record struct SceneCamera(int Width, int Height, float Scale, Vector2 Center, float OriginY)
{
    public Vector2 Screen(Vector2 world) => new(Width * .5f + (world.X - Center.X) * Scale,
        OriginY - (world.Y - Center.Y) * Scale);
    public bool Visible(Vector2 p, float extent) => p.X + extent >= 0 && p.Y + extent >= 0 &&
        p.X - extent <= Width && p.Y - extent <= Height;
}

internal static class ScenePalette
{
    public static readonly Vector4 Background = new(8 / 255f, 10 / 255f, 12 / 255f, 1);
}
