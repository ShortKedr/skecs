English | [Русский](COVER.ru.md)

# Current Gameplay Cover

[Project](../../README.md) | [Report](REPORT.md) | [SpaceBattle overview](README.md)

`cover.png` illustrates current gameplay from the production AOT web frontend
in headless Chromium. Only Canvas is captured; HTML controls and telemetry are
hidden without changing the viewport or camera. It is
not the frozen measured run, a benchmark, or evidence of realtime performance.
## Configuration And Local Capture

The authoritative parameters live in [cover-settings.json](cover-settings.json),
consumed by [cover.py](../../tools/space-battle/cover.py) and
[the browser capture](../../tools/web/capture-cover.mjs). Change that configuration to change
the scene; do not maintain a second set of capture constants in documentation
or workflow commands. The configured preset is seed 7331, 96 escorts per team,
tick 180, 1600 x 1000 pixels and zoom 2. The settings file, not this description,
is authoritative. The profile is nominal; the current web host supports exactly
96 escorts per team. Other `ShipsPerTeam` values fail rather than being ignored.

Local reproduction uses Python, Node.js, Playwright Chromium and the pinned
.NET 8 SDK with its `wasm-tools` workload already available. Build the same
production AOT web frontend into a local directory and use the helper with the settings JSON; this
is a capture-only flow, with no benchmark suite or report analysis. Capture is
selected by simulation tick, not elapsed wall time: a slower renderer must still
reach the configured tick before saving. Fixed settings make the scene
repeatable; byte-identical PNGs across graphics drivers are not promised.

From the repository root (on Linux, run the PowerShell helper through `pwsh`):

```bash
python -m pip install -r tools/space-battle/cover-requirements.txt
(cd tools/web && npm ci && npx playwright install chromium)
pwsh -File tools/build-space-battle-web.ps1 \
  -OutputDirectory artifacts/tooling/tmp/cover-web -BasePath /
python tools/space-battle/cover.py capture \
  --site artifacts/tooling/tmp/cover-web/wwwroot \
  --source-commit "$(git rev-parse HEAD)"
python tools/space-battle/cover.py validate \
  --directory artifacts/tooling/tmp/space-battle-cover --allow-dirty
```

On Windows PowerShell, run the build helper directly, run the npm commands from
`tools/web`, and pass the local HEAD hash as a literal `--source-commit` argument.
Use `PLAYWRIGHT_BROWSERS_PATH` consistently for browser installation and capture
when using a project-local browser cache. Uncommitted local previews are marked `SourceDirty`;
`--allow-dirty` is for local validation only and is never used by Actions.
Capture writes into `artifacts/tooling/tmp/space-battle-cover`, not the documents.

## Cover Provenance

[cover.png.json](cover.png.json) records the source commit, Chromium version,
loaded AOT build hashes, settings, simulation checksum and render counts.
These describe the cover, not a benchmark result.
