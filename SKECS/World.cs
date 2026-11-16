using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace SKECS;

/// <summary>Owns entities and their component storage.</summary>
/// <remarks>Use and dispose a world on one thread at a time. Structural changes and disposal
/// invalidate borrowed component references and chunk views. While a deterministic tick owns the
/// world, world operations throw InvalidOperationException and callers must stop using prior borrows.
/// After disposal, world operations throw ObjectDisposedException. The options remain readable and
/// Dispose is idempotent.</remarks>
public sealed partial class World : IDisposable
{
    private static long _nextWorldId;
    private readonly WorldOptions _options;
    private readonly ComponentTypeRegistry _componentTypes = new();
    private readonly Dictionary<ComponentSetKey, Archetype> _archetypes = new();
    private readonly List<Archetype> _archetypeList = new();
    private readonly Dictionary<QueryCacheKey, QueryCacheEntry> _queryCache = new();
    private readonly List<EntityRecord> _entityRecords = new();
    private readonly List<int> _freeEntityIds = new();
    private readonly Dictionary<SystemStage, List<Action<World>>> _systems = new();
    private readonly Archetype _rootArchetype;
    private readonly Dictionary<(int, int, int), Archetype> _creationArchetypes = new();
    private readonly List<AccessorStorage> _accessors = new();
    private int _queryDepth;
    private bool _disposed;

    public World(WorldOptions options = default)
    {
        Id = Interlocked.Increment(ref _nextWorldId);
        _options = options.ChunkCapacity == 0 ? new WorldOptions() : options;
        if (_options.ComponentPlacement is { HasPackedPools: true } placement)
        {
            if (_options.ChurnControl == null)
                throw new ArgumentException("Packed placement requires churn control.", nameof(options));
            _adaptive = new AdaptiveStorage(placement);
        }
        _rootArchetype = new Archetype(this, ComponentSetKey.Empty, Array.Empty<ComponentType>());
        _archetypes.Add(ComponentSetKey.Empty, _rootArchetype);
        _archetypeList.Add(_rootArchetype);

        _systems[SystemStage.PreUpdate] = new List<Action<World>>();
        _systems[SystemStage.Update] = new List<Action<World>>();
        _systems[SystemStage.PostUpdate] = new List<Action<World>>();

        if (_options.ChurnControl is { } control)
        {
            _entityRecords.Capacity = control.InitialEntityCapacity;
            _freeEntityIds.Capacity = control.InitialEntityCapacity;
            _churnPool = new ChurnChunkPool(ChunkCapacity, control);
        }
    }

    public int ChunkCapacity => _options.ChunkCapacity;

    public bool DiagnosticsEnabled => _options.DiagnosticsEnabled;

    internal long Id { get; }

    /// <summary>Interprets a stored ID and generation in this world. Use IsAlive to check the result.</summary>
    /// <remarks>Supply identifiers obtained from this world. The returned handle is not a liveness
    /// check. Do not serialize handles as raw bytes because their world identity is process-local.</remarks>
    public Entity BindEntity(int id, int version)
    {
        ThrowIfDisposed();
        return new Entity(id, version, Id);
    }

    public Entity CreateEntity()
    {
        ThrowIfDisposed();
        return CreateEntityCore(_rootArchetype);
    }

    private Entity CreateEntityCore(Archetype archetype)
    {
        var slot = ReserveCreation(archetype, out var entity, out var acquiredBacking);
        return PublishCreation(archetype, slot, entity, acquiredBacking);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private EntitySlot ReserveCreation(Archetype archetype, out Entity entity, out bool acquiredBacking)
    {
        EnsureAdaptiveTraversalAllowsStructuralChange();
        var lastFree = _freeEntityIds.Count - 1;
        if (lastFree >= 0)
        {
            var id = _freeEntityIds[lastFree];
            entity = new Entity(id, _entityRecords[id].Version, Id);
        }
        else
        {
            var id = _entityRecords.Count;
            PrepareNewEntityRecord(id);
            entity = new Entity(id, 1, Id);
        }

        return archetype.ReserveEntity(out acquiredBacking);
    }

    private void PrepareNewEntityRecord(int id)
    {
        // Allocation can fail. Reserve capacity before any slot or ID becomes visible.
        foreach (var accessor in _accessors) accessor.EnsureCapacity(id);
        if (id != _entityRecords.Capacity) return;
#if NET8_0_OR_GREATER
        _entityRecords.EnsureCapacity(checked(id + 1));
#else
        _entityRecords.Capacity = Math.Max(4, checked(id * 2));
#endif
        if (_diagnostics != null) RecordDiagnosticEntityTable();
    }

    private Entity PublishCreation(Archetype archetype, EntitySlot slot, Entity entity, bool acquiredBacking)
    {
        archetype.PublishEntity(slot, entity);
        var record = new EntityRecord
        {
            Version = entity.Version, Alive = true, Archetype = archetype,
            ChunkIndex = slot.ChunkIndex, RowIndex = slot.RowIndex
        };
        var lastFree = _freeEntityIds.Count - 1;
        if (lastFree >= 0)
        {
            _freeEntityIds.RemoveAt(lastFree);
            _entityRecords[entity.Id] = record;
        }
        else
        {
            _entityRecords.Add(record);
        }
        RefreshAccessors(entity.Id, record);
        if (_diagnostics != null) RecordDiagnosticCreate(archetype, slot.ChunkIndex, entity.Id);
        if (acquiredBacking || _adaptive != null) MaintainChurnPool();

        return entity;
    }

    private void CancelAdaptiveCreation(Archetype archetype, EntitySlot slot, Entity entity)
    {
        RemovePackedComponents(entity);
        archetype.ReturnIfEmpty(slot.ChunkIndex);
    }

    public Entity CreateEntity<T1>(in T1 component1) where T1 : unmanaged
    {
        ThrowIfDisposed();
        var type1 = _componentTypes.GetOrCreate<T1>().Id;
        var archetype = GetCreationArchetype(type1, -1, -1);
        if (_adaptive == null)
        {
            var offset = archetype.GetComponentOffset(type1);
            var column = ReserveCreation(archetype, out var created, out var acquired);
            column.InitializeComponent(offset, component1);
            return PublishCreation(archetype, column, created, acquired);
        }
        var slot = ReserveCreation(archetype, out var entity, out var acquiredBacking);
        try
        {
            WriteCreatedComponent(archetype, slot, entity, type1, component1);
        }
        catch
        {
            CancelAdaptiveCreation(archetype, slot, entity);
            throw;
        }
        return PublishCreation(archetype, slot, entity, acquiredBacking);
    }

    public Entity CreateEntity<T1, T2>(in T1 component1, in T2 component2)
        where T1 : unmanaged where T2 : unmanaged
        => PrepareEntity<T1, T2>().Create(component1, component2);

    public Entity CreateEntity<T1, T2, T3>(in T1 component1, in T2 component2, in T3 component3)
        where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
    {
        ThrowIfDisposed();
        var type1 = _componentTypes.GetOrCreate<T1>().Id;
        var type2 = _componentTypes.GetOrCreate<T2>().Id;
        var type3 = _componentTypes.GetOrCreate<T3>().Id;
        var archetype = GetCreationArchetype(type1, type2, type3);
        if (_adaptive == null)
        {
            var offset1 = archetype.GetComponentOffset(type1);
            var offset2 = archetype.GetComponentOffset(type2);
            var offset3 = archetype.GetComponentOffset(type3);
            var column = ReserveCreation(archetype, out var created, out var acquired);
            column.InitializeComponent(offset1, component1);
            column.InitializeComponent(offset2, component2);
            column.InitializeComponent(offset3, component3);
            return PublishCreation(archetype, column, created, acquired);
        }
        var slot = ReserveCreation(archetype, out var entity, out var acquiredBacking);
        try
        {
            WriteCreatedComponent(archetype, slot, entity, type1, component1);
            WriteCreatedComponent(archetype, slot, entity, type2, component2);
            WriteCreatedComponent(archetype, slot, entity, type3, component3);
        }
        catch
        {
            CancelAdaptiveCreation(archetype, slot, entity);
            throw;
        }
        return PublishCreation(archetype, slot, entity, acquiredBacking);
    }

    public EntityFactory<T1, T2> PrepareEntity<T1, T2>() where T1 : unmanaged where T2 : unmanaged
    {
        ThrowIfDisposed();
        var type1 = _componentTypes.GetOrCreate<T1>().Id;
        var type2 = _componentTypes.GetOrCreate<T2>().Id;
        var archetype = GetCreationArchetype(type1, type2, -1);
        if (_adaptive == null)
            return new EntityFactory<T1, T2>(this, archetype, archetype.GetComponentOffset(type1), archetype.GetComponentOffset(type2));
        return new EntityFactory<T1, T2>(this, archetype, type1, type2);
    }

    public void CreateEntities<T1, T2>(Span<Entity> output, in T1 component1, in T2 component2)
        where T1 : unmanaged where T2 : unmanaged
        => PrepareEntity<T1, T2>().CreateBatch(output, component1, component2);

    private unsafe Archetype GetCreationArchetype(int type1, int type2, int type3)
    {
        if (type1 == type2 || type1 == type3 || (type2 >= 0 && type2 == type3))
            throw new ArgumentException("A creation template cannot contain duplicate component types.");
        var key = (type1, type2, type3);
        if (_creationArchetypes.TryGetValue(key, out var archetype)) return archetype;
        if (_adaptive != null)
        {
            archetype = GetAdaptiveCreationArchetype(type1, type2, type3);
            _creationArchetypes.Add(key, archetype);
            return archetype;
        }
        var ids = stackalloc int[3];
        ids[0] = type1;
        ids[1] = type2;
        ids[2] = type3;
        archetype = GetArchetype(ComponentSetKey.Create(ids, type3 >= 0 ? 3 : type2 >= 0 ? 2 : 1));
        _creationArchetypes.Add(key, archetype);
        return archetype;
    }

    internal Entity CreateEntityIn<T1, T2>(Archetype archetype, int first, int second, in T1 component1, in T2 component2)
        where T1 : unmanaged where T2 : unmanaged
    {
        ThrowIfDisposed();
        if (_adaptive != null) return CreateAdaptiveEntityIn(archetype, first, second, component1, component2);
        var slot = ReserveCreation(archetype, out var entity, out var acquiredBacking);
        slot.InitializeComponent(first, component1);
        slot.InitializeComponent(second, component2);
        return PublishCreation(archetype, slot, entity, acquiredBacking);
    }

    private Entity CreateAdaptiveEntityIn<T1, T2>(Archetype archetype, int type1, int type2, in T1 component1, in T2 component2)
        where T1 : unmanaged where T2 : unmanaged
    {
        var slot = ReserveCreation(archetype, out var entity, out var acquiredBacking);
        try
        {
            WriteCreatedComponent(archetype, slot, entity, type1, component1);
            WriteCreatedComponent(archetype, slot, entity, type2, component2);
        }
        catch
        {
            CancelAdaptiveCreation(archetype, slot, entity);
            throw;
        }
        return PublishCreation(archetype, slot, entity, acquiredBacking);
    }

    public ComponentAccessor<T> GetAccessor<T>() where T : unmanaged
    {
        ThrowIfDisposed();
        var typeId = _componentTypes.GetOrCreate<T>().Id;
        if (GetPackedPool(typeId) is { } packed) return GetPackedAccessor<T>(packed);
        foreach (var accessor in _accessors)
            if (accessor.TypeId == typeId) return new ComponentAccessor<T>(accessor);
        var storage = new AccessorStorage(typeId, this, typeof(T));
        if (_options.ChurnControl is { InitialEntityCapacity: > 0 } control)
            storage.EnsureCapacity(control.InitialEntityCapacity - 1);
        for (var id = 0; id < _entityRecords.Count; id++)
        {
            storage.EnsureCapacity(id);
            storage.Update(id, _entityRecords[id]);
        }
        _accessors.Add(storage);
        return new ComponentAccessor<T>(storage);
    }

    public StorageStatistics GetStorageStatistics()
    {
        ThrowIfDisposed();
        var chunks = 0;
        var entities = 0;
        long nativeBytes = 0;
        foreach (var archetype in _archetypeList)
        {
            chunks += archetype.Chunks.Count;
            nativeBytes += archetype.NativeBytes;
            foreach (var chunk in archetype.Chunks) entities += chunk.Count;
        }
        long accessorBytes = 0;
        foreach (var accessor in _accessors) accessorBytes += accessor.ManagedBufferBytes;
        if (_churnPool is { } pool)
        {
            nativeBytes = pool.NativeBytes;
            chunks = checked((int)(pool.AllocationCount - pool.ReleaseCount));
        }
        return new StorageStatistics(_archetypeList.Count, chunks, entities, nativeBytes, accessorBytes);
    }

    public bool IsAlive(Entity entity)
    {
        ThrowIfDisposed();
        return IsAliveCore(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsAliveCore(Entity entity)
    {
        if (entity.WorldId != Id || (uint)entity.Id >= (uint)_entityRecords.Count)
        {
            return false;
        }

        var record = _entityRecords[entity.Id];
        return record.Alive && record.Version == entity.Version;
    }

    public void DestroyEntity(Entity entity)
    {
        ValidateEntity(entity);
        DestroyEntityCore(entity);
    }

    private void DestroyEntityCore(Entity entity)
    {
        var record = _entityRecords[entity.Id];
        EnsureAdaptiveTraversalAllowsStructuralChange();
        var canReuse = record.Version < int.MaxValue;
        var grew = canReuse && _freeEntityIds.Count == _freeEntityIds.Capacity;
        if (grew) _freeEntityIds.Capacity = Math.Max(4, checked(_freeEntityIds.Capacity * 2));
        if (_adaptive != null) RemovePackedComponents(entity);
        var removed = record.Archetype!.RemoveAt(record.ChunkIndex, record.RowIndex);
        UpdateMovedEntity(removed);

        record.Archetype = null;
        record.ChunkIndex = -1;
        record.RowIndex = -1;
        record.Alive = false;
        if (canReuse) record.Version++;
        _entityRecords[entity.Id] = record;
        RefreshAccessors(entity.Id, record);
        if (canReuse)
        {
            _freeEntityIds.Add(entity.Id);
            if (grew) RecordDiagnosticFreeTable(_freeEntityIds.Capacity);
        }
        if (_diagnostics != null) RecordDiagnosticDestroy(entity.Id, removed);
        CompleteChurnChange(removed);
    }

    public void Add<T>(Entity entity, in T component)
        where T : unmanaged
    {
        ValidateEntity(entity);
        MoveWithAddedComponentCore(entity, _componentTypes.GetId<T>(), component);
    }

    public void Set<T>(Entity entity, in T component)
        where T : unmanaged
    {
        ValidateEntity(entity);
        SetComponentCore(entity, _componentTypes.GetId<T>(), component);
    }

    private void SetComponentCore<T>(Entity entity, int typeId, in T component) where T : unmanaged
    {
        if (GetPackedPool(typeId) is { } pool)
        {
            var slot = pool.Find(entity);
            if (slot >= 0) pool.ValueAt<T>(slot) = component;
            else
            {
                AddPacked(pool, entity, component);
                MaintainChurnPool();
            }
            return;
        }
        var record = _entityRecords[entity.Id];
        if (record.Archetype!.HasComponent(typeId))
        {
            record.Archetype.WriteComponent(record.ChunkIndex, record.RowIndex, typeId, component);
            return;
        }

        MoveWithAddedComponentCore(entity, typeId, component);
    }

    public bool Has<T>(Entity entity)
        where T : unmanaged
    {
        ValidateEntity(entity);
        var typeId = _componentTypes.GetId<T>();
        return GetPackedPool(typeId) is { } pool ? pool.Find(entity) >= 0
            : _entityRecords[entity.Id].Archetype!.HasComponent(typeId);
    }

    public bool Remove<T>(Entity entity)
        where T : unmanaged
    {
        ValidateEntity(entity);
        var typeId = _componentTypes.GetId<T>();
        return RemoveComponentCore(entity, typeId);
    }

    private bool RemoveComponentCore(Entity entity, int typeId)
    {
        if (GetPackedPool(typeId) is { } pool)
        {
            if (!RemovePacked(pool, entity)) return false;
            MaintainChurnPool();
            return true;
        }
        var record = _entityRecords[entity.Id];
        if (!record.Archetype!.HasComponent(typeId))
        {
            return false;
        }

        EnsureAdaptiveTraversalAllowsStructuralChange();
        var transition = record.Archetype.GetRemoveTransition(typeId);
        var destination = transition.Destination;
        var slot = destination.ReserveEntity(out var acquiredBacking);
        try
        {
            transition.Copy(record.ChunkIndex, record.RowIndex, slot);
        }
        catch
        {
            destination.ReturnIfEmpty(slot.ChunkIndex);
            throw;
        }
        destination.PublishEntity(slot, entity);
        var removed = record.Archetype.RemoveAt(record.ChunkIndex, record.RowIndex);
        UpdateMovedEntity(removed);

        record.Archetype = destination;
        record.ChunkIndex = slot.ChunkIndex;
        record.RowIndex = slot.RowIndex;
        _entityRecords[entity.Id] = record;
        RefreshAccessors(entity.Id, record);
        if (_diagnostics != null) RecordDiagnosticTransition(transition, removed, slot.ChunkIndex);
        CompleteChurnChange(removed, acquiredBacking);
        return true;
    }

    /// <summary>Returns a component reference borrowed until the next structural change or disposal.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(Entity entity)
        where T : unmanaged
    {
        ThrowIfDisposed();
#if NET8_0_OR_GREATER
        var records = CollectionsMarshal.AsSpan(_entityRecords);
        if (entity.WorldId != Id || (uint)entity.Id >= (uint)records.Length) ThrowDeadEntity(entity);
        ref var record = ref records[entity.Id];
#else
        if (entity.WorldId != Id || (uint)entity.Id >= (uint)_entityRecords.Count) ThrowDeadEntity(entity);
        var record = _entityRecords[entity.Id];
#endif
        if (!record.Alive || record.Version != entity.Version) ThrowDeadEntity(entity);

        var typeId = _componentTypes.GetId<T>();
        if (_adaptive is { } storage && ResolvePackedPool(storage, typeId) is { } pool)
            return ref pool.Get<T>(entity);
        return ref record.Archetype!.GetComponentRef<T>(record.ChunkIndex, record.RowIndex, typeId);
    }

    public bool TryGet<T>(Entity entity, out T value)
        where T : unmanaged
    {
        if (!IsAlive(entity))
        {
            value = default;
            return false;
        }

        var typeId = _componentTypes.GetId<T>();
        if (GetPackedPool(typeId) is { } pool)
        {
            var slot = pool.Find(entity);
            if (slot >= 0) { value = pool.ValueAt<T>(slot); return true; }
            value = default;
            return false;
        }
        var record = _entityRecords[entity.Id];
        if (!record.Archetype!.HasComponent(typeId))
        {
            value = default;
            return false;
        }

        value = record.Archetype.ReadComponent<T>(record.ChunkIndex, record.RowIndex, typeId);
        return true;
    }

    public Query<T1> Query<T1>()
        where T1 : unmanaged
    {
        unsafe
        {
            var include = stackalloc int[1];
            include[0] = GetComponentType<T1>().Id;
            return new Query<T1>(this, GetOrCreateQueryCache(new QueryCacheKey(ComponentSetKey.Create(include, 1), ComponentSetKey.Empty)));
        }
    }

    public Query<T1, T2> Query<T1, T2>()
        where T1 : unmanaged
        where T2 : unmanaged
    {
        unsafe
        {
            var include = stackalloc int[2];
            include[0] = GetComponentType<T1>().Id;
            include[1] = GetComponentType<T2>().Id;
            return new Query<T1, T2>(this, GetOrCreateQueryCache(new QueryCacheKey(ComponentSetKey.Create(include, 2), ComponentSetKey.Empty)));
        }
    }

    public Query<T1, T2, T3> Query<T1, T2, T3>()
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
    {
        unsafe
        {
            var include = stackalloc int[3];
            include[0] = GetComponentType<T1>().Id;
            include[1] = GetComponentType<T2>().Id;
            include[2] = GetComponentType<T3>().Id;
            return new Query<T1, T2, T3>(this, GetOrCreateQueryCache(new QueryCacheKey(ComponentSetKey.Create(include, 3), ComponentSetKey.Empty)));
        }
    }

    public QueryBuilder QueryBuilder()
    {
        ThrowIfDisposed();
        return new QueryBuilder(this);
    }

    public void AddSystem(SystemStage stage, Action<World> system)
    {
        ThrowIfDisposed();
        _systems[stage].Add(system);
    }

    public void Run(SystemStage stage)
    {
        ThrowIfDisposed();
        var systems = _systems[stage];
        _legacyRunDepth++;
        try
        {
            for (var i = 0; i < systems.Count && !_disposed; i++)
                systems[i](this);
        }
        finally
        {
            _legacyRunDepth--;
        }
    }

    public void RunAll()
    {
        Run(SystemStage.PreUpdate);
        if (!_disposed) Run(SystemStage.Update);
        if (!_disposed) Run(SystemStage.PostUpdate);
        if (!_disposed) MaintainChurnPool();
    }

    internal ComponentType GetComponentType<T>()
        where T : unmanaged
    {
        var type = GetAdaptiveComponentType<T>();
        EnsureArchetypeType(type.Id);
        return type;
    }

    internal QueryCacheEntry GetOrCreateQueryCache(QueryCacheKey key)
    {
        ThrowIfDisposed();
        if (_queryCache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var entry = new QueryCacheEntry(this);
        for (var i = 0; i < _archetypeList.Count; i++)
        {
            if (key.Matches(_archetypeList[i].Key))
            {
                entry.Matches.Add(_archetypeList[i]);
            }
        }

        _queryCache.Add(key, entry);
        return entry;
    }

    internal void RegisterArchetype(Archetype archetype)
    {
        _archetypes.Add(archetype.Key, archetype);
        _archetypeList.Add(archetype);

        foreach (var pair in _queryCache)
        {
            if (pair.Key.Matches(archetype.Key))
            {
                pair.Value.Matches.Add(archetype);
            }
        }
        if (_diagnostics != null) RecordDiagnosticArchetype(archetype, _archetypeList.Count - 1);
    }

    internal Archetype GetArchetype(ComponentSetKey key)
    {
        if (_archetypes.TryGetValue(key, out var archetype))
        {
            return archetype;
        }

        archetype = new Archetype(this, key, _componentTypes.ResolveAll(key.TypeIds));
        RegisterArchetype(archetype);
        return archetype;
    }

    private void MoveWithAddedComponentCore<T>(Entity entity, int typeId, in T component)
        where T : unmanaged
    {
        EnsureAdaptiveTraversalAllowsStructuralChange();
        if (GetPackedPool(typeId) is { } pool)
        {
            AddPacked(pool, entity, component);
            MaintainChurnPool();
            return;
        }
        var record = _entityRecords[entity.Id];
        var source = record.Archetype!;

        if (source.HasComponent(typeId))
            throw new InvalidOperationException($"Entity {entity} already has component {typeof(T).Name}.");

        var transition = source.GetAddTransition(typeId);
        var destination = transition.Destination;
        var slot = destination.ReserveEntity(out var acquiredBacking);
        try
        {
            transition.Copy(record.ChunkIndex, record.RowIndex, slot);
            destination.WriteComponent(slot.ChunkIndex, slot.RowIndex, typeId, component);
        }
        catch
        {
            destination.ReturnIfEmpty(slot.ChunkIndex);
            throw;
        }
        destination.PublishEntity(slot, entity);
        var removed = source.RemoveAt(record.ChunkIndex, record.RowIndex);
        UpdateMovedEntity(removed);

        record.Archetype = destination;
        record.ChunkIndex = slot.ChunkIndex;
        record.RowIndex = slot.RowIndex;
        _entityRecords[entity.Id] = record;
        RefreshAccessors(entity.Id, record);
        if (_diagnostics != null) RecordDiagnosticTransition(transition, removed, slot.ChunkIndex);
        CompleteChurnChange(removed, acquiredBacking);
    }

    private void UpdateMovedEntity(Archetype.RemovalResult removed)
    {
        if (removed.MovedEntityId < 0)
        {
            return;
        }

        var moved = _entityRecords[removed.MovedEntityId];
        if (!ReferenceEquals(moved.Archetype, removed.Source))
        {
            return;
        }

        moved.Archetype = removed.Source;
        moved.ChunkIndex = removed.MovedChunkIndex;
        moved.RowIndex = removed.MovedRowIndex;
        _entityRecords[removed.MovedEntityId] = moved;
        RefreshAccessors(removed.MovedEntityId, moved);
    }

    private void RefreshAccessors(int id, EntityRecord record)
    {
        for (var i = 0; i < _accessors.Count; i++) _accessors[i].Update(id, record);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateEntity(Entity entity)
    {
        ThrowIfDisposed();
        if (!IsAliveCore(entity)) ThrowDeadEntity(entity);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowDeadEntity(Entity entity) =>
        throw new InvalidOperationException($"Entity {entity} is not alive in this world.");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(World));
        ThrowIfRunnerOwned();
    }

    internal void BeginQuery()
    {
        ThrowIfDisposed();
        _queryDepth++;
    }

    internal void EndQuery() => _queryDepth--;

    public void Dispose()
    {
        ThrowIfRunnerOwned();
        if (_disposed)
        {
            return;
        }

        if (_queryDepth != 0)
            throw new InvalidOperationException("A world cannot be disposed during Query.ForEach.");

        DisposeCore();
    }

    private void DisposeCore()
    {
        _disposed = true;
        foreach (var accessor in _accessors) accessor.Invalidate();
        for (var i = 0; i < _archetypeList.Count; i++)
        {
            _archetypeList[i].Dispose();
        }
        ReleasePackedPools();
        _churnPool?.Dispose();
        _diagnostics?.DisposeWorld();
    }

    internal readonly unsafe struct EntitySlot
    {
        public EntitySlot(Archetype.Chunk chunk, int rowIndex)
        {
            Chunk = chunk;
            RowIndex = rowIndex;
        }

        public Archetype.Chunk Chunk { get; }

        public int ChunkIndex => Chunk.SlotIndex;
        public int RowIndex { get; }

        // The offset was validated before reserving this slot. The unmanaged write cannot allocate.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void InitializeComponent<T>(int offset, in T value) where T : unmanaged =>
            ((T*)(Chunk.Memory + offset))[RowIndex] = value;
    }

    internal struct EntityRecord
    {
        public int Version;
        public bool Alive;
        public Archetype? Archetype;
        public int ChunkIndex;
        public int RowIndex;
    }
}
