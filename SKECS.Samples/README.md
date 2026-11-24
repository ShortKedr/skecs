# SKECS Samples

These examples exercise the modules and tools in this source checkout. Run the commands from the repository root with the .NET 10 SDK and .NET 8 runtime. For the released core quickstart, see the [SKECS 0.2.0 documentation](https://shortkedr.github.io/skecs/0.2.0/).

```sh
dotnet run --project SKECS.Samples -c Release -- list
```

| Sample | Run command | Expected result |
| --- | --- | --- |
| [Core](Core/Sample.cs) | `dotnet run --project SKECS.Samples -c Release -- core` | A movement system updates an entity to `(15, 5)` after five ticks. |
| [Serialization](Serialization/Sample.cs) | `dotnet run --project SKECS.Samples -c Release -- serialization` | A binary checkpoint restores the world, tick and RNG state. The restored simulation matches through tick 24. |
| [Network](Network/Sample.cs) | `dotnet run --project SKECS.Samples -c Release -- network` | Two loopback peers apply a full snapshot at tick 0 and a delta at tick 1. The replica reaches `(3, 1)`. |
| [GA](Ga/Sample.cs) | `dotnet run --project SKECS.Samples -c Release -- ga` | The portable movement kernel runs on the CPU and reaches `(15, 5)` and `(5, 6)`. |
| [Profiler](Profiler/Sample.cs) | `dotnet run --project SKECS.Samples -c Release -- profiler` | The XML trace contains three frames with four created, moved and destroyed rows and zero dropped events. |
| [Runtime Profiles](RuntimeProfiles/Sample.cs) | `dotnet run --project SKECS.Samples -c Release -- runtime-profiles` | A matching profile applies chunk capacity 64. A different workload identity is rejected and the sample uses defaults. |
| [Determinism](Determinism/Sample.cs) | `dotnet run --project SKECS.Samples -c Release -- determinism` | Two independent runs match, and the comparison identifies the intentional divergence at step 1 in a third trace. |
| [Agentic](Agentic/Sample.cs) | `dotnet run --project SKECS.Samples -c Release -- agentic` | The local harness discovers and runs the core test operation. The sample prints the exit code and the paths to the result JSON and TRX report. |

Each sample checks its result and returns a nonzero exit code on failure. Determinism leaves its three trace directories under `artifacts/samples/determinism/`.

## GA on Vulkan

`ga auto` demonstrates the CPU fallback when no device is supplied. On a system with a Vulkan compute device, supply the repository's Slang adapter and Vulkan native bridge to require GPU execution. This uses GA's experimental Vulkan profile. The sample compares GPU readback against the CPU state after each tick.

On Windows x64, build the tools with Visual Studio C++ tools and the Vulkan SDK configured as described in [build-probe.ps1](../tools/ga/probe/build-probe.ps1). Keep the SDK's `Bin` directory on `PATH` when running the sample.

```powershell
./tools/ga/probe/build-probe.ps1 -BuildOnly
$env:Path = "$env:VULKAN_SDK\Bin;$env:Path"
dotnet run --project SKECS.Samples -c Release -- ga vulkan artifacts/ga/probe/ga-slang-probe.exe artifacts/ga/probe/ga-vulkan-native.dll artifacts/samples/ga
```

## Export the profiler trace

Pass `--output <new-file.xml>` to retain the XML trace. The command requires an existing parent directory and creates a new file.

```sh
dotnet run --project SKECS.Samples -c Release -- profiler --output artifacts/sample-trace.xml
```

## Prepare the Agentic test operation

The harness runs locally in a Git checkout and uses an explicit restore step. Restore its core test dependencies once before running the sample. Repeat after dependency changes. The sample requires Git and uses no model credentials.

```sh
dotnet restore SKECS.Tests/SKECS.Tests.csproj --artifacts-path artifacts/agentic-build --packages artifacts/tooling/nuget-packages
dotnet run --project SKECS.Samples -c Release -- agentic
```
