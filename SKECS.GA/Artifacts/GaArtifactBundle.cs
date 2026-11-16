using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace SKECS.GA;

public sealed class GaArtifactBundle
{
    private readonly byte[] _artifact;
    private readonly byte[] _reflection;
    private readonly byte[] _sourceMap;

    private GaArtifactBundle(
        GaArtifactManifest manifest, string contentId, byte[] artifact, byte[] reflection, byte[] sourceMap)
    {
        Manifest = manifest;
        ContentId = contentId;
        _artifact = artifact;
        _reflection = reflection;
        _sourceMap = sourceMap;
    }

    public GaArtifactManifest Manifest { get; }
    public string ContentId { get; }
    public ReadOnlySpan<byte> Artifact => _artifact;
    public ReadOnlySpan<byte> Reflection => _reflection;
    public ReadOnlySpan<byte> SourceMap => _sourceMap;

    public IReadOnlyList<GaSourceSpan> DecodeSourceMap()
    {
        var spans = JsonSerializer.Deserialize(_sourceMap, GaManifestJsonContext.Default.GaSourceSpanArray);
        if (spans is null || spans.Any(span => span.File is null || span.Start < 0 || span.Length < 0 ||
            (long)span.Start + span.Length > int.MaxValue))
            throw new InvalidDataException("The GA source map contains invalid source spans.");
        return Array.AsReadOnly(spans);
    }

    public static GaArtifactBundle Load(
        GaArtifactManifest manifest, string expectedContentId, ReadOnlySpan<byte> artifact,
        ReadOnlySpan<byte> reflection, ReadOnlySpan<byte> sourceMap)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        GaArtifactManifest.ValidateDigest(expectedContentId, "expected manifest");
        var normalized = manifest.Normalize();
        if (normalized.GetContentId() != expectedContentId)
            throw new InvalidDataException("GA manifest content ID differs from the expected ID.");

        var ownedArtifact = artifact.ToArray();
        var ownedReflection = reflection.ToArray();
        var ownedSourceMap = sourceMap.ToArray();
        if (ownedArtifact.Length == 0 || GaArtifactManifest.ComputeDigest(ownedArtifact) != normalized.ArtifactDigest)
            throw new InvalidDataException("GA shader artifact is empty or has an unexpected digest.");
        if (!HasExpectedFormat(normalized.Toolchain, ownedArtifact))
            throw new InvalidDataException("GA shader artifact does not have the declared target format.");
        if (ownedReflection.Length == 0 || GaArtifactManifest.ComputeDigest(ownedReflection) != normalized.ReflectionDigest)
            throw new InvalidDataException("GA target reflection is empty or has an unexpected digest.");
        if (GaArtifactManifest.ComputeDigest(ownedSourceMap) != normalized.SourceMapDigest)
            throw new InvalidDataException("GA source map has an unexpected digest.");

        return new GaArtifactBundle(normalized, expectedContentId, ownedArtifact, ownedReflection, ownedSourceMap);
    }

    private static bool HasExpectedFormat(GaToolchain toolchain, ReadOnlySpan<byte> artifact) => toolchain.Format switch
    {
        GaArtifactFormat.SpirV => artifact.Length >= 20 && artifact.Length % 4 == 0 &&
            BinaryPrimitives.ReadUInt32LittleEndian(artifact) == 0x07230203 &&
            BinaryPrimitives.ReadUInt32LittleEndian(artifact[12..]) != 0 &&
            BinaryPrimitives.ReadUInt32LittleEndian(artifact[16..]) == 0,
        GaArtifactFormat.Dxil or GaArtifactFormat.Dxbc => IsDirect3DContainer(toolchain.Format, artifact),
        GaArtifactFormat.MetalLibrary => artifact.StartsWith("MTLB"u8),
        GaArtifactFormat.Wgsl => true, // The WebGPU shader module validates the source.
        GaArtifactFormat.Glsl => toolchain.TargetProfile == "glsl_430" &&
            artifact.StartsWith("#version 430\n"u8) && artifact.IndexOf((byte)0) < 0,
        _ => false
    };

    private static bool IsDirect3DContainer(GaArtifactFormat format, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 32 || !bytes.StartsWith("DXBC"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]) != bytes.Length)
            return false;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..]);
        if (count > (bytes.Length - 32) / 4)
            return false;

        bool dxil = false;
        bool legacy = false;
        var headerSize = 32 + (int)count * 4;
        for (int i = 0; i < count; i++)
        {
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(32 + i * 4)..]);
            if (offset < headerSize || offset > bytes.Length - 8)
                return false;
            var chunk = bytes[(int)offset..];
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (size > chunk.Length - 8)
                return false;
            dxil |= chunk.StartsWith("DXIL"u8);
            legacy |= chunk.StartsWith("SHEX"u8) || chunk.StartsWith("SHDR"u8);
        }
        return format == GaArtifactFormat.Dxil ? dxil : legacy && !dxil;
    }
}
