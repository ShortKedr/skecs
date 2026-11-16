using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using SKECS.Serialization;
using static SKECS.Network.ReplicationFrame;

namespace SKECS.Network;

/// <summary>A stop-and-wait stream from one authority to one replica.</summary>
/// <remarks>
/// The host supplies an authorized transport and owns the tick schedule. Start one operation at a
/// time on each session. Dispose may interrupt transport operations. A replica's world and all its
/// handles belong to the current state and are valid only until the next successful apply or disposal.
/// </remarks>
public sealed class ReplicationSession : IAsyncDisposable
{
    private readonly IReplicationTransport _transport;
    private readonly ReplicationOptions _options;
    private readonly CheckpointSnapshot _checkpoint;
    private readonly object _stateLock = new();
    private readonly int _maxCheckpointBytes;
    private ReplicaState? _current;
    private Baseline? _baseline;
    private Baseline? _outstanding;
    private ReplicationFrame? _pending;
    private byte[]? _reply;
    private ulong _sequence;
    private ulong _tick;
    private bool _outstandingDelta;
    private bool _forceFull;
    private int _busy;
    private int _closed;
    private int _disposed;

    private ReplicationSession(IReplicationTransport transport, ReplicationOptions options,
        CodecRegistry registry, ReplicationHandshake handshake)
    {
        _transport = transport;
        _options = options;
        SessionId = handshake.Session;
        MaxFrameBytes = handshake.FrameBytes;
        DeltaEnabled = handshake.Delta;
        _maxCheckpointBytes = MaxFrameBytes - HeaderBytes - DigestBytes;
        _checkpoint = new CheckpointSnapshot(registry, _maxCheckpointBytes, handshake.Slots);
    }

    public Guid SessionId { get; }
    public int MaxFrameBytes { get; }
    public bool DeltaEnabled { get; }
    public ReplicaState? Current => Volatile.Read(ref _current);

    /// <summary>Looks up an entity in the authority's most recently published snapshot.</summary>
    public bool TryGetNetworkEntityId(Entity entity, out NetworkEntityId id)
    {
        using var operation = Begin(ReplicationRole.Authority, default);
        if (_checkpoint.TryGetCapturedId(entity, out var value))
        {
            id = new NetworkEntityId(SessionId, value);
            return true;
        }
        id = default;
        return false;
    }

    /// <summary>Negotiates the session and takes ownership of the connected transport.</summary>
    public static async ValueTask<ReplicationSession> ConnectAsync(IReplicationTransport transport,
        ReplicationOptions options, CodecRegistry registry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(registry);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handshake = await ReplicationHandshake.ExchangeAsync(transport, options, registry, cancellationToken)
                .ConfigureAwait(false);
            return new ReplicationSession(transport, options, registry, handshake);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Captures the completed authoritative world synchronously, then sends its frame.</summary>
    public async ValueTask<SnapshotSendMetrics> PublishAsync(World world, ulong tick,
        CancellationToken cancellationToken = default)
    {
        using var operation = Begin(ReplicationRole.Authority, cancellationToken);
        if (_outstanding is not null)
            throw new InvalidOperationException("Wait for the outstanding snapshot acknowledgement.");
        if (_sequence != 0 && (tick < _tick || tick == _tick && !_forceFull))
            throw new ArgumentOutOfRangeException(nameof(tick), "Snapshot ticks must increase between commits.");
        var sequence = checked(_sequence + 1);
        var start = Stopwatch.GetTimestamp();
        var checkpoint = _checkpoint.Capture(world);
        var captured = Stopwatch.GetTimestamp();
        var candidate = new Baseline(sequence, tick, checkpoint, SHA256.HashData(checkpoint));
        var (frame, kind) = Encode(candidate);
        var encoded = Stopwatch.GetTimestamp();

        await SendAsync(frame, cancellationToken).ConfigureAwait(false);
        var sent = Stopwatch.GetTimestamp();
        _outstanding = candidate;
        _outstandingDelta = kind == SnapshotEncoding.Delta;
        _sequence = sequence;
        _tick = tick;

        return new SnapshotSendMetrics(new SnapshotNotice(kind, sequence, tick, frame.Length),
            Stopwatch.GetElapsedTime(start, captured), Stopwatch.GetElapsedTime(captured, encoded),
            Stopwatch.GetElapsedTime(encoded, sent));
    }

    /// <summary>Accepts a confirmation for the frame in flight or requests its replacement with a full snapshot.</summary>
    public async ValueTask<SnapshotResult> WaitForAcknowledgementAsync(CancellationToken cancellationToken = default)
    {
        using var operation = Begin(ReplicationRole.Authority, cancellationToken);
        var sent = _outstanding ?? throw new InvalidOperationException("Publish a snapshot before waiting for its acknowledgement.");
        try
        {
            var reply = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (reply.Sequence != sent.Sequence || reply.Tick != sent.Tick)
                throw new InvalidDataException("Acknowledgement does not match the outstanding snapshot.");
            if (reply.Kind == FrameKind.Ack && reply.Payload.SequenceEqual(sent.Digest))
            {
                _baseline = DeltaEnabled ? sent : null;
                _forceFull = false;
                _outstanding = null;
                return SnapshotResult.Applied;
            }
            if (reply.Kind == FrameKind.Resync && reply.Payload.IsEmpty && _outstandingDelta)
            {
                _baseline = null;
                _forceFull = true;
                _outstanding = null;
                return SnapshotResult.ResyncRequired;
            }
            throw new InvalidDataException("Invalid replication acknowledgement or resync request.");
        }
        catch
        {
            await CloseTransportAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Receives and stages one frame. The current world is untouched until apply.</summary>
    public async ValueTask<SnapshotNotice> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        using var operation = Begin(ReplicationRole.Replica, cancellationToken);
        if (_pending is not null || _reply is not null)
            throw new InvalidOperationException("Apply and confirm the previously received snapshot.");
        try
        {
            var frame = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (_sequence == ulong.MaxValue || frame.Sequence != _sequence + 1 ||
                _sequence != 0 && (frame.Tick < _tick || frame.Tick == _tick && !_forceFull) ||
                frame.Kind is not (FrameKind.Full or FrameKind.Delta))
                throw new InvalidDataException("Unexpected replication snapshot kind, sequence or tick.");
            if (frame.Kind == FrameKind.Delta && (!DeltaEnabled || _forceFull) ||
                frame.Payload.Length <= DigestBytes + (frame.Kind == FrameKind.Delta ? DeltaHeaderBytes : 0))
                throw new InvalidDataException("Unexpected delta or missing snapshot data.");
            _pending = frame;
            return frame.Notice;
        }
        catch
        {
            await CloseTransportAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Checks and replaces the replica world at a host-controlled completed tick boundary.</summary>
    /// <remarks>Successful application completes synchronously. Exclude all concurrent access to the replica world.</remarks>
    public async ValueTask<SnapshotResult> ApplyAtBoundaryAsync(CancellationToken cancellationToken = default)
    {
        using var operation = Begin(ReplicationRole.Replica, cancellationToken);
        var frame = _pending ?? throw new InvalidOperationException("Receive a snapshot before applying it.");
        try
        {
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                return Apply(frame);
            }
        }
        catch
        {
            await CloseTransportAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Sends the prepared acknowledgement or resync request after the boundary operation.</summary>
    public async ValueTask ConfirmAsync(CancellationToken cancellationToken = default)
    {
        using var operation = Begin(ReplicationRole.Replica, cancellationToken);
        var reply = _reply ?? throw new InvalidOperationException("Apply a snapshot before confirming it.");
        await SendAsync(reply, cancellationToken).ConfigureAwait(false);
        _reply = null;
    }

    /// <summary>Drops only the replica's retained delta data. An incoming delta then requests resync.</summary>
    public void ForgetBaseline()
    {
        using var operation = Begin(ReplicationRole.Replica, default);
        if (_pending is not null || _reply is not null)
            throw new InvalidOperationException("Complete the snapshot in flight before dropping the baseline.");
        _baseline = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            await CloseTransportAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_stateLock)
                Interlocked.Exchange(ref _current, null)?.DisposeWorld();
        }
    }

    private (byte[] Frame, SnapshotEncoding Kind) Encode(Baseline target)
    {
        var basis = _baseline;
        byte[]? delta = basis is not null && !_forceFull
            ? CheckpointDelta.Encode(basis.Bytes, target.Bytes) : null;
        if (basis is not null && delta is not null && delta.Length + DeltaHeaderBytes < target.Bytes.Length)
        {
            var frame = Create(FrameKind.Delta, SessionId, target.Sequence, target.Tick,
                DigestBytes + DeltaHeaderBytes + delta.Length);
            var payload = frame.AsSpan(HeaderBytes);
            target.Digest.CopyTo(payload);
            BinaryPrimitives.WriteUInt64LittleEndian(payload[DigestBytes..], basis.Sequence);
            basis.Digest.CopyTo(payload[(DigestBytes + sizeof(ulong))..]);
            delta.CopyTo(payload[(DigestBytes + DeltaHeaderBytes)..]);
            return (frame, SnapshotEncoding.Delta);
        }

        var full = Create(FrameKind.Full, SessionId, target.Sequence, target.Tick, DigestBytes + target.Bytes.Length);
        target.Digest.CopyTo(full.AsSpan(HeaderBytes));
        target.Bytes.CopyTo(full.AsSpan(HeaderBytes + DigestBytes));
        return (full, SnapshotEncoding.Full);
    }

    private SnapshotResult Apply(ReplicationFrame frame)
    {
        var payload = frame.Payload;
        byte[] checkpoint;
        if (frame.Kind == FrameKind.Delta)
        {
            var basis = _baseline;
            var sequence = BinaryPrimitives.ReadUInt64LittleEndian(payload[DigestBytes..]);
            if (sequence == 0 || sequence >= frame.Sequence)
                throw new InvalidDataException("Invalid replication baseline sequence.");
            if (basis is null || sequence != basis.Sequence ||
                !payload.Slice(DigestBytes + sizeof(ulong), DigestBytes).SequenceEqual(basis.Digest))
            {
                PrepareReply(frame, FrameKind.Resync, []);
                _baseline = null;
                _forceFull = true;
                return SnapshotResult.ResyncRequired;
            }
            // Patch the retained bytes into a new candidate. The baseline remains unchanged on failure.
            checkpoint = CheckpointDelta.Decode(basis.Bytes, payload[(DigestBytes + DeltaHeaderBytes)..], _maxCheckpointBytes);
        }
        else
        {
            checkpoint = payload[DigestBytes..].ToArray();
        }

        var digest = SHA256.HashData(checkpoint);
        if (!payload[..DigestBytes].SequenceEqual(digest))
            throw new InvalidDataException("Snapshot digest does not match the reconstructed checkpoint.");
        var baseline = DeltaEnabled ? new Baseline(frame.Sequence, frame.Tick, checkpoint, digest) : null;
        var acknowledgement = Create(FrameKind.Ack, SessionId, frame.Sequence, frame.Tick, DigestBytes);
        digest.CopyTo(acknowledgement.AsSpan(HeaderBytes));

        var restored = _checkpoint.Restore(SessionId, frame.Tick, checkpoint, _options.ReplicaWorldOptions, _current);
        Interlocked.Exchange(ref _current, restored)?.DisposeWorld();
        _reply = acknowledgement;
        _sequence = frame.Sequence;
        _tick = frame.Tick;
        _pending = null;
        _baseline = baseline;
        _forceFull = false;
        return SnapshotResult.Applied;
    }

    private void PrepareReply(ReplicationFrame frame, FrameKind kind, ReadOnlySpan<byte> payload)
    {
        _reply = Create(kind, SessionId, frame.Sequence, frame.Tick, payload.Length);
        payload.CopyTo(_reply.AsSpan(HeaderBytes));
        _sequence = frame.Sequence;
        _tick = frame.Tick;
        _pending = null;
    }

    private async ValueTask<ReplicationFrame> ReadAsync(CancellationToken cancellationToken)
    {
        var bytes = await _transport.ReceiveAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new EndOfStreamException("Replication peer disconnected.");
        var frame = Parse(bytes, MaxFrameBytes);
        if (frame.Session != SessionId) throw new InvalidDataException("Replication frame belongs to a different session.");
        return frame;
    }

    private async ValueTask SendAsync(byte[] frame, CancellationToken cancellationToken)
    {
        try
        {
            await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await CloseTransportAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask CloseTransportAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
            await _transport.DisposeAsync().ConfigureAwait(false);
    }

    private Operation Begin(ReplicationRole role, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0 || Volatile.Read(ref _disposed) != 0, this);
        if (_options.Role != role) throw new InvalidOperationException("Operation is invalid for this replication role.");
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            throw new InvalidOperationException("A replication operation is already in progress.");
        return new Operation(this);
    }

    private sealed record Baseline(ulong Sequence, ulong Tick, byte[] Bytes, byte[] Digest);

    private readonly struct Operation(ReplicationSession owner) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref owner._busy, 0);
    }
}
