
namespace SpaceBattle.Simulations.Skecs;
public sealed partial class Battle
{
    private int entityCount;
    public string Name => "SKECS";
    private readonly global::SKECS.World world = new();
    internal global::SKECS.World NativeWorld => world;
    private void InitializeNative() { }
    private void DisposeNative() => world.Dispose();
    public EntityKey Create() { var e = world.CreateEntity(); entityCount++; return new(e.Id, e.Version); }
    public bool Alive(EntityKey e) => !disposed && !e.IsNone && world.IsAlive(new(e.Id, e.Generation));
    public void Destroy(EntityKey e) { if (Alive(e)) { world.DestroyEntity(new(e.Id, e.Generation)); entityCount--; } }
    public ref T Get<T>(EntityKey e) where T : unmanaged => ref world.Get<T>(new(e.Id, e.Generation));
    public bool Has<T>(EntityKey e) where T : unmanaged => Alive(e) && world.Has<T>(new(e.Id, e.Generation));
    public void Set<T>(EntityKey e, in T value) where T : unmanaged => world.Set(new(e.Id, e.Generation), value);
    public void Remove<T>(EntityKey e) where T : unmanaged { if (Alive(e)) world.Remove<T>(new(e.Id, e.Generation)); }
    public void Commit() { }
    public StoreDiagnostics Diagnostics() { var d = world.GetStorageStatistics(); return new(d.EntityCount, d.ArchetypeCount, d.ChunkCount, d.NativeBytes, d.AccessorBufferBytes); }
    private global::SKECS.Query<Identity, Transform>? queryShipCollector;
    private unsafe void RunShipCollector(ref ShipCollector processor)
    {
        var query = queryShipCollector ??= world.Query<Identity, Transform>();
        query.ForEach(ref processor);
    }
    private global::SKECS.Query<Projectile>? queryProjectileCollector;
    private unsafe void RunProjectileCollector(ref ProjectileCollector processor)
    {
        var query = queryProjectileCollector ??= world.Query<Projectile>();
        query.ForEach(ref processor);
    }
    private global::SKECS.Query<Debris>? queryDebrisCollector;
    private unsafe void RunDebrisCollector(ref DebrisCollector processor)
    {
        var query = queryDebrisCollector ??= world.Query<Debris>();
        query.ForEach(ref processor);
    }
    private global::SKECS.Query<Particle>? queryParticleCollector;
    private unsafe void RunParticleCollector(ref ParticleCollector processor)
    {
        var query = queryParticleCollector ??= world.Query<Particle>();
        query.ForEach(ref processor);
    }
    private global::SKECS.Query<Identity, Brain, Transform>? queryAiSystem;
    private unsafe void RunAiSystem(ref AiSystem processor)
    {
        var query = queryAiSystem ??= world.Query<Identity, Brain, Transform>();
        query.ForEach(ref processor);
    }
    private global::SKECS.Query<Identity, Heat, Shield>? queryThermalSystem;
    private unsafe void RunThermalSystem(ref ThermalSystem processor)
    {
        var query = queryThermalSystem ??= world.Query<Identity, Heat, Shield>();
        query.ForEach(ref processor);
    }
    private global::SKECS.Query<Identity, Inventory, Hull>? queryInventorySystem;
    private unsafe void RunInventorySystem(ref InventorySystem processor)
    {
        var query = queryInventorySystem ??= world.Query<Identity, Inventory, Hull>();
        query.ForEach(ref processor);
    }
    private global::SKECS.Query<Transform, Motion, Brain>? querySteeringSystem;
    private unsafe void RunSteeringSystem(ref SteeringSystem processor)
    {
        var query = querySteeringSystem ??= world.Query<Transform, Motion, Brain>();
        query.ForEach(ref processor);
    }
    private global::SKECS.Query<Identity, Weapon, Brain>? queryWeaponSystem;
    private unsafe void RunWeaponSystem(ref WeaponSystem processor)
    {
        var query = queryWeaponSystem ??= world.Query<Identity, Weapon, Brain>();
        query.ForEach(ref processor);
    }
    private global::SKECS.Query<Identity, SecondaryWeapon, Brain>? querySecondarySystem;
    private unsafe void RunSecondarySystem(ref SecondarySystem processor)
    {
        var query = querySecondarySystem ??= world.Query<Identity, SecondaryWeapon, Brain>();
        query.ForEach(ref processor);
    }
    private global::SKECS.Query<Identity, Carrier, Transform>? queryCarrierSystem;
    private unsafe void RunCarrierSystem(ref CarrierSystem processor)
    {
        var query = queryCarrierSystem ??= world.Query<Identity, Carrier, Transform>();
        query.ForEach(ref processor);
    }
    private global::SKECS.Query<Identity, Docked, Hull>? queryDockSystem;
    private unsafe void RunDockSystem(ref DockSystem processor)
    {
        var query = queryDockSystem ??= world.Query<Identity, Docked, Hull>();
        query.ForEach(ref processor);
    }
    private global::SKECS.Query<Transform, Motion, Projectile>? queryProjectileMotionSystem;
    private unsafe void RunProjectileMotionSystem(ref ProjectileMotionSystem processor)
    {
        var query = queryProjectileMotionSystem ??= world.Query<Transform, Motion, Projectile>();
        foreach (var chunk in query.Chunks())
        {
            var c0 = new Span<Transform>(chunk.Component1, chunk.Count);
            var c1 = new Span<Motion>(chunk.Component2, chunk.Count);
            var c2 = new Span<Projectile>(chunk.Component3, chunk.Count);
            for (var row = 0; row < chunk.Count; row++) processor.Execute(ref c0[row], ref c1[row], ref c2[row]);
        }
    }
    private global::SKECS.Query<Transform, Motion, Debris>? queryDebrisMotionSystem;
    private unsafe void RunDebrisMotionSystem(ref DebrisMotionSystem processor)
    {
        var query = queryDebrisMotionSystem ??= world.Query<Transform, Motion, Debris>();
        foreach (var chunk in query.Chunks())
        {
            var c0 = new Span<Transform>(chunk.Component1, chunk.Count);
            var c1 = new Span<Motion>(chunk.Component2, chunk.Count);
            var c2 = new Span<Debris>(chunk.Component3, chunk.Count);
            for (var row = 0; row < chunk.Count; row++) processor.Execute(ref c0[row], ref c1[row], ref c2[row]);
        }
    }
    private global::SKECS.Query<Transform, Motion, Particle>? queryParticleMotionSystem;
    private unsafe void RunParticleMotionSystem(ref ParticleMotionSystem processor)
    {
        var query = queryParticleMotionSystem ??= world.Query<Transform, Motion, Particle>();
        foreach (var chunk in query.Chunks())
        {
            var c0 = new Span<Transform>(chunk.Component1, chunk.Count);
            var c1 = new Span<Motion>(chunk.Component2, chunk.Count);
            var c2 = new Span<Particle>(chunk.Component3, chunk.Count);
            for (var row = 0; row < chunk.Count; row++) processor.Execute(ref c0[row], ref c1[row], ref c2[row]);
        }
    }
}
