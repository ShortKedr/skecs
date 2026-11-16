using System.Diagnostics;
using SKECS;
using SKECS.GA;
using SKECS.Numerics;

internal readonly record struct Scenario(int Rows, uint Seed, int Chunk, int Budget, int Workers);

internal readonly record struct Coverage(long Rows, long Transfers, long Overheated, long HeatSpent, long Docked,
    long Kits, long EnabledKits, long HeatRefusals, int Slots, long DeviceRows, long DeviceBatches)
{
    public static Coverage operator +(Coverage left, Coverage right) => new(
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

#if GA_BROWSER_CONFORMANCE
    public static async Task<Coverage> Run(Scenario scenario, int ticks, GaStartupResult? startup, GaContractIdentity contract)
#else
    public static Coverage Run(Scenario scenario, int ticks, GaStartupResult? startup, GaContractIdentity contract)
#endif
    {
        using var world = new World(new WorldOptions(scenario.Chunk));
        var rows = Seed(world, scenario);
        var workload = new DeterministicWorkload<ThermalFrame>();
        var counters = workload.AddReduction(CounterSeed, MergeCounters);
        long deviceRows = 0;
        long deviceBatches = 0;
        if (startup is null)
            ThermalAdapter.Register(workload, SystemStage.Update, counters);
        else
            workload.AddBatchSystem(SystemStage.Update, ThermalAdapterRowCodec.CreateQuery(),
                new GaBatchProcessor<ThermalFrame>(startup, contract.SystemId, contract.SourceDigest, contract.IrDigest,
                    contract.ComponentSchemaDigest, ThermalAdapterRowCodec.WordsPerRow, ThermalAdapterRowCodec.Pack<ThermalFrame>,
                    ThermalAdapterRowCodec.Apply<ThermalFrame>, contract.EventCapacity,
                    batch => { deviceRows += batch.Rows; deviceBatches++; }, null, false, [], [],
                    ThermalAdapterRowCodec.TickResource, null, ThermalAdapterRowCodec.CreateEventTypes(counters)));

        using var runner = new DeterministicRunner<ThermalFrame>(world, workload, scenario.Workers);
        var coverage = default(Coverage);
        for (int tick = 1; tick <= ticks; tick++)
        {
            var raw = RawFrame.At(tick, scenario.Seed);
            ApplyExternalEdits(world, rows, raw, tick);
            var input = raw.CreateInput();
            runner.BeginTick(tick, in input, Fixed.One);
            // The caller may reuse its input storage while the runner owns its copied frame.
            input = default;
#if GA_BROWSER_CONFORMANCE
            bool waitedForReadback = await Drain(runner, scenario.Budget);
            if (startup is not null && scenario.Rows != 0 && !waitedForReadback)
                throw new InvalidDataException($"WebGPU did not yield at {scenario}, tick {tick}.");
#else
            Drain(runner, scenario.Budget);
#endif
            if (startup is not null && deviceRows != (long)scenario.Rows * tick)
                throw new InvalidDataException($"Wrong actual device row count at {scenario}, tick {tick}.");
            coverage += VerifyTick(world, rows, raw, scenario, tick, runner.LastCompleted!, counters);
            if (ticks >= 1_000 && tick % 1_000 == 0)
                Console.WriteLine($"progress tick={tick}/{ticks} deviceRows={deviceRows} deviceBatches={deviceBatches}");
        }
        return coverage with { DeviceRows = deviceRows, DeviceBatches = deviceBatches };
    }

    private static ThermalContribution MergeCounters(ThermalContribution sum, ThermalContribution leaf) => new()
    {
        Transfers = checked(sum.Transfers + leaf.Transfers), Overheated = checked(sum.Overheated + leaf.Overheated),
        HeatSpent = sum.HeatSpent + leaf.HeatSpent, Stamp = unchecked((sum.Stamp * 16777619u) ^ leaf.Stamp)
    };

    private static ModelRow[] Seed(World world, Scenario scenario)
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
                storage.State.Items[slot] = items[slot].ToComponent();
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
                item = item with { Units = 2 + row % 5, Credit = 0x01111111L };
                world.Get<ThermalInventory>(model.Entity).State.Items[slot] = item.ToComponent();
            }
        }
    }

    private static Coverage VerifyTick(World world, ModelRow[] rows, RawFrame frame, Scenario scenario, int tick,
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
                item = item with { Units = item.Units - 1, Credit = item.Credit + frame.CreditStep };
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
                var observed = actual.State.Items[index];
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
        return new Coverage(rows.Length, transfers, overheated, spent, docked, kits, enabledKits, heatRefusals, slots, 0, 0);
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

#if GA_BROWSER_CONFORMANCE
    private static async Task<bool> Drain(DeterministicRunner<ThermalFrame> runner, int budget)
    {
        var timer = Stopwatch.StartNew();
        bool pending = false;
        var status = runner.Advance(budget);
        while (status == DeterministicTickStatus.Pending && timer.Elapsed < TimeSpan.FromSeconds(20))
        {
            pending = true;
            await Task.Delay(1);
            status = runner.Advance(budget);
        }
        if (status == DeterministicTickStatus.Faulted)
            throw new InvalidOperationException("ThermalInventory tick faulted.", runner.Fault);
        if (status != DeterministicTickStatus.Completed)
            throw new TimeoutException("ThermalInventory stayed pending for 20 seconds.");
        return pending;
    }
#else
    private static void Drain(DeterministicRunner<ThermalFrame> runner, int budget)
    {
        long started = Stopwatch.GetTimestamp();
        var spin = new SpinWait();
        while (true)
        {
            var status = runner.Advance(budget);
            if (status == DeterministicTickStatus.Completed)
                return;
            if (status == DeterministicTickStatus.Faulted)
                throw new InvalidOperationException("ThermalInventory tick faulted.", runner.Fault);
            double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (elapsed >= 20_000)
                throw new TimeoutException("ThermalInventory stayed pending for 20 seconds.");
            if (elapsed < 2)
                spin.SpinOnce(sleep1Threshold: -1);
            else
                Thread.Sleep(1);
        }
    }
#endif

    private readonly record struct RawFrame(int SlotShift, long Ambient, long TransferHeat, long HeatLimit,
        long ShieldLimit, long ShieldDrain, long CreditStep)
    {
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

    private readonly record struct Item(int Units, long Credit, bool Enabled)
    {
        public InventoryItem ToComponent() => new() { Units = Units, Credit = Fixed.FromRaw(Credit), Enabled = Enabled };
    }

    private sealed class ModelRow(Entity entity, int serial, Item[] items, long heat, long shield, bool docked, bool hasKit, CoolingKit kit)
    {
        public Entity Entity { get; } = entity;
        public int Serial { get; } = serial;
        public Item[] Items { get; } = items;
        public long Heat = heat;
        public long Shield = shield;
        public bool Docked = docked;
        public bool HasKit = hasKit;
        public CoolingKit Kit { get; } = kit;
    }
}
