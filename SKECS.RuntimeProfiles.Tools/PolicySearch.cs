namespace SKECS.RuntimeProfiles.Tools;

internal sealed record ReserveBlock(int NativeBytes, int Count);

internal sealed record Policy(long TargetRetainedBytes, long StartupReserveBytes, ReserveBlock[] StartupBlocks,
    int InitialEntityCapacity, int MaxOversizePercent, int ReleaseBatchSize)
{
    internal static Policy Generic(long target) => new(target, 0, [], 0, 25, 4);

    internal ChurnProfileSettings ToSettings() => new(TargetRetainedBytes, StartupReserveBytes,
        StartupBlocks.Select(block => new ChurnStartupBlock(block.NativeBytes, block.Count)).ToArray(),
        InitialEntityCapacity, MaxOversizePercent, ReleaseBatchSize);
}

internal readonly record struct Prediction(long PeakRetained, long MeasuredAllocations, long EarlyAllocations,
    long MeasuredReleases, long StartupBytes)
{
    internal bool Dominates(Prediction other) =>
        PeakRetained <= other.PeakRetained && MeasuredAllocations <= other.MeasuredAllocations &&
        EarlyAllocations <= other.EarlyAllocations && MeasuredReleases <= other.MeasuredReleases &&
        StartupBytes <= other.StartupBytes && this != other;
}

internal sealed record Candidate(string Id, Policy Policy, Prediction Estimated);
internal sealed record ScenarioTrace(Scenario Scenario, ProfilerTrace Trace);

internal static class PolicySearch
{
    internal static Candidate[] Propose(ScenarioTrace[] training, long? limit)
    {
        var minimum = training.Max(entry => entry.Trace.LowerBound.Total);
        if (limit < minimum)
        {
            var conflict = training.First(entry => entry.Trace.LowerBound.Total > limit);
            var peak = conflict.Trace.LowerBound;
            throw new InvalidDataException($"Memory limit {limit} is below {conflict.Scenario.Id} frame {peak.Frame}: " +
                $"{peak.Total} bytes (native {peak.OccupiedNative}, pinned {peak.OccupiedPinned}, " +
                $"entity records {peak.EntityRecords}, free IDs {peak.FreeIds}, accessors {peak.Accessors}).");
        }

        var maximum = Math.Min(training.Max(entry => entry.Trace.PeakRetained), limit ?? long.MaxValue);
        var churnPeaks = training.Where(entry => !Corpus.IsDense(entry.Scenario)).Select(entry => entry.Trace.LowerBound.Total).ToArray();
        var headroom = Math.Min((maximum - minimum) / 4, Math.Max(0, churnPeaks.Max() - Median(churnPeaks)));
        var pinned = 8L * training[0].Trace.ChunkCapacity;
        var reserve = StartupDemand(training);
        var reserveBytes = reserve.Sum(block => checked((block.NativeBytes + pinned) * block.Count));
        var entityCapacity = training.Min(entry => entry.Trace.EarlyEntityCapacity);
        var grid = new List<Candidate>();
        for (var step = 0; step <= 4; step++)
        {
            var target = maximum - (long)((decimal)(maximum - minimum) * (4 - step) / 4);
            target += Math.Min(headroom, maximum - target);
            if (grid.Any(candidate => candidate.Policy.TargetRetainedBytes == target)) continue;
            Add(Policy.Generic(target));
            Add(Policy.Generic(target) with { MaxOversizePercent = 0, ReleaseBatchSize = 16 });
            var startupTableBytes = checked(training.Max(entry => entry.Trace.TableBytesPerEntity) * entityCapacity);
            if (reserveBytes > 0 && reserveBytes <= target - startupTableBytes)
                Add(new Policy(target, reserveBytes, reserve, entityCapacity, 25, 4));
        }

        var frontier = grid.Where(candidate => !grid.Any(other => other.Estimated.Dominates(candidate.Estimated)))
            .GroupBy(candidate => candidate.Estimated).Select(group => group.First())
            .OrderBy(candidate => candidate.Policy.TargetRetainedBytes)
            .ThenBy(candidate => candidate.Estimated.MeasuredAllocations).ToArray();
        // Keep the ends of the estimated frontier and the points with significant early reuse within a bounded run budget.
        if (frontier.Length <= 5) return frontier;
        return new[] { frontier[0], frontier[frontier.Length / 2], frontier[^1],
            frontier.MinBy(candidate => candidate.Estimated.MeasuredAllocations)!,
            frontier.MinBy(candidate => candidate.Estimated.EarlyAllocations)! }.Distinct().ToArray();

        void Add(Policy policy)
        {
            var results = training.Select(entry => Simulate(entry, policy)).ToArray();
            var prediction = new Prediction(results.Max(result => result.PeakRetained),
                results.Sum(result => result.MeasuredAllocations), results.Sum(result => result.EarlyAllocations),
                results.Sum(result => result.MeasuredReleases), results.Max(result => result.StartupBytes));
            if (prediction.PeakRetained <= (limit ?? long.MaxValue))
                grid.Add(new Candidate($"candidate-{grid.Count + 1:D2}", policy, prediction));
        }
    }

    internal static Prediction Simulate(ScenarioTrace entry, Policy policy)
    {
        if (entry.Trace.Header.SchemaVersion != 1) throw new InvalidDataException("Policy simulation requires a baseline trace.");
        var occupied = new Dictionary<TraceSlot, int>();
        var available = new LinkedList<EmptyBacking>();
        var bySize = new SortedDictionary<int, LinkedList<EmptyBacking>>();
        var byArchetype = new Dictionary<int, LinkedList<EmptyBacking>>();
        var pinned = 8L * entry.Trace.ChunkCapacity;
        long retained = 0, measuredAllocations = 0, earlyAllocations = 0, measuredReleases = 0;
        var reservedTables = checked(policy.InitialEntityCapacity * entry.Trace.TableBytesPerEntity);
        var tables = reservedTables;
        foreach (var seed in policy.StartupBlocks)
        {
            retained = checked(retained + (seed.NativeBytes + pinned) * seed.Count);
            for (var i = 0; i < seed.Count; i++) Return(seed.NativeBytes, -1);
        }
        var startup = retained + tables;
        var peak = startup;
        foreach (var change in entry.Trace.Changes)
        {
            switch (change.Action)
            {
                case TraceAction.Occupy:
                    var requested = checked((int)change.Bytes);
                    var rented = Rent(requested, change.Slot.Archetype);
                    if (rented == null)
                    {
                        retained = checked(retained + requested + pinned);
                        if (change.Frame >= entry.Scenario.WarmupFrames) measuredAllocations++;
                        if (change.Frame <= 0) earlyAllocations++;
                    }
                    occupied.Add(change.Slot, rented ?? requested);
                    break;
                case TraceAction.Empty:
                    Return(occupied[change.Slot], change.Slot.Archetype);
                    occupied.Remove(change.Slot);
                    break;
                case TraceAction.Tables:
                    // Floor for preallocated tables. A real run measures the actual table growth and retention.
                    tables = Math.Max(change.Bytes, reservedTables);
                    break;
                case TraceAction.Trim:
                    for (var batch = 0; batch < policy.ReleaseBatchSize &&
                        retained + tables > policy.TargetRetainedBytes && available.First?.Value is { } oldest; batch++)
                    {
                        retained -= Unlink(oldest) + pinned;
                        if (change.Frame >= entry.Scenario.WarmupFrames) measuredReleases++;
                    }
                    break;
            }
            peak = Math.Max(peak, checked(retained + tables));
        }
        return new(peak, measuredAllocations, earlyAllocations, measuredReleases, startup);

        void Return(int size, int archetype)
        {
            if (!bySize.TryGetValue(size, out var sizes)) bySize[size] = sizes = new();
            var backing = new EmptyBacking(size);
            backing.Age = available.AddLast(backing);
            backing.SizeClass = sizes.AddLast(backing);
            if (archetype >= 0)
            {
                if (!byArchetype.TryGetValue(archetype, out var items)) byArchetype[archetype] = items = new();
                backing.Local = items.AddLast(backing);
            }
        }

        int? Rent(int requested, int archetype)
        {
            if (byArchetype.TryGetValue(archetype, out var local) && local.Last is { } last)
                return Unlink(last.Value);
            var largest = requested + (long)requested * policy.MaxOversizePercent / 100;
            foreach (var pair in bySize)
            {
                if (pair.Key > largest) break;
                if (pair.Key >= requested && pair.Value.Last is { } match) return Unlink(match.Value);
            }
            return null;
        }

        int Unlink(EmptyBacking backing)
        {
            backing.SizeClass.List!.Remove(backing.SizeClass);
            if (backing.Local is { } local) local.List!.Remove(local);
            available.Remove(backing.Age);
            return backing.Size;
        }
    }

    private sealed class EmptyBacking(int size)
    {
        internal int Size { get; } = size;
        internal LinkedListNode<EmptyBacking> Age { get; set; } = null!;
        internal LinkedListNode<EmptyBacking> SizeClass { get; set; } = null!;
        internal LinkedListNode<EmptyBacking>? Local { get; set; }
    }

    private static ReserveBlock[] StartupDemand(IEnumerable<ScenarioTrace> traces)
    {
        var demand = new SortedDictionary<int, int>();
        foreach (var entry in traces)
        {
            var current = new Dictionary<int, int>();
            foreach (var change in entry.Trace.Changes.TakeWhile(change => change.Frame <= 0))
            {
                if (change.Action is not (TraceAction.Occupy or TraceAction.Empty)) continue;
                var size = checked((int)change.Bytes);
                var count = current.GetValueOrDefault(size) + (change.Action == TraceAction.Occupy ? 1 : -1);
                current[size] = count;
                demand[size] = Math.Max(demand.GetValueOrDefault(size), count);
            }
        }
        return demand.Select(pair => new ReserveBlock(pair.Key, pair.Value)).ToArray();
    }

    private static long Median(IEnumerable<long> source)
    {
        var values = source.Order().ToArray();
        return values[(values.Length - 1) / 2];
    }
}
