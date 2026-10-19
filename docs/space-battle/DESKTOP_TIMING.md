English | [Русский](DESKTOP_TIMING.ru.md)

# Why HUD Mean differs from headless

Cold Release startup with tiering off: 4.002 ms HUD Mean at tick 30 and 0.736 ms at ticks 601–1200. Default runtime: 5.582 ms at tick 10 and 0.707 ms at ticks 601–1200.

### Colour scale

![Lag up to 5%](../benchmarks/heatmap/best.svg) up to 5% · ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 15% · ![Lag from 30%](../benchmarks/heatmap/worst.svg) from 30% · ![Neutral](../benchmarks/heatmap/equal.svg) neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](../benchmarks/COLORS.md).

Five independent Release processes replay the same nominal game battle: seed 7331, 96 escorts and 3 carriers per team, LightSwarm, Tactical, reinforcements enabled. Core, SKECS and adapter hashes match; final checksums, counters, tactics and populations match.

Checksum: `4876EB52A0D4AFC3`.

Headless warms 600 separate ticks in another battle. Desktop starts without Step warmup, including initial JIT and lazy initialization. Default removes the DOTNET_TieredCompilation override; tiering0 disables tiered compilation. Paced desktop uses a 60 frame/s cap, exactly one Step/frame, VSync off. This is controlled replay; ordinary gameplay accumulator FPS is not measured. Processes run sequentially.

HUD Mean averages the last min(600, completed ticks) samples. Early 10/30/60-tick samples include startup; after 600 this is a rolling window. Capture, rendering and frame waiting are outside Battle.LastTickMs; their process load can affect the next Step.

## HUD Mean at tick, ms

| Tick | Headless warm / fast | Headless warm / 60 Hz | Desktop cold / tiering off | Desktop cold / default | Desktop cold / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| 10 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.6025 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8712 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 9.5218 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 5.5824 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 5.5468 |
| 30 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3894 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.7016 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 4.0021 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2.8311 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2.8843 |
| 60 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3366 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.6384 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2.2158 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2.1258 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.9331 |
| 120 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3870 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.7113 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.4359 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2.0304 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.4868 |
| 600 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.6385 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.0712 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.9610 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2.0620 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.9683 |
| 1200 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.6458 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.0769 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.7362 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.7067 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.7578 |

## Stage mean: 1–600, ms

| Stage | Headless warm / fast | Headless warm / 60 Hz | Desktop cold / tiering off | Desktop cold / default | Desktop cold / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1394 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2525 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1712 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.4684 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1971 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1423 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2442 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2209 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.4465 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2132 |
| thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0113 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0217 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0257 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0476 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0203 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0378 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0594 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0555 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1386 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0585 |
| weapons | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0259 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0480 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0587 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0961 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0540 |
| carriers | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0018 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0085 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0049 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0055 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0788 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1441 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1282 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2887 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1633 |
| hits | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1730 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2426 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.2087 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.4377 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.1892 |
| lifecycle | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0297 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0569 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0836 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1334 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0670 |
| Total HUD Step | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.6385 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.0712 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.9610 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2.0620 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.9683 |

## Stage mean: 601–1200, ms

| Stage | Headless warm / fast | Headless warm / 60 Hz | Desktop cold / tiering off | Desktop cold / default | Desktop cold / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1581 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2926 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.1780 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.1877 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2212 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1251 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2201 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.1466 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.1405 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.1431 |
| thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0091 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0170 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0111 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0112 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0117 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0250 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0423 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0308 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0286 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0285 |
| weapons | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0220 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0406 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0290 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0303 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0287 |
| carriers | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0005 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0021 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0013 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0021 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0030 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0657 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1243 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0823 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0813 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0793 |
| hits | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.2050 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2784 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.2153 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1858 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.1985 |
| lifecycle | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0353 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0596 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0417 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0394 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0438 |
| Total HUD Step | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.6458 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.0769 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.7362 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.7067 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.7578 |

This is one run per configuration. Meaningful comparison requires matching settings, runtime, tick ranges, warmup and timer scopes. These data do not establish the cause of a sustained value near 4 ms in a different battle phase. RawCsvPath stores a filename without a local user path.

[Full high-profile game run and all five ECS](REPORT.md).

Raw CSV is retained locally as lossless gzip archives. The local manifest records archive and decompressed SHA256 hashes; analysis reads gzip directly with .NET 8.



```sh
dotnet run --project SpaceBattle.Benchmarks -- report desktop --data artifacts/benchmarks/current/desktop --output docs/space-battle/desktop-current
```

Checksum: `4876EB52A0D4AFC3`. Local measurement inputs: `artifacts/benchmarks/current/desktop`.
