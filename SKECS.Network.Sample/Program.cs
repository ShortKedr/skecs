using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using SKECS;
using SKECS.Network;
using SKECS.Network.Transport;
using SKECS.Serialization;

if (args is ["--help"])
{
    Console.WriteLine("dotnet run -c Release --project SKECS.Network.Sample -- [--transport loopback|tcp] [--mode full|delta] [--entities N] [--ticks N] [--warmup N]");
    Console.WriteLine("Frame byte totals count snapshot protocol frames. TCP length prefixes and acknowledgement frames are excluded.");
    Console.WriteLine("Receive await includes time spent waiting for publication and can overlap capture, encode and send. Round trip spans publication through acknowledgement. Verification is timed separately.");
    return;
}

string transport = "loopback";
string mode = "delta";
int entities = 1_000;
int ticks = 30;
int warmup = 5;
for (int index = 0; index < args.Length; index += 2)
{
    string value = index + 1 < args.Length ? args[index + 1] :
        throw new ArgumentException($"Missing value for {args[index]}.");
    switch (args[index])
    {
        case "--transport" when value is "loopback" or "tcp":
            transport = value;
            break;
        case "--mode" when value is "full" or "delta":
            mode = value;
            break;
        case "--entities" when int.TryParse(value, out int count) && count is >= 1 and <= 65_536:
            entities = count;
            break;
        case "--ticks" when int.TryParse(value, out int count) && count > 0:
            ticks = count;
            break;
        case "--warmup" when int.TryParse(value, out int count) && count >= 0:
            warmup = count;
            break;
        default:
            throw new ArgumentException($"Invalid option {args[index]} {value}. Use --help for usage.");
    }
}

const int sourceChunkCapacity = 128;
const int mirrorChunkCapacity = 256;
const int frameLimit = 16 * 1024 * 1024;
int changedPerTick = Math.Max(1, entities / 100);
using var setupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var (authorityPath, replicaPath) = await CreateTransportPairAsync(transport, frameLimit, setupTimeout.Token);
await using var authorityTransport = authorityPath;
await using var replicaTransport = replicaPath;
var authorityId = Guid.NewGuid();
var replicaId = Guid.NewGuid();
var authorityOptions = new ReplicationOptions(authorityId, replicaId, ReplicationRole.Authority, "sample-mover-v1")
{
    EnableDelta = mode == "delta",
    MaxFrameBytes = frameLimit,
    MaxSlots = entities
};
var replicaOptions = authorityOptions with
{
    LocalPeerId = replicaId,
    ExpectedPeerId = authorityId,
    Role = ReplicationRole.Replica,
    ReplicaWorldOptions = new WorldOptions(mirrorChunkCapacity)
};
var authorityConnect = ReplicationSession.ConnectAsync(authorityTransport, authorityOptions, CreateRegistry(), setupTimeout.Token).AsTask();
var replicaConnect = ReplicationSession.ConnectAsync(replicaTransport, replicaOptions, CreateRegistry(), setupTimeout.Token).AsTask();
await Task.WhenAll(authorityConnect, replicaConnect);
await using var authority = await authorityConnect;
await using var replica = await replicaConnect;

using var source = new World(new WorldOptions(sourceChunkCapacity));
var handles = new Entity[entities];
var expected = new Position[entities];
var seen = new bool[entities];
for (int index = 0; index < entities; index++)
{
    expected[index] = new Position(index, index * 3, -index * 5);
    handles[index] = source.CreateEntity(expected[index]);
}

var bootstrap = await ReplicateAsync(0);
var warmupSamples = new Samples();
var measuredSamples = new Samples();
int cursor = 0;
for (long index = 0; index < (long)warmup + ticks; index++)
{
    Advance(source, handles, expected, changedPerTick, ref cursor);
    var sample = await ReplicateAsync((ulong)index + 1);
    (index < warmup ? warmupSamples : measuredSamples).Add(sample);
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    scenario = "sparse-mover-position-v1",
    transport,
    mode,
    entities,
    changedPerTick,
    sourceChunkCapacity,
    mirrorChunkCapacity,
    bootstrap = new { encoding = bootstrap.Snapshot.Encoding.ToString(), frameBytes = bootstrap.Snapshot.FrameBytes },
    warmup = new { samples = warmup, full = warmupSamples.Full, delta = warmupSamples.Delta },
    measured = new
    {
        samples = ticks,
        full = measuredSamples.Full,
        delta = measuredSamples.Delta,
        snapshotFrameBytes = measuredSamples.FrameBytes,
        fullFrameBytes = measuredSamples.FullFrameBytes,
        deltaFrameBytes = measuredSamples.DeltaFrameBytes,
        meanMilliseconds = new
        {
            capture = measuredSamples.Capture / ticks,
            encode = measuredSamples.Encode / ticks,
            send = measuredSamples.Send / ticks,
            receiveAwait = measuredSamples.Receive / ticks,
            apply = measuredSamples.Apply / ticks,
            acknowledgement = measuredSamples.Acknowledgement / ticks,
            roundTrip = measuredSamples.RoundTrip / ticks,
            verify = measuredSamples.Verify / ticks
        }
    }
}));

async ValueTask<TickSample> ReplicateAsync(ulong tick)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var token = timeout.Token;
    var reception = ReceiveTimedAsync(replica, token);
    var roundTripStart = Stopwatch.GetTimestamp();
    var send = await authority.PublishAsync(source, tick, token);
    var (received, receiveTime) = await reception;
    if (received != send.Snapshot)
        throw new InvalidDataException("The receiver staged a different frame.");

    var applyStart = Stopwatch.GetTimestamp();
    var applied = await replica.ApplyAtBoundaryAsync(token);
    var ackStart = Stopwatch.GetTimestamp();
    if (applied != SnapshotResult.Applied)
        throw new InvalidDataException($"Replica could not apply tick {tick}.");

    var reply = authority.WaitForAcknowledgementAsync(token);
    await replica.ConfirmAsync(token);
    var acknowledgement = await reply;
    var ackEnd = Stopwatch.GetTimestamp();
    if (acknowledgement != SnapshotResult.Applied)
        throw new InvalidDataException($"Authority could not acknowledge tick {tick}.");

    var verifyStart = Stopwatch.GetTimestamp();
    VerifyReplica(replica.Current, tick, expected, seen, mirrorChunkCapacity);
    return new TickSample(send.Snapshot, send.Capture, send.Encode, send.Send, receiveTime,
        Stopwatch.GetElapsedTime(applyStart, ackStart), Stopwatch.GetElapsedTime(ackStart, ackEnd),
        Stopwatch.GetElapsedTime(roundTripStart, ackEnd), Stopwatch.GetElapsedTime(verifyStart));
}

static async ValueTask<(SnapshotNotice Snapshot, TimeSpan Elapsed)> ReceiveTimedAsync(
    ReplicationSession replica, CancellationToken cancellationToken)
{
    var start = Stopwatch.GetTimestamp();
    var snapshot = await replica.ReceiveAsync(cancellationToken);
    return (snapshot, Stopwatch.GetElapsedTime(start));
}

static void Advance(World world, Entity[] handles, Position[] expected, int changed, ref int cursor)
{
    for (int offset = 0; offset < changed; offset++)
    {
        ref var position = ref world.Get<Position>(handles[cursor]);
        position = position with { X = unchecked(position.X + 3), Y = unchecked(position.Y - 2) };
        var previous = expected[cursor];
        expected[cursor] = new Position(cursor, unchecked(previous.X + 3), unchecked(previous.Y - 2));
        cursor = (cursor + 1) % handles.Length;
    }
}

static void VerifyReplica(ReplicaState? state, ulong tick, Position[] expected, bool[] seen, int chunkCapacity)
{
    if (state is null || state.Tick != tick || state.EntityCount != expected.Length ||
        state.World.ChunkCapacity != chunkCapacity)
        throw new InvalidDataException($"Unexpected replica metadata at tick {tick}.");

    Array.Clear(seen);
    var verifier = new PositionVerifier(expected, seen);
    state.World.Query<Position>().ForEach(ref verifier);
    if (verifier.Count != expected.Length)
        throw new InvalidDataException($"A position is missing at tick {tick}.");
}

static CodecRegistry CreateRegistry() => new CodecRegistry().Register(
    new ComponentSchema(1, 1, new FieldSchema(1, FieldKind.Int32), new FieldSchema(2, FieldKind.Int32),
        new FieldSchema(3, FieldKind.Int32)), new PositionCodec());

static async ValueTask<(IReplicationTransport, IReplicationTransport)> CreateTransportPairAsync(
    string transport, int frameLimit, CancellationToken cancellationToken)
{
    if (transport == "loopback")
    {
        var (first, second) = LoopbackTransport.CreatePair(frameLimit);
        return (first, second);
    }

    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var client = new TcpClient(AddressFamily.InterNetwork);
    TcpClient? accepted = null;
    try
    {
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        await client.ConnectAsync(endpoint.Address, endpoint.Port, cancellationToken);
        accepted = await listener.AcceptTcpClientAsync(cancellationToken);
        client.NoDelay = true;
        accepted.NoDelay = true;
        return (new TcpReplicationTransport(client, frameLimit), new TcpReplicationTransport(accepted, frameLimit));
    }
    catch
    {
        client.Dispose();
        accepted?.Dispose();
        throw;
    }
    finally
    {
        listener.Stop();
    }
}

internal readonly record struct Position(int Index, int X, int Y);

internal sealed class PositionCodec : IComponentCodec<Position>
{
    public void Write(in Position value, ComponentWriter fields)
    {
        fields.Set(1, FieldValue.From(value.Index));
        fields.Set(2, FieldValue.From(value.X));
        fields.Set(3, FieldValue.From(value.Y));
    }

    public Position Read(ComponentReader fields) =>
        new(fields.Get(1).AsInt32(), fields.Get(2).AsInt32(), fields.Get(3).AsInt32());
}

internal struct PositionVerifier(Position[] expected, bool[] seen) : IQueryProcessor<Position>
{
    public int Count { get; private set; }

    public void Execute(Entity entity, ref Position position)
    {
        int index = position.Index;
        if ((uint)index >= (uint)expected.Length || seen[index] || position != expected[index])
            throw new InvalidDataException($"Unexpected position for mover {index}.");
        seen[index] = true;
        Count++;
    }
}

internal readonly record struct TickSample(SnapshotNotice Snapshot, TimeSpan Capture, TimeSpan Encode,
    TimeSpan Send, TimeSpan Receive, TimeSpan Apply, TimeSpan Acknowledgement, TimeSpan RoundTrip, TimeSpan Verify);

internal sealed class Samples
{
    public int Full;
    public int Delta;
    public long FrameBytes;
    public long FullFrameBytes;
    public long DeltaFrameBytes;
    public double Capture;
    public double Encode;
    public double Send;
    public double Receive;
    public double Apply;
    public double Acknowledgement;
    public double RoundTrip;
    public double Verify;

    public void Add(TickSample sample)
    {
        var bytes = sample.Snapshot.FrameBytes;
        if (sample.Snapshot.Encoding == SnapshotEncoding.Full)
        {
            Full++;
            FullFrameBytes += bytes;
        }
        else
        {
            Delta++;
            DeltaFrameBytes += bytes;
        }

        FrameBytes += bytes;
        Capture += sample.Capture.TotalMilliseconds;
        Encode += sample.Encode.TotalMilliseconds;
        Send += sample.Send.TotalMilliseconds;
        Receive += sample.Receive.TotalMilliseconds;
        Apply += sample.Apply.TotalMilliseconds;
        Acknowledgement += sample.Acknowledgement.TotalMilliseconds;
        RoundTrip += sample.RoundTrip.TotalMilliseconds;
        Verify += sample.Verify.TotalMilliseconds;
    }
}
