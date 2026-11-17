using SKECS.GA;
using SKECS.GA.Backend.D3D12;
using SKECS.GA.Backend.Vulkan;
using SpaceBattle;
using SpaceBattle.Web.Ga;
using Battle = SpaceBattle.Simulations.Skecs.Battle;

internal sealed class NativeParticleExpiry : IDisposable
{
    private readonly IDisposable? _backend;
    private readonly GaStartupResult _startup;
    private readonly GaPackedKernel? _kernel;
    private readonly List<Battle.ParticleExpiryRow> _rows = [];
    private readonly Dictionary<EntityKey, bool> _expired = [];
    private uint[] _input = [];
    private GaPackedSubmission? _pending;
    private Battle? _pendingBattle;
    private int _tick;
    private bool _faulted;
    private bool _disposed;

    private NativeParticleExpiry(IDisposable? backend, GaStartupResult startup, GaStartupDecision decision)
    {
        _backend = backend;
        _startup = startup;
        Decision = decision;
        if (decision.Route == GaRoute.Gpu)
            _kernel = (GaPackedKernel)startup.GetPreparedKernel(decision.Contract.SystemId);
    }

    internal GaStartupDecision Decision { get; }
    internal int LastRows { get; private set; }
    internal bool HasPending => _pending is not null;

    internal static NativeParticleExpiry Open(string backend, string nativeBridge, string bundleDirectory, int deviceIndex, GaPolicy policy)
    {
        string extension = backend switch
        {
            "vulkan" => ".spv",
            "d3d12" => ".dxil",
            _ => throw new ArgumentException("Particle expiry supports vulkan or d3d12.", nameof(backend)),
        };
        ArgumentOutOfRangeException.ThrowIfNegative(deviceIndex);
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy));

        var contract = new GaContractIdentity(ParticleExpiryAdapterRowCodec.SystemId, "spacebattle-particle-expiry/1",
            ParticleExpiryAdapterRowCodec.SourceDigest, ParticleExpiryAdapterRowCodec.IrDigest,
            ParticleExpiryAdapterRowCodec.ComponentSchemaDigest, GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
        {
            EventCapacity = ParticleExpiryAdapterRowCodec.EventCapacity,
            TickResourceType = ParticleExpiryAdapterRowCodec.TickResourceType,
            TickResourceWords = ParticleExpiryAdapterRowCodec.TickResourceWords,
        };
        IReadOnlyList<GaArtifactCandidate> artifacts = [];
        IReadOnlyList<IGaDevice> devices = [];
        IDisposable? owner = null;
        GaStartupResult? startup = null;
        string? bundleReadError = null;
        string? openError = null;
        try
        {
            if (policy != GaPolicy.CpuOnly)
            {
                string stem = Path.Combine(bundleDirectory, contract.SystemId);
                try
                {
                    artifacts = [GaArtifactCandidate.Load(File.ReadAllBytes(stem + ".manifest.json"),
                        File.ReadAllText(stem + ".content-id").Trim(), File.ReadAllBytes(stem + extension),
                        File.ReadAllBytes(stem + ".layout"), File.ReadAllBytes(stem + ".source-map.json"))];
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    bundleReadError = failure.Message;
                }
            }

            if (artifacts.Count != 0 && artifacts[0].Bundle?.Manifest.Contract == contract)
            {
                if (backend == "vulkan")
                {
                    if (GaVulkanBackend.TryOpen(nativeBridge, out var vulkan, out openError))
                    {
                        owner = vulkan;
                        devices = vulkan.Devices;
                    }
                }
                else if (GaD3D12Backend.TryOpen(nativeBridge, out var d3d12, out openError))
                {
                    owner = d3d12;
                    devices = d3d12.Devices;
                }

                if (devices.Count > 0)
                {
                    if (deviceIndex >= devices.Count)
                        throw new ArgumentOutOfRangeException(nameof(deviceIndex), "Particle expiry adapter index is out of range.");
                    devices = [devices[deviceIndex]];
                }
            }

            startup = GaStartup.Select(policy, [new(contract, artifacts)], devices, allowExperimental: true);
            var decision = startup.Decisions[0];
            if (decision.Reason == GaStartupReason.MissingArtifact && bundleReadError is not null)
                decision = decision with { Detail = bundleReadError };
            if (decision.Reason == GaStartupReason.NoComputeApi && openError is not null)
                decision = decision with { Detail = openError };
            if (!startup.Succeeded)
                throw new InvalidOperationException($"Particle expiry GA startup failed ({decision.Reason}): {decision.Detail}");
            return new NativeParticleExpiry(owner, startup, decision);
        }
        catch
        {
            try { startup?.Dispose(); }
            finally { owner?.Dispose(); }
            throw;
        }
    }

    internal void Step(Battle battle)
    {
        if (TryBegin(battle))
            Complete(wait: true);
    }

    // A false return means the tick completed inline, including an empty GPU batch.
    internal bool TryBegin(Battle battle)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faulted || _pending is not null)
            throw new InvalidOperationException("Particle expiry requires an idle, healthy host.");

        LastRows = 0;
        try
        {
            if (_kernel is null)
            {
                battle.Step();
                return false;
            }

            battle.CopyParticleExpiryRows(_rows);
            if (_rows.Count == 0)
            {
                battle.Step();
                return false;
            }

            _tick = unchecked(battle.Tick + 1);
            const int stride = ParticleExpiryAdapterRowCodec.WordsPerRow;
            const int resourceWords = ParticleExpiryAdapterRowCodec.TickResourceWords;
            int inputWords = checked(1 + _rows.Count * stride + resourceWords);
            if (_input.Length < inputWords)
                _input = new uint[inputWords];
            var input = _input.AsSpan(0, inputWords);
            input[0] = (uint)_rows.Count;
            for (int i = 0; i < _rows.Count; i++)
            {
                // The generated row codec stores Expired before ExpiresTick.
                input[1 + i * stride] = 0;
                input[2 + i * stride] = unchecked((uint)_rows[i].ExpiresTick);
            }
            ParticleExpiryAdapterRowCodec.TickResource.Pack(new ParticleExpiryFrame { Tick = _tick }, input[^resourceWords..]);
            _pending = _kernel.BeginDispatch(input, stride);
            _pendingBattle = battle;
            return true;
        }
        catch
        {
            _faulted = true;
            throw;
        }
    }

    internal bool Complete(bool wait = false)
    {
        var pending = _pending ?? throw new InvalidOperationException("Particle expiry has no pending tick.");
        if (!wait && !pending.IsCompleted)
            return false;
        _pending = null;
        var battle = _pendingBattle!;
        _pendingBattle = null;
        try
        {
            var result = pending.Drain();
            if (result.FindFirstFault() is { } fault)
                throw new InvalidDataException($"Particle expiry GA failed at row {fault.Row}: {fault.Fault}.");
            const int stride = ParticleExpiryAdapterRowCodec.WordsPerRow;
            if (result.RowCount != _rows.Count || result.Output.Length != checked(_rows.Count * stride))
                throw new InvalidDataException("Particle expiry GA returned an invalid row count.");

            _expired.Clear();
            for (int i = 0; i < _rows.Count; i++)
            {
                int offset = i * stride;
                uint flag = result.Output[offset];
                if (flag > 1u || result.Output[offset + 1] != unchecked((uint)_rows[i].ExpiresTick))
                    throw new InvalidDataException($"Particle expiry GA returned an invalid row at index {i}.");
                _expired.Add(_rows[i].Entity, flag != 0);
            }
            battle.StepWithParticleExpiry(_tick, _expired);
            LastRows = _rows.Count;
            return true;
        }
        catch
        {
            _faulted = true;
            throw;
        }
    }

    internal bool TryDiscard()
    {
        if (_pending is not { } pending)
            return true;
        if (!pending.IsCompleted)
            return false;
        _pending = null;
        _pendingBattle = null;
        try { pending.Discard(); }
        catch
        {
            _faulted = true;
            throw;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _startup.Dispose(); }
        finally { _backend?.Dispose(); }
    }
}
