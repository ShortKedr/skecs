using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SKECS;

public sealed partial class World
{
    private RunnerLease? _runnerOwner;
    private int _legacyRunDepth;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ThrowIfRunnerOwned()
    {
        if (Volatile.Read(ref _runnerOwner) != null)
            throw new InvalidOperationException("A deterministic tick owns this world.");
    }

    internal RunnerLease AcquireRunner()
    {
        ThrowIfDisposed();
        if (_queryDepth != 0 || _legacyRunDepth != 0)
            throw new InvalidOperationException("A deterministic tick cannot begin during a legacy query or system.");
        var lease = new RunnerLease(this);
        if (Interlocked.CompareExchange(ref _runnerOwner, lease, null) != null)
            throw new InvalidOperationException("A deterministic tick already owns this world.");
        return lease;
    }

    // The lease is never passed to processors or result callbacks. Public operations always check ownership.
    internal sealed class RunnerLease
    {
        private readonly World _world;

        internal RunnerLease(World world) => _world = world;

        public Entity Create()
        {
            RequireOwnership();
            return _world.CreateEntityCore(_world._rootArchetype);
        }

        public void Destroy(Entity entity)
        {
            RequireOwnership();
            if (_world.IsAliveCore(entity)) _world.DestroyEntityCore(entity);
        }

        public void Add<T>(Entity entity, int typeId, T value) where T : unmanaged
        {
            RequireOwnership();
            if (_world.IsAliveCore(entity) && !_world._entityRecords[entity.Id].Archetype!.HasComponent(typeId))
                _world.MoveWithAddedComponentCore(entity, typeId, value);
        }

        public void Remove(Entity entity, int typeId)
        {
            RequireOwnership();
            if (_world.IsAliveCore(entity)) _world.RemoveComponentCore(entity, typeId);
        }

        public void Set<T>(Entity entity, int typeId, T value) where T : unmanaged
        {
            RequireOwnership();
            if (_world.IsAliveCore(entity)) _world.SetComponentCore(entity, typeId, value);
        }

        public void Release()
        {
            RequireOwnership();
            Volatile.Write(ref _world._runnerOwner, null);
        }

        public void Discard()
        {
            RequireOwnership();
            _world.DisposeCore();
            Release();
        }

        private void RequireOwnership()
        {
            if (!ReferenceEquals(Volatile.Read(ref _world._runnerOwner), this))
                throw new InvalidOperationException("The deterministic lease no longer owns this world.");
        }
    }
}
