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
    internal int TargetId;
    internal int TargetGeneration;
}

internal struct GaSignal { internal int Value; }

[SecsSystem(SystemId = "demo.fixed-movement")]
internal partial struct GaMovement
{
    public void Execute(in SystemContext context, ref GaPosition position, in GaVelocity velocity)
    {
        if (velocity.Divide)
            context.Emit(position.Cell);
        int signal = context.ReadSnapshotInt32(velocity.TargetId, velocity.TargetGeneration, -777);
        var delta = velocity.Divide ? velocity.Delta / velocity.Step : velocity.Delta * velocity.Step;
        position.X += delta;
        position.Cell = (int)position.X + signal;
        context.Emit(position.Cell);
    }
}
