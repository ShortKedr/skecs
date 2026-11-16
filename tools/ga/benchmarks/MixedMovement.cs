using SKECS.Numerics;

namespace SKECS.GA.Benchmarks;

internal struct Position
{
    public Fixed X;
    public int Cell;
}

internal struct Velocity
{
    public Fixed Delta;
    public Fixed Scale;
    public int Shift;
    public bool Divide;
}

[SecsSystem(SystemId = "benchmarks.mixed-movement")]
internal partial struct MixedMovement
{
    public void Execute(ref Position position, in Velocity velocity)
    {
        var delta = velocity.Divide ? velocity.Delta / velocity.Scale : velocity.Delta * velocity.Scale;
        position.X += delta;
        position.Cell += (int)position.X >>> velocity.Shift;
    }
}
