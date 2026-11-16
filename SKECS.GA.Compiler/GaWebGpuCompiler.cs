using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SKECS.GA;

public static class GaWebGpuPackager
{
    public static Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId, string slangCompilerExecutable,
        string outputDirectory, string artifactName) =>
        Build(program, simulationContractId, slangCompilerExecutable, outputDirectory, artifactName, GaSlangModule.ThreadsPerGroup);

    public static Task<GaArtifactBundle> Build(GaProgram program, string simulationContractId, string slangCompilerExecutable,
        string outputDirectory, string artifactName, int workgroupX)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slangCompilerExecutable);
        return GaArtifactPackager.Build(program, simulationContractId, Path.GetFullPath(slangCompilerExecutable),
            outputDirectory, artifactName, GaArtifactFormat.Wgsl, null, workgroupX);
    }
}

internal static class GaWebGpuCompiler
{
    private const string CompilerDigest = "bac1eff312cae0fb5ffb50445215d2774a4875c478b9505303f873b1afa4eaeb";
    private const string LibraryDigest = "dfca2d5eb494fe91e6a650aeaa45000d983dc6801728537bb7201d70bb5d76e6";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly string[] LogicalNames = ["gaInput", "gaOutput", "gaFaults"];

    internal static GaToolchain ReadToolchain(FileStream executable, FileStream library)
    {
        ValidateHash(executable, "slangc.exe", CompilerDigest);
        ValidateHash(library, "slang.dll", LibraryDigest);
        return new GaToolchain("2025.6.1", $"Slang WGSL emitter slangc.exe sha256:{CompilerDigest}, slang.dll sha256:{LibraryDigest}",
            GaArtifactFormat.Wgsl, "wgsl", ["-entry main", "-target wgsl", "storage arrayLength"], [], []);
    }

    private static void ValidateHash(FileStream file, string name, string expected)
    {
        string actual = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
        if (actual != expected)
            throw new InvalidDataException($"Expected {name} SHA-256 {expected}; found {actual}.");
    }

    internal static async Task<(GaResourceBinding[], GaResourceUsage[])> Compile(string executable, string source,
        string shaderPath, string layoutPath, int workgroupX)
    {
        await GaArtifactPackager.RunCompiler(executable,
            [source, "-entry", GaSlangModule.EntryPoint, "-target", "wgsl", "-reflection-json", layoutPath, "-o", shaderPath],
            "Slang WGSL").ConfigureAwait(false);
        string shader = CanonicalText(await File.ReadAllTextAsync(shaderPath, Utf8).ConfigureAwait(false));
        string layout = CanonicalText(await File.ReadAllTextAsync(layoutPath, Utf8).ConfigureAwait(false));
        VerifyShader(shader, workgroupX);
        VerifyReflection(layout, workgroupX);
        await File.WriteAllTextAsync(shaderPath, shader, Utf8).ConfigureAwait(false);
        await File.WriteAllTextAsync(layoutPath, layout, Utf8).ConfigureAwait(false);

        GaResourceBinding[] bindings = LogicalNames.Select((name, slot) => new GaResourceBinding(name,
            GaBindingNamespace.WebGpuBinding, 0, slot, 1, GaResourceKind.StorageBuffer,
            slot == 0 ? GaResourceAccess.Read : GaResourceAccess.ReadWrite, new GaResourceLayout(4, 4, 4, []))).ToArray();
        return (bindings, LogicalNames.Select(name => new GaResourceUsage(name, true)).ToArray());
    }

    private static string CanonicalText(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n') + "\n";

    private static void VerifyShader(string shader, int workgroupX)
    {
        string entry = FormattableString.Invariant($"@compute\n@workgroup_size({workgroupX}, 1, 1)\nfn main(");
        if (!shader.Contains(entry, StringComparison.Ordinal) || shader.Contains('\0') ||
            shader.Contains(".GetDimensions(", StringComparison.Ordinal) || Regex.IsMatch(shader, @"(?m)^[ \t]*(enable|requires)\s"))
            throw new InvalidDataException("Slang WGSL does not match the portable compute entry point.");

        var declarations = Regex.Matches(shader,
            @"(?m)^@binding\(([0-9]+)\) @group\(0\) var<storage, (read|read_write)> ([a-zA-Z][a-zA-Z0-9_]+) : array<u32>;$");
        var slots = new HashSet<int>();
        foreach (Match declaration in declarations)
        {
            if (!int.TryParse(declaration.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int slot) ||
                (uint)slot >= LogicalNames.Length || !slots.Add(slot) ||
                declaration.Groups[2].Value != (slot == 0 ? "read" : "read_write") ||
                !declaration.Groups[3].Value.StartsWith(LogicalNames[slot] + "_", StringComparison.Ordinal))
                throw new InvalidDataException("Slang WGSL buffer declarations differ from the GA storage ABI.");
        }
        if (slots.Count != LogicalNames.Length || Regex.Matches(shader, @"@binding\(").Count != LogicalNames.Length)
            throw new InvalidDataException("Slang WGSL must contain exactly three GA storage bindings.");
    }

    private static void VerifyReflection(string layout, int workgroupX)
    {
        try
        {
            using var document = JsonDocument.Parse(layout);
            var root = document.RootElement;
            var entries = root.GetProperty("entryPoints");
            if (entries.GetArrayLength() != 1 || root.EnumerateObject().Any(field => field.Name is not ("parameters" or "entryPoints")))
                throw new InvalidDataException("Slang WGSL reflection must contain one GA compute entry and no other resources.");
            var entry = entries[0];
            if (entry.GetProperty("name").GetString() != GaSlangModule.EntryPoint || entry.GetProperty("stage").GetString() != "compute" ||
                !entry.GetProperty("threadGroupSize").EnumerateArray().Select(axis => axis.GetInt32()).SequenceEqual([workgroupX, 1, 1]))
                throw new InvalidDataException("Slang WGSL reflection differs from the expected compute workgroup.");

            var declaredSlots = new HashSet<int>();
            foreach (var parameter in root.GetProperty("parameters").EnumerateArray())
            {
                int slot = ReadSlot(parameter, declaredSlots);
                var type = parameter.GetProperty("type");
                var element = type.GetProperty("resultType");
                string? access = type.TryGetProperty("access", out var property) ? property.GetString() : null;
                if (type.GetProperty("kind").GetString() != "resource" || type.GetProperty("baseShape").GetString() != "structuredBuffer" ||
                    element.GetProperty("kind").GetString() != "scalar" || element.GetProperty("scalarType").GetString() != "uint32" ||
                    (slot == 0 ? access is not (null or "read") : access != "readWrite"))
                    throw new InvalidDataException("Slang WGSL reflection differs from the u32 GA storage layout.");
            }
            var usedSlots = new HashSet<int>();
            foreach (var resource in entry.GetProperty("bindings").EnumerateArray())
            {
                ReadSlot(resource, usedSlots);
                if (resource.GetProperty("binding").GetProperty("used").GetInt32() != 1)
                    throw new InvalidDataException("Slang WGSL reflection reported an unused GA storage binding.");
            }
            if (declaredSlots.Count != LogicalNames.Length || usedSlots.Count != LogicalNames.Length)
                throw new InvalidDataException("Slang WGSL reflection omits required GA storage bindings.");
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new InvalidDataException("Invalid Slang WGSL reflection.", failure);
        }
    }

    private static int ReadSlot(JsonElement resource, HashSet<int> seen)
    {
        var binding = resource.GetProperty("binding");
        int slot = binding.GetProperty("index").GetInt32();
        if ((uint)slot >= LogicalNames.Length || !seen.Add(slot) ||
            resource.GetProperty("name").GetString() != LogicalNames[slot] ||
            binding.GetProperty("kind").GetString() != "descriptorTableSlot" ||
            (binding.TryGetProperty("space", out var space) && space.GetInt32() != 0) ||
            (binding.TryGetProperty("count", out var count) && count.GetInt32() != 1))
            throw new InvalidDataException("Slang WGSL reflection differs from the GA storage bindings.");
        return slot;
    }
}
