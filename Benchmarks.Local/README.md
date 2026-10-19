English | [Русский](README.ru.md)

# Local ECS Comparisons

This directory is part of the public repository. It contains the reproducible
five-framework comparison runner, adapters, and published reports.
"Local" refers to running benchmarks on a machine; internal development notes
belong in `../skecs-private/`.

[Repository](../README.md) | [Benchmark report](../SKECS.Benchmarks/README.md) | [SpaceBattle report](../docs/space-battle/REPORT.md) | [Third-party notices](../THIRD_PARTY_NOTICES.md)

This directory contains the five-framework Stopwatch comparison harness, its adapters and stress backends. The portable [.NET tools](../tools/BenchmarkTools/README.md) build, validate, measure, verify and publish reports. BenchmarkDotNet suites live separately in `SKECS.Benchmarks` and `SKECS.OtherBenchmarks` and measure SKECS, not all five frameworks.

## Source Catalogue

| Framework | Base workload implementation | Stress implementation | SpaceBattle native store | Runtime sources |
| --- | --- | --- | --- | --- |
| SKECS | [SkecsWorkload](Adapters.cs), wrapping [ComparisonWorkload](../benchmarks/SKECS.Benchmarks.Workloads/ComparisonWorkload.cs) | [SkecsStressBackend](../benchmarks/SKECS.Benchmarks.Workloads/SkecsStressBackend.cs) | [SkecsStore](../SpaceBattle.SKECS/SkecsStore.cs) | [SKECS](../SKECS/) |
| LeoECS Lite | [LiteWorkload](Adapters.cs) | [LiteStressBackend](StressAdapters.cs) | [LeoEcsLiteStore](../SpaceBattle.Adapters/LeoEcsLiteStore.cs) | [ecslite/src](vendor/ecslite/src/) |
| LeoECS Classic | [ClassicWorkload](Adapters.cs) | [ClassicStressBackend](StressAdapters.cs) | [LeoEcsClassicStore](../SpaceBattle.Adapters/LeoEcsClassicStore.cs) | [ecs/src](vendor/ecs/src/) |
| Morpeh | [MorpehWorkload](Adapters.cs) | [MorpehStressBackend](StressAdapters.cs) | [MorpehStore](../SpaceBattle.Adapters/MorpehStore.cs) | [Morpeh Core](vendor/morpeh/Scellecs.Morpeh/Core/) |
| StaticEcs | [StaticWorkload](StaticAdapters.cs) | [StaticStressBackend](StaticAdapters.cs) | [StaticEcsStore](../SpaceBattle.Adapters/StaticEcsStore.cs) | [StaticEcs/Src](vendor/StaticEcs/Src/), [StaticPack/Src](vendor/StaticPack/Src/) |

Build definitions: [Comparison.csproj](Comparison.csproj), [StaticLibrary.csproj](StaticLibrary.csproj), [MorpehLibrary.csproj](MorpehLibrary.csproj), and [SpaceBattle.Adapters.csproj](../SpaceBattle.Adapters/SpaceBattle.Adapters.csproj). StaticPack supports StaticEcs; it is not a sixth compared framework. SpaceBattle stores share the [IEcsStore](../SpaceBattle.Core/EcsContract.cs) contract and [NativeStore](../SpaceBattle.Core/NativeStore.cs) identity layer; see the [adapter contract](../SpaceBattle.Adapters/README.md).

The [vendor manifest](vendor-manifest.json) records pinned sources; `dotnet run --project tools/BenchmarkTools -c Release -- verify-vendor` verifies the distributed inventory. The distribution contains runtime source subsets and exact upstream licenses, not upstream documentation/assets, binary NuGet caches, or generated build products. A normal clone includes these source subsets and needs no vendor download. `dotnet run --project tools/BenchmarkTools -c Release -- restore-vendor` optionally recovers missing files from pinned archives. [VendorRestore.cs](../tools/BenchmarkTools/VendorRestore.cs) checks lengths and hashes before installation and refuses to overwrite changed local sources; downloading requires network access.

Licenses are not uniformly unrestricted MIT: LeoECS Classic offers proprietary or MIT-Red terms, LeoECS Lite uses MIT-Red, and Morpeh, StaticEcs, and StaticPack use MIT. Read the [licensing inventory](../THIRD_PARTY_NOTICES.md) and exact [Classic](vendor/ecs/LICENSE.md), [Lite](vendor/ecslite/LICENSE.md), [Morpeh](vendor/morpeh/LICENSE.md), [StaticEcs](vendor/StaticEcs/LICENSE.md), and [StaticPack](vendor/StaticPack/LICENSE.md) licenses before redistribution. This catalogue is not a legal guarantee.

## Shared Workloads

The .NET 8 class library [SKECS.Benchmarks.Workloads](../benchmarks/SKECS.Benchmarks.Workloads/SKECS.Benchmarks.Workloads.csproj) supplies `ComparisonWorkload`, `StressContract`, `SkecsStressBackend`, `StressWorkload` and the generated scenario catalog to both this runner and BenchmarkDotNet. Both consumers use project references.

[Program.cs](Program.cs) registers all five base adapters. At 1,000 and 100,000 entities it runs Create, Iterate, Churn, and Destroy: Position/Velocity each contain two floats; movement adds velocity to position; churn adds then removes a one-float Mass component.

Validation checks every entity before timing. Setup, seeding, world disposal, and forced GC are outside timings; Morpeh commits are included. Each case warms for at least 400 ms and retains 20 samples. Create/Destroy use one fresh batch per sample; Iterate/Churn target 30 ms with at most 1,000 repetitions.

[StressRunner.cs](StressRunner.cs), [StressContract.cs](../benchmarks/SKECS.Benchmarks.Workloads/StressContract.cs), and [StressWorkload.cs](../benchmarks/SKECS.Benchmarks.Workloads/StressWorkload.cs) share initialized data, shape masks, and seeded operation orders across all five backends. The normal matrix is 100,000/1,000,000 entities, seven workloads, eight samples per case, at least 300 ms warmup, and up to 16 repetitions targeting 30 ms. MixedFrames uses one invocation per sample.

| Stress scenario | One operation |
| --- | --- |
| DenseIteration | Move all entities across populated layouts |
| SparseIteration | Move the 1% Hot selection |
| WideIteration | Move entities and update first/last fields of a 256-byte payload |
| RandomLookup | Read positions in seeded shuffled order and return a checksum |
| PayloadChurn | Add and remove the 256-byte payload in shuffled order |
| Recycle10Percent | Destroy/recreate 10%, restoring logical data and layouts |
| MixedFrames | 120 frames, each with full movement, 5% payload churn, 10% reads, 1% recycling |

Stress uses 256 base masks from eight optional four-byte components, 12-byte Position/Velocity, and four-byte Hot on every hundredth entity. Intermediate transitions and Hot can create additional archetypes. MixedFrames repeats fixed subsets; it is not uniform random population replacement.

RNG, sorting, setup, validation, disposal, and forced GC are untimed. Natural GC caused by measured operations remains included.

Equivalent logical work does not mean identical layouts, queries, or optimal APIs.

Allocation fields measure managed allocations on the measured thread. They exclude native storage and do not represent total memory. Error bars represent sample standard deviation, not confidence intervals. Results apply to the recorded machine/runtime/revisions, not Unity/IL2CPP or a universal framework ranking. SKECS-only optimized creation/accessor experiments are separate and do not replace the shared API comparison.

The JSON `machine` field uses the anonymous label `local-machine`. CPU, OS,
runtime, source revisions and measurement conditions remain recorded so the
results can be interpreted without publishing the computer name.
For a campaign combining different computers, set a distinct neutral
`SKECS_BENCHMARK_MACHINE_LABEL` on each one; the default label alone does not
prove that two runs came from the same host.

## Current campaign

The [current report](../docs/benchmarks/current/REPORT.md) presents the complete five-framework base/stress matrix and SVG charts. The [supplement](../docs/benchmarks/current/SUPPLEMENT.md) covers all default SKECS and legacy BenchmarkDotNet cases plus API/storage diagnostics. Raw machine JSON, individual samples, tick CSV/gzip archives and BenchmarkDotNet exports stay in ignored `artifacts/benchmarks/current/`; published Markdown and SVG stay under `docs/`.

## Reproduce

Run from the repository root with the .NET 8 runtime and an SDK supporting C# 13 (9.0 or newer). The tool itself targets .NET 8 and uses only its standard library. Python, PowerShell, matplotlib and NumPy are unnecessary. Building the measured projects restores their declared NuGet dependencies; an offline build requires those packages to be cached.

```sh
dotnet run --project tools/BenchmarkTools -c Release -- generate --check
dotnet run --project tools/BenchmarkTools -c Release -- verify-vendor
dotnet run --project tools/BenchmarkTools -c Release -- validate all
dotnet run --project tools/BenchmarkTools -c Release -- run all
```

`run all` builds Release, validates contracts, then runs the five-framework comparison, full headless SpaceBattle and supplemental suites sequentially. It regenerates bilingual reports and SVG charts after each suite. Add `--include-desktop` to include the matched desktop startup study, or select `run desktop`; desktop measurement requires a working graphics device. Stop other builds, benchmarks, games and profilers before timing.

```sh
dotnet run --project tools/BenchmarkTools -c Release -- run all --include-desktop
dotnet run --project tools/BenchmarkTools -c Release -- report all
dotnet run --project tools/BenchmarkTools -c Release -- verify all
```

`report all` and `verify all` include desktop when its local dataset exists; `--include-desktop` requires it.

`report` regenerates published output from saved local inputs; `verify` checks those inputs without changing reports. Both reject missing or inconsistent evidence. The tools retain source, assembly and input hashes and configure project-local .NET/NuGet/temp directories. `--no-build` requires an unchanged build certified by a previous tool invocation.

Use `run comparison`, `run supplement` or `run space-battle` for an individual suite. Keep a reproduction separate from canonical output with `--output`; select its saved data later with `--data`:

```sh
dotnet run --project tools/BenchmarkTools -c Release -- run comparison --output artifacts/benchmarks/repro/micro
dotnet run --project tools/BenchmarkTools -c Release -- report comparison --data artifacts/benchmarks/repro/micro --output artifacts/benchmarks/repro/report
dotnet run --project tools/BenchmarkTools -c Release -- verify comparison --data artifacts/benchmarks/repro/micro
```

For `run all --output <root>`, raw suites occupy `<root>/micro`, `<root>/space-battle` and, when requested, `<root>/desktop`; reports go under each suite's `reports/`. For `report all --data <root> --output <published-root>`, the tool reads those suite subdirectories and creates corresponding publication subdirectories. Raw `run --output` must stay under ignored `artifacts/`; `report --output` may select any directory inside this workspace. See the [CLI guide](../tools/BenchmarkTools/README.md) for defaults and all options.

`hardware --output artifacts/benchmarks/repro/hardware.json` captures portable CPU/OS metadata and the GC available-memory limit, rather than physical RAM. `report composition` and `verify composition` process saved entity-composition diagnostics separately from timing measurements; see the [CLI guide](../tools/BenchmarkTools/README.md) for complete commands and paths.

Shared benchmark sources come from the [model](CodeGen/benchmark-model.json) and native API templates in [CodeGen](CodeGen/README.md). After editing inputs, run `dotnet run --project tools/BenchmarkTools -c Release -- generate`. `generate --check` accepts LF or CRLF checkouts, detects stale output without writing, and runs before measurement. Generated native loops preserve framework-specific queries and APIs.

Base validation checks entity data and lifetime at both measured population sizes. Stress preflight uses 257/4,097 entities, all seven scenarios repeated three times, payload round-trips and retired-handle checks. Every measured stress case validates the full population after warmup and samples. These are workload/adapter contract checks, rather than complete upstream test suites.

## Retained experiments

The [optimization methodology](../docs/benchmarks/optimization-methodology.md) explains preserved baseline/optimized binaries and paired historical datasets. Earlier runs and charts retain their own metadata and scope; they do not describe the current ranking. Do not combine observations from different campaigns or use Dry/smoke timings as a full performance ranking.

[BenchmarkDotNet suites](../SKECS.Benchmarks/README.md) and the [SpaceBattle guide](../docs/space-battle/README.md) describe their separate workloads.
