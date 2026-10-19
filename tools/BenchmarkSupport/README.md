English | [Русский](README.ru.md)

# Shared benchmark support

This .NET 8 class library supplies workspace path guards, process execution, source/assembly hashing, portable hardware metadata, SVG charts and the shared report colour scale. It has no command-line entry point and contains no measured workloads.

Use the target that owns the workload:

| Target | Guide | Scope |
| --- | --- | --- |
| `SKECS.Benchmarks` | [ECS benchmarks](../../SKECS.Benchmarks/README.md) | SKECS and five-framework microbenchmarks, source generation, vendor verification and analysis |
| `SpaceBattle.Benchmarks` | [Game benchmarks](../../SpaceBattle.Benchmarks/README.md) | Headless simulation, desktop timing and saved composition analysis |

Each target owns its measurement and report implementations. This library holds only shared infrastructure; it does not dispatch commands between targets. Both use ignored `artifacts/` for raw evidence and `artifacts/tooling` for caches and temporary files. Published reports contain generated Markdown, SVG and summary tables.

[BenchmarkMachineInfo.cs](../BenchmarkMachineInfo.cs) and [BenchmarkMachineLabel.cs](../BenchmarkMachineLabel.cs) also serve game/measurement projects. CPU metadata uses supported .NET APIs, with an architecture fallback. Hardware capture records the GC available-memory limit rather than physical RAM. The default machine label is `local-machine`; use distinct neutral `SKECS_BENCHMARK_MACHINE_LABEL` values for different computers.

```sh
dotnet test tools/BenchmarkSupport.Tests/BenchmarkSupport.Tests.csproj -c Release
```

The tests exercise shared infrastructure and both targets' generation, validation and reporting contracts. Report generation uses the .NET standard library without Python, PowerShell or external graphics libraries. Measured targets retain their declared NuGet dependencies; optional desktop measurements require a working graphics device.
