using System.Threading.Channels;

namespace SKECS.Network.Transport;

public sealed class LoopbackTransport : IReplicationTransport
{
    private readonly Channel<byte[]> _incoming;
    private readonly Channel<byte[]> _outgoing;
    private int _disposed;

    private LoopbackTransport(Channel<byte[]> incoming, Channel<byte[]> outgoing, int maxFrameBytes)
    {
        _incoming = incoming;
        _outgoing = outgoing;
        MaxFrameBytes = maxFrameBytes;
    }

    public TransportDelivery Delivery => TransportDelivery.Reliable | TransportDelivery.Ordered;
    public int MaxFrameBytes { get; }

    public static (LoopbackTransport, LoopbackTransport) CreatePair(int maxFrameBytes, int capacity = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxFrameBytes, Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        var firstToSecond = Channel.CreateBounded<byte[]>(capacity);
        var secondToFirst = Channel.CreateBounded<byte[]>(capacity);
        return (new LoopbackTransport(secondToFirst, firstToSecond, maxFrameBytes),
            new LoopbackTransport(firstToSecond, secondToFirst, maxFrameBytes));
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frame.Length, MaxFrameBytes);
        if (!await _outgoing.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false) ||
            !_outgoing.Writer.TryWrite(frame.ToArray()))
        {
            throw new IOException("The loopback connection is closed.");
        }
    }

    public async ValueTask<byte[]?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (await _incoming.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false) &&
            _incoming.Reader.TryRead(out byte[]? frame))
        {
            return frame;
        }

        return null;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _incoming.Writer.TryComplete();
            _outgoing.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }
}
