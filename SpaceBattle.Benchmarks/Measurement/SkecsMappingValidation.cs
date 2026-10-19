using SpaceBattle;
using Battle = SpaceBattle.Simulations.Skecs.Battle;

internal static partial class Validation
{
    public static void SkecsMappingChecks()
    {
        using var battle = new Battle(new BattleConfig { ShipsPerTeam = 8 });
        var expected = new Dictionary<EntityKey, int>(); var entities = new List<EntityKey>();
        for (var i = 0; i < 4101; i++)
        {
            var e = battle.Create(); battle.Set(e, new MappingValue { Value = i });
            battle.Set(e, new MappingOther { Value = 17 }); battle.Set(e, new MappingThird { Value = 23 });
            expected.Add(e, i); entities.Add(e);
            if (i is 2 or 599 or 4095) CheckSkecsNativeQueries(battle, expected);
        }
        battle.Set(entities[0], new MappingTag()); battle.Remove<MappingTag>(entities[0]);
        CheckSkecsNativeQueries(battle, expected);
        var current = entities[0];
        for (var i = 0; i < 256; i++)
        {
            var old = current; battle.Destroy(old); expected.Remove(old);
            Require(!battle.Alive(old) && !battle.Has<MappingValue>(old), "native stale key is rejected");
            current = battle.Create(); battle.Set(current, new MappingValue { Value = i });
            battle.Set(current, new MappingOther { Value = 17 }); battle.Set(current, new MappingThird { Value = 23 }); expected.Add(current, i);
            Require(current.Id == old.Id && current.Generation != old.Generation, "native identity reuse advances generation");
            Require(!battle.Alive(old) && battle.NativeWorld.IsAlive(new SKECS.Entity(current.Id, current.Generation)), "EntityKey directly represents native identity");
        }
        CheckSkecsNativeQueries(battle, expected);
        Require(battle.Diagnostics().AccessorBytes == 0, "native queries retain chunk storage without optional accessor buffers");
        Console.WriteLine("PASS: SKECS direct native key/query identity, component migration, chunk growth and generation reuse.");
    }
    private static void CheckSkecsNativeQueries(Battle battle, Dictionary<EntityKey, int> expected)
    {
        var one = new MappingOne { Expected = expected, Seen = new() }; battle.NativeWorld.Query<MappingValue>().ForEach(ref one);
        var two = new MappingTwo { Expected = expected, Seen = new() }; battle.NativeWorld.Query<MappingValue, MappingOther>().ForEach(ref two);
        var three = new MappingThree { Expected = expected, Seen = new() }; battle.NativeWorld.Query<MappingValue, MappingOther, MappingThird>().ForEach(ref three);
        Require(one.Seen.Count == expected.Count && two.Seen.Count == expected.Count && three.Seen.Count == expected.Count, "native one/two/three-column query membership");
    }
    private static void VisitNativeMapping(Dictionary<EntityKey, int> expected, HashSet<EntityKey> seen, SKECS.Entity native, ref MappingValue value)
    {
        var key = new EntityKey(native.Id, native.Version);
        Require(expected.TryGetValue(key, out var want) && want == value.Value && seen.Add(key), "native query exposes full live identity");
        value.Value++; expected[key] = value.Value;
    }
    private struct MappingValue { public int Value; }
    private struct MappingOther { public int Value; }
    private struct MappingThird { public int Value; }
    private struct MappingTag { }
    private struct MappingOne : SKECS.IQueryProcessor<MappingValue>
    {
        public Dictionary<EntityKey, int> Expected; public HashSet<EntityKey> Seen;
        public void Execute(SKECS.Entity native, ref MappingValue value) => VisitNativeMapping(Expected, Seen, native, ref value);
    }
    private struct MappingTwo : SKECS.IQueryProcessor<MappingValue, MappingOther>
    {
        public Dictionary<EntityKey, int> Expected; public HashSet<EntityKey> Seen;
        public void Execute(SKECS.Entity native, ref MappingValue value, ref MappingOther other)
        { Require(other.Value == 17, "native second component survived migration"); VisitNativeMapping(Expected, Seen, native, ref value); }
    }
    private struct MappingThree : SKECS.IQueryProcessor<MappingValue, MappingOther, MappingThird>
    {
        public Dictionary<EntityKey, int> Expected; public HashSet<EntityKey> Seen;
        public void Execute(SKECS.Entity native, ref MappingValue value, ref MappingOther other, ref MappingThird third)
        { Require(other.Value == 17 && third.Value == 23, "native query references survived migration"); VisitNativeMapping(Expected, Seen, native, ref value); }
    }
}
