using SKECS.GA;
using SKECS.Numerics;
using InventoryItems = SKECS.GA.FixedArray16<global::InventoryItem>;

public struct ThermalFrame
{
    public int SlotShift;
    public Fixed AmbientCooling;
    public Fixed TransferHeat;
    public Fixed HeatLimit;
    public Fixed ShieldLimit;
    public Fixed ShieldDrain;
    public Fixed CreditStep;
}

public struct InventoryItem
{
    public int Units;
    public Fixed Credit;
    public bool Enabled;
}

public struct InventoryState
{
    public InventoryItems Items;
}

public struct ThermalInventory
{
    public int Serial;
    public int LastSlot;
    public InventoryState State;
}

public struct Heat
{
    public Fixed Temperature;
}

public struct Shield
{
    public Fixed Charge;
}

public struct Docked { }

public struct CoolingKit
{
    public Fixed Bonus;
    public bool Enabled;
}

public struct ThermalReport
{
    public int Serial;
    public int Slot;
    public bool Docked;
    public bool HasKit;
    public bool Overheated;
    public Fixed Temperature;
    public Fixed Shield;
}

public struct InventoryTransfer
{
    public int Serial;
    public int Slot;
    public int Remaining;
    public Fixed HeatSpent;
}

public struct ThermalContribution
{
    public int Transfers;
    public int Overheated;
    public Fixed HeatSpent;
    public uint Stamp;
}

[SecsSystem(SystemId = "thermal-inventory")]
public partial struct ThermalKernel
{
    [GaTickResource(typeof(ThermalFrame))]
    [GaAccess(typeof(Heat), GaOwnAccess.ReadWrite)]
    [GaAccess(typeof(Shield), GaOwnAccess.ReadWrite)]
    [GaAccess(typeof(Docked), GaOwnAccess.OptionalRead)]
    [GaAccess(typeof(CoolingKit), GaOwnAccess.OptionalRead)]
    public void Execute(in SystemContext context, ref ThermalInventory inventory)
    {
        ref readonly var frame = ref context.Resource<ThermalFrame>();
        int slot = (inventory.Serial + frame.SlotShift) & 15;
        inventory.LastSlot = slot;
        bool docked = context.Has<Docked>();
        bool hasKit = context.TryRead<CoolingKit>(out var kit);
        Fixed cooling = frame.AmbientCooling;
        if (hasKit && kit.Enabled)
            cooling += kit.Bonus;

        Fixed zero = Fixed.FromRaw(0L);
        Fixed heat = context.Own<Heat>().Temperature;
        heat = heat > cooling ? heat - cooling : zero;
        Fixed shield = context.Own<Shield>().Charge;
        if (docked)
        {
            shield += cooling;
            if (shield > frame.ShieldLimit)
                shield = frame.ShieldLimit;
        }
        else
        {
            shield = shield > frame.ShieldDrain ? shield - frame.ShieldDrain : zero;
        }

        int units = InventoryItems.At(ref inventory.State.Items, slot).Units;
        bool accepted = false;
        Fixed spent = zero;
        if (docked && InventoryItems.At(ref inventory.State.Items, slot).Enabled && units > 0)
        {
            Fixed cost = InventoryItems.At(ref inventory.State.Items, slot).Credit + frame.TransferHeat;
            if (heat + cost <= frame.HeatLimit)
            {
                heat += cost;
                spent = cost;
                accepted = true;
                units--;
                InventoryItems.At(ref inventory.State.Items, slot).Units = units;
                InventoryItems.At(ref inventory.State.Items, slot).Credit += frame.CreditStep;
            }
        }
        context.Own<Heat>().Temperature = heat;
        context.Own<Shield>().Charge = shield;
        bool overheated = heat >= frame.HeatLimit;
        context.Emit(new ThermalReport
        {
            Serial = inventory.Serial, Slot = slot, Docked = docked, HasKit = hasKit,
            Overheated = overheated, Temperature = heat, Shield = shield
        });
        context.Contribute(new ThermalContribution
        {
            Transfers = accepted ? 1 : 0, Overheated = overheated ? 1 : 0, HeatSpent = spent,
            Stamp = ((uint)inventory.Serial << 8) ^ (uint)slot ^ (docked ? 0x10000000u : 0u)
        });
        if (accepted)
            context.Emit(new InventoryTransfer { Serial = inventory.Serial, Slot = slot, Remaining = units, HeatSpent = spent });
    }
}
