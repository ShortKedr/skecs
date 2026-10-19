English | [Русский](README.ru.md)

# Maintained tools

The maintained tool set serves CI, current benchmark execution and analysis, or the documented programmatic validation harness. Tests and imported helpers belong to the same set as their entry point. Workflow path filters alone do not make a tool active.

## Entry points

| Purpose | Entry point | Caller |
| --- | --- | --- |
| Benchmark generation, measurements and reports | [SKECS.Benchmarks](../SKECS.Benchmarks/README.md), [SpaceBattle.Benchmarks](../SpaceBattle.Benchmarks/README.md), [shared support](BenchmarkSupport/README.md), [tests](BenchmarkSupport.Tests) | [Library CI](../.github/workflows/skecs-ci.yml), target CLIs |
| Portable measurement metadata | [BenchmarkMachineInfo.cs](BenchmarkMachineInfo.cs), [BenchmarkMachineLabel.cs](BenchmarkMachineLabel.cs) | Benchmark and game projects |
| Bilingual documentation checks | [check_docs.py](check_docs.py), [tests](test_check_docs.py) | Library and Pages CI |
| Package and release verification | [release](release), [consumer fixtures](release-consumers/README.md) | Library/release CI and the release validation harness |
| Web build and packaging | [build-space-battle-web.ps1](build-space-battle-web.ps1), [web dependencies](web/package.json) | [Pages CI](../.github/workflows/space-battle-pages.yml), cover and scene builds |
| Browser smoke and iOS diagnostics | [check-web.mjs](web/check-web.mjs), [run-ios-smoke.mjs](web/run-ios-smoke.mjs) | Pages CI |
| Cover capture and validation | [cover.py](space-battle/cover.py) | [Cover CI](../.github/workflows/space-battle-cover.yml) |
| Scene search and capture | [Scene tools](space-battle/scenes/README.md) | Programmatic capture harness; unit tests run in Pages CI |
| Browser profiling | [profile-web.mjs](web/profile-web.mjs) | [Web development guide](../SpaceBattle.Web/DEVELOPMENT.md#metrics-and-reproduction) |
| Mono compiler analysis | [Mono tools](web/README.md) | Pages CI tests and browser analysis |

Web build dependencies include `prepare-web-atlases.mjs`, `finalize-web-site.mjs`, scalar-WASM checks, runtime tests and their tests. Cover capture also uses `web/capture-cover.mjs`. Scene tools require the `space-battle/prepare-scenes.ps1`, `find-scenes.ps1` and `capture-scene.ps1` wrappers, `space-battle/scenes/`, and the pinned Playwright/pngjs dependencies in `web/package*.json`.


Mono CI covers the compiler-study, CPU-profile, build-audit and summary modules and tests, plus the build/cache PowerShell scripts.

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

The current benchmark CLIs write intermediate data only under ignored `artifacts/`; published output consists of reports, charts and summary tables.
