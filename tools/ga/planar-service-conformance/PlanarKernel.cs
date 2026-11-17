using SKECS.GA;
using SKECS.Numerics;

public struct PlanarFrame
{
    public Fixed Delta;
}

public struct PlanarMotion
{
    public Fixed X;
    public Fixed Y;
    public Fixed Vx;
    public Fixed Vy;
}

public struct PlanarLink
{
    public int Slot;
    public int Generation;
}

public struct PlanarObservation
{
    public int Slot;
    public int Generation;
    public Fixed X;
    public Fixed Y;
    public Fixed Vx;
    public Fixed Vy;
}

[SecsSystem(SystemId = "planar-service")]
public readonly partial struct IntegratePlanarBodies
{
    [GaTickResource(typeof(PlanarFrame))]
    public void Execute(in SystemContext context, ref PlanarMotion motion, in PlanarLink link)
    {
        Fixed delta = context.Resource<PlanarFrame>().Delta;
        motion.X += motion.Vx * delta;
        motion.Y += motion.Vy * delta;
        context.Emit(new PlanarObservation
        {
            Slot = link.Slot, Generation = link.Generation, X = motion.X, Y = motion.Y, Vx = motion.Vx, Vy = motion.Vy
        });
    }
}
