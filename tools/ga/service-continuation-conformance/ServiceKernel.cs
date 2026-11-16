using SKECS.GA;
using SKECS.Numerics;

public struct IntegrationFrame
{
    public Fixed Delta;
}

public struct ContactMotion
{
    public Fixed Position;
    public Fixed Velocity;
}

public struct PhysicsLink
{
    public int Slot;
    public int Generation;
}

public struct BodyObservation
{
    public int Slot;
    public int Generation;
    public Fixed Position;
    public Fixed Velocity;
}

[SecsSystem(SystemId = "service-continuation")]
public readonly partial struct IntegrateBodies
{
    [GaTickResource(typeof(IntegrationFrame))]
    public void Execute(in SystemContext context, ref ContactMotion motion, in PhysicsLink link)
    {
        motion.Position += motion.Velocity * context.Resource<IntegrationFrame>().Delta;
        context.Emit(new BodyObservation
        {
            Slot = link.Slot, Generation = link.Generation, Position = motion.Position, Velocity = motion.Velocity
        });
    }
}
