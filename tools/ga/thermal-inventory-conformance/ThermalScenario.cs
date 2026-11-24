#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SKECS;
using SKECS.GA;
using SKECS.Numerics;
using InventoryItems = SKECS.GA.FixedArray16<global::InventoryItem>;

internal readonly struct ThermalCase
{
    internal ThermalCase(int rows, uint seed, int chunk, int budget, int workers) =>
        (Rows, Seed, Chunk, Budget, Workers) = (rows, seed, chunk, budget, workers);

    internal int Rows { get; }
    internal uint Seed { get; }
    internal int Chunk { get; }
    internal int Budget { get; }
    internal int Workers { get; }

    public override string ToString() => $"rows={Rows}, seed={Seed}, chunk={Chunk}, budget={Budget}, workers={Workers}";
}

internal readonly struct ThermalCoverage
{
    internal ThermalCoverage(long rows, long transfers, long overheated, long heatSpent, long docked,
        long kits, long enabledKits, long heatRefusals, int slots, long deviceRows, long deviceBatches)
    {
        (Rows, Transfers, Overheated, HeatSpent, Docked) = (rows, transfers, overheated, heatSpent, docked);
        (Kits, EnabledKits, HeatRefusals, Slots) = (kits, enabledKits, heatRefusals, slots);
        (DeviceRows, DeviceBatches) = (deviceRows, deviceBatches);
    }

    internal long Rows { get; }
    internal long Transfers { get; }
    internal long Overheated { get; }
    internal long HeatSpent { get; }
    internal long Docked { get; }
    internal long Kits { get; }
    internal long EnabledKits { get; }
    internal long HeatRefusals { get; }
    internal int Slots { get; }
    internal long DeviceRows { get; }
    internal long DeviceBatches { get; }

    public static ThermalCoverage operator +(ThermalCoverage left, ThermalCoverage right) => new(
        left.Rows + right.Rows, left.Transfers + right.Transfers, left.Overheated + right.Overheated,
        left.HeatSpent + right.HeatSpent, left.Docked + right.Docked, left.Kits + right.Kits,
        left.EnabledKits + right.EnabledKits, left.HeatRefusals + right.HeatRefusals, left.Slots | right.Slots,
        left.DeviceRows + right.DeviceRows, left.DeviceBatches + right.DeviceBatches);
}

internal static class ThermalScenario
{
    private const long Unit = 4_294_967_296L;
    private const uint StampSeed = 2166136261u;
    private static readonly ThermalContribution CounterSeed = new() { Stamp = StampSeed };

    internal static (ThermalCase[] Cases, int Ticks) SelectCases(string profile, bool singleWorker)
    {
        int[] rows = { 0, 1, 63, 64, 65, 127, 128, 129, 1024 };
        uint[] seeds = { 0, 1, 27, 7331, uint.MaxValue };
        int[] chunks = { 1, 64, 127, 256 };
        int[] budgets = { 1, 3, 64 };
        int[] workers = singleWorker ? new[] { 1 } : new[] { 1, 2, Environment.ProcessorCount };
        if (profile is "short" or "long")
            return (new[] { new ThermalCase(65, 27, 127, 64, singleWorker ? 1 : 2) }, profile == "short" ? 1_000 : 100_000);
        if (profile is not ("smoke" or "matrix"))
            throw new ArgumentException($"Unknown ThermalInventory profile '{profile}'.");
        var scenarios = new List<ThermalCase>();
        foreach (int rowCount in rows)
        {
            int firstSeed = profile == "matrix" ? 0 : scenarios.Count % seeds.Length;
            int lastSeed = profile == "matrix" ? seeds.Length : firstSeed + 1;
            for (int seed = firstSeed; seed < lastSeed; seed++)
            {
                int i = scenarios.Count;
                scenarios.Add(new ThermalCase(rowCount, seeds[seed], chunks[i % chunks.Length],
                    budgets[i % budgets.Length], workers[i % workers.Length]));
            }
        }
        return (scenarios.ToArray(), 16);
    }

    internal static async ValueTask<ThermalCoverage> Run(ThermalCase scenario, int ticks, bool useGpu, bool requireYield,
        Action<DeterministicWorkload<ThermalFrame>, DeterministicReduction<ThermalContribution>, Action<GaBatchTimings>> register,
        Func<DeterministicRunner<ThermalFrame>, int, ValueTask<bool>> drain, Action<string> report)
    {
        using var world = new World(new WorldOptions(scenario.Chunk));
        var rows = Seed(world, scenario);
        var workload = new DeterministicWorkload<ThermalFrame>();
        var counters = workload.AddReduction(CounterSeed, MergeCounters);
        long deviceRows = 0;
        long deviceBatches = 0;
        register(workload, counters, batch => { deviceRows += batch.Rows; deviceBatches++; });

        using var runner = new DeterministicRunner<ThermalFrame>(world, workload, scenario.Workers);
        var coverage = default(ThermalCoverage);
        for (int tick = 1; tick <= ticks; tick++)
        {
            var raw = RawFrame.At(tick, scenario.Seed);
            ApplyExternalEdits(world, rows, raw, tick);
            var input = raw.CreateInput();
            runner.BeginTick(tick, in input, Fixed.One);
            // The caller may reuse its input storage while the runner owns its copied frame.
            input = default;
            bool waited = await drain(runner, scenario.Budget);
            if (requireYield && useGpu && scenario.Rows != 0 && !waited)
                throw new InvalidDataException($"GPU work did not yield at {scenario}, tick {tick}.");
            if (useGpu && deviceRows != (long)scenario.Rows * tick)
                throw new InvalidDataException($"Wrong actual device row count at {scenario}, tick {tick}.");
            coverage += VerifyTick(world, rows, raw, scenario, tick, runner.LastCompleted!, counters);
            if (ticks >= 1_000 && tick % 1_000 == 0)
                report($"progress tick={tick}/{ticks} deviceRows={deviceRows} deviceBatches={deviceBatches}");
        }
        return coverage + new ThermalCoverage(0, 0, 0, 0, 0, 0, 0, 0, 0, deviceRows, deviceBatches);
    }

    internal static void VerifyCoverage(ThermalCoverage coverage, bool useGpu)
    {
        if (coverage.Rows <= 0 || coverage.Transfers <= 0 || coverage.Overheated <= 0 || coverage.HeatRefusals <= 0 ||
            coverage.Docked <= 0 || coverage.Docked >= coverage.Rows || coverage.EnabledKits <= 0 ||
            coverage.EnabledKits >= coverage.Kits || coverage.Kits >= coverage.Rows || coverage.Slots != 0xffff)
            throw new InvalidDataException("The corpus missed a required transfer, optional component, or inventory slot.");
        if (useGpu ? coverage.DeviceRows != coverage.Rows || coverage.DeviceBatches <= 0 :
            coverage.DeviceRows != 0 || coverage.DeviceBatches != 0)
            throw new InvalidDataException("Actual device work did not match expected routes.");
    }

    private static ThermalContribution MergeCounters(ThermalContribution sum, ThermalContribution leaf) => new()
    {
        Transfers = checked(sum.Transfers + leaf.Transfers), Overheated = checked(sum.Overheated + leaf.Overheated),
        HeatSpent = sum.HeatSpent + leaf.HeatSpent, Stamp = unchecked((sum.Stamp * 16777619u) ^ leaf.Stamp)
    };

    private static ModelRow[] Seed(World world, ThermalCase scenario)
    {
        int salt = (int)(scenario.Seed & 255);
        var rows = new ModelRow[scenario.Rows];
        for (int row = 0; row < rows.Length; row++)
        {
            int serial = rows.Length - row - 1;
            var storage = new ThermalInventory { Serial = serial, LastSlot = -1 };
            var items = new Item[16];
            for (int slot = 0; slot < items.Length; slot++)
            {
                items[slot] = new Item((5 * row + 3 * slot + salt) % 6, 0x01111111L + (slot % 5) * 0x09000000L + row % 7,
                    (row + slot + salt) % 5 != 0);
                InventoryItems.At(ref storage.State.Items, slot) = items[slot].ToComponent();
            }
            long heat = ((row + salt + 9) % 22) * Unit + 0x07ffffff;
            long shield = ((row + salt) % 12) * Unit + 0x0ffffff;
            var entity = world.CreateEntity(storage, new Heat { Temperature = Fixed.FromRaw(heat) },
                new Shield { Charge = Fixed.FromRaw(shield) });
            if (entity.Id != row || entity.Version != 1)
                throw new InvalidDataException("Thermal fixture entity allocation differed.");
            bool docked = (row + salt) % 3 != 1;
            bool hasKit = (row + salt) % 3 != 2;
            var kit = new CoolingKit { Bonus = Fixed.FromRaw(0x20000000L + row % 11), Enabled = (row + salt) % 2 == 0 };
            if (docked) world.Add(entity, new Docked());
            if (hasKit) world.Add(entity, kit);
            rows[row] = new ModelRow(entity, serial, items, heat, shield, docked, hasKit, kit);
        }
        return rows;
    }

    private static void ApplyExternalEdits(World world, ModelRow[] rows, RawFrame frame, int tick)
    {
        int cycle = (tick - 1) % 16;
        for (int row = 0; row < rows.Length; row++)
        {
            var model = rows[row];
            if (cycle == 4 && row % 3 == 0)
            {
                model.Docked = !model.Docked;
                if (model.Docked) world.Add(model.Entity, new Docked());
                else if (!world.Remove<Docked>(model.Entity)) throw new InvalidDataException("Could not remove dock tag.");
            }
            if (cycle == 6 && row % 5 == 0)
            {
                model.HasKit = !model.HasKit;
                if (model.HasKit) world.Add(model.Entity, model.Kit);
                else if (!world.Remove<CoolingKit>(model.Entity)) throw new InvalidDataException("Could not remove cooling kit.");
            }
            if (cycle == 0 && tick > 1)
            {
                int slot = (model.Serial + frame.SlotShift) % 16;
                ref var item = ref model.Items[slot];
                item.Units = 2 + row % 5;
                item.Credit = 0x01111111L;
                InventoryItems.At(ref world.Get<ThermalInventory>(model.Entity).State.Items, slot) = item.ToComponent();
            }
        }
    }

    private static ThermalCoverage VerifyTick(World world, ModelRow[] rows, RawFrame frame, ThermalCase scenario, int tick,
        DeterministicTickResult result, DeterministicReduction<ThermalContribution> counters)
    {
        IReadOnlyList<DeterministicEvent> events = result.Events;
        int nextEvent = 0;
        int transfers = 0;
        int overheated = 0;
        int docked = 0;
        int kits = 0;
        int enabledKits = 0;
        int heatRefusals = 0;
        int slots = 0;
        long spent = 0;
        uint stamp = StampSeed;
        if (result.TickIndex != tick)
            throw new InvalidDataException($"Wrong completed tick {result.TickIndex}.");
        for (int row = 0; row < rows.Length; row++)
        {
            var model = rows[row];
            int slot = (model.Serial + frame.SlotShift) % 16;
            slots |= 1 << slot;
            long cooling = frame.Ambient + (model.HasKit && model.Kit.Enabled ? model.Kit.Bonus.Raw : 0);
            model.Heat = Math.Max(0, model.Heat - cooling);
            model.Shield = model.Docked ? Math.Min(frame.ShieldLimit, model.Shield + cooling) : Math.Max(0, model.Shield - frame.ShieldDrain);
            ref var item = ref model.Items[slot];
            long charge = item.Credit + frame.TransferHeat;
            bool eligible = model.Docked && item.Enabled && item.Units > 0;
            bool accepted = eligible && charge <= frame.HeatLimit - model.Heat;
            if (accepted)
            {
                item.Units--;
                item.Credit += frame.CreditStep;
                model.Heat += charge;
                spent += charge;
                transfers++;
            }
            if (eligible && !accepted) heatRefusals++;
            bool hot = model.Heat >= frame.HeatLimit;
            if (hot) overheated++;
            if (model.Docked) docked++;
            if (model.HasKit) kits++;
            if (model.HasKit && model.Kit.Enabled) enabledKits++;
            uint leafStamp = ((uint)model.Serial * 256) | (uint)slot;
            if (model.Docked) leafStamp |= 0x10000000u;
            stamp = (uint)(((ulong)stamp * 16777619u ^ leafStamp) & uint.MaxValue);

            var report = ReadEvent<ThermalReport>(events, ref nextEvent, tick, model.Entity, 0);
            if (report.Serial != model.Serial || report.Slot != slot || report.Docked != model.Docked ||
                report.HasKit != model.HasKit || report.Overheated != hot || report.Temperature.Raw != model.Heat ||
                report.Shield.Raw != model.Shield)
                throw new InvalidDataException($"Thermal report diverged at {scenario}, tick {tick}, row {row}, slot {slot}.");
            if (accepted)
            {
                var transfer = ReadEvent<InventoryTransfer>(events, ref nextEvent, tick, model.Entity, 2);
                if (transfer.Serial != model.Serial || transfer.Slot != slot || transfer.Remaining != item.Units ||
                    transfer.HeatSpent.Raw != charge)
                    throw new InvalidDataException($"Inventory transfer diverged at {scenario}, tick {tick}, row {row}, slot {slot}.");
            }
            var actual = world.Get<ThermalInventory>(model.Entity);
            if (actual.Serial != model.Serial || actual.LastSlot != slot ||
                world.Get<Heat>(model.Entity).Temperature.Raw != model.Heat || world.Get<Shield>(model.Entity).Charge.Raw != model.Shield)
                throw new InvalidDataException($"Own state diverged at {scenario}, tick {tick}, row {row}, slot {slot}.");
            for (int index = 0; index < model.Items.Length; index++)
            {
                var observed = InventoryItems.Read(in actual.State.Items, index);
                var expected = model.Items[index];
                if (observed.Units != expected.Units || observed.Credit.Raw != expected.Credit || observed.Enabled != expected.Enabled)
                    throw new InvalidDataException($"Inline inventory slot {index} diverged at {scenario}, tick {tick}, row {row}.");
            }
        }
        if (nextEvent != events.Count)
            throw new InvalidDataException($"Unexpected public event at {scenario}, tick {tick}, index {nextEvent}.");
        var reduction = ReadCounters(result, counters);
        if (reduction.Transfers != transfers || reduction.Overheated != overheated || reduction.HeatSpent.Raw != spent ||
            reduction.Stamp != stamp)
            throw new InvalidDataException($"Typed counter diverged at {scenario}, tick {tick}: " +
                $"expected ({transfers},{overheated},{spent},{stamp}), got " +
                $"({reduction.Transfers},{reduction.Overheated},{reduction.HeatSpent.Raw},{reduction.Stamp}).");
        return new ThermalCoverage(rows.Length, transfers, overheated, spent, docked, kits, enabledKits, heatRefusals, slots, 0, 0);
    }

    private static ThermalContribution ReadCounters(DeterministicTickResult result, DeterministicReduction<ThermalContribution> counters)
        => result.GetReduction(counters);

    private static T ReadEvent<T>(IReadOnlyList<DeterministicEvent> events, ref int index, int tick, Entity entity, int ordinal)
        where T : unmanaged
    {
        if (index >= events.Count)
            throw new InvalidDataException($"Missing {typeof(T).Name} at tick {tick}, source {entity.Id}, ordinal {ordinal}.");
        var item = events[index++];
        if (item.Key is not { Stage: SystemStage.Update, SystemOrder: 0, ProducerOrder: 0, SourceKind: DeterministicSourceKind.Entity,
                DomainKey: null } || item.Key.TickIndex != tick || item.Key.SourceId != entity.Id ||
            item.Key.SourceGeneration != entity.Version || item.Key.LocalOrdinal != ordinal || item.PayloadType != typeof(T))
            throw new InvalidDataException($"Unexpected {item.PayloadType.Name} or key {item.Key} at tick {tick}, " +
                $"source {entity.Id}, ordinal {ordinal}; expected {typeof(T).Name}.");
        return item.Get<T>();
    }

    private readonly struct RawFrame
    {
        private RawFrame(int slotShift, long ambient, long transferHeat, long heatLimit, long shieldLimit, long shieldDrain, long creditStep)
        {
            (SlotShift, Ambient, TransferHeat, HeatLimit) = (slotShift, ambient, transferHeat, heatLimit);
            (ShieldLimit, ShieldDrain, CreditStep) = (shieldLimit, shieldDrain, creditStep);
        }

        public int SlotShift { get; }
        public long Ambient { get; }
        public long TransferHeat { get; }
        public long HeatLimit { get; }
        public long ShieldLimit { get; }
        public long ShieldDrain { get; }
        public long CreditStep { get; }

        public static RawFrame At(int tick, uint seed) => new((tick - 1) % 16,
            0x18000000L + ((tick + seed % 7) % 6) * 0x10000000L, Unit + (tick % 5) * 0x20000000L,
            (5 + (tick % 8)) * Unit + 0x04aaaaaaL, 10 * Unit + 0x21000000L,
            0x90000000L + (tick % 4) * 0x10000000L, 0x02777777L);

        public ThermalFrame CreateInput() => new()
        {
            SlotShift = SlotShift, AmbientCooling = Fixed.FromRaw(Ambient), TransferHeat = Fixed.FromRaw(TransferHeat),
            HeatLimit = Fixed.FromRaw(HeatLimit), ShieldLimit = Fixed.FromRaw(ShieldLimit), ShieldDrain = Fixed.FromRaw(ShieldDrain),
            CreditStep = Fixed.FromRaw(CreditStep)
        };
    }

    private struct Item
    {
        public Item(int units, long credit, bool enabled) => (Units, Credit, Enabled) = (units, credit, enabled);
        public int Units;
        public long Credit;
        public bool Enabled;

        public InventoryItem ToComponent() => new() { Units = Units, Credit = Fixed.FromRaw(Credit), Enabled = Enabled };
    }

    private sealed class ModelRow
    {
        public ModelRow(Entity entity, int serial, Item[] items, long heat, long shield, bool docked, bool hasKit, CoolingKit kit)
        {
            (Entity, Serial, Items, Kit) = (entity, serial, items, kit);
            (Heat, Shield, Docked, HasKit) = (heat, shield, docked, hasKit);
        }

        public Entity Entity { get; }
        public int Serial { get; }
        public Item[] Items { get; }
        public long Heat;
        public long Shield;
        public bool Docked;
        public bool HasKit;
        public CoolingKit Kit { get; }
    }
}
