using Scellecs.Morpeh;
namespace SpaceBattle.Simulations.Morpeh;
public sealed partial class Battle
{
    private int entityCount;
    public string Name => "Morpeh";
    private struct Wrapper<T> : IComponent where T : unmanaged { public T Value; }
    private struct LifetimeMarker : IComponent { public byte Value; }
    private readonly World world = World.Create();
    // Indexed by native ID; references retain native generations with no identity remapping.
    private Entity[] nativeEntities = new Entity[64];
    private void InitializeNative() { }
    private void DisposeNative() => world.Dispose();
    private Entity Native(EntityKey e) => nativeEntities[e.Id];
    private static EntityKey Key(Entity e) => new(e.Id, e.Generation);
    public EntityKey Create() { var e = world.CreateEntity(); if(e.Id >= nativeEntities.Length) Array.Resize(ref nativeEntities, Math.Max(e.Id + 1, nativeEntities.Length * 2)); nativeEntities[e.Id] = e; world.GetStash<LifetimeMarker>().Add(e).Value = 1; entityCount++; return Key(e); }
    public bool Alive(EntityKey e) => !disposed && (uint)e.Id < (uint)nativeEntities.Length && Native(e).Generation == e.Generation && !world.IsDisposed(Native(e));
    public void Destroy(EntityKey e) { if (Alive(e)) { world.RemoveEntity(Native(e)); entityCount--; } }
    public ref T Get<T>(EntityKey e) where T : unmanaged => ref world.GetStash<Wrapper<T>>().Get(Native(e)).Value;
    public bool Has<T>(EntityKey e) where T : unmanaged => Alive(e) && world.GetStash<Wrapper<T>>().Has(Native(e));
    public void Set<T>(EntityKey e, in T value) where T : unmanaged => world.GetStash<Wrapper<T>>().Set(Native(e), new Wrapper<T> { Value = value });
    public void Remove<T>(EntityKey e) where T : unmanaged { if (Alive(e)) world.GetStash<Wrapper<T>>().Remove(Native(e)); }
    public void Commit() => world.Commit();
    public StoreDiagnostics Diagnostics() => new(entityCount, null, null, null, null);
    private Filter? filterShipCollector;
    private void RunShipCollector(ref ShipCollector processor)
    {
        var filter = filterShipCollector ??= world.Filter.With<Wrapper<Identity>>().With<Wrapper<Transform>>().Build();
        var c0 = world.GetStash<Wrapper<Identity>>();
        var c1 = world.GetStash<Wrapper<Transform>>();
        foreach (var entity in filter) processor.Execute(Key(entity), ref c0.Get(entity).Value, ref c1.Get(entity).Value);
    }
    private Filter? filterProjectileCollector;
    private void RunProjectileCollector(ref ProjectileCollector processor)
    {
        var filter = filterProjectileCollector ??= world.Filter.With<Wrapper<Projectile>>().Build();
        var c0 = world.GetStash<Wrapper<Projectile>>();
        foreach (var entity in filter) processor.Execute(Key(entity), ref c0.Get(entity).Value);
    }
    private Filter? filterDebrisCollector;
    private void RunDebrisCollector(ref DebrisCollector processor)
    {
        var filter = filterDebrisCollector ??= world.Filter.With<Wrapper<Debris>>().Build();
        var c0 = world.GetStash<Wrapper<Debris>>();
        foreach (var entity in filter) processor.Execute(Key(entity), ref c0.Get(entity).Value);
    }
    private Filter? filterParticleCollector;
    private void RunParticleCollector(ref ParticleCollector processor)
    {
        var filter = filterParticleCollector ??= world.Filter.With<Wrapper<Particle>>().Build();
        var c0 = world.GetStash<Wrapper<Particle>>();
        foreach (var entity in filter) processor.Execute(Key(entity), ref c0.Get(entity).Value);
    }
    private Filter? filterAiSystem;
    private void RunAiSystem(ref AiSystem processor)
    {
        var filter = filterAiSystem ??= world.Filter.With<Wrapper<Identity>>().With<Wrapper<Brain>>().With<Wrapper<Transform>>().Build();
        var c0 = world.GetStash<Wrapper<Identity>>();
        var c1 = world.GetStash<Wrapper<Brain>>();
        var c2 = world.GetStash<Wrapper<Transform>>();
        foreach (var entity in filter) processor.Execute(Key(entity), ref c0.Get(entity).Value, ref c1.Get(entity).Value, ref c2.Get(entity).Value);
    }
    private Filter? filterThermalSystem;
    private void RunThermalSystem(ref ThermalSystem processor)
    {
        var filter = filterThermalSystem ??= world.Filter.With<Wrapper<Identity>>().With<Wrapper<Heat>>().With<Wrapper<Shield>>().Build();
        var c0 = world.GetStash<Wrapper<Identity>>();
        var c1 = world.GetStash<Wrapper<Heat>>();
        var c2 = world.GetStash<Wrapper<Shield>>();
        foreach (var entity in filter) processor.Execute(Key(entity), ref c0.Get(entity).Value, ref c1.Get(entity).Value, ref c2.Get(entity).Value);
    }
    private Filter? filterInventorySystem;
    private void RunInventorySystem(ref InventorySystem processor)
    {
        var filter = filterInventorySystem ??= world.Filter.With<Wrapper<Identity>>().With<Wrapper<Inventory>>().With<Wrapper<Hull>>().Build();
        var c0 = world.GetStash<Wrapper<Identity>>();
        var c1 = world.GetStash<Wrapper<Inventory>>();
        var c2 = world.GetStash<Wrapper<Hull>>();
        foreach (var entity in filter) processor.Execute(Key(entity), ref c0.Get(entity).Value, ref c1.Get(entity).Value, ref c2.Get(entity).Value);
    }
    private Filter? filterSteeringSystem;
    private void RunSteeringSystem(ref SteeringSystem processor)
    {
        var filter = filterSteeringSystem ??= world.Filter.With<Wrapper<Transform>>().With<Wrapper<Motion>>().With<Wrapper<Brain>>().Build();
        var c0 = world.GetStash<Wrapper<Transform>>();
        var c1 = world.GetStash<Wrapper<Motion>>();
        var c2 = world.GetStash<Wrapper<Brain>>();
        foreach (var entity in filter) processor.Execute(Key(entity), ref c0.Get(entity).Value, ref c1.Get(entity).Value, ref c2.Get(entity).Value);
    }
    private Filter? filterWeaponSystem;
    private void RunWeaponSystem(ref WeaponSystem processor)
    {
        var filter = filterWeaponSystem ??= world.Filter.With<Wrapper<Identity>>().With<Wrapper<Weapon>>().With<Wrapper<Brain>>().Build();
        var c0 = world.GetStash<Wrapper<Identity>>();
        var c1 = world.GetStash<Wrapper<Weapon>>();
        var c2 = world.GetStash<Wrapper<Brain>>();
        foreach (var entity in filter) processor.Execute(Key(entity), ref c0.Get(entity).Value, ref c1.Get(entity).Value, ref c2.Get(entity).Value);
    }
    private Filter? filterSecondarySystem;
    private void RunSecondarySystem(ref SecondarySystem processor)
    {
        var filter = filterSecondarySystem ??= world.Filter.With<Wrapper<Identity>>().With<Wrapper<SecondaryWeapon>>().With<Wrapper<Brain>>().Build();
        var c0 = world.GetStash<Wrapper<Identity>>();
        var c1 = world.GetStash<Wrapper<SecondaryWeapon>>();
        var c2 = world.GetStash<Wrapper<Brain>>();
        foreach (var entity in filter) processor.Execute(Key(entity), ref c0.Get(entity).Value, ref c1.Get(entity).Value, ref c2.Get(entity).Value);
    }
    private Filter? filterCarrierSystem;
    private void RunCarrierSystem(ref CarrierSystem processor)
    {
        var filter = filterCarrierSystem ??= world.Filter.With<Wrapper<Identity>>().With<Wrapper<Carrier>>().With<Wrapper<Transform>>().Build();
        var c0 = world.GetStash<Wrapper<Identity>>();
        var c1 = world.GetStash<Wrapper<Carrier>>();
        var c2 = world.GetStash<Wrapper<Transform>>();
        foreach (var entity in filter) processor.Execute(Key(entity), ref c0.Get(entity).Value, ref c1.Get(entity).Value, ref c2.Get(entity).Value);
    }
    private Filter? filterDockSystem;
    private void RunDockSystem(ref DockSystem processor)
    {
        var filter = filterDockSystem ??= world.Filter.With<Wrapper<Identity>>().With<Wrapper<Docked>>().With<Wrapper<Hull>>().Build();
        var c0 = world.GetStash<Wrapper<Identity>>();
        var c1 = world.GetStash<Wrapper<Docked>>();
        var c2 = world.GetStash<Wrapper<Hull>>();
        foreach (var entity in filter) processor.Execute(Key(entity), ref c0.Get(entity).Value, ref c1.Get(entity).Value, ref c2.Get(entity).Value);
    }
    private Filter? filterProjectileMotionSystem;
    private void RunProjectileMotionSystem(ref ProjectileMotionSystem processor)
    {
        var filter = filterProjectileMotionSystem ??= world.Filter.With<Wrapper<Transform>>().With<Wrapper<Motion>>().With<Wrapper<Projectile>>().Build();
        var c0 = world.GetStash<Wrapper<Transform>>();
        var c1 = world.GetStash<Wrapper<Motion>>();
        var c2 = world.GetStash<Wrapper<Projectile>>();
        foreach (var entity in filter) processor.Execute(ref c0.Get(entity).Value, ref c1.Get(entity).Value, ref c2.Get(entity).Value);
    }
    private Filter? filterDebrisMotionSystem;
    private void RunDebrisMotionSystem(ref DebrisMotionSystem processor)
    {
        var filter = filterDebrisMotionSystem ??= world.Filter.With<Wrapper<Transform>>().With<Wrapper<Motion>>().With<Wrapper<Debris>>().Build();
        var c0 = world.GetStash<Wrapper<Transform>>();
        var c1 = world.GetStash<Wrapper<Motion>>();
        var c2 = world.GetStash<Wrapper<Debris>>();
        foreach (var entity in filter) processor.Execute(ref c0.Get(entity).Value, ref c1.Get(entity).Value, ref c2.Get(entity).Value);
    }
    private Filter? filterParticleMotionSystem;
    private void RunParticleMotionSystem(ref ParticleMotionSystem processor)
    {
        var filter = filterParticleMotionSystem ??= world.Filter.With<Wrapper<Transform>>().With<Wrapper<Motion>>().With<Wrapper<Particle>>().Build();
        var c0 = world.GetStash<Wrapper<Transform>>();
        var c1 = world.GetStash<Wrapper<Motion>>();
        var c2 = world.GetStash<Wrapper<Particle>>();
        foreach (var entity in filter) processor.Execute(ref c0.Get(entity).Value, ref c1.Get(entity).Value, ref c2.Get(entity).Value);
    }
}
