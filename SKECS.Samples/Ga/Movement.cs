using SKECS.GA;

namespace Skecs.Samples.Ga;

public struct Position
{
    public int X;
    public int Y;
}

public struct Velocity
{
    public int X;
    public int Y;
}

[SecsSystem(SystemId = "samples.movement")]
public partial struct Movement
{
    public void Execute(ref Position position, in Velocity velocity)
    {
        position.X += velocity.X;
        position.Y += velocity.Y;
    }
}
