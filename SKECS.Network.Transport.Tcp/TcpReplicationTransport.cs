using System.Buffers.Binary;
using System.Net.Sockets;

namespace SKECS.Network.Transport;

public sealed class TcpReplicationTransport : IReplicationTransport
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly byte[] _sendHeader = new byte[sizeof(int)];
    private readonly byte[] _receiveHeader = new byte[sizeof(int)];
    private int _disposed;

    public TcpReplicationTransport(TcpClient client, int maxFrameBytes)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxFrameBytes, Array.MaxLength);
        _stream = client.GetStream();
        _client = client;
        MaxFrameBytes = maxFrameBytes;
    }

    public TransportDelivery Delivery => TransportDelivery.Reliable | TransportDelivery.Ordered;
    public int MaxFrameBytes { get; }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frame.Length, MaxFrameBytes);
        cancellationToken.ThrowIfCancellationRequested();

        BinaryPrimitives.WriteInt32LittleEndian(_sendHeader, frame.Length);
        try
        {
            await _stream.WriteAsync(_sendHeader, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Close();
            throw;
        }
    }

    public async ValueTask<byte[]?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            int read = await _stream.ReadAsync(_receiveHeader, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return null;

            await _stream.ReadExactlyAsync(_receiveHeader.AsMemory(read), cancellationToken).ConfigureAwait(false);
            int length = BinaryPrimitives.ReadInt32LittleEndian(_receiveHeader);
            if (length < 0 || length > MaxFrameBytes)
                throw new InvalidDataException("The TCP frame length is out of range.");

            var frame = new byte[length];
            await _stream.ReadExactlyAsync(frame, cancellationToken).ConfigureAwait(false);
            return frame;
        }
        catch
        {
            Close();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        Close();
        return ValueTask.CompletedTask;
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _client.Dispose();
    }
}
