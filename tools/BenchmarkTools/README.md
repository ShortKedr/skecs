English | [Русский](README.ru.md)

# Portable benchmark tools

This .NET 8 command-line application generates benchmark sources, verifies vendored runtime sources/licenses, builds and validates workloads, runs sequential measurements and produces bilingual Markdown/SVG reports. The tool uses the .NET standard library without external packages, Python, PowerShell, platform management commands or graphics libraries. The optional desktop measurement runs the game's graphics client.

Run from the repository root. The measured solution needs the .NET 8 runtime and an SDK supporting C# 13 (9.0 or newer). Its projects retain their declared NuGet dependencies; network access is needed for uncached package restore. A normal clone already contains the pinned vendor source subsets and upstream licenses.

```sh
dotnet run --project tools/BenchmarkTools -c Release -- generate --check
dotnet run --project tools/BenchmarkTools -c Release -- verify-vendor
dotnet run --project tools/BenchmarkTools -c Release -- validate all
dotnet run --project tools/BenchmarkTools -c Release -- run all
dotnet run --project tools/BenchmarkTools -c Release -- report all
dotnet run --project tools/BenchmarkTools -c Release -- verify all
```

`run all` is headless: the five-framework base/stress comparison, full SpaceBattle and supplemental SKECS suites run sequentially. Each suite validates its contracts before measurement, records source/assembly/input hashes and regenerates its reports. Add `--include-desktop` or use `run desktop` for the matched desktop startup study; it requires a working graphics device. Stop other measurement processes, builds, games and profilers before timing.

## Commands and suites

| Command | Purpose |
| --- | --- |
| `generate` | Emit ten generated C# sources from the model/templates; `--check` checks freshness without writing |
| `verify-vendor` | Verify pinned distributed sources and exact licenses against the manifest |
| `restore-vendor` | Download pinned archives and recover only missing, verified vendor files |
| `hardware` | Capture portable CPU, OS and memory-limit metadata |
| `validate [suite]` | Build Release and validate contracts without running timing campaigns |
| `run [suite]` | Build, validate, measure sequentially and render reports |
| `report [suite]` | Verify saved local inputs and regenerate Markdown/SVG |
| `verify [suite]` | Verify saved local evidence without changing publications |

Suites are `all` (default), `comparison`, `supplement`, `space-battle` and `desktop`. `report all` and `verify all` process desktop when saved data exists; add `--include-desktop` to require it. Explicit `report desktop` or `verify desktop` always requires its dataset. Missing or inconsistent requested inputs fail; a clone containing only published reports cannot regenerate raw evidence.

| Suite | Default local raw data | Published output |
| --- | --- | --- |
| `comparison`, `supplement` | `artifacts/benchmarks/current/micro` | `docs/benchmarks/current` |
| `space-battle` | `artifacts/benchmarks/current/space-battle` | `docs/space-battle/REPORT.md`, Russian counterpart and `current/*.svg` |
| `desktop` | `artifacts/benchmarks/current/desktop` | `docs/space-battle/DESKTOP_TIMING.md`, Russian counterpart and `desktop-current/*.svg` |

Machine JSON, raw timing/allocation samples, CSV/gzip archives, full BenchmarkDotNet exports and manifests are ignored local artifacts. Published reports and SVG charts are versioned. Report values are derived from validated inputs, rather than entered manually.

## Paths and options

`run --output` selects a raw output directory under ignored `artifacts/`; a custom run places reports in its `reports/` subdirectory. `report`/`verify --data` selects saved raw inputs. `report --output` selects any publication destination inside this workspace, including `docs/`. Single-suite paths refer directly to that suite; `all` uses `micro`, `space-battle` and optional `desktop` subdirectories of each supplied root.

```sh
dotnet run --project tools/BenchmarkTools -c Release -- run all --include-desktop --output artifacts/benchmarks/repro
dotnet run --project tools/BenchmarkTools -c Release -- report all --data artifacts/benchmarks/repro --output artifacts/benchmarks/repro-published
dotnet run --project tools/BenchmarkTools -c Release -- verify all --data artifacts/benchmarks/repro
```

All paths must stay inside this workspace. `--root` overrides automatic repository discovery. The tool keeps .NET, NuGet and temporary files in project-local directories. `--no-build` reuses only a certified build whose source and assembly hashes still match and whose project set covers the requested suites.

SpaceBattle smoke overrides `--ticks N --warmup N` require separate output; shortened runs do not qualify as full reports. `--skip-ticks` skips CSV recomputation during report/verification while retaining archive integrity checks; use the default full verification for publication.

The neutral machine label defaults to `local-machine`. Use distinct `SKECS_BENCHMARK_MACHINE_LABEL` values when combining different computers. Hardware/runtime/revision metadata remain recorded.

## Recovery and diagnostics

A normal clone needs no vendor recovery. If pinned files are missing:

```sh
dotnet run --project tools/BenchmarkTools -c Release -- restore-vendor
dotnet run --project tools/BenchmarkTools -c Release -- verify-vendor
```

[VendorRestore.cs](VendorRestore.cs) reads remote pinned archives and installs local manifest files only after checking their lengths and SHA-256 hashes. It refuses to overwrite changed local sources. Downloading needs network access; license conditions remain in force.

```sh
dotnet run --project tools/BenchmarkTools -c Release -- hardware --output artifacts/benchmarks/repro/hardware.json
```

[HardwareCapture.cs](HardwareCapture.cs) records the neutral machine label, CPU/available logical processor count, OS and GC available-memory limit. That memory limit is not physical RAM. The default destination is `artifacts/benchmarks/current/hardware.json`; custom `--output` must also stay under `artifacts/`.

Saved entity-composition diagnostics are verified and published separately from timing measurements; they are outside `run all`:

```sh
dotnet run --project tools/BenchmarkTools -c Release -- report composition --data artifacts/benchmarks/history/docs/space-battle/composition --output docs/space-battle/composition
dotnet run --project tools/BenchmarkTools -c Release -- verify composition --data artifacts/benchmarks/history/docs/space-battle/composition
```

[CompositionReports.cs](CompositionReports.cs) reads `low.json`, `nominal.json` and `high.json`, checks census/component conservation and verifies available source CSV/results against recorded hashes and aggregates. Historical diagnostics describe the recorded revision, rather than current gameplay performance. The paths above are the defaults.

## Sources and checks

The [model/templates](../../Benchmarks.Local/CodeGen/README.md) generate native adapter code without introducing dispatch in timed loops. [SKECS.Benchmarks.Workloads](../../benchmarks/SKECS.Benchmarks.Workloads/SKECS.Benchmarks.Workloads.csproj) is the shared .NET 8 library consumed by the comparison runner and BenchmarkDotNet.

```sh
dotnet test tools/BenchmarkTools.Tests/BenchmarkTools.Tests.csproj -c Release
```

See the [comparison harness](../../Benchmarks.Local/README.md), [SKECS suites](../../SKECS.Benchmarks/README.md), [SpaceBattle guide](../../docs/space-battle/README.md) and [third-party notices](../../THIRD_PARTY_NOTICES.md) for workload, interpretation and licensing details.
