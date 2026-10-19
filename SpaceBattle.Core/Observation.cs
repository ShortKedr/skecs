using System.Numerics;

namespace SpaceBattle;

public readonly record struct ShipObservation(int Serial, int Team, int Kind, Vector2 Position, float Radius,
    float Hull, float Shield, float Heat, int TargetSerial, int DockCarrierSerial);
public readonly record struct ObservedShipDeath(int Serial, int KillerSerial, Vector2 Position, int Tick);
