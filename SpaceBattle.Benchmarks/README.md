English | [Русский](README.ru.md)

# SpaceBattle benchmarks

[SpaceBattle guide](../docs/space-battle/README.md) | [Current report](../docs/space-battle/REPORT.md) | [Desktop timing](../docs/space-battle/DESKTOP_TIMING.md) | [ECS microbenchmarks](../SKECS.Benchmarks/README.md)

This .NET 8 executable owns SpaceBattle measurement, simulation validation and saved-data analysis. It compares SKECS, LeoECS Lite, LeoECS Classic, Morpeh and StaticEcs using five independent native C# cores. SKECS-only and framework microbenchmarks belong to `SKECS.Benchmarks`.

## Reproduce

Run from the repository root with the .NET 8 runtime and an SDK supporting C# 13 (9.0 or newer). The native CLI builds Release, validates contracts, measures sequentially and generates bilingual Markdown/SVG reports. Python, PowerShell and external graphics libraries are unnecessary for analysis. Desktop measurement requires a working graphics device. Builds restore declared NuGet dependencies; offline builds require cached packages.

```sh
dotnet run --project SpaceBattle.Benchmarks -c Release -- validate space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- run space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- report space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- verify space-battle
```

On Windows, rebuilding a loaded host DLL can fail with MSB3021. Run the campaign
controller from a fresh ignored directory to keep its files separate from the
outputs it builds and measures; source and binary certification still apply:

```powershell
$driver = "artifacts/tooling/space-battle-driver"
dotnet build SpaceBattle.Benchmarks -c Release
Copy-Item SpaceBattle.Benchmarks/bin/Release/net8.0 $driver -Recurse
dotnet "$driver/SpaceBattle.Benchmarks.dll" run space-battle
```

The full campaign uses profile `high`, preset `game`, gameplay `ChurnMultiplier=2`, 108000 measured ticks after 600 separate warmup ticks, seed 7331 and 96 escorts per team. It adds no synthetic churn or diagnostic entity pools. The report preserves full Step totals, nine stage means, 600-tick HUD windows and opening phases.

`validate` runs detailed mechanics checks on the native SKECS core and external initial-state/every-tick replay comparison for the other native cores without a timing campaign. `run` builds, validates, measures and renders. `report` verifies local evidence and regenerates publications; `verify` validates without writing reports. `all` is the headless SpaceBattle suite; it does not run ECS microbenchmarks. Stop other measurements, builds, games, rendering, profilers and browser tests before timing.

```sh
dotnet run --project SpaceBattle.Benchmarks -c Release -- run desktop
dotnet run --project SpaceBattle.Benchmarks -c Release -- run all --include-desktop
dotnet run --project SpaceBattle.Benchmarks -c Release -- report all
dotnet run --project SpaceBattle.Benchmarks -c Release -- verify all
```

Desktop compares matched headless and graphical startup cases over 1200 ticks. `report all` and `verify all` include saved desktop data when present; `--include-desktop` requires it. Explicit `desktop` always requires its dataset.

## Data and paths

| Suite | Default local raw data | Published output |
| --- | --- | --- |
| `space-battle` | `artifacts/benchmarks/current/space-battle` | `docs/space-battle/REPORT.md`, Russian counterpart, `current/*.svg` and summary CSV |
| `desktop` | `artifacts/benchmarks/current/desktop` | `docs/space-battle/DESKTOP_TIMING.md`, Russian counterpart and `desktop-current/*.svg` |
| `composition` | Saved local census supplied with `--data` | Local reports in `artifacts/benchmarks/composition-reports` |

Raw JSON, tick CSV/gzip, manifests and metadata are ignored local artifacts. Only final reports, charts and summary tables are published. Default report verification recomputes complete tick evidence and checks raw/archive hashes. `--skip-ticks` skips CSV recomputation while retaining archive integrity checks; use full verification for publication. A fresh clone can read publications but needs retained raw data to regenerate them.

```sh
dotnet run --project SpaceBattle.Benchmarks -c Release -- run space-battle --output artifacts/benchmarks/repro/space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- report space-battle --data artifacts/benchmarks/repro/space-battle --output artifacts/benchmarks/repro/report
dotnet run --project SpaceBattle.Benchmarks -c Release -- verify space-battle --data artifacts/benchmarks/repro/space-battle
```

`run --output` must stay inside ignored `artifacts/`; custom reports are written in its `reports/` subdirectory. `report --output` may select any workspace directory. Single-suite paths refer directly to the suite. With `all`, supplied roots contain `space-battle` and optional `desktop` subdirectories. `--root` overrides repository discovery. All paths remain within this workspace. .NET/NuGet caches and temporary files stay under `artifacts/tooling`. `--no-build` accepts only an unchanged certified build.

Short `--ticks N --warmup N` overrides require separate `--output` and produce smoke evidence, without qualifying as full benchmark reports.

## Diagnostics and implementation

```sh
dotnet run --project SpaceBattle.Benchmarks -c Release -- hardware --output artifacts/benchmarks/repro/hardware.json
dotnet run --project SpaceBattle.Benchmarks -c Release -- report composition --data artifacts/benchmarks/repro/composition
dotnet run --project SpaceBattle.Benchmarks -c Release -- verify composition --data artifacts/benchmarks/repro/composition
```

Hardware capture records portable CPU/OS metadata and the GC available-memory limit, rather than physical RAM. Its default destination is `artifacts/benchmarks/current/hardware.json`. The neutral machine label defaults to `local-machine`; use distinct `SKECS_BENCHMARK_MACHINE_LABEL` values when combining computers. Hardware/runtime/revision metadata remain recorded.

[Measurement](Measurement/) contains the headless host and simulation validation. [Analysis](Analysis/) owns full campaign, desktop and entity-composition reports. Composition verifies saved `low.json`, `nominal.json` and `high.json`, census/component conservation and available source hashes; it is local diagnostics outside `run all`. Its output stays under ignored artifacts and is not part of the current benchmark publication.

[Native cores](../SpaceBattle.Simulations/) use pinned runtime sources owned by the ECS benchmark target. Vendor checks and optional missing-file recovery are available there:

```sh
dotnet run --project SKECS.Benchmarks -c Release -- verify-vendor
dotnet run --project SKECS.Benchmarks -c Release -- restore-vendor
dotnet test tools/BenchmarkSupport.Tests/BenchmarkSupport.Tests.csproj -c Release
```

Recovery reads pinned remote archives and only writes verified missing local files; it refuses changed local sources. A normal clone includes vendors and needs no recovery. See [third-party notices](../THIRD_PARTY_NOTICES.md) for license conditions.

Each [headless host](Measurement/Headless/) directly calls its concrete core.
[SpaceBattle.Replay](../SpaceBattle.Replay/) hashes the initial state and every
tick, including counters; each measured tick is compared with a SKECS
replay generated in a separate process before warmup. State capture, hashing and comparison are outside Step timing and managed
allocation. Format 4 retains each tick's `stateHash` and the full trace SHA-256;
analysis rejects legacy shared-core campaigns and divergent state traces.
