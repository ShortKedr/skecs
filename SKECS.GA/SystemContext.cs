namespace SKECS.GA;

/// <summary>Refers to an entity created by an earlier command in the same invocation.</summary>
public readonly ref struct GaEntityTicket { }

/// <summary>Access to operations bound by a compiled GA system.</summary>
public readonly ref struct SystemContext
{
    /// <summary>Reads a row from the declared immutable tick buffer.</summary>
    public ref readonly T ReadBuffer<T>(int index) where T : unmanaged =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to access the read buffer.");

    /// <summary>Returns the number of rows in the declared immutable tick buffer.</summary>
    public int BufferLength<T>() where T : unmanaged =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to access the read buffer.");

    /// <summary>Reads the value supplied to the runner at the start of the tick.</summary>
    public ref readonly T Resource<T>() where T : unmanaged =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to access the tick input.");

    /// <summary>Accesses the declared required component on the current entity.</summary>
    public ref T Own<T>() where T : unmanaged =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to access own components.");

    /// <summary>Tests whether the current entity has a declared optional component.</summary>
    public bool Has<T>() where T : unmanaged =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to test optional components.");

    /// <summary>Copies the declared optional component or returns false with a default value.</summary>
    public bool TryRead<T>(out T value) where T : unmanaged =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to read optional components.");

    /// <summary>Emits an ordered signed 32-bit event for the current entity.</summary>
    public void Emit(int value) =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to emit events.");

    /// <summary>Sets the domain key for subsequent outputs of this invocation.</summary>
    public void SetDomainOrder(int primary, int secondary) =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to set output order.");

    /// <summary>Reads the bound Int32 snapshot, returning the fallback for an absent or stale key.</summary>
    public int ReadSnapshotInt32(int entityId, int generation, int fallback) =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to read snapshots.");

    /// <summary>Reads a field from the declared snapshot, returning the fallback for an absent or stale key.</summary>
    public T ReadSnapshot<T>(int entityId, int generation, int field, T fallback) where T : unmanaged =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to read snapshots.");

    /// <summary>Requests destruction of the given entity generation at the system commit barrier.</summary>
    public void Destroy(int entityId, int generation) =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to enqueue commands.");

    /// <summary>Requests entity creation at the system commit barrier.</summary>
    public GaEntityTicket Create() =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to enqueue commands.");

    /// <summary>Requests destruction of an earlier creation from this invocation.</summary>
    public void Destroy(GaEntityTicket ticket) =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to enqueue commands.");

    /// <summary>Requests removal of a component from the given entity generation at the system commit barrier.</summary>
    public void Remove<T>(int entityId, int generation) where T : unmanaged =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to enqueue commands.");

    /// <summary>Requests removal of a component from an earlier creation in this invocation.</summary>
    public void Remove<T>(GaEntityTicket ticket) where T : unmanaged =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to enqueue commands.");

    /// <summary>Requests addition of a component to the given entity generation at the system commit barrier.</summary>
    public void Add<T>(int entityId, int generation, T value) where T : unmanaged =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to enqueue commands.");

    /// <summary>Requests addition of a component to an earlier creation in this invocation.</summary>
    public void Add<T>(GaEntityTicket ticket, T value) where T : unmanaged =>
        throw new InvalidOperationException("Run a GA system through its compiled adapter to enqueue commands.");
}
