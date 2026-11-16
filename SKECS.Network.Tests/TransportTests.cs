using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using SKECS.Network.Transport;

namespace SKECS.Network.Tests;

public sealed class TransportTests
{
    [Fact]
    public async Task LoopbackBackpressurePreservesOrderOwnershipAndCancellation()
    {
        var (sender, receiver) = LoopbackTransport.CreatePair(maxFrameBytes: 16);
        await using var a = sender;
        await using var b = receiver;
        byte[] buffer = [1, 2, 3];
        await a.SendAsync(buffer);
        buffer[0] = 9;
        using var cancel = new CancellationTokenSource();
        var blocked = a.SendAsync(new byte[] { 10 }, cancel.Token).AsTask();
        Assert.False(blocked.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        Assert.Equal(new byte[] { 1, 2, 3 }, await b.ReceiveAsync());

        await a.SendAsync(buffer);
        var next = a.SendAsync(new byte[] { 7 }).AsTask();
        Assert.False(next.IsCompleted);
        var closing = b.DisposeAsync();
        await closing;
        await Assert.ThrowsAsync<IOException>(() => next);
        Assert.Null(await a.ReceiveAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdaptersPreserveFrameBoundariesAndReportCleanEof(bool tcp)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        var (sender, receiver) = await ReplicationTests.CreateTransport(tcp, token);
        await using var a = sender;
        await using var b = receiver;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => a.SendAsync(new byte[a.MaxFrameBytes + 1], token).AsTask());
        var payload = new byte[] { 0, 4, 7, 0 };
        await a.SendAsync(payload, token);
        Assert.Equal(payload, await b.ReceiveAsync(token));
        await a.SendAsync(ReadOnlyMemory<byte>.Empty, token);
        Assert.Empty((await b.ReceiveAsync(token))!);
        await a.DisposeAsync();
        Assert.Null(await b.ReceiveAsync(token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TcpRejectsOversizedAndTruncatedFrames(bool oversized)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, token);
        await using var receiver = new TcpReplicationTransport(await listener.AcceptTcpClientAsync(token), 16);
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, oversized ? 17 : 2);
        await client.GetStream().WriteAsync(prefix, token);
        client.Client.Shutdown(SocketShutdown.Send);
        if (oversized)
            await Assert.ThrowsAsync<InvalidDataException>(() => receiver.ReceiveAsync(token).AsTask());
        else
            await Assert.ThrowsAsync<EndOfStreamException>(() => receiver.ReceiveAsync(token).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => receiver.ReceiveAsync(token).AsTask());
    }
}
