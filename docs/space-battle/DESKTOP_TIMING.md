English | [Русский](DESKTOP_TIMING.ru.md)

# Why HUD Mean differs from headless

Cold Release startup with tiering off: 4.384 ms HUD Mean at tick 30 and 0.618 ms at ticks 601–1200. Default runtime: 4.805 ms at tick 10 and 0.580 ms at ticks 601–1200.

### Colour scale

![Lag up to 5%](../benchmarks/heatmap/best.svg) up to 5% · ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 15% · ![Lag from 30%](../benchmarks/heatmap/worst.svg) from 30% · ![Neutral](../benchmarks/heatmap/equal.svg) neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](../benchmarks/COLORS.md).

Five independent Release processes replay the same nominal game battle: seed 7331, 96 escorts and 3 carriers per team, LightSwarm, Tactical, reinforcements enabled. Simulation and contract assembly hashes match; final checksums, counters, tactics and populations match.

Checksum: `97D6C5AE0577ACA4`.

Headless warms 600 separate ticks in another battle. Desktop starts without Step warmup, including initial JIT and lazy initialization. Default removes the DOTNET_TieredCompilation override; tiering0 disables tiered compilation. Paced desktop uses a 60 frame/s cap, exactly one Step/frame, VSync off. This is controlled replay; ordinary gameplay accumulator FPS is not measured. Processes run sequentially.

HUD Mean averages the last min(600, completed ticks) samples. Early 10/30/60-tick samples include startup; after 600 this is a rolling window. Capture, rendering and frame waiting are outside Battle.LastTickMs; their process load can affect the next Step.

## HUD Mean at tick, ms

| Tick | Headless warm / fast | Headless warm / 60 Hz | Desktop cold / tiering off | Desktop cold / default | Desktop cold / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| 10 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5315 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.6597 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 10.7380 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 4.8045 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 5.0207 |
| 30 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3177 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.4923 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 4.3845 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2.3991 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2.5706 |
| 60 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.2659 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.4582 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2.3733 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.8037 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.7721 |
| 120 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3071 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.5649 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.4961 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.7805 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.5312 |
| 600 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5273 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8794 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8719 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.8040 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8259 |
| 1200 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5239 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.9137 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.6180 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.5796 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.6198 |

## Stage mean: 1–600, ms

| Stage | Headless warm / fast | Headless warm / 60 Hz | Desktop cold / tiering off | Desktop cold / default | Desktop cold / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1100 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1862 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1334 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.3659 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.1242 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1182 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2002 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2061 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.4154 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1972 |
| thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0083 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0152 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0233 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0350 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0170 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0292 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0478 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0467 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1180 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0536 |
| weapons | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0166 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0327 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0564 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0829 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0538 |
| carriers | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0001 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0010 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0074 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0033 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0032 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0728 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1325 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1257 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2848 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1731 |
| hits | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1385 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2089 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1840 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.3948 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.1584 |
| lifecycle | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0335 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0549 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0889 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1040 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0452 |
| Total HUD Step | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5273 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8794 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8719 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.8040 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8259 |

## Stage mean: 601–1200, ms

| Stage | Headless warm / fast | Headless warm / 60 Hz | Desktop cold / tiering off | Desktop cold / default | Desktop cold / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1239 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2279 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.1453 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1280 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.1433 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1009 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1807 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1240 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1223 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1367 |
| thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0063 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0126 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0078 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0074 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0080 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0197 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0362 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0244 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0247 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0251 |
| weapons | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0139 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0295 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0188 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0226 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0252 |
| carriers | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0015 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0006 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0015 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0016 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0587 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1157 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0763 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0883 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0739 |
| hits | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.1606 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2461 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1747 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1423 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1755 |
| lifecycle | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0397 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0634 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0461 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0426 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0305 |
| Total HUD Step | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5239 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.9137 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.6180 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.5796 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.6198 |

This is one run per configuration. Meaningful comparison requires matching settings, runtime, tick ranges, warmup and timer scopes. These data do not establish the cause of a sustained value near 4 ms in a different battle phase. RawCsvPath stores a filename without a local user path.

[Full high-profile game run and all five ECS](REPORT.md).

Raw CSV is retained locally as lossless gzip archives. The local manifest records archive and decompressed SHA256 hashes; analysis reads gzip directly with .NET 8.



```sh
dotnet run --project SpaceBattle.Benchmarks -- report desktop --data artifacts/benchmarks/current/desktop --output docs/space-battle/desktop-current
```

Checksum: `97D6C5AE0577ACA4`. Local measurement inputs: `artifacts/benchmarks/current/desktop`.
