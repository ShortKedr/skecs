using System.Runtime.Versioning;
using SKECS.GA;
using SKECS.GA.Backend.WebGpu;

namespace SpaceBattle.Web.Ga;

[SupportedOSPlatform("browser")]
internal sealed class ParticleExpiryGpu(
    GaWebGpuBackend device, GaStartupResult startup, GaPackedKernel kernel) : IAsyncDisposable
{
    private const string ContractId = "spacebattle-particle-expiry/1";
    private const string ResourcePrefix = "SpaceBattle.Web.Ga.Bundle.particle-expiry";

    internal static async Task<ParticleExpiryGpu?> TryOpenAsync(string moduleUrl)
    {
        var contract = new GaContractIdentity(ParticleExpiryAdapterRowCodec.SystemId, ContractId,
            ParticleExpiryAdapterRowCodec.SourceDigest, ParticleExpiryAdapterRowCodec.IrDigest,
            ParticleExpiryAdapterRowCodec.ComponentSchemaDigest,
            GaProgram.NumericVersion, GaProgram.SubsetVersion, GaProgram.IrVersion)
        {
            EventCapacity = ParticleExpiryAdapterRowCodec.EventCapacity,
            TickResourceType = ParticleExpiryAdapterRowCodec.TickResourceType,
            TickResourceWords = ParticleExpiryAdapterRowCodec.TickResourceWords
        };
        var artifact = GaArtifactCandidate.Load(ReadResource(".manifest.json"),
            System.Text.Encoding.UTF8.GetString(ReadResource(".content-id")).Trim(),
            ReadResource(".wgsl"), ReadResource(".layout"), ReadResource(".source-map.json"));
        if (artifact.Bundle is null)
            throw new InvalidDataException($"Invalid particle expiry GA bundle: {artifact.Error}");
        if (artifact.Bundle.Manifest.Contract != contract)
            throw new InvalidDataException("The particle expiry GA bundle does not match the generated contract.");

        var opened = await GaWebGpuBackend.TryOpenAsync(new Uri(moduleUrl, UriKind.Absolute).AbsoluteUri);
        if (opened.Backend is not { } backend)
            return null;

        GaStartupResult? selected = null;
        bool transferred = false;
        try
        {
            selected = await GaStartup.SelectAsync(GaPolicy.RequireGpu, [new(contract, [artifact])], [backend],
                allowExperimental: true);
            var decision = selected.Decisions[0];
            if (decision.Route == GaRoute.Gpu)
            {
                var prepared = (GaPackedKernel)selected.GetPreparedKernel(contract.SystemId);
                var result = new ParticleExpiryGpu(backend, selected, prepared);
                transferred = true;
                return result;
            }

            return decision.Reason switch
            {
                GaStartupReason.NoComputeApi or GaStartupReason.MissingCapability or
                GaStartupReason.ResourceBudgetExceeded or GaStartupReason.UnqualifiedConfiguration or
                GaStartupReason.PipelineCreationFailed or GaStartupReason.ProbeFailed => null,
                _ => throw new InvalidOperationException(
                    $"Particle expiry GA startup failed ({decision.Reason}): {decision.Detail}")
            };
        }
        finally
        {
            if (!transferred)
            {
                try { selected?.Dispose(); }
                finally { backend.Dispose(); }
            }
        }
    }

    internal async Task<bool[]> ClassifyAsync(int tick, int[] expirations)
    {
        ArgumentNullException.ThrowIfNull(expirations);
        if (expirations.Length == 0)
            return [];

        // The generated row codec lays out Expired and ExpiresTick in field-name order.
        const int stride = ParticleExpiryAdapterRowCodec.WordsPerRow;
        var input = new uint[checked(1 + expirations.Length * stride + ParticleExpiryAdapterRowCodec.TickResourceWords)];
        input[0] = (uint)expirations.Length;
        for (int row = 0; row < expirations.Length; row++)
            input[1 + row * stride + 1] = unchecked((uint)expirations[row]);
        ParticleExpiryAdapterRowCodec.TickResource.Pack(new ParticleExpiryFrame { Tick = tick }, input.AsSpan(input.Length - 1));
        var result = await kernel.BeginDispatch(input, stride).DrainAsync();
        return ReadFlags(input, expirations.Length, result);
    }

    public async ValueTask DisposeAsync()
    {
        try { await kernel.DisposeAsync(); }
        finally
        {
            try { startup.Dispose(); }
            finally { device.Dispose(); }
        }
    }

    private static bool[] ReadFlags(uint[] input, int count, GaPackedDispatchResult result)
    {
        const int stride = ParticleExpiryAdapterRowCodec.WordsPerRow;
        if (result.FindFirstFault() is { } fault)
            throw new InvalidDataException($"Particle expiry GA failed at row {fault.Row}: {fault.Fault}.");
        if (result.RowCount != count || result.Output.Length != count * stride)
            throw new InvalidDataException("Particle expiry GA returned an invalid row count.");

        var flags = new bool[count];
        for (int row = 0; row < count; row++)
        {
            int offset = row * stride;
            uint flag = result.Output[offset];
            if (flag > 1 || result.Output[offset + 1] != input[offset + 2])
                throw new InvalidDataException($"Particle expiry GA returned an invalid row at index {row}.");
            flags[row] = flag != 0;
        }
        return flags;
    }

    private static byte[] ReadResource(string extension)
    {
        var name = ResourcePrefix + extension;
        using var stream = typeof(ParticleExpiryGpu).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"Missing particle expiry GA resource {name}.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
