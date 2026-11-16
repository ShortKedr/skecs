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
    internal int DestroyId;
    internal int DestroyGeneration;
    internal int RemoveId;
    internal int RemoveGeneration;
}

internal struct GaSignal { internal int Value; }
internal struct GaRemovalMarker { internal int Value; }
internal struct GaAddedData
{
    internal int Count;
    internal bool Flag;
}

internal struct GaAdded
{
    internal Fixed Amount;
    internal GaAddedData Data;
}

[SecsSystem(SystemId = "demo.fixed-movement")]
internal partial struct GaMovement
{
    public void Execute(in SystemContext context, ref GaPosition position, in GaVelocity velocity)
    {
        if (velocity.Divide)
            context.Emit(position.Cell);
        if (velocity.DestroyGeneration != 0)
        {
            context.Remove<GaRemovalMarker>(velocity.RemoveId, velocity.RemoveGeneration);
            context.Remove<GaRemovalMarker>(velocity.RemoveId, velocity.RemoveGeneration);
            context.Add(velocity.RemoveId, velocity.RemoveGeneration, new GaAdded
            {
                Amount = Fixed.FromRaw(-0x1_0000_0007L),
                Data = new GaAddedData { Count = position.Cell - 37, Flag = velocity.Divide }
            });
            context.Add(velocity.RemoveId, velocity.RemoveGeneration, new GaAdded
            {
                Amount = Fixed.FromRaw(7L),
                Data = new GaAddedData { Count = 772, Flag = false }
            });
            context.Destroy(velocity.DestroyId, velocity.DestroyGeneration);
            context.Destroy(velocity.DestroyId, velocity.DestroyGeneration);
            context.Create();
            var kept = context.Create();
            context.Add(kept, new GaAdded { Amount = Fixed.FromRaw(17L) });
            context.Remove<GaAdded>(kept);
            context.Add(kept, new GaAdded
            {
                Amount = Fixed.FromRaw(0x2000_0000_0000_0021L),
                Data = new GaAddedData { Count = ~position.Cell, Flag = velocity.Divide }
            });
            var ticket = context.Create();
            context.Remove<GaRemovalMarker>(ticket);
            context.Destroy(ticket);
        }
        int signal = context.ReadSnapshotInt32(velocity.TargetId, velocity.TargetGeneration, -777);
        var delta = velocity.Divide ? velocity.Delta / velocity.Step : velocity.Delta * velocity.Step;
        position.X += delta;
        position.Cell = (int)position.X + signal;
        context.Emit(position.Cell);
    }
}
