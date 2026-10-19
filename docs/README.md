English | [Русский](README.ru.md)

# Documentation map

[Project overview](../README.md)

These guides describe the current implementation and published measurements.

## Using SKECS

- [Package installation](skecs/INSTALL.md): local NuGet package, Unity DLLs, and Godot C# setup.
- [Library guide](skecs/README.md): queries, systems, ownership, and lifetime rules.
- [Runtime optimizations](SKECS_OPTIMIZATION.md): direct creation, accessors, retained storage, and diagnostics.
- [Sample](../SKECS.Sample/Program.cs) and [tests](../SKECS.Tests): executable examples and current contracts.

## Measurements

[Maintained tools](../tools/README.md) covers CI, benchmark execution and analysis, and the programmatic validation harness.

| Workload | Method and results |
| --- | --- |
| Five-framework microbenchmarks | [Results and scope](benchmarks/current/REPORT.md) |
| SKECS BenchmarkDotNet suites and API diagnostics | [Supplemental results](benchmarks/current/SUPPLEMENT.md) |
| Reproduce microbenchmarks | [SKECS.Benchmarks](../SKECS.Benchmarks/README.md) |
| Real-game high workload across five ECS implementations | [Space Battle report](space-battle/REPORT.md) |
| Reproduce SpaceBattle benchmarks | [SpaceBattle.Benchmarks](../SpaceBattle.Benchmarks/README.md) |
| Desktop startup and HUD Mean | [Matched Release/headless measurements](space-battle/DESKTOP_TIMING.md) |
| Colour markers in reports and charts | [Shared comparison scale](benchmarks/COLORS.md) |

Read the measurement date, source revisions, hardware and configuration before comparing results. Raw evidence stays in ignored local artifacts; reports describe the recorded software and workload.

Current internal specifications and development plans are kept in the [restricted SKECS repository](https://github.com/ShortKedr/skecs-private).

## Space Battle

[Game documentation map](space-battle/README.md) covers simulation systems, physics, fleets, assets, desktop/Web hosts and reproduction.

## Source and licenses

[Original project code, documentation, data, and artwork](../LICENSE) are under MIT. [Third-party notices](../THIRD_PARTY_NOTICES.md) identify licenses and pinned sources. The four comparison frameworks are included in `SKECS.Benchmarks/Vendor/Sources`. Consult the inventory before reuse, including LeoECS conditional MIT-Red terms.
