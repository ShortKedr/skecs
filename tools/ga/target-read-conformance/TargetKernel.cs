using SKECS.GA;
using SKECS.Numerics;

public struct TargetBody
{
    public Fixed Position;
    public Fixed Step;
}

public struct TargetLink
{
    public int Id;
    public int Generation;
}

public struct TargetObservation
{
    public int Id;
    public int Generation;
    public bool Alive;
    public bool HasTag;
    public Fixed PreviousPosition;
    public Fixed Position;
}

[SecsSystem(SystemId = "target-read")]
public partial struct TargetKernel
{
    [GaSnapshot(GaScalar.Bool, GaScalar.Bool, GaScalar.Fixed)]
    public void Execute(in SystemContext context, ref TargetBody body, in TargetLink link)
    {
        bool alive = context.ReadSnapshot<bool>(link.Id, link.Generation, 0, false);
        bool hasTag = context.ReadSnapshot<bool>(link.Id, link.Generation, 1, false);
        Fixed previous = context.ReadSnapshot<Fixed>(link.Id, link.Generation, 2, Fixed.FromRaw(-0x112233445566L));
        body.Position = (alive && hasTag ? previous : body.Position) + body.Step;
        context.Emit(new TargetObservation
        {
            Id = link.Id,
            Generation = link.Generation,
            Alive = alive,
            HasTag = hasTag,
            PreviousPosition = previous,
            Position = body.Position
        });
    }
}
