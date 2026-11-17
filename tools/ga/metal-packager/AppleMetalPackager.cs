using System.Diagnostics;
using System.Security.Cryptography;
using SKECS.GA;

internal static class AppleMetalPackager
{
    internal static async Task<string> Build(MetalSource source, string outputDirectory)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Metal packaging requires the Apple SDK and xcrun on macOS.");

        string output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        if (Path.Exists(output) || Path.GetDirectoryName(output) is not { } parent)
            throw new IOException("Metal package output must be a new directory.");
        string stage = Path.Combine(parent, ".ga-metal-" + Guid.NewGuid().ToString("N"));
        string package = Path.Combine(stage, "package");
        Directory.CreateDirectory(package);
        try
        {
            string sdk = source.Platform == "ios" ? "iphoneos" : "macosx";
            string sdkVersion = await Xcrun(stage, sdk, "--show-sdk-version");
            string metal = await Xcrun(stage, sdk, "--find", "metal");
            string compilerVersion = (await Xcrun(stage, sdk, "metal", "--version")).Split('\n')[0].Trim();
            string linker = await FindLegacyLinker(stage, sdk) ?? metal;
            string linkerName = linker == metal ? "metal" : "metallib";
            var options = new[]
            {
                source.Platform == "ios" ? "-std=ios-metal2.3" : "-std=macos-metal2.3",
                "-fmodules-cache-path=modules", "-c", "kernel.metal", "-o", "kernel.air",
            };
            string[] linkOptions = ["kernel.air", "-o", "kernel.metallib"];

            await File.WriteAllBytesAsync(Path.Combine(stage, "kernel.metal"), source.Source);
            await Xcrun(stage, sdk, ["metal", .. options]);
            await Xcrun(stage, sdk, [linkerName, .. linkOptions]);
            byte[] artifact = await File.ReadAllBytesAsync(Path.Combine(stage, "kernel.metallib"));
            var input = source.Vulkan.Manifest;
            var manifest = input with
            {
                Toolchain = input.Toolchain with
                {
                    DownstreamVersion = $"{input.Toolchain.DownstreamVersion}; SPIRV-Cross export; {sdk} {sdkVersion}; " +
                        $"{compilerVersion}; metal sha256={Digest(metal)}; {linkerName} linker sha256={Digest(linker)}",
                    Format = GaArtifactFormat.MetalLibrary,
                    TargetProfile = $"metal_2_3-{source.Platform}",
                    Options = [.. input.Toolchain.Options, "spirv-cross", .. source.Options, "xcrun", "--no-cache", "--sdk", sdk, "metal",
                        .. options, "xcrun", "--no-cache", "--sdk", sdk, linkerName, .. linkOptions],
                },
                EntryPoint = source.EntryPoint,
                DeclaredBindings = source.Bindings,
                EntryPointUsage = source.Bindings.Select(binding => new GaResourceUsage(binding.LogicalResourceId, true)).ToArray(),
                ArtifactDigest = GaArtifactManifest.ComputeDigest(artifact),
                ReflectionDigest = GaArtifactManifest.ComputeDigest(source.Reflection),
            };

            byte[] bytes = manifest.GetCanonicalBytes();
            string contentId = manifest.GetContentId();
            byte[] sourceMap = source.Vulkan.SourceMap.ToArray();
            var candidate = GaArtifactCandidate.Load(bytes, contentId, artifact, source.Reflection, sourceMap);
            if (candidate.Bundle is null)
                throw new InvalidDataException($"Invalid Metal bundle: {candidate.Error}");
            string stem = Path.Combine(package, source.ArtifactName);
            await File.WriteAllBytesAsync(stem + ".metallib", artifact);
            await File.WriteAllBytesAsync(stem + ".layout", source.Reflection);
            await File.WriteAllBytesAsync(stem + ".source-map.json", sourceMap);
            await File.WriteAllBytesAsync(stem + ".manifest.json", bytes);
            await File.WriteAllTextAsync(stem + ".content-id", contentId + "\n");
            Directory.Move(package, output);
            return contentId;
        }
        finally
        {
            Directory.Delete(stage, recursive: true);
        }
    }

    private static string Digest(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static async Task<string?> FindLegacyLinker(string stage, string sdk)
    {
        var result = await RunXcrun(stage, sdk, ["--find", "metallib"]);
        return result.ExitCode == 0 ? result.Output : null;
    }

    private static async Task<string> Xcrun(string stage, string sdk, params string[] arguments)
    {
        var result = await RunXcrun(stage, sdk, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Apple toolchain failed ({sdk}, {string.Join(" ", arguments)}): {result.Error}");
        if (result.Error.Length != 0)
            Console.Error.WriteLine(result.Error);
        return result.Output;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunXcrun(string stage, string sdk, string[] arguments)
    {
        var start = new ProcessStartInfo("/usr/bin/xcrun")
        {
            WorkingDirectory = stage,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "--no-cache", "--sdk", sdk }.Concat(arguments))
            start.ArgumentList.Add(argument);
        start.Environment["TMPDIR"] = stage;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start Apple xcrun.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, (await output).Trim(), (await error).Trim());
    }
}
