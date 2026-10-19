using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using SpaceBattle;
using SKECS.BenchmarkTools;

internal static class ReplayOracle
{
    public static ulong[] Reference(BattleConfig config, int ticks)
    {
        var workspace = new Workspace(Workspace.FindRoot());
        // Immutable oracle cache is keyed by the exact configuration and host/vendor
        // binaries. Reuse only data; generate it out of process so SKECS gets the
        // same 600-tick warmup as every other measured framework.
        var configJson = JsonSerializer.Serialize(config);
        var identity = new StringBuilder(ReplayHash.Revision).Append('|').Append(ticks).Append('|').Append(configJson)
            .Append('|').Append(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription)
            .Append('|').Append(System.Runtime.InteropServices.RuntimeInformation.OSDescription)
            .Append('|').Append(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll").Order(StringComparer.Ordinal))
            identity.Append('|').Append(Path.GetFileName(dll)).Append(':').Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())));
        var output = workspace.ArtifactPath($"artifacts/benchmarks/replay-oracles/{key}.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (!File.Exists(output)) Create();
        var bytes = File.ReadAllBytes(output);
        if (bytes.Length != checked((ticks + 1) * 8)) throw new InvalidDataException("Incomplete SKECS replay oracle.");
        var hashes = new ulong[ticks + 1];
        for (var i = 0; i < hashes.Length; i++) hashes[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(i * 8, 8));
        return hashes;

        void Create()
        {
            var temporary = output + $".{Guid.NewGuid():N}.tmp";
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate measurement host.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(ReplayOracle).Assembly.Location);
            foreach (var argument in new[] { "measure", "--write-replay-oracle", temporary, "--ticks", ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), "--config-json", configJson })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start SKECS replay oracle.");
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("SKECS replay oracle failed: " + process.ExitCode);
            if (new FileInfo(temporary).Length != checked((ticks + 1) * 8)) throw new InvalidDataException("Incomplete SKECS replay oracle.");
            // A concurrent identical request may already have published this key.
            try { File.Move(temporary, output); }
            catch (IOException) when (File.Exists(output)) { File.Delete(temporary); }
        }
    }

    public static void Write(string[] args)
    {
        string Value(string name)
        {
            var index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 == args.Length) throw new ArgumentException("Missing " + name);
            return args[index + 1];
        }
        var workspace = new Workspace(Workspace.FindRoot());
        var output = workspace.ArtifactPath(Value("--write-replay-oracle"));
        if (File.Exists(output)) throw new ArgumentException("Replay oracle output already exists.");
        var ticks = int.Parse(Value("--ticks"), System.Globalization.CultureInfo.InvariantCulture);
        if (ticks < 1 || ticks > 1_000_000) throw new ArgumentOutOfRangeException(nameof(ticks));
        var config = JsonSerializer.Deserialize<BattleConfig>(Value("--config-json")) ?? throw new ArgumentException("Missing replay configuration.");
        config.Validate();
        var hashes = NativeReplayValidation.Reference(config, ticks);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        using var writer = new BinaryWriter(file);
        foreach (var hash in hashes) writer.Write(hash);
        Console.WriteLine($"SKECS oracle: {ticks} ticks, {hashes[^1]:X16} (separate process)");
    }
}
