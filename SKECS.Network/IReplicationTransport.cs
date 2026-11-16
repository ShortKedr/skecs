namespace SKECS.Network;

[Flags]
public enum TransportDelivery
{
    Reliable = 1,
    Ordered = 2
}

/// <summary>One connected, bidirectional framed path owned by a replication session.</summary>
/// <remarks>
/// One send and one receive may overlap. Send can wait for space and may borrow its input until it
/// completes. Receive returns an owned frame or null after a clean end of stream. The adapter must
/// enforce MaxFrameBytes before allocating an inbound frame. Disposal releases pending operations.
/// </remarks>
public interface IReplicationTransport : IAsyncDisposable
{
    TransportDelivery Delivery { get; }
    int MaxFrameBytes { get; }
    ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default);
    ValueTask<byte[]?> ReceiveAsync(CancellationToken cancellationToken = default);
}
