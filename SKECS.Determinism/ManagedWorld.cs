namespace SKECS.Determinism;

internal sealed class ManagedWorld
{
    private readonly Dictionary<int, EntityState> _live = new();
    private readonly HashSet<int> _issued = new();
    private readonly List<(int Id, int Version)> _free = new();
    private int _nextId;

    internal List<string> Results { get; } = new();
    internal List<string> Visits { get; } = new();
    internal List<SystemFrame> Systems { get; } = new();

    internal EntityState[] Capture() => Snapshot.Order(_live.Values);

    internal void Apply(Operation input)
    {
        Results.Clear();
        Visits.Clear();
        Systems.Clear();

        switch (input.Kind)
        {
            case "initial":
                break;
            case "create":
                Create(input.Key, null, null);
                break;
            case "direct":
            case "factory":
                Create(input.Key, new Transform(input.A, input.B), new Motion(-3, 5));
                break;
            case "batch":
                Create(input.Key, new Transform(input.A, input.B), new Motion(-3, 5));
                Create(checked(input.Key + 1), new Transform(input.A, input.B), new Motion(-3, 5));
                break;
            case "destroy":
                var entity = _live[input.Key];
                _live.Remove(input.Key);
                _free.Add((entity.Id, checked(entity.Version + 1)));
                Results.Add("alive:false");
                break;
            case "probe":
                var alive = _live.TryGetValue(input.Key, out var observed);
                Results.Add(Trace.Probe(input.Key, alive, observed?.Transform));
                break;
            case "tick":
                Tick();
                break;
            default:
                Edit(input);
                break;
        }

        Observe("all", _ => true);
        Observe("transform", entity => entity.Transform.HasValue);
        Observe("moving", entity => entity.Transform.HasValue && entity.Motion.HasValue);
        Observe("marked-moving", entity => entity.Transform.HasValue && entity.Motion.HasValue && entity.Marker.HasValue);
        Observe("unmarked", entity => entity.Transform.HasValue && !entity.Marker.HasValue);
        Observe("impossible", _ => false);
    }

    private void Create(int key, Transform? transform, Motion? motion)
    {
        if (!_issued.Add(key))
            throw new InvalidDataException($"Logical key {key} was already used by a creation command.");

        var identity = (Id: _nextId, Version: 1);
        if (_free.Count == 0)
            _nextId++;
        else
        {
            identity = _free[^1];
            _free.RemoveAt(_free.Count - 1);
        }
        _live.Add(key, new EntityState(key, identity.Id, identity.Version, transform, motion, null));
        Results.Add(Trace.Created(key, identity.Id, identity.Version));
    }

    private void Edit(Operation input)
    {
        var entity = _live[input.Key];
        var replacement = input.Kind switch
        {
            "set-transform" => entity with { Transform = new Transform(input.A, input.B) },
            "add-motion" when entity.Motion.HasValue => throw new InvalidDataException("Motion is already attached."),
            "add-motion" or "set-motion" => entity with { Motion = new Motion(input.A, input.B) },
            "set-marker" => entity with { Marker = new Marker(unchecked((byte)input.A), input.B) },
            "remove-transform" => Remove(entity, entity.Transform.HasValue, entity with { Transform = null }),
            "remove-motion" => Remove(entity, entity.Motion.HasValue, entity with { Motion = null }),
            "remove-marker" => Remove(entity, entity.Marker.HasValue, entity with { Marker = null }),
            _ => throw new InvalidDataException($"Unknown operation '{input.Kind}'.")
        };
        _live[input.Key] = replacement;
    }

    private EntityState Remove(EntityState entity, bool present, EntityState without)
    {
        Results.Add(present ? "removed:true" : "removed:false");
        return present ? without : entity;
    }

    private void Tick()
    {
        foreach (var entity in _live.Values.Where(entity => entity.Marker.HasValue && entity.Motion.HasValue).ToArray())
        {
            var velocity = entity.Motion!.Value;
            _live[entity.Key] = entity with { Motion = new Motion(velocity.Dx / 2, velocity.Dy / 2) };
        }
        Systems.Add(new SystemFrame("pre/damp", Capture()));

        foreach (var entity in _live.Values.Where(entity => entity.Transform.HasValue && entity.Motion.HasValue).ToArray())
        {
            var position = entity.Transform!.Value;
            var velocity = entity.Motion!.Value;
            _live[entity.Key] = entity with
            {
                Transform = new Transform(unchecked((int)((long)position.X + velocity.Dx)),
                    unchecked((int)((long)position.Y + velocity.Dy)))
            };
        }
        Systems.Add(new SystemFrame("update/move", Capture()));

        foreach (var entity in _live.Values.Where(entity => entity.Transform.HasValue).ToArray())
        {
            var position = entity.Transform!.Value;
            _live[entity.Key] = entity with { Transform = position with { X = unchecked((int)(3L * position.X + 1)) } };
        }
        Systems.Add(new SystemFrame("update/offset", Capture()));

        Observe("post", entity => entity.Transform.HasValue && !entity.Marker.HasValue);
        Systems.Add(new SystemFrame("post/observe", Capture()));
    }

    private void Observe(string query, Func<EntityState, bool> predicate)
    {
        Visits.Add(query);
        foreach (var entity in _live.Values.Where(predicate))
            Visits.Add(Trace.Visit(query, entity.Id, entity.Version));
    }
}
