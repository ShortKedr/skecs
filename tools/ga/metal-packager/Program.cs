using System.Text.Json;
using SKECS.GA;

if (args is not [("validate" or "pack"), ("macos" or "ios"), ..] ||
    args.Length != (args[0] == "pack" ? 5 : 4))
{
    Console.Error.WriteLine("Usage: Ga.MetalPackager validate <macos|ios> <Vulkan bundle dir> <MSL export dir>");
    Console.Error.WriteLine("       Ga.MetalPackager pack <macos|ios> <Vulkan bundle dir> <MSL export dir> <new output dir>");
    return 2;
}

try
{
    var source = MetalSource.Load(args[1], args[2], args[3]);
    if (args[0] == "pack")
        Console.WriteLine(await AppleMetalPackager.Build(source, args[4]));
    else
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            source.Platform,
            source.ArtifactName,
            source.Vulkan.ContentId,
            source.Vulkan.Manifest.Contract,
            source.EntryPoint,
            source.Bindings,
        }));
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
    InvalidOperationException or PlatformNotSupportedException or System.ComponentModel.Win32Exception or ArgumentException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
