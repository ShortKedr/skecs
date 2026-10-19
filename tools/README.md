English | [Русский](README.ru.md)

# Maintained tools

The maintained tool set serves CI, current benchmark execution and analysis, or the documented programmatic validation harness. Tests and imported helpers belong to the same set as their entry point. Workflow path filters alone do not make a tool active.

## Entry points

| Purpose | Entry point | Caller |
| --- | --- | --- |
| Benchmark generation, measurements and reports | [BenchmarkTools](BenchmarkTools/README.md), [tests](BenchmarkTools.Tests) | [Library CI](../.github/workflows/skecs-ci.yml), benchmark CLI |
| Portable measurement metadata | [BenchmarkMachineInfo.cs](BenchmarkMachineInfo.cs), [BenchmarkMachineLabel.cs](BenchmarkMachineLabel.cs) | Benchmark and game projects |
| Bilingual documentation checks | [check_docs.py](check_docs.py), [tests](test_check_docs.py) | Library and Pages CI |
| Package and release verification | [release](release), [consumer fixtures](release-consumers/README.md) | Library/release CI and the release validation harness |
| Web build and packaging | [build-space-battle-web.ps1](build-space-battle-web.ps1), [web dependencies](web/package.json) | [Pages CI](../.github/workflows/space-battle-pages.yml), cover and scene builds |
| Browser smoke and iOS diagnostics | [check-web.mjs](web/check-web.mjs), [run-ios-smoke.mjs](web/run-ios-smoke.mjs) | Pages CI |
| Cover capture and validation | [cover.py](space-battle/cover.py) | [Cover CI](../.github/workflows/space-battle-cover.yml) |
| Scene search and capture | [Scene tools](space-battle/scenes/README.md) | Programmatic capture harness; unit tests run in Pages CI |
| Browser profiling | [profile-web.mjs](web/profile-web.mjs) | [Web development guide](../SpaceBattle.Web/DEVELOPMENT.md#metrics-and-reproduction) |
| Mono compiler analysis | [Mono tools](web/MONO_COMPILER_STUDY.md) | Pages CI tests and browser analysis |

Web build dependencies include `prepare-web-atlases.mjs`, `finalize-web-site.mjs`, scalar-WASM checks, runtime tests and their tests. Cover capture also uses `web/capture-cover.mjs`. Scene tools require the `space-battle/prepare-scenes.ps1`, `find-scenes.ps1` and `capture-scene.ps1` wrappers, `space-battle/scenes/`, and the pinned Playwright/pngjs dependencies in `web/package*.json`.

Mono CI covers the compiler-study, CPU-profile, build-audit and summary modules and tests, plus the build/cache PowerShell scripts. `mono-compiler-study.mjs` also reads `profile-revisions.mjs` and `profile-render.mjs` for its source identity. These files remain dependencies even when a workflow invokes only their tests.

## Programmatic validation harness

There is no separate repository-owned AI harness configuration. The supported harness consists of the documented browser/scene/asset checks below and their shared helpers. These checks validate current application behavior; they are not ECS performance measurements.

| Contract | Checks |
| --- | --- |
| Armament and heavy hits | `web/check-armament.mjs`, `web/check-heavy-cadence.mjs`, `web/check-heavy-impact.mjs` |
| Deployment and desktop rendering | `web/check-deployment.mjs`, `web/check-desktop-scene.mjs` |
| Effects and reinforcements | `web/check-effects.mjs`, `web/check-effect-contrast.mjs`, `web/check-warp-reinforcements.mjs`, `web/effect-contrast-metrics.mjs` |
| Source atlas pixels | `space-battle/test_effect_atlas.py`, `space-battle/test_gun_atlas.py`, `space-battle/test_warp_atlas.py` |
| Frontend module contracts | `web/test-collider-debug.mjs`, `web/test-observer-camera.mjs`, `web/test-effect-contrast.mjs` |
| Live browser asset/physics checks | `web/physics-study.mjs --verify`, `web/physics-study.mjs --validate-assets-live` |

Their contracts are described in the [asset guide](../SpaceBattle.Game/Assets/PROVENANCE.md), [armament guide](../docs/space-battle/ARMAMENT.md), [deployment guide](../docs/space-battle/DEPLOYMENT.md), [reinforcement guide](../docs/space-battle/REINFORCEMENTS.md), and [desktop development guide](../SpaceBattle.Game/DEVELOPMENT.md). Frontend module tests import production JavaScript directly. Python atlas checks need Pillow; browser checks use the pinned `web` dependencies. Browser checks that compare builds need matching local builds and workload settings.

## Deprecated

The following tools are outside the maintained entry points above. They are not called by current CI, the current benchmark campaign, its report generators, or the documented validation harness. Do not use them to generate current benchmark publications. Retaining source for a distinct experiment does not make it part of the current benchmark suite.

| Tools | Status and removal boundary |
| --- | --- |
| Executables, scripts and tests in [performance](performance), [TacticsStudy](TacticsStudy), [HardwareStudy](HardwareStudy), [physics](physics) | Deprecated standalone research harnesses. Can be removed from the maintained source set. Kernel experiments and hardware/store probes provide distinct diagnostics; the current benchmark CLI does not reproduce those experiments. |
| [space-battle/composition](space-battle/composition) | Deprecated collector with an obsolete Aether assembly/schema dependency. Current saved-data analysis uses `BenchmarkTools report composition`. |
| `space-battle/summarize-spatial.mjs`, `space-battle/test_summarize_spatial.mjs`, `web/summarize-performance.mjs` | Deprecated analysis specific to the standalone research harnesses. |
| `benchmark_colors.py`, `colorize_research_reports.py`, their tests, `space-battle/heatmap_policy.json` | Deprecated research formatting. Current reports use `BenchmarkTools/ReportColors.cs`. |
| `web/check-colliders.mjs`, `web/check-observer.mjs`, `web/check-tactics.mjs`, `web/check-team-tint.mjs` | Deprecated unregistered browser experiments; no current caller or documented harness entry point. |
| [verify-source-distribution.ps1](verify-source-distribution.ps1) | Deprecated standalone copy/build script. Current CI validates a fresh checkout and native benchmark contracts. |
| [check_vendor.py](check_vendor.py), [tests](test_check_vendor.py) | Deprecated duplicate entry point, but retain until its audited metadata and exact Git publication-inventory checks have native coverage. `verify-vendor` currently verifies manifest bytes/licenses and rejects extra runtime C# files; it does not replace those extra checks. |

Published result documents are separate from executable support status. No deprecated tool is required to build the current application or run the current benchmark CLI. The current CLI writes intermediate data only under ignored `artifacts/`; published output consists of reports, charts and summary tables.
