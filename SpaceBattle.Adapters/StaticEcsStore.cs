using System.Runtime.CompilerServices;
using FFS.Libraries.StaticEcs;
using NativeWorld = FFS.Libraries.StaticEcs.World<SpaceBattle.SpaceBattleStaticWorld>;

namespace SpaceBattle;

internal readonly struct SpaceBattleStaticWorld : IWorldType { }

public sealed class StaticEcsStore : NativeStore<EntityGID>
{
    private struct Wrapper<T> : IComponent, IComponentConfig<Wrapper<T>> where T : unmanaged { public T Value; public ComponentTypeConfig<Wrapper<T>> Config() => new(noDataLifecycle: true); }
    private static int active;
    private EntityKey[] reverse = Array.Empty<EntityKey>();

    // Additional unmanaged types can be registered here, before native Initialize.
    public sealed class ComponentRegistration
    {
        private bool open = true;
        public void Register<T>() where T : unmanaged
        {
            if (!open) throw new InvalidOperationException("StaticEcs registration has closed.");
            if (NativeWorld.IsComponentTypeRegistered<Wrapper<T>>()) return;
            NativeWorld.Types().Component<Wrapper<T>>();
        }
        internal void Close() => open = false;
    }

    public StaticEcsStore() : this(null) { }

    public StaticEcsStore(Action<ComponentRegistration>? registerAdditional)
    {
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
            throw new InvalidOperationException("Only one StaticEcsStore may be active; dispose it first.");
        bool created = false;
        var registration = new ComponentRegistration();
        try
        {
            NativeWorld.Create(WorldConfig.Default());
            created = true;
            registration.Register<Identity>();
            registration.Register<Transform>();
            registration.Register<Motion>();
            registration.Register<Hull>();
            registration.Register<Shield>();
            registration.Register<Heat>();
            registration.Register<Brain>();
            registration.Register<Weapon>();
            registration.Register<SecondaryWeapon>();
            registration.Register<MountedTurret>();
            registration.Register<Collider>();
            registration.Register<PhysicsLink>();
            registration.Register<CombatState>();
            registration.Register<Carrier>();
            registration.Register<Docked>();
            registration.Register<Overheated>();
            registration.Register<InCombat>();
            registration.Register<Boosting>();
            registration.Register<ShieldBroken>();
            registration.Register<Projectile>();
            registration.Register<Debris>();
            registration.Register<Particle>();
            registration.Register<Lifetime>();
            registration.Register<InventoryItem>();
            registration.Register<InventoryItems>();
            registration.Register<Inventory>();
            registerAdditional?.Invoke(registration);
            registration.Close();
            NativeWorld.Initialize();
        }
        catch
        {
            try
            {
                if (created)
                {
                    // Native Destroy requires an initialized world, even on registration failure.
                    if (!NativeWorld.IsWorldInitialized) NativeWorld.Initialize();
                    NativeWorld.Destroy();
                }
            }
            finally { Interlocked.Exchange(ref active, 0); }
            throw;
        }
        finally { registration.Close(); }
    }

    public override string Name => "StaticEcs 2.2.9";
    private void CheckType<T>() where T : unmanaged
    {
        CheckOpen();
        if (!NativeWorld.IsComponentTypeRegistered<Wrapper<T>>())
            throw new InvalidOperationException($"Register {typeof(T)} in the StaticEcsStore constructor before initialization.");
    }
    protected override EntityGID CreateNative()
    {
        var native = NativeWorld.NewEntity<Default>();
        if (native.ID < (uint)reverse.Length) reverse[native.ID] = default;
        return native.GID;
    }
    protected override bool AliveNative(EntityGID e) => e.TryUnpack<SpaceBattleStaticWorld>(out _);
    protected override void DestroyNative(EntityGID e)
    {
        var native = e.Unpack<SpaceBattleStaticWorld>();
        if (native.ID < (uint)reverse.Length) reverse[native.ID] = default;
        native.Destroy();
    }
    protected override void DisposeNative()
    {
        NativeWorld.Destroy();
        reverse = Array.Empty<EntityKey>();
        Interlocked.Exchange(ref active, 0);
    }
    public override ref T Get<T>(EntityKey e)
    {
        CheckType<T>();
        var native = Native(e).Unpack<SpaceBattleStaticWorld>();
        if (!native.Has<Wrapper<T>>()) throw new InvalidOperationException("Component is absent.");
        return ref native.Ref<Wrapper<T>>().Value;
    }
    public override bool Has<T>(EntityKey e)
    {
        if (!Alive(e)) return false;
        CheckType<T>();
        return Native(e).Unpack<SpaceBattleStaticWorld>().Has<Wrapper<T>>();
    }
    public override void Set<T>(EntityKey e, in T value)
    {
        CheckMutation();
        CheckType<T>();
        Native(e).Unpack<SpaceBattleStaticWorld>().Set(new Wrapper<T> { Value = value });
    }
    public override void Remove<T>(EntityKey e)
    {
        CheckMutation();
        CheckType<T>();
        if (Alive(e)) Native(e).Unpack<SpaceBattleStaticWorld>().Delete<Wrapper<T>>();
    }
    public override void ForEach<A, P>(ref P p)
    {
        CheckType<A>();
        BeginQuery();
        try
        {
            var bridge = new Bridge<A, P> { Processor = p, Store = this };
            NativeWorld.Query().Write<Wrapper<A>>().For(ref bridge);
            p = bridge.Processor;
        }
        finally { EndQuery(); }
    }
    public override void ForEach<A, B, P>(ref P p)
    {
        CheckType<A>(); CheckType<B>();
        BeginQuery();
        try
        {
            var bridge = new Bridge<A, B, P> { Processor = p, Store = this };
            NativeWorld.Query().Write<Wrapper<A>, Wrapper<B>>().For(ref bridge);
            p = bridge.Processor;
        }
        finally { EndQuery(); }
    }
    public override void ForEach<A, B, C, P>(ref P p)
    {
        CheckType<A>(); CheckType<B>(); CheckType<C>();
        BeginQuery();
        try
        {
            var bridge = new Bridge<A, B, C, P> { Processor = p, Store = this };
            NativeWorld.Query().Write<Wrapper<A>, Wrapper<B>, Wrapper<C>>().For(ref bridge);
            p = bridge.Processor;
        }
        finally { EndQuery(); }
    }
    public override void ForEachComponents<A, B, C, P>(ref P processor)
    {
        CheckType<A>(); CheckType<B>(); CheckType<C>();
        BeginQuery();
        try
        {
            var bridge = new ComponentsBridge<A, B, C, P> { Processor = processor };
            NativeWorld.Query().WriteBlock<Wrapper<A>, Wrapper<B>, Wrapper<C>>().For(ref bridge);
            processor = bridge.Processor;
        }
        finally { EndQuery(); }
    }
    private EntityKey QueryKey(NativeWorld.Entity native)
    {
        var id = native.ID;
        if (id < (uint)reverse.Length)
        {
            var cached = reverse[id];
            if (cached.Generation != 0) return cached;
        }
        var key = Key(native.GID);
        if (id >= (uint)reverse.Length)
            Array.Resize(ref reverse, (int)Math.Max(id + 1, Math.Max(64u, (uint)reverse.Length * 2)));
        reverse[id] = key;
        return key;
    }

    private struct Bridge<A, P> : NativeWorld.IQuery.Write<Wrapper<A>> where A : unmanaged where P : struct, IProcessor<A>
    {
        public P Processor; public StaticEcsStore Store;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Invoke(NativeWorld.Entity e, ref Wrapper<A> a) => Processor.Execute(Store.QueryKey(e), ref a.Value);
    }
    private struct Bridge<A, B, P> : NativeWorld.IQuery.Write<Wrapper<A>, Wrapper<B>> where A : unmanaged where B : unmanaged where P : struct, IProcessor<A, B>
    {
        public P Processor; public StaticEcsStore Store;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Invoke(NativeWorld.Entity e, ref Wrapper<A> a, ref Wrapper<B> b) => Processor.Execute(Store.QueryKey(e), ref a.Value, ref b.Value);
    }
    private struct Bridge<A, B, C, P> : NativeWorld.IQuery.Write<Wrapper<A>, Wrapper<B>, Wrapper<C>> where A : unmanaged where B : unmanaged where C : unmanaged where P : struct, IProcessor<A, B, C>
    {
        public P Processor; public StaticEcsStore Store;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Invoke(NativeWorld.Entity e, ref Wrapper<A> a, ref Wrapper<B> b, ref Wrapper<C> c) => Processor.Execute(Store.QueryKey(e), ref a.Value, ref b.Value, ref c.Value);
    }
    private struct ComponentsBridge<A, B, C, P> : NativeWorld.IQueryBlock.Write<Wrapper<A>, Wrapper<B>, Wrapper<C>>
        where A : unmanaged where B : unmanaged where C : unmanaged where P : struct, IComponentProcessor<A, B, C>
    {
        public P Processor;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Invoke(uint count, NativeWorld.EntityBlock entitiesBlock, Block<Wrapper<A>> first, Block<Wrapper<B>> second, Block<Wrapper<C>> third)
        {
            for (uint row = 0; row < count; row++)
                Processor.Execute(ref first[row].Value, ref second[row].Value, ref third[row].Value);
        }
    }
}
