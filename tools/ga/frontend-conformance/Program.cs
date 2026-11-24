using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using FrontendFixture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SKECS;
using SKECS.GA;
using SKECS.Numerics;

const string KernelFile = "Kernels.cs";
const int TickIndex = 41;
bool directOnly = args is ["--direct"];
string? selected = args is ["--kernel", var selectedName] ? selectedName : null;
using var gpu = args is ["--gpu", var backend, var probe, var tools, var outputPath, var index]
    ? GpuLane.Open(backend, probe, tools, outputPath, int.Parse(index))
    : null;
if (args.Length > 0 && !directOnly && selected is null && gpu is null)
    throw new ArgumentException("Usage: FrontendConformance [--direct | --kernel TYPE | --gpu BACKEND PROBES TOOLS OUTPUT DEVICE]");

var kernels = Cases.Create().Where(kernel => selected is null || kernel.Type == selected).ToArray();
Require(kernels.Length != 0, $"Unknown GA fixture kernel '{selected}'.");
var sourceAssembly = typeof(State).Assembly;
using var embedded = sourceAssembly.GetManifestResourceStream(KernelFile)!;
using var reader = new StreamReader(embedded);
var parse = new CSharpParseOptions(LanguageVersion.CSharp12);
var source = CSharpSyntaxTree.ParseText(reader.ReadToEnd(), parse, KernelFile, Encoding.UTF8);
var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
    checkOverflow: false, deterministic: true, nullableContextOptions: NullableContextOptions.Enable);
var referencePaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Append(typeof(World).Assembly.Location).Append(typeof(SecsSystemAttribute).Assembly.Location)
    .Distinct(StringComparer.OrdinalIgnoreCase).Where(path => path != sourceAssembly.Location);
var references = referencePaths.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
var sourceCompilation = CSharpCompilation.Create("GaFrontendSource", [source], references, options);
int successes = 0;
int faults = 0;
int canonicalFaultChecks = 0;

foreach (Kernel kernel in kernels)
{
    // The cases run with the SDK-compiled CLR body even when frontend compilation is unavailable.
    var expected = kernel.Cases.Select(sample => RunDirect(kernel, sample)).ToArray();
    var sites = kernel.Cases.Select(sample => sample.Fault is { } fault
        ? (GaSourceSpan?)LocateFault(source, fault.Expression)
        : null).ToArray();
    if (!directOnly)
    {
        var entry = sourceCompilation.GetTypeByMetadataName("FrontendFixture." + kernel.Type)!
            .GetMembers("Execute").OfType<IMethodSymbol>().Single();
        var result = GaCompiler.Compile(sourceCompilation, entry);
        var program = result.Program ?? throw new InvalidOperationException(
            $"GA frontend rejected {kernel.Type}.{Environment.NewLine}{string.Join(Environment.NewLine, result.Diagnostics)}");
        Require(kernel.SystemId == program.SystemId, $"{kernel.Type} generated an unexpected system ID.");
        var register = EmitAdapter(program, kernel.Type, parse, options, references, sourceAssembly, out var codecType);
        var deviceRows = new List<GpuRow>();
        for (int i = 0; i < kernel.Cases.Length; i++)
        {
            Case sample = kernel.Cases[i];
            string label = $"{kernel.Type}/{sample.Name}";
            uint[] input = Pack(program, sample.State, sample.Input);
            uint[] output = (uint[])input.Clone();
            var irFault = GaInterpreter.Execute(program, output);
            var oracle = sample.Fault is { } fault
                ? (GaKernelFault?)new(fault.Kind, fault.Ordinal, sites[i]!.Value)
                : null;
            Require(irFault == oracle, $"{label} GA interpreter fault {irFault}; expected {oracle}.");
            uint[] expectedWords = oracle is null ? Pack(program, expected[i], sample.Input) : input;
            Require(output.AsSpan().SequenceEqual(expectedWords),
                $"{label} GA interpreter row differs. Actual [{string.Join(',', output)}], expected [{string.Join(',', expectedWords)}].");
            if (gpu is not null)
                deviceRows.Add(new GpuRow(label, input, expectedWords, oracle));

            AdapterResult actual = RunAdapter(register, [sample], chunkCapacity: 1, workers: 1, budget: 1);
            if (oracle is { } expectedFault)
                CheckFault(actual, expectedFault, kernel.SystemId, ordinal: 0, label);
            else
            {
                Require(actual.Fault is null && actual.States.Length == 1 && SameState(actual.States[0], expected[i]),
                    $"{label} generated CPU result differs. Actual fault {actual.Fault}.");
                if (kernel.Type is nameof(IndexedReadonlyReadFirstKernel) or nameof(IndexedReadonlyStoreFirstKernel))
                {
                    var query = (DeterministicQuery)codecType.GetMethod("CreateQuery", Type.EmptyTypes)!.Invoke(null, null)!;
                    var apply = codecType.GetMethod("Apply")!.MakeGenericMethod(typeof(byte))
                        .CreateDelegate<GaBatchProcessor<byte>.ApplyJob>();
                    var applied = RunAdapter((workload, stage) =>
                        workload.AddSystem(stage, query, job => apply(job, 1, expectedWords)),
                        [sample], chunkCapacity: 1, workers: 1, budget: 1);
                    Require(applied.Fault is null && applied.States.Length == 1 && SameState(applied.States[0], expected[i]),
                        $"{label} generated row codec applied different readonly fields.");
                }
            }
        }

        if (kernel.Type == nameof(NamedKernel))
        {
            // An earlier entity beats a lower evaluation ordinal in a later entity.
            Case[] competing = [kernel.Cases[0], kernel.Cases[3], kernel.Cases[1]];
            var expectedFault = new GaKernelFault(GaFaultKind.Overflow, 2, sites[3]!.Value);
            foreach (int chunk in new[] { 1, 3 })
            {
                var resultWithCompetition = RunAdapter(register, competing, chunk, workers: 2, budget: 1);
                CheckFault(resultWithCompetition, expectedFault, kernel.SystemId, ordinal: 1,
                    $"{kernel.Type}/competing entities, chunk {chunk}");
                canonicalFaultChecks++;
            }
        }
        gpu?.Compare(program, deviceRows);
    }

    int succeeded = kernel.Cases.Count(sample => sample.Fault is null);
    successes += succeeded;
    faults += kernel.Cases.Length - succeeded;
    Console.WriteLine($"{kernel.SystemId} success={succeeded} faults={kernel.Cases.Length - succeeded}");
}

string lanes = directOnly ? "direct-csharp" : "direct-csharp,ga-interpreter,generated-cpu";
if (gpu is not null)
    lanes += "," + gpu.Backend;
Console.WriteLine($"Frontend conformance passed lanes={lanes}" +
    $" kernels={kernels.Length} successfulRows={successes} faultRows={faults} canonicalFaultChecks={canonicalFaultChecks}");

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static State RunDirect(Kernel kernel, Case sample)
{
    State state = sample.State;
    GaFaultKind? actual = null;
    try
    {
        kernel.Execute(ref state, sample.Input);
    }
    catch (OverflowException)
    {
        actual = GaFaultKind.Overflow;
    }
    catch (DivideByZeroException)
    {
        actual = GaFaultKind.DivideByZero;
    }
    catch (IndexOutOfRangeException)
    {
        actual = GaFaultKind.IndexOutOfRange;
    }
    Require(actual == sample.Fault?.Kind,
        $"{kernel.Type}/{sample.Name} direct C# fault {actual}; expected {sample.Fault?.Kind}.");
    return state;
}

static GaSourceSpan LocateFault(SyntaxTree source, string expression)
{
    var matches = source.GetRoot().DescendantNodes().OfType<ExpressionSyntax>()
        .Where(node => node.ToString() == expression).ToArray();
    Require(matches.Length == 1, $"Fault marker '{expression}' must identify exactly one source expression.");
    var span = matches[0].Span;
    return new GaSourceSpan(source.FilePath, span.Start, span.Length);
}

static Func<DeterministicWorkload<byte>, SystemStage, DeterministicWorkload<byte>> EmitAdapter(
    GaProgram program, string kernel, CSharpParseOptions parse, CSharpCompilationOptions options,
    MetadataReference[] references, Assembly sourceAssembly, out Type codecType)
{
    string name = kernel + "Adapter";
    var adapter = CSharpSyntaxTree.ParseText(GaCpuAdapterEmitter.Emit(program, name),
        parse, name + ".g.cs", Encoding.UTF8);
    var codec = CSharpSyntaxTree.ParseText(GaRowCodecEmitter.Emit(program, name + "RowCodec"),
        parse, name + ".RowCodec.g.cs", Encoding.UTF8);
    var compilation = CSharpCompilation.Create(name, [adapter, codec],
        references.Append(MetadataReference.CreateFromFile(sourceAssembly.Location)), options);
    using var stream = new MemoryStream();
    var emitted = compilation.Emit(stream);
    Require(emitted.Success, $"Generated source did not compile.{Environment.NewLine}{string.Join(Environment.NewLine, emitted.Diagnostics)}");
    stream.Position = 0;
    var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
    codecType = assembly.GetType(name + "RowCodec")!;
    var method = assembly.GetType(name)!.GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(candidate => candidate.Name == "Register" &&
            candidate.GetParameters() is [_, var stage] && stage.ParameterType == typeof(SystemStage));
    return method.MakeGenericMethod(typeof(byte))
        .CreateDelegate<Func<DeterministicWorkload<byte>, SystemStage, DeterministicWorkload<byte>>>();
}

static AdapterResult RunAdapter(Func<DeterministicWorkload<byte>, SystemStage, DeterministicWorkload<byte>> register,
    Case[] rows, int chunkCapacity, int workers, int budget)
{
    using var world = new World(new WorldOptions(chunkCapacity));
    var entities = rows.Select(row => world.CreateEntity(row.State, row.Input)).ToArray();
    var workload = register(new DeterministicWorkload<byte>(), SystemStage.Update);
    using var runner = new DeterministicRunner<byte>(world, workload, workers);
    runner.BeginTick(TickIndex, 0, Fixed.One);
    DeterministicTickStatus status = DeterministicTickStatus.Pending;
    for (int step = 0; status == DeterministicTickStatus.Pending && step < 50; step++)
        status = runner.Advance(budget);
    if (status == DeterministicTickStatus.Faulted)
    {
        Require(runner.Fault is GaExecutionException && runner.LastCompleted is null,
            $"The faulted CPU workload published a tick or returned an unexpected exception. {runner.Fault}");
        Require(WorldOwned(world, entities[0], rows[0].State), "The runner released the faulted world.");
        return new AdapterResult((GaExecutionException)runner.Fault!, entities, []);
    }

    Require(status == DeterministicTickStatus.Completed &&
        runner.LastCompleted is { TickIndex: TickIndex, Events.Count: 0 }, $"Generated CPU returned {status}. {runner.Fault}");
    State[] outputs = new State[rows.Length];
    for (int i = 0; i < rows.Length; i++)
    {
        outputs[i] = world.Get<State>(entities[i]);
        Require(world.Get<Input>(entities[i]).Equals(rows[i].Input), "Generated CPU changed a read-only input.");
    }
    return new AdapterResult(null, entities, outputs);
}

static bool WorldOwned(World world, Entity entity, State unchanged)
{
    try
    {
        world.Set(entity, unchanged);
        return false;
    }
    catch (InvalidOperationException)
    {
        return true;
    }
}

static void CheckFault(AdapterResult result, GaKernelFault expected, string id, int ordinal, string label)
{
    var entity = result.Entities[ordinal];
    var fault = result.Fault;
    Require(fault is { TickIndex: TickIndex, Stage: SystemStage.Update, SystemOrder: 0, KernelOrder: 0 } &&
        fault.SystemId == id && fault.SourceOrderOrdinal == ordinal && fault.SourceId == entity.Id &&
        fault.SourceGeneration == entity.Version && fault.KernelFault == expected,
        $"{label} generated CPU chose {fault?.KernelFault} at source ordinal {fault?.SourceOrderOrdinal}; expected {expected} at {ordinal}.");
}

static bool SameState(State a, State b) =>
    a.Current.Equals(b.Current) && a.Mirror.Equals(b.Mirror) && a.Point.Equals(b.Point) &&
    a.Balance.Equals(b.Balance) && a.Result == b.Result &&
    a.Slots[0] == b.Slots[0] && a.Slots[1] == b.Slots[1] && a.Slots[2] == b.Slots[2] && a.Slots[3] == b.Slots[3] &&
    a.Packets[0].Equals(b.Packets[0]) && a.Packets[1].Equals(b.Packets[1]) &&
    a.Packets[2].Equals(b.Packets[2]) && a.Packets[3].Equals(b.Packets[3]) &&
    a.ReadonlyPackets[0].Equals(b.ReadonlyPackets[0]) && a.ReadonlyPackets[1].Equals(b.ReadonlyPackets[1]) &&
    a.ReadonlyPackets[2].Equals(b.ReadonlyPackets[2]) && a.ReadonlyPackets[3].Equals(b.ReadonlyPackets[3]);

static uint[] Pack(GaProgram program, State state, Input input)
{
    var words = new uint[program.WordCount];
    int offset = 0;
    foreach (var field in program.Fields)
    {
        object leaf = field.Component switch
        {
            "global::FrontendFixture.State" => field.Path.StartsWith("Slots[", StringComparison.Ordinal)
                ? state.Slots[int.Parse(field.Path.AsSpan(6, field.Path.Length - 7))]
                : ReadStateField(state, field.Path),
            "global::FrontendFixture.Input" => ReadField(input, field.Path),
            _ => throw new InvalidOperationException($"Unexpected GA component {field.Component}.")
        };
        switch (field.Type, leaf)
        {
            case (GaScalar.Int32, int value):
                words[offset++] = unchecked((uint)value);
                break;
            case (GaScalar.UInt32, uint value):
                words[offset++] = value;
                break;
            case (GaScalar.Bool, bool value):
                words[offset++] = value ? 1u : 0u;
                break;
            case (GaScalar.Fixed, Fixed value):
                words[offset++] = unchecked((uint)value.Raw);
                words[offset++] = unchecked((uint)(value.Raw >> 32));
                break;
            default:
                throw new InvalidOperationException($"Unexpected type for {field.Component}.{field.Path} ({field.Type}).");
        }
    }
    Require(offset == words.Length, "Packed row width differs.");
    return words;
}

static object ReadStateField(State state, string path)
{
    if (path.StartsWith("ReadonlyPackets[", StringComparison.Ordinal))
    {
        int terminator = path.IndexOf(']');
        int element = int.Parse(path.AsSpan("ReadonlyPackets[".Length, terminator - "ReadonlyPackets[".Length));
        return ReadField(state.ReadonlyPackets[element], path[(terminator + 2)..]);
    }
    if (!path.StartsWith("Packets[", StringComparison.Ordinal))
        return ReadField(state, path);
    int end = path.IndexOf(']');
    int index = int.Parse(path.AsSpan("Packets[".Length, end - "Packets[".Length));
    return ReadField(state.Packets[index], path[(end + 2)..]);
}

static object ReadField(object component, string path)
{
    foreach (string part in path.Split('.'))
        component = component.GetType().GetField(part)?.GetValue(component)
            ?? throw new InvalidOperationException($"Could not locate field {part} of {path}.");
    return component;
}

internal sealed record AdapterResult(GaExecutionException? Fault, Entity[] Entities, State[] States);
