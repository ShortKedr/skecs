English | [Русский](README.ru.md)

# Using SKECS 0.2.0

[Project overview](../../README.md) · [Documentation map](../README.md)

Install [`ShortKedr.SKECS` 0.2.0](INSTALL.md) and use the `SKECS` namespace. The
[sample source](../../SKECS.Sample/Program.cs) shows systems and typed chunk iteration.
If you use SECS 0.1.0, follow the [migration guide](MIGRATION.md) before changing
the package and namespace.

## What 0.2.0 includes

The released package contains worlds and entities, archetype storage, sequential
CPU systems and queries, typed chunk access, batch creation, component accessors,
and storage counters. Components must satisfy the C# `unmanaged` constraint.
The package provides .NET Standard 2.0, .NET Standard 2.1, and .NET 8 assets.
See the [release notes](RELEASE_NOTES_0.2.0.md#verified-runtimes) for the consumer
runtimes verified with these artifacts.

`new World()` uses a chunk capacity of 256 with diagnostics disabled. To change
those settings, use `new World(new WorldOptions(chunkCapacity: 128, diagnosticsEnabled: true))`.
Nonpositive capacities fall back to 256. Component type IDs are assigned in
first-access order within each world. For a persistent format, define stable
component identifiers separately.

## A minimal query

<!-- docs-example: Position: (3, 1) -->
```csharp
using SKECS;

using var world = new World();
var entity = world.CreateEntity(new Position { X = 0, Y = 0 },
                                new Velocity { X = 3, Y = 1 });
var movement = new Movement();
world.Query<Position, Velocity>().ForEach(ref movement);

var position = world.Get<Position>(entity);
Console.WriteLine($"Position: ({position.X}, {position.Y})");

struct Position { public float X, Y; }
struct Velocity { public float X, Y; }
struct Movement : IQueryProcessor<Position, Velocity>
{
    public void Execute(Entity entity, ref Position position, ref Velocity velocity)
    {
        position.X += velocity.X;
        position.Y += velocity.Y;
    }
}
```

The processor is a struct passed by reference. Queries with one, two, or three
component types visit all entities containing those types, including entities
with additional components. Changes through the component `ref` update stored
values. This example prints `Position: (3, 1)`.

## Entities and components

| Operation | Use |
| --- | --- |
| `CreateEntity()` / `DestroyEntity(entity)` | Create an empty entity or destroy a live one. |
| `CreateEntity(component1, ...)` | Create directly with one, two, or three distinct component types. |
| `Add(entity, component)` | Add a missing component; an existing value is left unchanged. |
| `Set(entity, component)` | Replace a value, or add the component when absent. |
| `Get<T>(entity)` | Access an existing component by `ref`. |
| `TryGet<T>(entity, out value)` | Get a value copy or return `false` if the handle is invalid or the component is absent. |
| `Has<T>(entity)` / `Remove<T>(entity)` | Check membership or remove a component from a live entity. |
| `IsAlive(entity)` | Check ownership, ID, generation, and whether the entity is alive. |
| `BindEntity(id, version)` | Bind coordinates to this world before checking liveness. |

Keep the bound `Entity` returned by the world or a query. Equality and hashing
include the owning world, so handles from different worlds compare unequal even
when their IDs and generations match. Destroyed IDs may be reused with a new
generation.

To reconstruct a handle from chunk IDs and generations, call
`world.BindEntity(id, version)` on the world that supplied them, then call
`world.IsAlive(entity)` before using it. The two-argument `Entity` constructor
creates an unbound value. `Entity.IsValid` checks that the handle is bound and
has a valid shape. It can remain `true` after entity destruction or world
disposal. Use `World.IsAlive` to check liveness.

On a live world, `World.IsAlive` and `World.TryGet` return `false` for a foreign,
unbound, or stale handle. Strict operations such as `World.Has`, `World.Get`,
and `World.DestroyEntity` throw `InvalidOperationException` for those handles in
both Debug and Release. The [accessor guide](../SKECS_OPTIMIZATION.md#cached-random-access)
describes the corresponding accessor behavior.

Creation, destruction, and changes to component membership take effect
immediately. Perform them between query iterations. Queries traverse live
storage, so structural mutation inside a processor has no safe deferred-command
or snapshot contract.

A structural change or world disposal invalidates component refs and chunk
pointers. Cache handles and queries, and reacquire refs when needed. Use a world
and its queries, factories, and accessors from one thread at a time.

Dispose the world after queries return to release its native buffers. A call to
`Dispose` inside `Query.ForEach` throws `InvalidOperationException`. After
disposal, operations requiring a live world, including `World.IsAlive` and
`World.TryGet`, throw `ObjectDisposedException`. Repeated `Dispose` is safe.

Run this example to verify handle ownership and liveness across two worlds.

<!-- docs-example: Handle lifetime verified. -->
```csharp
using SKECS;

using var world = new World();
using var other = new World();
var entity = world.CreateEntity(12);
var foreign = other.CreateEntity(16);
var rebound = world.BindEntity(entity.Id, entity.Version);
var accessor = world.GetAccessor<int>();

if (entity == foreign || world.IsAlive(foreign) || accessor.Has(foreign))
    throw new InvalidOperationException("A foreign handle was accepted.");
if (rebound != entity || !world.IsAlive(rebound) || accessor.Get(rebound) != 12)
    throw new InvalidOperationException("The rebound handle should be live.");

world.DestroyEntity(entity);
if (!rebound.IsValid || world.IsAlive(rebound) || accessor.TryGet(rebound, out _))
    throw new InvalidOperationException("A destroyed entity should be absent.");

Console.WriteLine("Handle lifetime verified.");
```

## Systems and queries

Register an `Action<World>` using `AddSystem(stage, system)`. `Run(stage)` executes
that stage in registration order; `RunAll()` executes `PreUpdate`, `Update`, and
`PostUpdate` in that order. The host supplies its own tick loop and time inputs.

`QueryBuilder().With<T>().Without<U>().Build()` creates an include/exclude
filter. Its `MatchCount` counts matching archetypes. Typed queries
expose `Chunks()` with component pointers and `Count`; access requires an
`unsafe` context. Iterate rows below `Count`, as in the sample. Component types
have separate columns, but fields inside a component struct retain their layout.

Iteration follows archetype creation order, chunk order, and current rows.
Swap removal can change row order. The API does not promise sorted entity IDs
or order independent of storage history.

## Creation throughput and random access

For repeated two-component creation, use `PrepareEntity<T1, T2>()` and the
resulting factory's `Create` or `CreateBatch`. Batch creation assigns the same
input values to every entity. For frequent random access, `GetAccessor<T>()`
maintains an address/generation table across structural changes. Both features
are opt-in and have costs; see [runtime optimizations](../SKECS_OPTIMIZATION.md)
for examples and storage diagnostics.

## Working on the library

[Tests](../../SKECS.Tests) cover the current API. [Benchmarks](../../SKECS.Benchmarks/README.md)
measure specific workloads.
