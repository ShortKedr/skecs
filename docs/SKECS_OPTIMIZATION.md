English | [Русский](SKECS_OPTIMIZATION.ru.md)

# SKECS 0.2.0 Runtime Optimizations

[Library guide](skecs/README.md) · [Documentation map](README.md) · [Current ECS campaign](benchmarks/current/REPORT.md) · [Current API and storage measurements](benchmarks/current/SUPPLEMENT.md)

This guide describes the 0.2.0 [CPU runtime](../SKECS/World.cs) and the costs of
its storage APIs. The benchmark reports list the measured revisions and workloads.

## Existing API

`World.Get<T>`, `Add<T>`, `Remove<T>`, `Set<T>`, and queries retain their
existing call patterns. Component IDs remain world-local; a generic registration
slot accelerates resolution without imposing a shared component registration order.
Archetypes use indexed column lookup. Structural edges cache the destination
archetype and a plan of source/destination column offsets and sizes.

Each archetype tracks non-full chunks. Adding an entity reuses retained free
capacity before allocating another chunk. Removal still swaps with the last
entity in the same chunk and updates that entity's location. Empty chunks are
retained for reuse, not released immediately; storage follows historical peak
occupancy per archetype rather than current occupancy alone.

## Cached Random Access

```csharp
var positions = world.GetAccessor<Position>();
ref var position = ref positions.Get(entity);
position.X += 1;
if (positions.TryGet(entity, out var copy)) { /* use the value copy */ }
```

The accessor caches component addresses and entity generations. Creation,
migration, swap-removal, and destruction maintain its table. Repeated requests for
the same component share one table in that world. Give it a bound `Entity` from
its world. `Has` and `TryGet` return `false` for foreign, unbound, or stale handles
and for a missing component. `Get` throws `InvalidOperationException` in these
cases in both Debug and Release. After the world is disposed, an existing
accessor's `Has` and `TryGet` return `false`, and `Get` throws
`InvalidOperationException`.

This is opt-in: ordinary queries and `World.Get<T>` do not allocate address
tables. On a 64-bit runtime, each cached component costs 12 bytes per allocated
ID slot (8-byte address plus 4-byte generation), excluding array headers.
Capacity grows geometrically and includes IDs where the component is absent.
Structural operations update each registered accessor, so register only the
components needing frequent random access. Accessor construction scans existing
entity records and must not be mistaken for a free operation.

Handles from different worlds compare unequal even if ID and generation match.
Use the world and its accessor from one thread at a time. Cache the accessor and
reacquire component refs after structural changes. Disposal ends the lifetime of
every returned ref. See [entity ownership and liveness](skecs/README.md#entities-and-components).

## Direct Creation

```csharp
var entity = world.CreateEntity(new Position { X = 1 }, new Velocity { X = 2 });
var factory = world.PrepareEntity<Position, Velocity>();
var another = factory.Create(new Position { X = 3 }, new Velocity { X = 4 });
var entities = new Entity[1000];
factory.CreateBatch(entities, new Position { X = 5 }, new Velocity { X = 6 });
```

`CreateEntity` overloads accept one, two, or three unmanaged components and place
the entity directly in the final archetype. The two-component factory caches the
world, archetype, and component IDs. `World.CreateEntities` is the convenience
batch overload; a prepared factory avoids repeating template resolution.
Batch creation initializes every output entity with the same provided values.
Use individual factory calls for different values. Duplicate component types
are rejected before allocating an entity. Factories are world-bound and cannot
create after disposal.

## Storage Diagnostics

`World.GetStorageStatistics()` reports retained archetypes, chunks, live entity
count, native column-buffer bytes, and accessor address/version array payload
bytes. `NativeBytes` excludes managed entity/version arrays, metadata, and native
allocator overhead. `AccessorBufferBytes` excludes managed array headers. These
are diagnostic counters, not a complete process-memory measurement.

See the [five-ECS campaign](benchmarks/current/REPORT.md) and [API and storage measurements](benchmarks/current/SUPPLEMENT.md) for current results.
