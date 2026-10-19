using System.Numerics;

namespace SpaceBattle;

public enum DebugColliderKind { Ship, Projectile, Debris }

public readonly record struct DebugCollider(int Serial, DebugColliderKind Kind, ProjectileShape Shape,
    Vector2 Position, float Angle, float Radius, Vector2 HalfExtents);
