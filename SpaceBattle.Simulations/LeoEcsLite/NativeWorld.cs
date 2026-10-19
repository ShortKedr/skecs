using Leopotam.EcsLite;
namespace SpaceBattle.Simulations.LeoEcsLite;
public sealed partial class Battle
{
    private int entityCount;
    public string Name => "LeoECSLite";
    private struct LifetimeMarker { public byte Value; }
    private readonly EcsWorld world = new();
    private void InitializeNative() { }
    private void DisposeNative() => world.Destroy();
    public EntityKey Create() { var id = world.NewEntity(); world.GetPool<LifetimeMarker>().Add(id).Value = 1; entityCount++; var e = world.PackEntity(id); return new(e.Id, e.Gen); }
    public bool Alive(EntityKey e) => !disposed && !e.IsNone && new EcsPackedEntity { Id = e.Id, Gen = (short)e.Generation }.Unpack(world, out _);
    public void Destroy(EntityKey e) { if (Alive(e)) { world.DelEntity(e.Id); entityCount--; } }
    public ref T Get<T>(EntityKey e) where T : unmanaged => ref world.GetPool<T>().Get(e.Id);
    public bool Has<T>(EntityKey e) where T : unmanaged => Alive(e) && world.GetPool<T>().Has(e.Id);
    public void Set<T>(EntityKey e, in T value) where T : unmanaged { var pool = world.GetPool<T>(); if (pool.Has(e.Id)) pool.Get(e.Id) = value; else pool.Add(e.Id) = value; }
    public void Remove<T>(EntityKey e) where T : unmanaged { if (Has<T>(e)) world.GetPool<T>().Del(e.Id); }
    public void Commit() { }
    private EntityKey Key(int id) { var e = world.PackEntity(id); return new(e.Id, e.Gen); }
    public StoreDiagnostics Diagnostics() => new(entityCount, null, null, null, null);
    private EcsFilter? filterShipCollector;
    private void RunShipCollector(ref ShipCollector processor)
    {
        var filter = filterShipCollector ??= world.Filter<Identity>().Inc<Transform>().End();
        var c0 = world.GetPool<Identity>();
        var c1 = world.GetPool<Transform>();
        foreach (var id in filter) processor.Execute(Key(id), ref c0.Get(id), ref c1.Get(id));
    }
    private EcsFilter? filterProjectileCollector;
    private void RunProjectileCollector(ref ProjectileCollector processor)
    {
        var filter = filterProjectileCollector ??= world.Filter<Projectile>().End();
        var c0 = world.GetPool<Projectile>();
        foreach (var id in filter) processor.Execute(Key(id), ref c0.Get(id));
    }
    private EcsFilter? filterDebrisCollector;
    private void RunDebrisCollector(ref DebrisCollector processor)
    {
        var filter = filterDebrisCollector ??= world.Filter<Debris>().End();
        var c0 = world.GetPool<Debris>();
        foreach (var id in filter) processor.Execute(Key(id), ref c0.Get(id));
    }
    private EcsFilter? filterParticleCollector;
    private void RunParticleCollector(ref ParticleCollector processor)
    {
        var filter = filterParticleCollector ??= world.Filter<Particle>().End();
        var c0 = world.GetPool<Particle>();
        foreach (var id in filter) processor.Execute(Key(id), ref c0.Get(id));
    }
    private EcsFilter? filterAiSystem;
    private void RunAiSystem(ref AiSystem processor)
    {
        var filter = filterAiSystem ??= world.Filter<Identity>().Inc<Brain>().Inc<Transform>().End();
        var c0 = world.GetPool<Identity>();
        var c1 = world.GetPool<Brain>();
        var c2 = world.GetPool<Transform>();
        foreach (var id in filter) processor.Execute(Key(id), ref c0.Get(id), ref c1.Get(id), ref c2.Get(id));
    }
    private EcsFilter? filterThermalSystem;
    private void RunThermalSystem(ref ThermalSystem processor)
    {
        var filter = filterThermalSystem ??= world.Filter<Identity>().Inc<Heat>().Inc<Shield>().End();
        var c0 = world.GetPool<Identity>();
        var c1 = world.GetPool<Heat>();
        var c2 = world.GetPool<Shield>();
        foreach (var id in filter) processor.Execute(Key(id), ref c0.Get(id), ref c1.Get(id), ref c2.Get(id));
    }
    private EcsFilter? filterInventorySystem;
    private void RunInventorySystem(ref InventorySystem processor)
    {
        var filter = filterInventorySystem ??= world.Filter<Identity>().Inc<Inventory>().Inc<Hull>().End();
        var c0 = world.GetPool<Identity>();
        var c1 = world.GetPool<Inventory>();
        var c2 = world.GetPool<Hull>();
        foreach (var id in filter) processor.Execute(Key(id), ref c0.Get(id), ref c1.Get(id), ref c2.Get(id));
    }
    private EcsFilter? filterSteeringSystem;
    private void RunSteeringSystem(ref SteeringSystem processor)
    {
        var filter = filterSteeringSystem ??= world.Filter<Transform>().Inc<Motion>().Inc<Brain>().End();
        var c0 = world.GetPool<Transform>();
        var c1 = world.GetPool<Motion>();
        var c2 = world.GetPool<Brain>();
        foreach (var id in filter) processor.Execute(Key(id), ref c0.Get(id), ref c1.Get(id), ref c2.Get(id));
    }
    private EcsFilter? filterWeaponSystem;
    private void RunWeaponSystem(ref WeaponSystem processor)
    {
        var filter = filterWeaponSystem ??= world.Filter<Identity>().Inc<Weapon>().Inc<Brain>().End();
        var c0 = world.GetPool<Identity>();
        var c1 = world.GetPool<Weapon>();
        var c2 = world.GetPool<Brain>();
        foreach (var id in filter) processor.Execute(Key(id), ref c0.Get(id), ref c1.Get(id), ref c2.Get(id));
    }
    private EcsFilter? filterSecondarySystem;
    private void RunSecondarySystem(ref SecondarySystem processor)
    {
        var filter = filterSecondarySystem ??= world.Filter<Identity>().Inc<SecondaryWeapon>().Inc<Brain>().End();
        var c0 = world.GetPool<Identity>();
        var c1 = world.GetPool<SecondaryWeapon>();
        var c2 = world.GetPool<Brain>();
        foreach (var id in filter) processor.Execute(Key(id), ref c0.Get(id), ref c1.Get(id), ref c2.Get(id));
    }
    private EcsFilter? filterCarrierSystem;
    private void RunCarrierSystem(ref CarrierSystem processor)
    {
        var filter = filterCarrierSystem ??= world.Filter<Identity>().Inc<Carrier>().Inc<Transform>().End();
        var c0 = world.GetPool<Identity>();
        var c1 = world.GetPool<Carrier>();
        var c2 = world.GetPool<Transform>();
        foreach (var id in filter) processor.Execute(Key(id), ref c0.Get(id), ref c1.Get(id), ref c2.Get(id));
    }
    private EcsFilter? filterDockSystem;
    private void RunDockSystem(ref DockSystem processor)
    {
        var filter = filterDockSystem ??= world.Filter<Identity>().Inc<Docked>().Inc<Hull>().End();
        var c0 = world.GetPool<Identity>();
        var c1 = world.GetPool<Docked>();
        var c2 = world.GetPool<Hull>();
        foreach (var id in filter) processor.Execute(Key(id), ref c0.Get(id), ref c1.Get(id), ref c2.Get(id));
    }
    private EcsFilter? filterProjectileMotionSystem;
    private void RunProjectileMotionSystem(ref ProjectileMotionSystem processor)
    {
        var filter = filterProjectileMotionSystem ??= world.Filter<Transform>().Inc<Motion>().Inc<Projectile>().End();
        var c0 = world.GetPool<Transform>();
        var c1 = world.GetPool<Motion>();
        var c2 = world.GetPool<Projectile>();
        foreach (var id in filter) processor.Execute(ref c0.Get(id), ref c1.Get(id), ref c2.Get(id));
    }
    private EcsFilter? filterDebrisMotionSystem;
    private void RunDebrisMotionSystem(ref DebrisMotionSystem processor)
    {
        var filter = filterDebrisMotionSystem ??= world.Filter<Transform>().Inc<Motion>().Inc<Debris>().End();
        var c0 = world.GetPool<Transform>();
        var c1 = world.GetPool<Motion>();
        var c2 = world.GetPool<Debris>();
        foreach (var id in filter) processor.Execute(ref c0.Get(id), ref c1.Get(id), ref c2.Get(id));
    }
    private EcsFilter? filterParticleMotionSystem;
    private void RunParticleMotionSystem(ref ParticleMotionSystem processor)
    {
        var filter = filterParticleMotionSystem ??= world.Filter<Transform>().Inc<Motion>().Inc<Particle>().End();
        var c0 = world.GetPool<Transform>();
        var c1 = world.GetPool<Motion>();
        var c2 = world.GetPool<Particle>();
        foreach (var id in filter) processor.Execute(ref c0.Get(id), ref c1.Get(id), ref c2.Get(id));
    }
}
