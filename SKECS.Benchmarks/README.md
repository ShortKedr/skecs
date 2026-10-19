English | [Русский](README.ru.md)

# SKECS benchmarks and ECS comparisons

[Repository](../README.md) | [Current report](../docs/benchmarks/current/REPORT.md) | [Supplement](../docs/benchmarks/current/SUPPLEMENT.md) | [SpaceBattle benchmarks](../SpaceBattle.Benchmarks/README.md) | [Licenses](../THIRD_PARTY_NOTICES.md)

One .NET 8 executable owns SKECS benchmarks, five-framework comparisons, source generation, validation and result analysis. SpaceBattle workloads belong to the separate `SpaceBattle.Benchmarks` project.

`comparison` runs 40 base and 70 stress cases for SKECS, LeoECS Lite, LeoECS Classic, Morpeh and StaticEcs. `supplement` runs 34 standard and 51 extended SKECS BenchmarkDotNet cases, 10 API cases and 21 storage diagnostics. `all` combines only these ECS suites.

![Base comparison](../docs/benchmarks/current/comparison.svg)

## Source Catalogue

| Framework | Base workload implementation | Stress implementation | SpaceBattle native store | Runtime sources |
| --- | --- | --- | --- | --- |
| SKECS | [SkecsWorkload](Comparison/Adapters.cs), wrapping [ComparisonWorkload](Workloads/ComparisonWorkload.cs) | [SkecsStressBackend](Workloads/SkecsStressBackend.cs) | [SkecsStore](../SpaceBattle.SKECS/SkecsStore.cs) | [SKECS](../SKECS/) |
| LeoECS Lite | [LiteWorkload](Comparison/Adapters.cs) | [LiteStressBackend](Comparison/StressAdapters.cs) | [LeoEcsLiteStore](../SpaceBattle.Adapters/LeoEcsLiteStore.cs) | [ecslite/src](Vendor/Sources/ecslite/src/) |
| LeoECS Classic | [ClassicWorkload](Comparison/Adapters.cs) | [ClassicStressBackend](Comparison/StressAdapters.cs) | [LeoEcsClassicStore](../SpaceBattle.Adapters/LeoEcsClassicStore.cs) | [ecs/src](Vendor/Sources/ecs/src/) |
| Morpeh | [MorpehWorkload](Comparison/Adapters.cs) | [MorpehStressBackend](Comparison/StressAdapters.cs) | [MorpehStore](../SpaceBattle.Adapters/MorpehStore.cs) | [Morpeh Core](Vendor/Sources/morpeh/Scellecs.Morpeh/Core/) |
| StaticEcs | [StaticWorkload](Comparison/StaticAdapters.cs) | [StaticStressBackend](Comparison/StaticAdapters.cs) | [StaticEcsStore](../SpaceBattle.Adapters/StaticEcsStore.cs) | [StaticEcs/Src](Vendor/Sources/StaticEcs/Src/), [StaticPack/Src](Vendor/Sources/StaticPack/Src/) |

Build definitions: [SKECS.Benchmarks.csproj](SKECS.Benchmarks.csproj), [StaticLibrary.csproj](Vendor/StaticLibrary.csproj), [MorpehLibrary.csproj](Vendor/MorpehLibrary.csproj), and [SpaceBattle.Adapters.csproj](../SpaceBattle.Adapters/SpaceBattle.Adapters.csproj). StaticPack supports StaticEcs; it is not a sixth compared framework. SpaceBattle stores share the [IEcsStore](../SpaceBattle.Core/EcsContract.cs) contract and [NativeStore](../SpaceBattle.Core/NativeStore.cs) identity layer; see the [adapter contract](../SpaceBattle.Adapters/README.md).

The [vendor manifest](Vendor/manifest.json) records pinned sources; `dotnet run --project SKECS.Benchmarks -c Release -- verify-vendor` verifies the distributed inventory. The distribution contains runtime source subsets and exact upstream licenses, not upstream documentation/assets, binary NuGet caches, or generated build products. A normal clone includes these source subsets and needs no vendor download. `dotnet run --project SKECS.Benchmarks -c Release -- restore-vendor` optionally recovers missing files from pinned archives. [VendorRestore.cs](Analysis/VendorRestore.cs) checks lengths and hashes before installation and refuses to overwrite changed local sources; downloading requires network access.

Licenses are not uniformly unrestricted MIT: LeoECS Classic offers proprietary or MIT-Red terms, LeoECS Lite uses MIT-Red, and Morpeh, StaticEcs, and StaticPack use MIT. Read the [licensing inventory](../THIRD_PARTY_NOTICES.md) and exact [Classic](Vendor/Sources/ecs/LICENSE.md), [Lite](Vendor/Sources/ecslite/LICENSE.md), [Morpeh](Vendor/Sources/morpeh/LICENSE.md), [StaticEcs](Vendor/Sources/StaticEcs/LICENSE.md), and [StaticPack](Vendor/Sources/StaticPack/LICENSE.md) licenses before redistribution. This catalogue is not a legal guarantee.

## Shared Workloads

The [Workloads](Workloads/) directory inside this project supplies `ComparisonWorkload`, `StressContract`, `SkecsStressBackend`, `StressWorkload` and the generated scenario catalog to both this runner and BenchmarkDotNet. Stopwatch and BenchmarkDotNet use the same compiled implementations.

[ComparisonRunner.cs](Comparison/ComparisonRunner.cs) registers all five base adapters. At 1,000 and 100,000 entities it runs Create, Iterate, Churn, and Destroy: Position/Velocity each contain two floats; movement adds velocity to position; churn adds then removes a one-float Mass component.

Validation checks every entity before timing. Setup, seeding, world disposal, and forced GC are outside timings; Morpeh commits are included. Each case warms for at least 400 ms and retains 20 samples. Create/Destroy use one fresh batch per sample; Iterate/Churn target 30 ms with at most 1,000 repetitions.

[StressRunner.cs](Comparison/StressRunner.cs), [StressContract.cs](Workloads/StressContract.cs), and [StressWorkload.cs](Workloads/StressWorkload.cs) share initialized data, shape masks, and seeded operation orders across all five backends. The normal matrix is 100,000/1,000,000 entities, seven workloads, eight samples per case, at least 300 ms warmup, and up to 16 repetitions targeting 30 ms. MixedFrames uses one invocation per sample.

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

The [current report](../docs/benchmarks/current/REPORT.md) presents the complete five-framework base/stress matrix and SVG charts. The [supplement](../docs/benchmarks/current/SUPPLEMENT.md) covers all standard and extended SKECS BenchmarkDotNet cases plus API/storage diagnostics. Raw machine JSON, individual samples and BenchmarkDotNet exports stay in ignored `artifacts/benchmarks/current/`; published Markdown and SVG stay under `docs/`.

## Reproduce

Run from the repository root with the .NET 8 runtime and an SDK supporting C# 13 (9.0 or newer). Generation, validation, reports and SVG use the .NET standard library; Python, PowerShell, matplotlib and NumPy are unnecessary. Measured projects retain their declared NuGet dependencies; offline builds require cached packages.

```sh
dotnet run --project SKECS.Benchmarks -c Release -- generate --check
dotnet run --project SKECS.Benchmarks -c Release -- verify-vendor
dotnet run --project SKECS.Benchmarks -c Release -- validate all
dotnet run --project SKECS.Benchmarks -c Release -- run all
dotnet run --project SKECS.Benchmarks -c Release -- report all
dotnet run --project SKECS.Benchmarks -c Release -- verify all
```

`validate` builds Release and checks contracts without measurement. `run` validates and measures the requested suites sequentially, retains raw evidence and generates bilingual reports. `report` verifies saved local data and regenerates publications; `verify` checks the data without writing reports. Each accepts `comparison`, `supplement` or `all` (default). Stop other builds, benchmarks, games and profilers before timing.

Default raw data is `artifacts/benchmarks/current/micro`; publications are in `docs/benchmarks/current`. JSON, individual samples, manifests and full BenchmarkDotNet exports are ignored by Git; Markdown, SVG and summary tables are published.

```sh
dotnet run --project SKECS.Benchmarks -c Release -- run all --output artifacts/benchmarks/repro/micro
dotnet run --project SKECS.Benchmarks -c Release -- report all --data artifacts/benchmarks/repro/micro --output artifacts/benchmarks/repro/report
dotnet run --project SKECS.Benchmarks -c Release -- verify all --data artifacts/benchmarks/repro/micro
```

Here `all` uses the same micro directory as individual ECS suites. `run --output` must stay under ignored `artifacts/`; `report --output` may select any directory inside this workspace. `--root` overrides repository discovery. `--no-build` accepts only a build certified by a previous invocation with unchanged source and assembly hashes. .NET/NuGet caches and temporary files are under `artifacts/tooling`.

## BenchmarkDotNet suites

[Standard suites](Suites/Standard/) include the original iteration/churn cases, the shared [ComparisonBenchmarks](Suites/Standard/ComparisonBenchmarks.cs) and [StressBenchmarks](Suites/Standard/StressBenchmarks.cs) contracts, and separate [OptimizedApiBenchmarks](Suites/Standard/OptimizedApiBenchmarks.cs). [Extended suites](Suites/Extended/) retain additional SKECS experiments. They all run through this executable.

```sh
dotnet run --project SKECS.Benchmarks -c Release -- bdn --suite standard --list flat
dotnet run --project SKECS.Benchmarks -c Release -- bdn --suite extended --list flat
dotnet run --project SKECS.Benchmarks -c Release -- bdn --suite all --filter '*'
```

`bdn` passes remaining arguments to BenchmarkDotNet. Use `run supplement` for the published supplement: fixed Short/InProcess, three warmup and eight measured iterations, and full JSON exports. InProcess isolation differs from the default toolchain. The opt-in `SKECS_STRESS_EXTREME=1` five-million-entity matrix is outside the default campaign. Dry/smoke jobs check execution and are not full performance rankings.

## Generation and checks

The [model and templates](CodeGen/README.md) generate common adapter and workload code. After editing inputs, run `generate`, then `generate --check`; the check accepts LF/CRLF and writes nothing. Native queries and APIs remain in templates, without adding dispatch inside timed loops.

Base validation checks entity data and lifetime at both measured population sizes. Stress preflight uses 257/4,097 entities, all seven scenarios repeated three times, payload round-trips and retired-handle checks. Measured stress cases validate the full population after warmup and samples. These check workload/adapter contracts, rather than complete upstream test suites.

```sh
dotnet test tools/BenchmarkSupport.Tests/BenchmarkSupport.Tests.csproj -c Release
```
