English | [Русский](README.ru.md)

**[Watch four promo clips](../promo/space-battle/README.md)**

# SpaceBattle

SpaceBattle is an AI fleet battle and a realistic ECS comparison workload. Desktop and browser clients share the C# simulation; the headless runner can use SKECS, LeoECS Lite, LeoECS Classic, Morpeh or StaticEcs.

## Start Here

| Goal | Guide |
| --- | --- |
| Watch in a browser or run locally | [Web client](../../SpaceBattle.Web/README.md) |
| Run desktop and learn its controls | [Desktop client](../../SpaceBattle.Game/README.md) |
| Read the single high game workload comparison | [Current benchmark report](REPORT.md) |
| Read the previous game preset campaign | [Previous game report](GAME_REPORT.md) |
| Understand the current stages, components and mutation boundaries | [Current architecture](ARCHITECTURE.md) |
| Understand the ECS adapter contract | [Native adapters](../../SpaceBattle.Adapters/README.md) |
| Find and record real combat moments | [Scene search and capture](../../tools/space-battle/scenes/README.md) |
| Explore earlier experiments | [Research index](RESEARCH.md) |

## Current Gameplay

Interactive clients start with 96 escorts and three carriers per team. Ships fight autonomously; you control the camera, speed and viewing tools. Depleted fleets receive warp reinforcements without resetting survivors. Headless offers two presets: `game` uses the interactive fleet, deployment and reinforcements; `benchmark` retains the finite battle for historical comparisons. The primary performance campaign uses `game` with high battle intensity.

| Rule | Current guide |
| --- | --- |
| LightSwarm roster | [Fleet composition](FLEETS.md) |
| Dispersed startup positions | [Deployment](DEPLOYMENT.md) |
| Target selection, small squads and approach lanes | [Layered squad tactics v3](TACTICS_V3.md) |
| Main weapons and mounted turrets | [Armament](ARMAMENT.md) |
| Shield and hull damage multipliers | [Layered damage](DAMAGE.md) |
| Circle contacts and projectile collision queries | [Physics](PHYSICS.md) |
| Endless corner arrivals | [Reinforcements](REINFORCEMENTS.md) |

Tuning values are implementation choices, not proof of balanced matchups. A performance claim needs the rules, configuration, binaries and environment recorded in its report.

## Benchmark Reproduction

Run from the repository root with the .NET 8 runtime and an SDK supporting C# 13 (9.0 or newer). The [portable .NET CLI](../../tools/BenchmarkTools/README.md) builds Release, validates adapters, measures sequentially and creates bilingual Markdown/SVG reports. Python and PowerShell are unnecessary; builds restore the measured projects' NuGet dependencies.

```sh
dotnet run --project tools/BenchmarkTools -c Release -- generate --check
dotnet run --project tools/BenchmarkTools -c Release -- verify-vendor
dotnet run --project tools/BenchmarkTools -c Release -- validate space-battle
dotnet run --project tools/BenchmarkTools -c Release -- run space-battle
dotnet run --project tools/BenchmarkTools -c Release -- report space-battle
dotnet run --project tools/BenchmarkTools -c Release -- verify space-battle
```

The current suite measures five ECS implementations on one real game workload: profile `high`, preset `game`, gameplay `ChurnMultiplier=2`, and 108000 measured ticks after 600 separate warmup ticks. It adds no synthetic churn or diagnostic entity pools. The report records full Step totals, nine stage means, 600-tick HUD windows and short opening phases.

Raw JSON, tick CSV/gzip, metadata and hashes remain local in ignored `artifacts/benchmarks/current/space-battle`. Published Markdown reports and SVG charts live under `docs/space-battle`. `report` and `verify` validate archives and recompute complete tick evidence using .NET. A fresh clone can read publications; regenerating them requires retained local raw inputs.

`run all` runs the headless collection: five-framework comparison, full headless SpaceBattle and supplemental microbenchmarks. Desktop measurement is explicit:

```sh
dotnet run --project tools/BenchmarkTools -c Release -- run desktop
dotnet run --project tools/BenchmarkTools -c Release -- run all --include-desktop
dotnet run --project tools/BenchmarkTools -c Release -- report all
dotnet run --project tools/BenchmarkTools -c Release -- verify all
```

Desktop requires a working graphics device. `report all` and `verify all` include saved desktop data when present; `--include-desktop` requires it. Finish other builds, games, rendering, profilers and browser tests before timing. Keep configurations and revisions consistent.

Use `run space-battle --output artifacts/benchmarks/repro/space-battle` for separate raw output, then `report space-battle --data artifacts/benchmarks/repro/space-battle --output artifacts/benchmarks/repro/report` and `verify space-battle --data artifacts/benchmarks/repro/space-battle`, each with the same `dotnet run --project tools/BenchmarkTools -c Release --` prefix. All paths stay inside this workspace. `--no-build` requires an unchanged certified build. Short `--ticks`/`--warmup` overrides are smoke checks with separate `--output`; they do not replace the full report.

The `Machine` field uses the anonymous `local-machine` label; hardware, runtime and revisions remain recorded for interpretation. Assign distinct neutral `SKECS_BENCHMARK_MACHINE_LABEL` values when combining different computers; the default label does not establish host identity.

Historical results retain their original `SECS` labels and hashes. The [previous game report](GAME_REPORT.md) and [finite battle report](REPORT_2026-10-14_FINITE.md) varied gameplay multipliers to change churn, so their profiles describe different battle trajectories.

The [2026-10-11 report](REPORT_2026-10-11.md) preserves the `predictive-neutral-v1` Aether campaign, its measurements and analysis scope. Its source links navigate to current files; they do not restore the historical implementation. No verified source snapshot or command for an exact historical rerun is identified here, and current suite commands produce different workloads. See the [historical systems catalogue](SYSTEMS.md) for the measured composition. These results do not establish current-source performance. The [cover workflow](COVER.md) produces an illustration, not timing evidence.
