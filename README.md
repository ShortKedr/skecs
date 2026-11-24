English | [Русский](README.ru.md)

**[Space Battle promo: four recorded clips](docs/promo/space-battle/README.md)**

<p align="center">
  <img src="docs/branding/logo/shortkedr-ecs-200.png" width="200" height="200" alt="SKECS — squirrel and cedar cone emblem">
</p>

# SKECS — ShortKedr ECS

A small entity-component-system (ECS) library with .NET Standard 2.0/2.1 and
.NET 8 builds. SKECS groups entities
by their component types and stores unmanaged component data in chunks. Systems update
that data through typed `ref` queries and run sequentially in `PreUpdate`,
`Update`, and `PostUpdate`. The repository includes tests, benchmarks, and an
autonomous Space Battle sample.

## Start here

| Your goal | Where to go |
| --- | --- |
| Use the library | [Release documentation](https://shortkedr.github.io/skecs/) · [Quickstart](#quickstart) · [API and lifetime rules](https://shortkedr.github.io/skecs/0.2.0/guide/using.html) |
| Work on SKECS | [Build from source](#build-from-source) · [Module samples](SKECS.Samples/README.md) · [Tests](SKECS.Tests) |
| Read or reproduce measurements | [ECS benchmarks](SKECS.Benchmarks/README.md) · [SpaceBattle benchmarks](SpaceBattle.Benchmarks/README.md) · [Space Battle report](docs/space-battle/REPORT.md) |
| Run the game | [Desktop](SpaceBattle.Game/README.md) · [Web](SpaceBattle.Web/README.md) |

`SKECS.Benchmarks/` owns SKECS benchmarks, five-framework comparisons and result analysis. `SpaceBattle.Benchmarks/` owns game workload measurements and analysis. Raw data and caches stay in ignored `artifacts/`; final reports and charts are published.

## Quickstart

Install `ShortKedr.SKECS` 0.2.0 from the GitHub release and run the packaged
library sample in a .NET console app. Unity and Godot installation instructions
are in the same release guide. The 0.2.0 notes list the consumer runtimes
verified at release time. [Install 0.2.0](https://shortkedr.github.io/skecs/0.2.0/guide/installation.html) · [Run the sample](https://shortkedr.github.io/skecs/0.2.0/guide/sample.html) · [Verified runtimes](https://shortkedr.github.io/skecs/0.2.0/guide/release-notes.html#verified-runtimes)

## Build from source

Use a .NET SDK that supports .NET 8 and run from the repository root. These
PowerShell settings keep tool and package caches inside the project:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/artifacts/tooling/dotnet-home"
$env:NUGET_PACKAGES = "$PWD/artifacts/tooling/nuget-packages"

dotnet run --project SKECS.Sample/SKECS.Sample.csproj -c Release
dotnet test SKECS.Tests/SKECS.Tests.csproj -c Release
```

## Space Battle

**[Play in your browser](https://shortkedr.github.io/skecs/demo/)** ·
**[Read the benchmark report](docs/space-battle/REPORT.md)**

Autonomous fleets use AI steering, intercept aiming, turrets, shields, carrier
production, docking, and repair. SKECS, LeoECS Lite, LeoECS Classic, Morpeh and StaticEcs each have an independent
simulation core and matching desktop, browser and headless implementations.
Headless measurements validate every tick against a separate SKECS reference
outside the simulation timer; see the [architecture](docs/space-battle/ARCHITECTURE.md).

[![Space Battle gameplay](docs/space-battle/cover.png)](docs/space-battle/README.md)

*See the report for the measured workload, configuration, and source revisions.*

## Status and evidence

The released 0.2.0 core runs on CPU with sequential systems and queries.
Benchmark results describe particular machines, revisions, and workloads;
see each report for its method and limitations.

[License: MIT for original project material](LICENSE) ·
[Documentation map](docs/README.md) ·
[Internal plans (restricted access)](https://github.com/ShortKedr/skecs-private) ·
[Third-party licenses and pinned sources](THIRD_PARTY_NOTICES.md)
