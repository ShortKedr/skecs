English | [Русский](DESKTOP_TIMING.ru.md)

# Why HUD Mean differs from headless

Cold Release startup with tiering off: 4.381 ms HUD Mean at tick 30 and 0.618 ms at ticks 601–1200. Default runtime: 5.075 ms at tick 10 and 0.610 ms at ticks 601–1200.

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
| 10 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5211 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.7035 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 10.6390 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 5.0745 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 4.9649 |
| 30 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3131 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.5297 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 4.3811 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2.5248 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2.5492 |
| 60 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.2610 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.4946 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2.3680 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.8794 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.9094 |
| 120 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3018 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.5650 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.4915 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.8299 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.6990 |
| 600 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5171 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8850 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8733 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.9017 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8543 |
| 1200 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5255 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8917 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.6181 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.6095 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.6064 |

## Stage mean: 1–600, ms

| Stage | Headless warm / fast | Headless warm / 60 Hz | Desktop cold / tiering off | Desktop cold / default | Desktop cold / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1087 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2011 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1334 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.4309 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.1248 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1160 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1990 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2021 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.4249 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2053 |
| thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0080 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0150 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0237 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0372 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0186 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0285 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0489 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0472 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1249 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0640 |
| weapons | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0165 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0321 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0575 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0859 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0561 |
| carriers | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0001 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0008 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0079 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0030 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0029 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0711 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1335 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1272 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2908 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1789 |
| hits | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1354 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2004 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1840 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.3950 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.1585 |
| lifecycle | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0329 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0541 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0903 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1090 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0451 |
| Total HUD Step | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5171 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8850 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8733 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.9017 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8543 |

## Stage mean: 601–1200, ms

| Stage | Headless warm / fast | Headless warm / 60 Hz | Desktop cold / tiering off | Desktop cold / default | Desktop cold / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1233 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2264 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.1457 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1293 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.1401 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1015 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1789 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.1201 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.1212 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1341 |
| thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0061 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0127 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0080 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0078 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0084 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0196 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0359 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0246 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0255 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0242 |
| weapons | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0139 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0288 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0191 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0245 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0242 |
| carriers | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0012 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0006 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0015 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0014 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0591 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1138 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0768 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1160 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0773 |
| hits | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.1606 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2329 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1769 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1417 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.1678 |
| lifecycle | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0410 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0610 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0463 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0420 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0288 |
| Total HUD Step | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5255 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8917 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.6181 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.6095 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.6064 |

This is one run per configuration. Meaningful comparison requires matching settings, runtime, tick ranges, warmup and timer scopes. These data do not establish the cause of a sustained value near 4 ms in a different battle phase. RawCsvPath stores a filename without a local user path.

[Full high-profile game run and all five ECS](REPORT.md).

Raw CSV is retained locally as lossless gzip archives. The local manifest records archive and decompressed SHA256 hashes; analysis reads gzip directly with .NET 8.



```sh
dotnet run --project SpaceBattle.Benchmarks -- report desktop --data artifacts/benchmarks/current/desktop --output docs/space-battle/desktop-current
```

Checksum: `97D6C5AE0577ACA4`. Local measurement inputs: `artifacts/benchmarks/current/desktop`.
