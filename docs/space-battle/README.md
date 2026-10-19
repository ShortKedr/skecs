English | [Русский](README.ru.md)

**[Watch four promo clips](../promo/space-battle/README.md)**

# SpaceBattle

SpaceBattle is an AI fleet battle and a realistic ECS comparison workload. SKECS, LeoECS Lite, LeoECS Classic, Morpeh and StaticEcs each have an independent C# simulation core, desktop frontend, browser bridge/scene and headless runner. Hosts select the framework before running its implementation.

## Start Here

| Goal | Guide |
| --- | --- |
| Watch in a browser or run locally | [Web client](../../SpaceBattle.Web/README.md) |
| Run desktop and learn its controls | [Desktop client](../../SpaceBattle.Game/README.md) |
| Read the single high game workload comparison | [Current benchmark report](REPORT.md) |
| Understand the current stages, components and mutation boundaries | [Current architecture](ARCHITECTURE.md) |
| Find and record real combat moments | [Scene search and capture](../../tools/space-battle/scenes/README.md) |

## Current Gameplay

Interactive clients start with 96 escorts and three carriers per team. Ships fight autonomously; you control the camera, speed and viewing tools. Depleted fleets receive warp reinforcements without resetting survivors. Headless offers two presets: `game` uses the interactive fleet, deployment and reinforcements; `benchmark` uses a finite battle. The primary performance campaign uses `game` with high battle intensity.

| Rule | Current guide |
| --- | --- |
| LightSwarm roster | [Fleet composition](FLEETS.md) |
| Dispersed startup positions | [Deployment](DEPLOYMENT.md) |
| Target selection, small squads and approach lanes | [Layered squad tactics v3](TACTICS.md) |
| Main weapons and mounted turrets | [Armament](ARMAMENT.md) |
| Shield and hull damage multipliers | [Layered damage](DAMAGE.md) |
| Circle contacts and projectile collision queries | [Physics](PHYSICS.md) |
| Endless corner arrivals | [Reinforcements](REINFORCEMENTS.md) |

Tuning values are implementation choices, not proof of balanced matchups. A performance claim needs the rules, configuration, binaries and environment recorded in its report.

## Benchmark Reproduction

Run from the repository root with the .NET 8 runtime and an SDK supporting C# 13 (9.0 or newer). The [portable .NET CLI](../../SpaceBattle.Benchmarks/README.md) builds Release, checks concrete-core replay against SKECS, measures sequentially and creates bilingual Markdown/SVG reports. Python and PowerShell are unnecessary; builds restore the measured projects' NuGet dependencies.

```sh
dotnet run --project SKECS.Benchmarks -c Release -- verify-vendor
dotnet run --project SpaceBattle.Benchmarks -c Release -- validate space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- run space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- report space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- verify space-battle
```

The current suite measures five ECS implementations on one real game workload: profile `high`, preset `game`, gameplay `ChurnMultiplier=2`, and 108000 measured ticks after 600 separate warmup ticks. It adds no synthetic churn or diagnostic entity pools. The report records full Step totals, nine stage means, 600-tick HUD windows and short opening phases. Each runner compares tick zero and every measured tick with a separate SKECS reference replay. External state export, hashing and comparison run after the Step timer and per-tick allocation counter stop. See the [architecture](ARCHITECTURE.md) for the simulation and validation boundaries.

Raw JSON, tick CSV/gzip, metadata and hashes remain local in ignored `artifacts/benchmarks/current/space-battle`. Published Markdown reports and SVG charts live under `docs/space-battle`. `report` and `verify` validate archives and recompute complete tick evidence using .NET. A fresh clone can read publications; regenerating them requires retained local raw inputs.

`run all` runs the full headless SpaceBattle suite. ECS microbenchmarks run separately through [SKECS.Benchmarks](../../SKECS.Benchmarks/README.md). Desktop measurement is explicit:

```sh
dotnet run --project SpaceBattle.Benchmarks -c Release -- run desktop
dotnet run --project SpaceBattle.Benchmarks -c Release -- run all --include-desktop
dotnet run --project SpaceBattle.Benchmarks -c Release -- report all
dotnet run --project SpaceBattle.Benchmarks -c Release -- verify all
```

Desktop requires a working graphics device. `report all` and `verify all` include saved desktop data when present; `--include-desktop` requires it. Finish other builds, games, rendering, profilers and browser tests before timing. Keep configurations and revisions consistent.

Use `run space-battle --output artifacts/benchmarks/repro/space-battle` for separate raw output, then `report space-battle --data artifacts/benchmarks/repro/space-battle --output artifacts/benchmarks/repro/report` and `verify space-battle --data artifacts/benchmarks/repro/space-battle`, each with the same `dotnet run --project SpaceBattle.Benchmarks -c Release --` prefix. All paths stay inside this workspace. `--no-build` requires an unchanged certified build. Short `--ticks`/`--warmup` overrides are smoke checks with separate `--output`; they do not replace the full report.

The `Machine` field uses the anonymous `local-machine` label; hardware, runtime and revisions remain recorded for interpretation. Assign distinct neutral `SKECS_BENCHMARK_MACHINE_LABEL` values when combining different computers; the default label does not establish host identity.

The [cover workflow](COVER.md) produces gameplay illustrations. Timing evidence is recorded separately by the benchmark suite.
