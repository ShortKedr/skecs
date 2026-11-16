using System;
using SKECS;
using SKECS.GA;
using SKECS.Numerics;

public struct ShotFrame
{
    public uint Seed;
    public uint Tick;
    public int MaxProjectiles;
    public int Cooling;
    public int HeatCost;
    public int HeatLimit;
    public int Burst;
    public int CapacityId;
    public int CapacityGeneration;
}

public struct MountRow
{
    public int Serial;
    public int Heat;
    public int Slots;
    public uint Rng;
    public uint LastTick;
    public bool Initialized;
}

public struct ShotSpread
{
    public Fixed Offset;
    public bool Turret;
}

public struct ShotRequest
{
    public int Serial;
    public int Slot;
    public int Heat;
    public uint Draw;
    public ShotSpread Spread;
}

public struct CapacityState
{
    public int Active;
}

public struct Projectile
{
    public int Serial;
    public int Slot;
    public uint Draw;
    public uint Tick;
    public Fixed Offset;
}

public struct ShotDecision
{
    public int Serial;
    public int Slot;
    public uint Draw;
    public bool Accepted;
}

[SecsSystem(SystemId = "shot-burst-capacity")]
public partial struct ShotBurst
{
    [GaTickResource(typeof(ShotFrame))]
    public void Execute(in SystemContext context, ref MountRow row)
    {
        ref readonly var frame = ref context.Resource<ShotFrame>();
        if (row.LastTick != frame.Tick)
        {
            row.LastTick = frame.Tick;
            row.Slots = 0;
            row.Heat = row.Heat > frame.Cooling ? row.Heat - frame.Cooling : 0;
        }
        if (!row.Initialized)
        {
            uint state = frame.Seed ^ unchecked((uint)row.Serial * 0x85ebca6bu);
            row.Rng = state == 0 ? 0x6d2b79f5u : state;
            row.Initialized = true;
        }
        for (int attempt = 0; attempt < 2; attempt++)
        {
            int slot = row.Slots;
            row.Slots++;
            if (attempt < frame.Burst)
            {
                int heat = row.Heat + frame.HeatCost;
                if (heat <= frame.HeatLimit)
                {
                    // The simulation contract fixes one xorshift32 draw per submitted request.
                    uint draw = row.Rng;
                    draw ^= draw << 13;
                    draw ^= draw >> 17;
                    draw ^= draw << 5;
                    row.Rng = draw;
                    row.Heat = heat;
                    context.SetDomainOrder(row.Serial, slot);
                    context.Emit(new ShotRequest
                    {
                        Serial = row.Serial,
                        Slot = slot,
                        Heat = heat,
                        Draw = draw,
                        Spread = new ShotSpread
                        {
                            Offset = Fixed.FromRaw(-1073741824L) + Fixed.FromInt32((int)(draw & 7u) - 3),
                            Turret = slot >= 2
                        }
                    });
                }
            }
        }
    }
}

public static class ShotApplication
{
    public static void Apply(DeterministicOrderedApplyContext<ShotFrame> context)
    {
        var frame = context.Input;
        if (!context.TryRead<CapacityState>(frame.CapacityId, frame.CapacityGeneration, out var state))
            throw new InvalidOperationException("Shot capacity state is missing.");

        var commands = context.Commands;
        foreach (var emission in context.Events)
        {
            var request = emission.Get<ShotRequest>();
            bool accepted = state.Active < frame.MaxProjectiles;
            if (accepted)
            {
                var ticket = commands.Create();
                commands.Add(ticket, new Projectile
                {
                    Serial = request.Serial,
                    Slot = request.Slot,
                    Draw = request.Draw,
                    Tick = frame.Tick,
                    Offset = request.Spread.Offset
                });
                state.Active++;
            }
            commands.Emit(new ShotDecision
            {
                Serial = request.Serial,
                Slot = request.Slot,
                Draw = request.Draw,
                Accepted = accepted
            });
        }
        if (!context.TryWrite(frame.CapacityId, frame.CapacityGeneration, state))
            throw new InvalidOperationException("Shot capacity state disappeared during ordered apply.");
    }
}
