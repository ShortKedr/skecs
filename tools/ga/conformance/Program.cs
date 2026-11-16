using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SKECS.GA;
using static GaConformanceIo;

if (args.Length < 6)
{
    Console.Error.WriteLine("Usage: Ga.Conformance <ga-slang-probe> <spirv-val> <ga-vulkan> <output-directory> <native-library-or-dash> <device-index> ...");
    return 2;
}

string directory = Path.GetFullPath(args[3]);
Directory.CreateDirectory(directory);
var program = Compile();
var module = GaSlangEmitter.EmitVulkan(program);
string spirv = Path.Combine(directory, "integer.spv");
var bundle = await GaVulkanPackager.Build(program, "fixtures.integer/1", args[0], directory, "integer");
var rebuilt = await GaVulkanPackager.Build(program, "fixtures.integer/1", args[0], directory, "rebuild");
if (bundle.ContentId != rebuilt.ContentId)
    throw new InvalidDataException("Identical inputs produced different bundle identities.");
await Run(args[1], "--target-env", "vulkan1.1", spirv);

foreach (string device in args[5..])
{
    if (!int.TryParse(device, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        throw new ArgumentException("Device indexes must be nonnegative integers.");
    using var managed = args[4] == "-" ? null : ManagedVulkanConformance.Open(args[4], index, bundle);
    await Compare(device, 1, managed);
    await Compare(device, 65, managed);
    await Compare(device, 131, managed);
    Console.WriteLine($"Vulkan device {device}: independent C#, IR interpreter and generated SPIR-V agree.");
}
await FixedVulkanConformance.Compare(args[0], args[1], args[2], directory, args[5..]);
await SnapshotVulkanConformance.Compare(args[0], args[1], args[2], directory, args[5..]);
if (args[4] != "-")
    await FixedMovementConformance.Compare(args[0], args[1], args[4], directory, args[5..]);

return 0;

async Task Compare(string device, int count, ManagedVulkanConformance? managed)
{
    int width = program.Fields.Count;
    var input = new uint[1 + count * width];
    var gpu = new uint[count * width];
    var faults = new uint[count * GaSlangModule.FaultStride];
    Array.Fill(gpu, 0xfedcba98u);
    Array.Fill(faults, 0xffffffffu);
    input[0] = (uint)count;
    var cases = new (int Left, int Right, uint Mode)[]
    {
        (-7, 3, 1), (int.MinValue, 3, 1), (int.MinValue, -1, 1), (int.MinValue, -1, 0),
        (7, -3, 1), (-7, -3, 0), (42, 0, 0), (16, 0, 1), (-7, -3, 1),
        (int.MaxValue, 1, 2), (-7, 3, 2), (int.MinValue, 1, 3), (5, -3, 3),
        (65536, 65536, 4), (int.MinValue, 1, 4), (int.MinValue, -1, 4),
        (-46340, -46340, 4), (-1, 0, 5), (1, 0, 5), (0, 33, 6), (0, 33, 7), (-1, 0, 8)
    };
    var expected = new (int Result, GaFaultKind Kind, ulong Ordinal)[count];
    var sourceFaults = new GaKernelFault?[count];
    var random = new Random(63827);
    int resultIndex = Enumerable.Range(0, width).Single(i => program.Fields[i].Path == "Result");
    for (int row = 0; row < count; row++)
    {
        var (left, right, mode) = row < cases.Length ? cases[row] :
            ((int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1),
             (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1), (uint)random.Next(9));
        uint[] words = program.Fields.Select(field => field.Path switch
        {
            "Left" => unchecked((uint)left),
            "Right" => unchecked((uint)right),
            "Mode" => mode,
            "Result" => 9876u,
            _ => throw new InvalidOperationException()
        }).ToArray();
        words.CopyTo(input, 1 + row * width);
        expected[row] = Oracle(left, right, mode);
        sourceFaults[row] = GaInterpreter.Execute(program, words);
        var cpu = sourceFaults[row];
        Require((cpu?.Kind ?? 0) == expected[row].Kind &&
                (cpu?.EvaluationOrdinal ?? 0) == expected[row].Ordinal &&
                unchecked((int)words[resultIndex]) == expected[row].Result, "CPU oracle mismatch.", row);
    }

    string inputFile = Path.Combine(directory, $"device-{device}-{count}.in");
    string outputFile = Path.Combine(directory, $"device-{device}-{count}.out");
    string faultFile = Path.Combine(directory, $"device-{device}-{count}.fault");
    Write(inputFile, input);
    Write(outputFile, gpu);
    Write(faultFile, faults);
    string groups = ((count + GaSlangModule.ThreadsPerGroup - 1) / GaSlangModule.ThreadsPerGroup)
        .ToString(CultureInfo.InvariantCulture);
    var dispatch = new List<string> { "dispatch", device, spirv, "main", groups };
    AddBinding("gaInput", "r", inputFile);
    AddBinding("gaOutput", "rw", outputFile);
    AddBinding("gaFaults", "rw", faultFile);
    await Run(args[2], dispatch.ToArray());
    Read(outputFile, gpu);
    Read(faultFile, faults);
    for (int row = 0; row < count; row++)
    {
        int offset = row * GaSlangModule.FaultStride;
        var (result, kind, ordinal) = expected[row];
        Require((GaFaultKind)faults[offset] == kind, "GPU fault kind mismatch.", row);
        if (kind != 0)
        {
            ulong gpuOrdinal = faults[offset + 1] | ((ulong)faults[offset + 2] << 32);
            uint site = faults[offset + 3];
            Require(gpuOrdinal == ordinal && site < module.FaultSources.Count &&
                    module.FaultSources[(int)site] == sourceFaults[row]!.Value.Source,
                "GPU fault ordinal/site mismatch.", row);
            for (int word = 0; word < width; word++)
                Require(gpu[row * width + word] == 0xfedcba98u, "Faulting GPU row was written.", row);
        }
        else
        {
            Require(faults[offset + 1] == 0 && faults[offset + 2] == 0 && faults[offset + 3] == 0,
                "Successful GPU row has fault metadata.", row);
            for (int word = 0; word < width; word++)
            {
                uint expectedWord = word == resultIndex ? unchecked((uint)result) : input[1 + row * width + word];
                Require(gpu[row * width + word] == expectedWord, $"GPU output word {word} mismatch.", row);
            }
        }
    }
    managed?.Compare(input, width, gpu, faults);
    if (count == 1)
        managed?.VerifyDiscard(input, width);

    void Require(bool condition, string message, int row)
    {
        if (!condition)
            throw new InvalidOperationException($"Device {device}, {count} rows, row {row}: {message}");
    }

    void AddBinding(string logicalId, string access, string path)
    {
        var binding = bundle.Manifest.DeclaredBindings.Single(item => item.LogicalResourceId == logicalId);
        dispatch.Add(binding.Space.ToString(CultureInfo.InvariantCulture));
        dispatch.Add(binding.Index.ToString(CultureInfo.InvariantCulture));
        dispatch.Add(access);
        dispatch.Add(path);
    }
}

static (int Result, GaFaultKind Kind, ulong Ordinal) Oracle(int left, int right, uint mode)
{
    ulong ordinal = 0;
    try
    {
        if (mode <= 1 && (right == 0 || left == int.MinValue && right == -1))
            return (9876, right == 0 ? GaFaultKind.DivideByZero : GaFaultKind.Overflow, ordinal);
        if (mode == 8)
        {
            for (int i = 0; i < 4; i++, ordinal++)
                _ = checked(left + i);
            if (right == 0 || left == int.MinValue && right == -1)
                return (9876, right == 0 ? GaFaultKind.DivideByZero : GaFaultKind.Overflow, ordinal);
            return (left / right, 0, 0);
        }
        return (mode switch
        {
            0 => left / right,
            1 => left % right,
            2 => checked(left + right),
            3 => checked(left - right),
            4 => checked(left * right),
            5 => unchecked((int)checked((uint)left)),
            6 => left >> right,
            _ => left >>> right
        }, 0, 0);
    }
    catch (OverflowException)
    {
        return (9876, GaFaultKind.Overflow, ordinal);
    }
}

static GaProgram Compile()
{
    const string fixture = """
        using SKECS.GA;
        public struct State { public int Result; }
        public struct Input { public int Left, Right; public uint Mode; }
        [SecsSystem(SystemId = "fixtures.integer")]
        public partial struct IntegerFixture
        {
            public void Execute(ref State state, in Input input)
            {
                if (input.Mode == 0u) state.Result = input.Left / input.Right;
                else if (input.Mode == 1u) state.Result = input.Left % input.Right;
                else if (input.Mode == 2u) state.Result = checked(input.Left + input.Right);
                else if (input.Mode == 3u) state.Result = checked(input.Left - input.Right);
                else if (input.Mode == 4u) state.Result = checked(input.Left * input.Right);
                else if (input.Mode == 5u) state.Result = unchecked((int)checked((uint)input.Left));
                else if (input.Mode == 6u) state.Result = input.Left >> input.Right;
                else if (input.Mode == 7u) state.Result = input.Left >>> input.Right;
                else
                {
                    for (int i = 0; i < 4; i++)
                        state.Result = checked(input.Left + i);
                    state.Result = input.Left / input.Right;
                }
            }
        }
        """;
    string runtime = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");
    var compilation = CSharpCompilation.Create("Conformance",
        [CSharpSyntaxTree.ParseText(fixture, path: "IntegerFixture.cs")],
        new[] { typeof(object).Assembly.Location, runtime, typeof(SecsSystemAttribute).Assembly.Location }
            .Select(path => MetadataReference.CreateFromFile(path)),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var execute = compilation.GetTypeByMetadataName("IntegerFixture")!.GetMembers("Execute").OfType<IMethodSymbol>().Single();
    var result = GaCompiler.Compile(compilation, execute);
    return result.Program ?? throw new InvalidDataException(string.Join("\n", result.Diagnostics));
}

internal static class GaConformanceIo
{
    internal static void Write(string path, uint[] words)
    {
        var bytes = new byte[checked(words.Length * 4)];
        for (int i = 0; i < words.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), words[i]);
        File.WriteAllBytes(path, bytes);
    }

    internal static void Read(string path, uint[] words)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length != checked(words.Length * 4))
            throw new InvalidDataException("Vulkan buffer readback length changed.");
        for (int i = 0; i < words.Length; i++)
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4));
    }

    internal static async Task<string> Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{executable}: {await output}{await errors}");
        return await output;
    }
}
