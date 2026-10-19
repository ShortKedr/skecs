using SpaceBattle;
using Battle = SpaceBattle.Simulations.Skecs.Battle;

internal static partial class Validation
{
    public static unsafe void ComponentPassChecks()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic") == "0")
            Require(!System.Numerics.Vector.IsHardwareAccelerated && !System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated,
                "forced scalar runtime gate is active");
        using var battle = new Battle(new BattleConfig { ShipsPerTeam = 8 });
        var world = battle.NativeWorld;
        var keys = new List<SKECS.Entity>();
        foreach (var size in new[] { 0, 1, 3, 7, 8, 9, 17, 65, 513, 4099 })
        {
            while (keys.Count < size)
            {
                var entity = world.CreateEntity(); var value = keys.Count;
                world.Set(entity, new PassA { Value = value }); world.Set(entity, new PassB { Value = value + 1 });
                world.Set(entity, new PassC { Value = value + 2 }); keys.Add(entity);
            }
            CompareComponentRows(world, size);
        }
        world.Set(keys[0], new PassTag()); world.Remove<PassTag>(keys[0]);
        world.Get<PassA>(keys[1]).Value = -81;
        var stale = keys[2]; world.DestroyEntity(stale);
        var replacement = world.CreateEntity();
        world.Set(replacement, new PassA { Value = 900 }); world.Set(replacement, new PassB { Value = 901 });
        world.Set(replacement, new PassC { Value = 902 }); keys[2] = replacement;
        Require(!world.IsAlive(stale) && replacement.Version != stale.Version, "native component pass preserves stale identity");
        CompareComponentRows(world, keys.Count);
        var throwing = new PassThrow();
        try { world.Query<PassA, PassB, PassC>().ForEach(ref throwing); throw new InvalidOperationException("Expected native query exception"); }
        catch (PassException) { }
        CompareComponentRows(world, keys.Count);
        var query = world.Query<PassA, PassB, PassC>(); long sum = 0;
        foreach (var chunk in query.Chunks())
            for (var row = 0; row < chunk.Count; row++) sum += chunk.Component1[row].Value + chunk.Component2[row].Value + chunk.Component3[row].Value;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var pass = 0; pass < 20; pass++)
            foreach (var chunk in query.Chunks())
                for (var row = 0; row < chunk.Count; row++) sum += chunk.Component1[row].Value + chunk.Component2[row].Value + chunk.Component3[row].Value;
        Require(GC.GetAllocatedBytesForCurrentThread() == before && sum != 0, "native warmed component chunks allocate nothing");
        Console.WriteLine("PASS: SKECS native component chunks match keyed queries for empty/tails/growth/reuse/ref writes and allocate nothing when warmed.");
    }

    private static unsafe void CompareComponentRows(SKECS.World world, int expected)
    {
        var keyed = new PassKeyed { Rows = new() }; var query = world.Query<PassA, PassB, PassC>();
        query.ForEach(ref keyed);
        var rows = new List<(int, int, int)>();
        foreach (var chunk in query.Chunks())
        {
            var a = new Span<PassA>(chunk.Component1, chunk.Count); var b = new Span<PassB>(chunk.Component2, chunk.Count);
            var c = new Span<PassC>(chunk.Component3, chunk.Count);
            for (var row = 0; row < chunk.Count; row++) { rows.Add((a[row].Value, b[row].Value, c[row].Value)); a[row].Value++; b[row].Value -= 2; c[row].Value += 3; }
        }
        Require(rows.Count == expected && keyed.Rows.SequenceEqual(rows), "native chunk/query order and values");
        var after = new PassKeyed { Rows = new() }; query.ForEach(ref after);
        for (var row = 0; row < after.Rows.Count; row++)
            Require(after.Rows[row] == (rows[row].Item1 + 1, rows[row].Item2 - 2, rows[row].Item3 + 3), "native chunk reference writes");
    }
    private struct PassA { public int Value; }
    private struct PassB { public int Value; }
    private struct PassC { public int Value; }
    private struct PassTag { }
    private struct PassKeyed : SKECS.IQueryProcessor<PassA, PassB, PassC>
    {
        public List<(int, int, int)> Rows;
        public void Execute(SKECS.Entity entity, ref PassA a, ref PassB b, ref PassC c) => Rows.Add((a.Value, b.Value, c.Value));
    }
    private sealed class PassException : Exception { }
    private struct PassThrow : SKECS.IQueryProcessor<PassA, PassB, PassC>
    { public void Execute(SKECS.Entity entity, ref PassA a, ref PassB b, ref PassC c) => throw new PassException(); }
}
