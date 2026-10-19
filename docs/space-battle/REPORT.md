English | [Русский](REPORT.ru.md)

# Space Battle benchmarks — real-game high

Preset: `game`; profile: `high`; 2026-10-19T04:33:57.3983635+00:00 — 2026-10-19T04:44:30.9401893+00:00.

Five independent native ECS cores, 108,000 measured ticks each and 600 separate warmup ticks. Seed 7331, 96 escorts and 3 carriers per team; gameplay churn multiplier 2; endless reinforcements enabled.

### Colour scale

![Lag up to 5%](../benchmarks/heatmap/best.svg) up to 5% · ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 15% · ![Lag from 30%](../benchmarks/heatmap/worst.svg) from 30% · ![Neutral](../benchmarks/heatmap/equal.svg) neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](../benchmarks/COLORS.md).

## Validation and method

Validation passed: 540,000 raw ticks, archive/decompressed/input hashes, provenance, timings, stages, allocations, GC intervals, counters, census and per-tick state-hash parity against SKECS.

Outer tick includes gameplay, physics and ECS commits; HUD and stage timers cover narrower scopes. Rendering, state capture, replay hashing/comparison and report generation are excluded. Every initial state and tick is compared with a SKECS replay generated in a separate process before the 600-tick warmup; hashes include gameplay component state, pending reinforcements and counters. One run per ECS describes this machine and does not establish a stable ranking or confidence interval. Managed allocation excludes native ECS storage.

## Framework revisions used

| ECS | Source revision | Version |
| --- | --- | --- |
| SKECS | `48347501a0d41b453768c456e582c75f6b37af77` | 0.2.0 |
| LeoECSLite | `5053141d7d663c6b1855c8409ac93dfac807b239` | 2024.5.22 |
| LeoECS Classic | `1213c31c9ef8325d48e74a52b345c4db242d038b` | 2023.6.22 |
| Morpeh | `9f6369384792a01d74df24efd35b08ae313e4f12` | 2024.1.1 |
| StaticEcs 2.2.9 | `23b1cf36d89497e8a126d38459d688e2b0a5dea3` | 2.2.9 |

StaticPack: `7bfb6cf925901b9366dda884258d727a89fff76e`.

## Tick time

| ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick | Total outer s |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SKECS | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.5442 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.8947 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 1.1407 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 3.6522 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 584.1 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 58.770956 |
| LeoECSLite | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.5908 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.9544 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 1.2083 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 5.7411 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 666.8 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 63.808179 |
| LeoECS Classic | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.5715 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.9289 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 1.1744 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 3.5846 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 648.7 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 61.721136 |
| Morpeh | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.5784 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.9501 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 1.2273 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 3.8858 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 644.9 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 62.471811 |
| StaticEcs 2.2.9 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5084 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.8543 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 1.1080 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 3.9540 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 605.5 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 54.904581 |

![Mean tick time](current/MeanMs.svg)

![P99 tick time](current/P99Ms.svg)

## Opening windows

The first 600 and 2,000 windows are cumulative; the remainder is disjoint.

| ECS | 1–600 ms | 1–2000 ms | 2001–108000 ms | Full ms |
| --- | --- | --- | --- | --- |
| SKECS | 0.6207 | 0.5759 | 0.5436 | 0.5442 |
| LeoECSLite | 0.7145 | 0.6241 | 0.5902 | 0.5908 |
| LeoECS Classic | 0.6610 | 0.6004 | 0.5709 | 0.5715 |
| Morpeh | 0.6517 | 0.6083 | 0.5779 | 0.5784 |
| StaticEcs 2.2.9 | 0.5897 | 0.5274 | 0.5080 | 0.5084 |

![Opening windows](current/InitialWindows.svg)

[Summary CSV](current/summary.csv)

## Full-run stage means

| Stage | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1400 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1453 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.1567 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.1586 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1455 |
| ai | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0886 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1032 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0833 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0904 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0847 |
| thermal-inventory | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0065 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0080 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0062 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0062 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0058 |
| steering | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0193 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0225 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0187 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0194 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0179 |
| weapons | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0126 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0176 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0117 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0132 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0104 |
| carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0005 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0564 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0660 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0598 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0836 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0628 |
| hits | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1824 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1954 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2050 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1767 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1408 |
| lifecycle | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0378 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0322 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0298 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0297 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0399 |
| Battle.LastTickMs | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.5441 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.5907 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.5714 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.5784 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5083 |
| Outer tick | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.5442 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.5908 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.5715 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.5784 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5084 |

### SKECS opening stages

| Stage | 1–600 ms | 1–2000 ms | 2001–108000 ms | Full ms |
| --- | --- | --- | --- | --- |
| collect | 0.1541 | 0.1447 | 0.1399 | 0.1400 |
| ai | 0.1208 | 0.0989 | 0.0884 | 0.0886 |
| thermal-inventory | 0.0070 | 0.0064 | 0.0065 | 0.0065 |
| steering | 0.0242 | 0.0205 | 0.0192 | 0.0193 |
| weapons | 0.0164 | 0.0136 | 0.0126 | 0.0126 |
| carriers | 0.0001 | 0.0003 | 0.0005 | 0.0005 |
| physics | 0.0629 | 0.0585 | 0.0564 | 0.0564 |
| hits | 0.1943 | 0.1940 | 0.1822 | 0.1824 |
| lifecycle | 0.0407 | 0.0389 | 0.0377 | 0.0378 |

## HUD windows: first, second and common worst

The common worst window has the largest HUD mean among all five ports; each ECS uses that same tick range. Population means match across ports.

### First: 1–600

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.6206 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.7144 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.6609 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.6517 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5897 |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1541 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.1657 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.1787 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.1766 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1588 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1208 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1430 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1178 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1190 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1190 |
| thermal-inventory | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0070 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0089 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0076 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0064 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0057 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0242 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0301 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0241 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0250 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0233 |
| weapons | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0164 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0226 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0160 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.0149 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0134 |
| carriers | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0001 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0002 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0002 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0002 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0003 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0629 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0777 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0707 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0888 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0701 |
| hits | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1943 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2270 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2106 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.1912 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1588 |
| lifecycle | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0407 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0391 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0351 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0295 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0403 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 104.26 | 818.02 | 859.90 | 1558.18 | 48.44 |

### Second: 601–1200

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.7225 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.7800 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.7668 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.7575 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.6641 |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1893 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1962 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.2140 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.2120 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1915 |
| ai | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.1212 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1438 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1158 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.1228 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1133 |
| thermal-inventory | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0072 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0093 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.0075 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0076 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0068 |
| steering | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0237 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0269 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.0232 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0235 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0210 |
| weapons | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0146 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0197 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0143 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0151 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0116 |
| carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0004 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0760 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0868 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0818 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1077 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0817 |
| hits | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.2404 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2579 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2675 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.2320 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1863 |
| lifecycle | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0499 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0390 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0424 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0363 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0514 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 100.58 | 970.01 | 1513.26 | 1386.75 | 44.51 |

### Worst: 80401–81000

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.6399 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.6773 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.6743 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8305 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5797 |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1763 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1782 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.1929 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.2121 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1767 |
| ai | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.1009 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1166 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0979 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1290 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0959 |
| thermal-inventory | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0066 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0082 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0066 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0092 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0059 |
| steering | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0190 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0229 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0194 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0264 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0179 |
| weapons | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0150 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0185 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0128 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0213 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0107 |
| carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0012 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0005 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0627 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0722 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0662 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1258 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0674 |
| hits | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2138 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2241 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2395 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2487 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1592 |
| lifecycle | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0451 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0360 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0386 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0569 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0454 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 85.66 | 716.35 | 1336.79 | 1462.78 | 46.07 |

## Gameplay parity, census and tactical counters

Checksum: `80F8AC31F06DDEBA`.

| Counter | Value |
| --- | --- |
| ShipsCreated | 17469 |
| ShipsDestroyed | 17380 |
| Shots | 945589 |
| Hits | 728514 |
| PlasmaShieldHits | 54703 |
| ShieldBreaks | 29274 |
| Overheats | 6921 |
| Dockings | 2104 |
| Repairs | 411441 |
| Builds | 4466 |
| ItemUses | 30884 |
| DebrisHits | 637983 |
| Explosions | 17380 |
| StructuralAdds | 21128346 |
| StructuralRemoves | 21115896 |
| AimDecisions | 561298 |
| ImpreciseAimDecisions | 140481 |
| FriendlyFireHits | 282792 |
| SelfHits | 0 |
| HeavyShots | 1623 |
| TurretShots | 109331 |
| ReinforcementWaves | 198 |
| ReinforcementShips | 12805 |
| ReinforcementPlacementChecks | 22573 |
| TargetSwitches | 174153 |
| TargetRetentions | 271162 |
| GroupOrders | 38483 |
| CoverOrders | 6864 |
| GroupChanges | 44950 |
| FormationReviews | 20005 |
| CompatibilityChecks | 1732000 |
| ScoreEvaluations | 9127135 |
| SpatialWork | 24715259 |
| BudgetFallbacks | 71 |
| IntentProjectiles | 67984535 |

## Memory and store census

Peak samples across 30 minute checkpoints; store entities include gameplay entities only. Process memory includes harness buffers.

| ECS | Managed heap MiB | Working set MiB | Private MiB | Native MiB | Accessor MiB | Entities final | Archetypes final | Chunks final |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SKECS | 42.92 | 91.98 | 70.48 | 7.04 | 0.00 | 3820 | 81 | 119 |
| LeoECSLite | 47.78 | 85.46 | 65.01 | n/a | n/a | 3820 | n/a | n/a |
| LeoECS Classic | 47.65 | 89.81 | 68.75 | n/a | n/a | 3820 | n/a | n/a |
| Morpeh | 47.38 | 98.11 | 76.66 | n/a | n/a | 3820 | n/a | n/a |
| StaticEcs 2.2.9 | 44.22 | 90.27 | 65.11 | n/a | n/a | 3820 | n/a | n/a |

### Minute population census

| Tick | Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- | --- |
| 3600 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 49 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 547 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1426 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 747 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 44 |
| 7200 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 91 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 453 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1425 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 2027 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 40 |
| 10800 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 66 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 755 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1345 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1164 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 44 |
| 14400 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 91 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 827 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1369 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 726 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 50 |
| 18000 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 100 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 609 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1253 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 578 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 46 |
| 21600 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 103 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 532 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1271 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1415 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 44 |
| 25200 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 84 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 354 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1188 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 393 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 52 |
| 28800 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 93 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 698 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1233 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1641 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 56 |
| 32400 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 65 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 478 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1440 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 880 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 48 |
| 36000 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 82 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 770 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1325 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1301 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 58 |
| 39600 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 61 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 314 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1243 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 496 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 52 |
| 43200 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 73 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 490 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1177 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 774 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 40 |
| 46800 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 113 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 418 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 858 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 790 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 70 |
| 50400 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 103 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 777 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1427 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1392 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 50 |
| 54000 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 110 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1429 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1211 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 3808 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 50 |
| 57600 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 98 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 254 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1138 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 471 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 54 |
| 61200 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 97 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1052 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1461 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1949 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 50 |
| 64800 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 104 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 798 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1265 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1420 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 48 |
| 68400 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 62 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 378 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1364 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 303 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 40 |
| 72000 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 100 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 617 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1567 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 916 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 40 |
| 75600 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 96 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 456 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1096 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1090 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 44 |
| 79200 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 98 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 559 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1345 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 907 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 50 |
| 82800 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 64 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 734 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1288 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1062 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 46 |
| 86400 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 66 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 450 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 997 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 496 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 38 |
| 90000 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 47 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 438 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1408 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 489 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 52 |
| 93600 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 83 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 953 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1417 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1936 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 36 |
| 97200 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 98 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 637 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1084 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1310 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 46 |
| 100800 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 72 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 432 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1363 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1932 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 48 |
| 104400 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 72 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 887 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1525 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 618 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 36 |
| 108000 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 89 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 918 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1340 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1425 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 48 |

## Data provenance and reproduction

Runtime: .NET 8.0.32; OS: Microsoft Windows 10.0.26200; tiered compilation `0`.

Base commit: `48347501a0d41b453768c456e582c75f6b37af77`. Source and loaded DLL hashes describe the measured working tree.


Local lossless gzip archives retain the full tick CSV; archive and decompressed SHA256 hashes are recorded in the local manifest. Analysis reads gzip directly and requires only .NET 8.



```sh
dotnet run --project SpaceBattle.Benchmarks -- report space-battle --data artifacts/benchmarks/current/space-battle --output docs/space-battle/current
```

[Desktop timing](DESKTOP_TIMING.md) · [Architecture](ARCHITECTURE.md)

Local measurement inputs: `artifacts/benchmarks/current/space-battle`.
Base commit: `48347501a0d41b453768c456e582c75f6b37af77`.
GC total available memory limit; not physical RAM: 61.49 GiB.
CPU: AMD Ryzen 9 9900X 12-Core Processor.
