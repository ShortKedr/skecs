using SKECS.GA;
using SKECS.Numerics;

internal struct GaPosition
{
    internal Fixed X;
    internal int Cell;
}

internal struct GaVelocity
{
    internal Fixed Delta;
    internal Fixed Step;
    internal bool Divide;
}

[SecsSystem(SystemId = "demo.fixed-movement")]
internal partial struct GaMovement
{
    public void Execute(ref GaPosition position, in GaVelocity velocity)
    {
        var delta = velocity.Divide ? velocity.Delta / velocity.Step : velocity.Delta * velocity.Step;
        position.X += delta;
        position.Cell = (int)position.X;
    }
}
