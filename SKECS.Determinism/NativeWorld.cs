namespace SKECS.Determinism;

internal sealed unsafe class NativeWorld : IDisposable
{
    private readonly World _world;
    private readonly bool _useChunks;
    private readonly ComponentAccessor<Transform> _transform;
    private readonly Dictionary<int, Entity> _handles = new();
    private readonly Dictionary<int, int> _keys = new();
    private EntityFactory<Transform, Motion>? _factory;
    private Watchers? _watchers;

    internal NativeWorld(ReplayOptions options)
    {
        _world = new World(new WorldOptions(options.Capacity, options.Diagnostics));
        _useChunks = options.UseChunks;
        if (options.ReverseRegistration)
            _world.QueryBuilder().With<Marker>().With<Motion>().With<Transform>();
        else
            _world.QueryBuilder().With<Transform>().With<Motion>().With<Marker>();
        _transform = _world.GetAccessor<Transform>();
        if (options.WarmQueries)
            _watchers = new Watchers(_world);

        // Register stages out of execution order.
        _world.AddSystem(SystemStage.Update, Move);
        _world.AddSystem(SystemStage.PostUpdate, _ => Post());
        _world.AddSystem(SystemStage.PreUpdate, Damp);
        _world.AddSystem(SystemStage.Update, Offset);
    }

    internal List<string> Results { get; } = new();
    internal List<string> Visits { get; } = new();
    internal List<SystemFrame> Systems { get; } = new();

    internal EntityState[] Capture()
    {
        var checkpoint = _world.OpenCheckpoint();
        var states = new List<EntityState>();
        for (var id = 0; id < checkpoint.SlotCount; id++)
        {
            var slot = checkpoint.GetSlot(id);
            if (!slot.Alive)
                continue;
            var entity = _world.BindEntity(id, slot.Version);
            states.Add(ReplaySchema.Capture(_world, checkpoint, _keys[id], entity));
        }
        return states.ToArray();
    }

    internal void Apply(Operation input)
    {
        Results.Clear();
        Visits.Clear();
        Systems.Clear();

        switch (input.Kind)
        {
            case "initial":
                if (_watchers is null)
                {
                    Visits.AddRange(Watchers.Names);
                    return;
                }
                break;
            case "create":
                Created(input.Key, _world.CreateEntity());
                break;
            case "direct":
                Created(input.Key, _world.CreateEntity(new Transform(input.A, input.B), new Motion(-3, 5)));
                break;
            case "factory":
                _factory ??= _world.PrepareEntity<Transform, Motion>();
                Created(input.Key, _factory.Value.Create(new Transform(input.A, input.B), new Motion(-3, 5)));
                break;
            case "batch":
                Span<Entity> batch = stackalloc Entity[2];
                _world.CreateEntities(batch, new Transform(input.A, input.B), new Motion(-3, 5));
                Created(input.Key, batch[0]);
                Created(checked(input.Key + 1), batch[1]);
                break;
            case "destroy":
                var victim = _handles[input.Key];
                _world.DestroyEntity(victim);
                Results.Add(_world.IsAlive(victim) ? "alive:true" : "alive:false");
                break;
            case "probe":
                var handle = _handles.GetValueOrDefault(input.Key);
                Results.Add(Trace.Probe(input.Key, _world.IsAlive(handle),
                    _transform.TryGet(handle, out var value) ? value : null));
                break;
            case "tick":
                _world.RunAll();
                break;
            default:
                Edit(input, _handles[input.Key]);
                break;
        }
        Observe(_watchers ??= new Watchers(_world));
    }

    private void Created(int key, Entity entity)
    {
        _handles.Add(key, entity);
        _keys[entity.Id] = key;
        Results.Add(Trace.Created(key, entity.Id, entity.Version));
    }

    private void Edit(Operation input, Entity entity)
    {
        switch (input.Kind)
        {
            case "set-transform":
                _world.Set(entity, new Transform(input.A, input.B));
                break;
            case "add-motion":
                _world.Add(entity, new Motion(input.A, input.B));
                break;
            case "set-motion":
                _world.Set(entity, new Motion(input.A, input.B));
                break;
            case "set-marker":
                _world.Set(entity, new Marker(unchecked((byte)input.A), input.B));
                break;
            case "remove-transform":
                Removed(_world.Remove<Transform>(entity));
                break;
            case "remove-motion":
                Removed(_world.Remove<Motion>(entity));
                break;
            case "remove-marker":
                Removed(_world.Remove<Marker>(entity));
                break;
            default:
                throw new InvalidDataException($"Unknown operation '{input.Kind}'.");
        }
    }

    private void Removed(bool removed) => Results.Add(removed ? "removed:true" : "removed:false");

    private void Damp(World world)
    {
        var damp = new DampProcessor();
        world.Query<Motion, Marker>().ForEach(ref damp);
        Systems.Add(new SystemFrame("pre/damp", Capture()));
    }

    private void Move(World world)
    {
        var query = world.Query<Transform, Motion>();
        var move = new MoveProcessor();
        if (_useChunks)
        {
            foreach (var chunk in query.Chunks())
            {
                for (var row = 0; row < chunk.Count; row++)
                    MoveProcessor.Apply(ref chunk.Component1[row], chunk.Component2[row]);
            }
        }
        else
            query.ForEach(ref move);
        Systems.Add(new SystemFrame("update/move", Capture()));
    }

    private void Offset(World world)
    {
        var query = world.Query<Transform>();
        var offset = new OffsetProcessor();
        if (_useChunks)
        {
            foreach (var chunk in query.Chunks())
            {
                for (var row = 0; row < chunk.Count; row++)
                    OffsetProcessor.Apply(ref chunk.Component1[row]);
            }
        }
        else
            query.ForEach(ref offset);
        Systems.Add(new SystemFrame("update/offset", Capture()));
    }

    private void Post()
    {
        Observe("post", (_watchers ??= new Watchers(_world)).Unmarked);
        Systems.Add(new SystemFrame("post/observe", Capture()));
    }

    private void Observe(Watchers queries)
    {
        Observe("all", queries.All);
        Visits.Add("transform");
        var single = new Visitor("transform", Visits);
        queries.Transforms.ForEach(ref single);
        Visits.Add("moving");
        var pair = new Visitor("moving", Visits);
        queries.Moving.ForEach(ref pair);
        Visits.Add("marked-moving");
        var triple = new Visitor("marked-moving", Visits);
        queries.MarkedMoving.ForEach(ref triple);
        Observe("unmarked", queries.Unmarked);
        Observe("impossible", queries.Impossible);
    }

    private void Observe(string name, QueryDescription query)
    {
        Visits.Add(name);
        foreach (var chunk in query.Chunks())
        {
            for (var row = 0; row < chunk.Count; row++)
                Visits.Add(Trace.Visit(name, chunk.Entities[row], chunk.Versions[row]));
        }
    }

    public void Dispose() => _world.Dispose();

    private sealed class Watchers(World world)
    {
        internal static readonly string[] Names = ["all", "transform", "moving", "marked-moving", "unmarked", "impossible"];
        internal QueryDescription All { get; } = world.QueryBuilder().Build();
        internal Query<Transform> Transforms { get; } = world.Query<Transform>();
        internal Query<Transform, Motion> Moving { get; } = world.Query<Transform, Motion>();
        internal Query<Transform, Motion, Marker> MarkedMoving { get; } = world.Query<Transform, Motion, Marker>();
        internal QueryDescription Unmarked { get; } = world.QueryBuilder().With<Transform>().Without<Marker>().Build();
        internal QueryDescription Impossible { get; } = world.QueryBuilder().With<Marker>().Without<Marker>().Build();
    }

    private readonly struct Visitor(string name, List<string> visits)
        : IQueryProcessor<Transform>, IQueryProcessor<Transform, Motion>, IQueryProcessor<Transform, Motion, Marker>
    {
        public void Execute(Entity entity, ref Transform transform)
            => visits.Add(Trace.Visit(name, entity.Id, entity.Version));
        public void Execute(Entity entity, ref Transform transform, ref Motion motion)
            => Execute(entity, ref transform);
        public void Execute(Entity entity, ref Transform transform, ref Motion motion, ref Marker marker)
            => Execute(entity, ref transform);
    }

    private readonly struct DampProcessor : IQueryProcessor<Motion, Marker>
    {
        public void Execute(Entity entity, ref Motion motion, ref Marker marker)
            => motion = new Motion(motion.Dx / 2, motion.Dy / 2);
    }

    private readonly struct MoveProcessor : IQueryProcessor<Transform, Motion>
    {
        public void Execute(Entity entity, ref Transform transform, ref Motion motion)
            => Apply(ref transform, motion);
        internal static void Apply(ref Transform transform, Motion motion)
            => transform = new Transform(unchecked(transform.X + motion.Dx), unchecked(transform.Y + motion.Dy));
    }

    private readonly struct OffsetProcessor : IQueryProcessor<Transform>
    {
        public void Execute(Entity entity, ref Transform transform) => Apply(ref transform);
        internal static void Apply(ref Transform transform)
            => transform = transform with { X = unchecked(transform.X * 3 + 1) };
    }
}
