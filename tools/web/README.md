English | [Русский](README.ru.md)

# Web validation and Mono tooling

[Tool catalog](../README.md) | [Web development guide](../../SpaceBattle.Web/DEVELOPMENT.md)

The maintained tools build and validate the Web host, capture browser evidence, and inspect Mono compiler output. CI tests the tooling contracts; performance measurements require separate local runs. See the tool catalog for the browser and asset validation entry points.

## Mono compiler comparison

`build-mono-study.ps1` freezes current source into a new ignored study directory and builds isolated variants. It requires a separately installed project-local Windows SDK and its matching wasm-tools packs. The scripts target SDK 8.0.425 and runtime packs 8.0.31; `prepare-mono-lto-cache.ps1` additionally expects Emscripten 3.1.34. The default SDK directory is `artifacts/tooling/tmp/dotnet-aot`.

| Variant | Native C compilation | AOT bitcode and final link |
| --- | --- | --- |
| A | -Oz | -O2 |
| B | -Oz | -O3 |
| C | -O3 | -O3 |
| D | -O3, -flto | -O3, -flto |

These are experimental build variants. Production publish settings are defined by the Web project and its normal build script.

```powershell
./tools/web/build-mono-study.ps1 -Study artifacts/tooling/tmp/my-mono-study -Variant Freeze
./tools/web/build-mono-study.ps1 -Study artifacts/tooling/tmp/my-mono-study -Variant A
./tools/web/build-mono-study.ps1 -Study artifacts/tooling/tmp/my-mono-study -Variant B
./tools/web/build-mono-study.ps1 -Study artifacts/tooling/tmp/my-mono-study -Variant C
./tools/web/prepare-mono-lto-cache.ps1 -Study artifacts/tooling/tmp/my-mono-study
./tools/web/build-mono-study.ps1 -Study artifacts/tooling/tmp/my-mono-study -Variant D -PrivateLtoCache
node tools/web/mono-compiler-study.mjs --help
node tools/web/profile-mono-cpu.mjs --help
```

Build/cache scripts refuse reused output directories. Set `PLAYWRIGHT_BROWSERS_PATH` to an existing browser cache. Complete builds before timing, and stop competing measurement workloads.

`mono-compiler-study.mjs` accepts local site paths, labels and corresponding frozen source roots. Each replay uses a fresh browser process, context and page. A separate warmup battle is reset before measured ticks. The report aggregates independent run means and per-run percentiles. Frontend timing covers client update, export, DOM and Canvas submission; it does not measure GPU time or display pacing. Raw source manifests, executed command audits and browser results stay under ignored `artifacts/tooling/tmp`.

`audit-mono-build.py` checks actual compiler/link commands, response files and object formats. `summarize-mono-study.py` checks saved runs and equivalence. `profile-mono-cpu.mjs` uses symbol maps to classify CPU-profile samples; its shares are sample attribution, not exact stage durations. The profiler must use a separately prepared symbol-enabled build.

## Checks

```powershell
node --test tools/web/test-mono-compiler-study.mjs tools/web/test-profile-mono-cpu.mjs
python tools/web/test-audit-mono-build.py
python tools/web/test-summarize-mono-study.py
./tools/web/test-build-mono-study.ps1
```

A passing tooling test does not establish a performance improvement. Publish current measurements only after verifying their source identity, workload equivalence and complete raw evidence.
