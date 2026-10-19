using System.Numerics;

namespace SpaceBattle;

public readonly record struct CollisionEvent(int FirstSerial, int SecondSerial, float RelativeSpeed, Vector2 Position);
