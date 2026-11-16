using SKECS.GA;
using SKECS.Numerics;

public struct MovementFrame
{
    public Fixed DeltaTime;
}

public struct MovementPosition
{
    public Fixed X;
    public Fixed Y;
    public Fixed PreviousX;
    public Fixed PreviousY;
    public Fixed Angle;
    public int CellX;
    public int CellY;
    public int Steps;
}

public struct MovementVelocity
{
    public Fixed X;
    public Fixed Y;
    public Fixed Angular;
    public bool IsDebris;
}

[SecsSystem(SystemId = "movement")]
public partial struct Movement
{
    [GaTickResource(typeof(MovementFrame))]
    public void Execute(in SystemContext context, ref MovementPosition position, in MovementVelocity velocity)
    {
        var deltaTime = context.Resource<MovementFrame>().DeltaTime;
        position.PreviousX = position.X;
        position.PreviousY = position.Y;
        position.X += velocity.X * deltaTime;
        position.Y += velocity.Y * deltaTime;
        if (velocity.IsDebris)
            position.Angle += velocity.Angular * deltaTime;
        position.CellX = (int)position.X;
        position.CellY = (int)position.Y;
        position.Steps = unchecked(position.Steps + 1);
    }
}
