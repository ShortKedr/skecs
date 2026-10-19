using Leopotam.Ecs;
namespace SpaceBattle.Simulations.LeoEcsClassic;
public sealed partial class Battle
{
    private int entityCount;
    public string Name => "LeoECS Classic";
    private struct LifetimeMarker { public byte Value; }
    private readonly EcsWorld world = new();
    private void InitializeNative() { }
    private void DisposeNative() => world.Destroy();
    private EcsEntity Native(EntityKey e) => world.RestoreEntityFromInternalId(e.Id, e.Generation);
    private static EntityKey Key(EcsEntity e) => new(e.GetInternalId(), e.GetInternalGen());
    public EntityKey Create() { var e = world.NewEntity(); e.Get<LifetimeMarker>().Value = 1; entityCount++; return Key(e); }
    public bool Alive(EntityKey e) => !disposed && !e.IsNone && Native(e).IsAlive();
    public void Destroy(EntityKey e) { if (Alive(e)) { Native(e).Destroy(); entityCount--; } }
    public ref T Get<T>(EntityKey e) where T : unmanaged { var native = Native(e); return ref world.GetPool<T>().Items[native.GetComponentIndexInPool<T>()]; }
    public bool Has<T>(EntityKey e) where T : unmanaged => Alive(e) && Native(e).Has<T>();
    public void Set<T>(EntityKey e, in T value) where T : unmanaged => Native(e).Get<T>() = value;
    public void Remove<T>(EntityKey e) where T : unmanaged { if (Alive(e)) Native(e).Del<T>(); }
    public void Commit() { }
    public StoreDiagnostics Diagnostics() => new(entityCount, null, null, null, null);
    private EcsFilter<Identity, Transform>? filterShipCollector;
    private void RunShipCollector(ref ShipCollector processor)
    {
        var filter = filterShipCollector ??= (EcsFilter<Identity, Transform>)world.GetFilter(typeof(EcsFilter<Identity, Transform>));
        foreach (var row in filter) processor.Execute(Key(filter.GetEntity(row)), ref filter.Get1(row), ref filter.Get2(row));
    }
    private EcsFilter<Projectile>? filterProjectileCollector;
    private void RunProjectileCollector(ref ProjectileCollector processor)
    {
        var filter = filterProjectileCollector ??= (EcsFilter<Projectile>)world.GetFilter(typeof(EcsFilter<Projectile>));
        foreach (var row in filter) processor.Execute(Key(filter.GetEntity(row)), ref filter.Get1(row));
    }
    private EcsFilter<Debris>? filterDebrisCollector;
    private void RunDebrisCollector(ref DebrisCollector processor)
    {
        var filter = filterDebrisCollector ??= (EcsFilter<Debris>)world.GetFilter(typeof(EcsFilter<Debris>));
        foreach (var row in filter) processor.Execute(Key(filter.GetEntity(row)), ref filter.Get1(row));
    }
    private EcsFilter<Particle>? filterParticleCollector;
    private void RunParticleCollector(ref ParticleCollector processor)
    {
        var filter = filterParticleCollector ??= (EcsFilter<Particle>)world.GetFilter(typeof(EcsFilter<Particle>));
        foreach (var row in filter) processor.Execute(Key(filter.GetEntity(row)), ref filter.Get1(row));
    }
    private EcsFilter<Identity, Brain, Transform>? filterAiSystem;
    private void RunAiSystem(ref AiSystem processor)
    {
        var filter = filterAiSystem ??= (EcsFilter<Identity, Brain, Transform>)world.GetFilter(typeof(EcsFilter<Identity, Brain, Transform>));
        foreach (var row in filter) processor.Execute(Key(filter.GetEntity(row)), ref filter.Get1(row), ref filter.Get2(row), ref filter.Get3(row));
    }
    private EcsFilter<Identity, Heat, Shield>? filterThermalSystem;
    private void RunThermalSystem(ref ThermalSystem processor)
    {
        var filter = filterThermalSystem ??= (EcsFilter<Identity, Heat, Shield>)world.GetFilter(typeof(EcsFilter<Identity, Heat, Shield>));
        foreach (var row in filter) processor.Execute(Key(filter.GetEntity(row)), ref filter.Get1(row), ref filter.Get2(row), ref filter.Get3(row));
    }
    private EcsFilter<Identity, Inventory, Hull>? filterInventorySystem;
    private void RunInventorySystem(ref InventorySystem processor)
    {
        var filter = filterInventorySystem ??= (EcsFilter<Identity, Inventory, Hull>)world.GetFilter(typeof(EcsFilter<Identity, Inventory, Hull>));
        foreach (var row in filter) processor.Execute(Key(filter.GetEntity(row)), ref filter.Get1(row), ref filter.Get2(row), ref filter.Get3(row));
    }
    private EcsFilter<Transform, Motion, Brain>? filterSteeringSystem;
    private void RunSteeringSystem(ref SteeringSystem processor)
    {
        var filter = filterSteeringSystem ??= (EcsFilter<Transform, Motion, Brain>)world.GetFilter(typeof(EcsFilter<Transform, Motion, Brain>));
        foreach (var row in filter) processor.Execute(Key(filter.GetEntity(row)), ref filter.Get1(row), ref filter.Get2(row), ref filter.Get3(row));
    }
    private EcsFilter<Identity, Weapon, Brain>? filterWeaponSystem;
    private void RunWeaponSystem(ref WeaponSystem processor)
    {
        var filter = filterWeaponSystem ??= (EcsFilter<Identity, Weapon, Brain>)world.GetFilter(typeof(EcsFilter<Identity, Weapon, Brain>));
        foreach (var row in filter) processor.Execute(Key(filter.GetEntity(row)), ref filter.Get1(row), ref filter.Get2(row), ref filter.Get3(row));
    }
    private EcsFilter<Identity, SecondaryWeapon, Brain>? filterSecondarySystem;
    private void RunSecondarySystem(ref SecondarySystem processor)
    {
        var filter = filterSecondarySystem ??= (EcsFilter<Identity, SecondaryWeapon, Brain>)world.GetFilter(typeof(EcsFilter<Identity, SecondaryWeapon, Brain>));
        foreach (var row in filter) processor.Execute(Key(filter.GetEntity(row)), ref filter.Get1(row), ref filter.Get2(row), ref filter.Get3(row));
    }
    private EcsFilter<Identity, Carrier, Transform>? filterCarrierSystem;
    private void RunCarrierSystem(ref CarrierSystem processor)
    {
        var filter = filterCarrierSystem ??= (EcsFilter<Identity, Carrier, Transform>)world.GetFilter(typeof(EcsFilter<Identity, Carrier, Transform>));
        foreach (var row in filter) processor.Execute(Key(filter.GetEntity(row)), ref filter.Get1(row), ref filter.Get2(row), ref filter.Get3(row));
    }
    private EcsFilter<Identity, Docked, Hull>? filterDockSystem;
    private void RunDockSystem(ref DockSystem processor)
    {
        var filter = filterDockSystem ??= (EcsFilter<Identity, Docked, Hull>)world.GetFilter(typeof(EcsFilter<Identity, Docked, Hull>));
        foreach (var row in filter) processor.Execute(Key(filter.GetEntity(row)), ref filter.Get1(row), ref filter.Get2(row), ref filter.Get3(row));
    }
    private EcsFilter<Transform, Motion, Projectile>? filterProjectileMotionSystem;
    private void RunProjectileMotionSystem(ref ProjectileMotionSystem processor)
    {
        var filter = filterProjectileMotionSystem ??= (EcsFilter<Transform, Motion, Projectile>)world.GetFilter(typeof(EcsFilter<Transform, Motion, Projectile>));
        foreach (var row in filter) processor.Execute(ref filter.Get1(row), ref filter.Get2(row), ref filter.Get3(row));
    }
    private EcsFilter<Transform, Motion, Debris>? filterDebrisMotionSystem;
    private void RunDebrisMotionSystem(ref DebrisMotionSystem processor)
    {
        var filter = filterDebrisMotionSystem ??= (EcsFilter<Transform, Motion, Debris>)world.GetFilter(typeof(EcsFilter<Transform, Motion, Debris>));
        foreach (var row in filter) processor.Execute(ref filter.Get1(row), ref filter.Get2(row), ref filter.Get3(row));
    }
    private EcsFilter<Transform, Motion, Particle>? filterParticleMotionSystem;
    private void RunParticleMotionSystem(ref ParticleMotionSystem processor)
    {
        var filter = filterParticleMotionSystem ??= (EcsFilter<Transform, Motion, Particle>)world.GetFilter(typeof(EcsFilter<Transform, Motion, Particle>));
        foreach (var row in filter) processor.Execute(ref filter.Get1(row), ref filter.Get2(row), ref filter.Get3(row));
    }
}
