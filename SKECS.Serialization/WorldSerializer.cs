namespace SKECS.Serialization;

/// <summary>Reads and writes versioned world checkpoints on caller-owned streams.</summary>
public static class WorldSerializer
{
    public static void WriteBinary(World world, Stream destination, CodecRegistry registry, CheckpointWriteOptions? options = null) =>
        Write(world, destination, registry, options, binary: true);

    public static void WriteJson(World world, Stream destination, CodecRegistry registry, CheckpointWriteOptions? options = null) =>
        Write(world, destination, registry, options, binary: false);

    public static World ReadBinary(Stream source, CodecRegistry registry, CheckpointReadOptions? options = null) =>
        ReadBinary(source, registry, out _, options);

    public static World ReadBinary(Stream source, CodecRegistry registry, out IReadOnlyList<CheckpointExtension> extensions,
        CheckpointReadOptions? options = null) => Read(source, registry, out extensions, options, binary: true);

    public static World ReadJson(Stream source, CodecRegistry registry, CheckpointReadOptions? options = null) =>
        ReadJson(source, registry, out _, options);

    public static World ReadJson(Stream source, CodecRegistry registry, out IReadOnlyList<CheckpointExtension> extensions,
        CheckpointReadOptions? options = null) => Read(source, registry, out extensions, options, binary: false);

    private static void Write(World world, Stream destination, CodecRegistry registry, CheckpointWriteOptions? options, bool binary)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        options ??= new CheckpointWriteOptions();
        var limits = options.Limits.Copy();
        using WireWriter writer = binary ? new BinaryWireWriter(destination, limits) : new JsonWireWriter(destination, limits);
        CheckpointWriter.Write(writer, world, registry, options.Extensions, limits);
    }

    private static World Read(Stream source, CodecRegistry registry, out IReadOnlyList<CheckpointExtension> extensions,
        CheckpointReadOptions? options, bool binary)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        options ??= new CheckpointReadOptions();
        var limits = options.Limits.Copy();
        extensions = Array.Empty<CheckpointExtension>();
        using WireReader reader = binary ? new BinaryWireReader(source, limits) : new JsonWireReader(source, limits);
        return CheckpointReader.Read(reader, registry, options, limits, out extensions);
    }
}
