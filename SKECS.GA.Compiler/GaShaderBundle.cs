using System.Text.Json;

namespace SKECS.GA;

internal static class GaShaderBundle
{
    internal static GaArtifactBundle Create(GaProgram program, string simulationContractId, GaToolchain toolchain,
        string entryPoint, int workgroupX, GaResourceBinding[] bindings, GaResourceUsage[] usage,
        IReadOnlyList<GaSourceSpan> faultSources, byte[] shader, byte[] reflection)
    {
        byte[] sourceMap = JsonSerializer.SerializeToUtf8Bytes(faultSources.ToArray(),
            GaManifestJsonContext.Default.GaSourceSpanArray);
        var manifest = new GaArtifactManifest(GaArtifactManifest.CurrentSchemaVersion,
            new GaContractIdentity(program.SystemId, simulationContractId, program.SourceDigest,
                GaIrDigest.Program(program), GaIrDigest.Schema(program), GaProgram.NumericVersion,
                GaProgram.SubsetVersion, GaProgram.IrVersion)
            {
                EventCapacity = program.EventCapacity,
                UsesSnapshot = program.UsesSnapshot,
                SnapshotSchema = program.SnapshotLayout?.Schema,
                TickResourceType = program.TickResourceType,
                TickResourceWords = program.TickResourceWords,
                BufferSchema = program.BufferSchema,
                BufferRowWords = program.BufferRowWords,
                UsesCommands = program.UsesCommands,
                ComponentRemovalCount = program.RemovalComponents.Count,
                ComponentAdditionCount = program.AdditionComponents.Count,
                AdditionPayloadWords = program.AdditionPayloadWords,
                EventTypeCount = program.EventTypes.Count,
                EventPayloadWords = program.EventPayloadWords,
                ReductionTypeCount = program.ReductionTypeCount,
                UsesDomainOrder = program.UsesDomainOrder
            },
            toolchain, entryPoint, bindings, usage,
            new GaKernelRequirements(program.UsesFixed ? GaComputeProfiles.WideFixedEmulated : GaComputeProfiles.Portable32,
                [], workgroupX, 1, 1, 0, 0),
            GaArtifactManifest.ComputeDigest(shader), GaArtifactManifest.ComputeDigest(reflection),
            GaArtifactManifest.ComputeDigest(sourceMap));
        return GaArtifactBundle.Load(manifest, manifest.GetContentId(), shader, reflection, sourceMap);
    }

    internal static void ValidateArtifactName(string artifactName)
    {
        if (string.IsNullOrWhiteSpace(artifactName) || artifactName.Any(character =>
            character is not (>= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '-' or '_')))
            throw new ArgumentException("Use an ASCII artifact filename without an extension.", nameof(artifactName));
    }

    internal static async Task WriteMetadata(string stem, GaArtifactBundle bundle)
    {
        await File.WriteAllBytesAsync(stem + ".source-map.json", bundle.SourceMap.ToArray()).ConfigureAwait(false);
        await File.WriteAllBytesAsync(stem + ".manifest.json", bundle.Manifest.GetCanonicalBytes()).ConfigureAwait(false);
        await File.WriteAllTextAsync(stem + ".content-id", bundle.ContentId + "\n").ConfigureAwait(false);
    }
}
