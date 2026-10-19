using FFS.Libraries.StaticEcs;
using NativeWorld = FFS.Libraries.StaticEcs.World<SpaceBattle.Simulations.StaticEcs.NativeWorldType>;
namespace SpaceBattle.Simulations.StaticEcs;
internal readonly struct NativeWorldType : IWorldType { }
public sealed partial class Battle
{
    private int entityCount;
    public string Name => "StaticEcs 2.2.9";
    private struct Wrapper<T> : IComponent, IComponentConfig<Wrapper<T>> where T : unmanaged { public T Value; public ComponentTypeConfig<Wrapper<T>> Config() => new(noDataLifecycle: true); }
    private static int active;
    private void InitializeNative() {
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0) throw new InvalidOperationException("Only one StaticEcs Battle may be active.");
        NativeWorld.Create(WorldConfig.Default());
        NativeWorld.Types().Component<Wrapper<Identity>>();
        NativeWorld.Types().Component<Wrapper<Transform>>();
        NativeWorld.Types().Component<Wrapper<Motion>>();
        NativeWorld.Types().Component<Wrapper<Hull>>();
        NativeWorld.Types().Component<Wrapper<Shield>>();
        NativeWorld.Types().Component<Wrapper<Heat>>();
        NativeWorld.Types().Component<Wrapper<Brain>>();
        NativeWorld.Types().Component<Wrapper<Weapon>>();
        NativeWorld.Types().Component<Wrapper<SecondaryWeapon>>();
        NativeWorld.Types().Component<Wrapper<MountedTurret>>();
        NativeWorld.Types().Component<Wrapper<Collider>>();
        NativeWorld.Types().Component<Wrapper<PhysicsLink>>();
        NativeWorld.Types().Component<Wrapper<CombatState>>();
        NativeWorld.Types().Component<Wrapper<Carrier>>();
        NativeWorld.Types().Component<Wrapper<Docked>>();
        NativeWorld.Types().Component<Wrapper<Overheated>>();
        NativeWorld.Types().Component<Wrapper<InCombat>>();
        NativeWorld.Types().Component<Wrapper<Boosting>>();
        NativeWorld.Types().Component<Wrapper<ShieldBroken>>();
        NativeWorld.Types().Component<Wrapper<Projectile>>();
        NativeWorld.Types().Component<Wrapper<Debris>>();
        NativeWorld.Types().Component<Wrapper<Particle>>();
        NativeWorld.Types().Component<Wrapper<Lifetime>>();
        NativeWorld.Types().Component<Wrapper<InventoryItem>>();
        NativeWorld.Types().Component<Wrapper<InventoryItems>>();
        NativeWorld.Types().Component<Wrapper<Inventory>>();
        NativeWorld.Initialize();
    }
    private void DisposeNative() { NativeWorld.Destroy(); Interlocked.Exchange(ref active, 0); }
    private static EntityGID Gid(EntityKey e) => new((uint)e.Id, (ushort)e.Generation, 0);
    private static NativeWorld.Entity Native(EntityKey e) => new((uint)e.Id);
    private static EntityKey Key(NativeWorld.Entity e) { var gid = e.GID; return new((int)gid.Id, gid.Version); }
    public EntityKey Create() { var e = NativeWorld.NewEntity<Default>(); entityCount++; return Key(e); }
    public bool Alive(EntityKey e) => !disposed && !e.IsNone && e.Generation > 0 && Gid(e).TryUnpack<NativeWorldType>(out _);
    public void Destroy(EntityKey e)
    {
        if (!Alive(e)) return;
        if (OperatingSystem.IsBrowser()) DestroyBrowserEntity(e);
        else Native(e).Destroy();
        entityCount--;
    }

    // Mono WebAssembly AOT miscompiles the type-erased bool-returning _Delete
    // function pointer used by Entity.Destroy. The native batch API uses void
    // deletion callbacks and preserves the same component/lifetime semantics.
    // Select exactly one native entity; desktop/headless retain Entity.Destroy.
    private static void DestroyBrowserEntity(EntityKey e)
        => NativeWorld.Query(new SingleEntityFilter((uint)e.Id)).BatchDestroy(EntityStatusType.Any);

    private readonly struct SingleEntityFilter(uint id) : IQueryFilter
    {
        public ulong FilterChunk<TWorld>(uint chunkIdx) where TWorld : struct, IWorldType
            => chunkIdx == id >> Const.ENTITIES_IN_CHUNK_SHIFT
                ? 1UL << (int)((id >> Const.ENTITIES_IN_BLOCK_SHIFT) & Const.BLOCKS_IN_CHUNK_MASK) : 0;

        public ulong FilterEntities<TWorld>(uint segmentIdx, byte segmentBlockIdx) where TWorld : struct, IWorldType
            => segmentIdx == id >> Const.ENTITIES_IN_SEGMENT_SHIFT &&
                segmentBlockIdx == ((id >> Const.ENTITIES_IN_BLOCK_SHIFT) & Const.BLOCKS_IN_SEGMENT_MASK)
                ? 1UL << (int)(id & Const.ENTITIES_IN_BLOCK_MASK) : 0;

        public void Block<TWorld>(int value) where TWorld : struct, IWorldType { }
    }
    public ref T Get<T>(EntityKey e) where T : unmanaged => ref Native(e).Ref<Wrapper<T>>().Value;
    public bool Has<T>(EntityKey e) where T : unmanaged => Alive(e) && Native(e).Has<Wrapper<T>>();
    public void Set<T>(EntityKey e, in T value) where T : unmanaged => Native(e).Set(new Wrapper<T> { Value = value });
    public void Remove<T>(EntityKey e) where T : unmanaged { if (Alive(e)) Native(e).Delete<Wrapper<T>>(); }
    public void Commit() { }
    public StoreDiagnostics Diagnostics() => new(entityCount, null, null, null, null);
    private void RunShipCollector(ref ShipCollector processor) => NativeWorld.Query().Write<Wrapper<Identity>, Wrapper<Transform>>().For(ref processor);
    private void RunProjectileCollector(ref ProjectileCollector processor) => NativeWorld.Query().Write<Wrapper<Projectile>>().For(ref processor);
    private void RunDebrisCollector(ref DebrisCollector processor) => NativeWorld.Query().Write<Wrapper<Debris>>().For(ref processor);
    private void RunParticleCollector(ref ParticleCollector processor) => NativeWorld.Query().Write<Wrapper<Particle>>().For(ref processor);
    private void RunAiSystem(ref AiSystem processor) => NativeWorld.Query().Write<Wrapper<Identity>, Wrapper<Brain>, Wrapper<Transform>>().For(ref processor);
    private void RunThermalSystem(ref ThermalSystem processor) => NativeWorld.Query().Write<Wrapper<Identity>, Wrapper<Heat>, Wrapper<Shield>>().For(ref processor);
    private void RunInventorySystem(ref InventorySystem processor) => NativeWorld.Query().Write<Wrapper<Identity>, Wrapper<Inventory>, Wrapper<Hull>>().For(ref processor);
    private void RunSteeringSystem(ref SteeringSystem processor) => NativeWorld.Query().Write<Wrapper<Transform>, Wrapper<Motion>, Wrapper<Brain>>().For(ref processor);
    private void RunWeaponSystem(ref WeaponSystem processor) => NativeWorld.Query().Write<Wrapper<Identity>, Wrapper<Weapon>, Wrapper<Brain>>().For(ref processor);
    private void RunSecondarySystem(ref SecondarySystem processor) => NativeWorld.Query().Write<Wrapper<Identity>, Wrapper<SecondaryWeapon>, Wrapper<Brain>>().For(ref processor);
    private void RunCarrierSystem(ref CarrierSystem processor) => NativeWorld.Query().Write<Wrapper<Identity>, Wrapper<Carrier>, Wrapper<Transform>>().For(ref processor);
    private void RunDockSystem(ref DockSystem processor) => NativeWorld.Query().Write<Wrapper<Identity>, Wrapper<Docked>, Wrapper<Hull>>().For(ref processor);
    private struct ProjectileMotionSystemQuery : NativeWorld.IQueryBlock.Write<Wrapper<Transform>, Wrapper<Motion>, Wrapper<Projectile>>
    {
        public ProjectileMotionSystem Processor;
        public void Invoke(uint count, NativeWorld.EntityBlock entities, Block<Wrapper<Transform>> c0, Block<Wrapper<Motion>> c1, Block<Wrapper<Projectile>> c2)
        {
            for(uint row = 0; row < count; row++) Processor.Execute(ref c0[row].Value, ref c1[row].Value, ref c2[row].Value);
        }
    }
    private void RunProjectileMotionSystem(ref ProjectileMotionSystem processor) { var query = new ProjectileMotionSystemQuery { Processor = processor }; NativeWorld.Query().WriteBlock<Wrapper<Transform>, Wrapper<Motion>, Wrapper<Projectile>>().For(ref query); processor = query.Processor; }
    private struct DebrisMotionSystemQuery : NativeWorld.IQueryBlock.Write<Wrapper<Transform>, Wrapper<Motion>, Wrapper<Debris>>
    {
        public DebrisMotionSystem Processor;
        public void Invoke(uint count, NativeWorld.EntityBlock entities, Block<Wrapper<Transform>> c0, Block<Wrapper<Motion>> c1, Block<Wrapper<Debris>> c2)
        {
            for(uint row = 0; row < count; row++) Processor.Execute(ref c0[row].Value, ref c1[row].Value, ref c2[row].Value);
        }
    }
    private void RunDebrisMotionSystem(ref DebrisMotionSystem processor) { var query = new DebrisMotionSystemQuery { Processor = processor }; NativeWorld.Query().WriteBlock<Wrapper<Transform>, Wrapper<Motion>, Wrapper<Debris>>().For(ref query); processor = query.Processor; }
    private struct ParticleMotionSystemQuery : NativeWorld.IQueryBlock.Write<Wrapper<Transform>, Wrapper<Motion>, Wrapper<Particle>>
    {
        public ParticleMotionSystem Processor;
        public void Invoke(uint count, NativeWorld.EntityBlock entities, Block<Wrapper<Transform>> c0, Block<Wrapper<Motion>> c1, Block<Wrapper<Particle>> c2)
        {
            for(uint row = 0; row < count; row++) Processor.Execute(ref c0[row].Value, ref c1[row].Value, ref c2[row].Value);
        }
    }
    private void RunParticleMotionSystem(ref ParticleMotionSystem processor) { var query = new ParticleMotionSystemQuery { Processor = processor }; NativeWorld.Query().WriteBlock<Wrapper<Transform>, Wrapper<Motion>, Wrapper<Particle>>().For(ref query); processor = query.Processor; }
}
