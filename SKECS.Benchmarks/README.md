English | [Русский](README.ru.md)

# SKECS benchmarks

[Project overview](../README.md) | [SpaceBattle report](../docs/space-battle/REPORT.md) | [Comparison harness](../Benchmarks.Local/README.md) | [Portable tools](../tools/BenchmarkTools/README.md)

## Current comparison

The [current campaign report](../docs/benchmarks/current/REPORT.md) compares SKECS, LeoECS Lite, LeoECS Classic, Morpeh and StaticEcs: 40 base cases and 70 stress cases. All five frameworks use the same logical workload contract.

![Base comparison](../docs/benchmarks/current/comparison.svg)

Create, Iterate, Churn and Destroy run at 1,000 and 100,000 entities. Stress covers dense/sparse/wide iteration, random lookup, 256-byte payload churn, 10% recycling and a 120-frame mixed batch at 100,000 and 1,000,000 entities. The [harness guide](../Benchmarks.Local/README.md) describes the scenarios and validation.

These Stopwatch measurements characterize the recorded machine and source snapshots. They do not establish a universal ranking or Unity/IL2CPP performance. Error bars show sample standard deviation; managed allocation figures exclude native memory. Raw samples, metadata and hashes stay in ignored `artifacts/benchmarks/current/micro`; Markdown and SVG reports are published under `docs/benchmarks/current`.

## SKECS BenchmarkDotNet suites

[ComparisonBenchmarks](ComparisonBenchmarks.cs) and [StressBenchmarks](StressBenchmarks.cs) reference the .NET 8 [shared workload library](../benchmarks/SKECS.Benchmarks.Workloads/SKECS.Benchmarks.Workloads.csproj), as does the five-framework runner. The shared [base workload](../benchmarks/SKECS.Benchmarks.Workloads/ComparisonWorkload.cs) and [stress workload](../benchmarks/SKECS.Benchmarks.Workloads/StressWorkload.cs) therefore have one compiled implementation.

[OptimizedApiBenchmarks](OptimizedApiBenchmarks.cs) evaluates separate factory/accessor APIs. [Program.cs](Program.cs) retains the original iteration/churn benchmarks, and `SKECS.OtherBenchmarks` contains the legacy suite. BenchmarkDotNet measures SKECS here; optimized API results are separate from the shared comparison.

Run from the repository root with .NET 8 runtime and an SDK supporting C# 13 (9.0 or newer):

```sh
dotnet run --project tools/BenchmarkTools -c Release -- generate --check
dotnet run --project tools/BenchmarkTools -c Release -- verify-vendor
dotnet run --project tools/BenchmarkTools -c Release -- validate all
dotnet run --project tools/BenchmarkTools -c Release -- run supplement
dotnet run --project tools/BenchmarkTools -c Release -- report supplement
dotnet run --project tools/BenchmarkTools -c Release -- verify supplement
```

The [supplemental report](../docs/benchmarks/current/SUPPLEMENT.md) covers 34 default cases here and 51 legacy cases with Short/InProcess, three warmup and eight measured iterations, plus direct-creation/accessor cases and retained-storage diagnostics. The portable .NET tool builds and runs these sequentially and retains full BenchmarkDotNet exports locally. Its reporting and SVG generation need no Python or PowerShell.

`run all` also measures the five-framework comparison and full headless SpaceBattle. Desktop startup measurement is explicit: `run desktop` or `run all --include-desktop`. `report all` and `verify all` include saved desktop data when present; `--include-desktop` requires it.

Use `run supplement --output artifacts/benchmarks/repro/micro` for separate raw output. Then use `report supplement --data artifacts/benchmarks/repro/micro --output artifacts/benchmarks/repro/report` and `verify supplement --data artifacts/benchmarks/repro/micro`. These suffixes follow the same `dotnet run --project tools/BenchmarkTools -c Release --` prefix. See the [CLI guide](../tools/BenchmarkTools/README.md) for all options.

The opt-in `SKECS_STRESS_EXTREME=1` five-million-entity matrix remains separate from the default campaign. InProcess has different process isolation from the default BenchmarkDotNet toolchain. Dry/smoke jobs check launch and execution; they are not full performance rankings.

## Historical experiments

The [2026-10-11 optimization methodology](../docs/benchmarks/optimization-methodology.md) records a separate SECS before/after experiment and the preserved binaries needed to interpret it. [Previous charts](../docs/benchmarks/updated/comparison.png) retain historical context. Raw historical measurements are local artifacts; rebuilding current sources does not reproduce the old baseline binary.
