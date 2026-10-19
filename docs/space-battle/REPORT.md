English | [Русский](REPORT.ru.md)

# Space Battle benchmarks — real-game high

Preset: `game`; profile: `high`; 2026-10-18T15:27:07.5787241+05:00 — 2026-10-18T15:34:23.4895992+05:00.

Five ECS implementations, 108,000 measured ticks each and 600 separate warmup ticks. Seed 7331, 96 escorts and 3 carriers per team; gameplay churn multiplier 2; endless reinforcements enabled.

### Colour scale

![Lag up to 5%](../benchmarks/heatmap/best.svg) up to 5% · ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 15% · ![Lag from 30%](../benchmarks/heatmap/worst.svg) from 30% · ![Neutral](../benchmarks/heatmap/equal.svg) neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](../benchmarks/COLORS.md).

## Validation and method

Validation passed: 540,000 raw ticks, archive/decompressed/input hashes, provenance, timings, stages, allocations, GC intervals, counters, census and per-tick gameplay parity.

Outer tick includes gameplay, physics and ECS commits; HUD and stage timers cover narrower scopes. Rendering, snapshots and report generation are excluded. One run per ECS describes this machine and does not establish a stable ranking or confidence interval. Managed allocation excludes native ECS storage.

## Framework revisions used

| ECS | Source revision | Version |
| --- | --- | --- |
| SKECS | `9063e9d780526dc937d949a6709b33241b47a092` | 0.2.0 |
| LeoECSLite | `5053141d7d663c6b1855c8409ac93dfac807b239` | 2024.5.22 |
| LeoECS Classic | `1213c31c9ef8325d48e74a52b345c4db242d038b` | 2023.6.22 |
| Morpeh | `9f6369384792a01d74df24efd35b08ae313e4f12` | 2024.1.1 |
| StaticEcs 2.2.9 | `23b1cf36d89497e8a126d38459d688e2b0a5dea3` | 2.2.9 |

StaticPack: `7bfb6cf925901b9366dda884258d727a89fff76e`.

## Tick time

| ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick | Total outer s |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SKECS | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.6662 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 1.0452 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 1.3122 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 3.9294 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 601.6 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 71.954668 |
| LeoECSLite | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8767 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 1.3194 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 1.6137 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 4.2005 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 681.0 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 94.678999 |
| LeoECS Classic | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.8412 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 1.3188 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.7073 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 4.4115 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 667.5 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 90.848046 |
| Morpeh | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.7588 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 1.1732 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 1.4542 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 5.1162 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 655.8 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 81.947525 |
| StaticEcs 2.2.9 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.6536 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 1.0177 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 1.2704 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 4.1364 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 621.6 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 70.592594 |

![Mean tick time](current/MeanMs.svg)

![P99 tick time](current/P99Ms.svg)

## Opening windows

The first 600 and 2,000 windows are cumulative; the remainder is disjoint.

| ECS | 1–600 ms | 1–2000 ms | 2001–108000 ms | Full ms |
| --- | --- | --- | --- | --- |
| SKECS | 0.7655 | 0.6998 | 0.6656 | 0.6662 |
| LeoECSLite | 0.9728 | 0.9107 | 0.8760 | 0.8767 |
| LeoECS Classic | 0.9683 | 0.8982 | 0.8401 | 0.8412 |
| Morpeh | 0.8880 | 0.8239 | 0.7575 | 0.7588 |
| StaticEcs 2.2.9 | 0.7587 | 0.6957 | 0.6528 | 0.6536 |

![Opening windows](current/InitialWindows.svg)

[Summary CSV](current/summary.csv)

## Full-run stage means

| Stage | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1798 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.2207 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.1911 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.1847 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1732 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1089 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1317 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1313 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.1175 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1055 |
| thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0093 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0139 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0132 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0107 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0105 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0249 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0292 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0295 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.0271 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0247 |
| weapons | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0202 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0264 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0240 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0225 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0195 |
| carriers | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0008 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0009 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0008 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0008 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0009 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0614 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1323 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1054 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1131 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0757 |
| hits | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.2287 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2771 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2939 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.2456 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.2102 |
| lifecycle | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0322 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0443 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0518 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0365 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0333 |
| Battle.LastTickMs | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.6662 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8766 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.8411 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.7587 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.6536 |
| Outer tick | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.6662 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8767 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.8412 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.7588 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.6536 |

### SKECS opening stages

| Stage | 1–600 ms | 1–2000 ms | 2001–108000 ms | Full ms |
| --- | --- | --- | --- | --- |
| collect | 0.1933 | 0.1851 | 0.1797 | 0.1798 |
| ai | 0.1493 | 0.1225 | 0.1086 | 0.1089 |
| thermal-inventory | 0.0102 | 0.0091 | 0.0093 | 0.0093 |
| steering | 0.0336 | 0.0270 | 0.0248 | 0.0249 |
| weapons | 0.0270 | 0.0214 | 0.0202 | 0.0202 |
| carriers | 0.0003 | 0.0005 | 0.0008 | 0.0008 |
| physics | 0.0712 | 0.0625 | 0.0614 | 0.0614 |
| hits | 0.2443 | 0.2393 | 0.2285 | 0.2287 |
| lifecycle | 0.0363 | 0.0324 | 0.0322 | 0.0322 |

## HUD windows: first, second and common worst

The common worst window has the largest HUD mean among all five ports; each ECS uses that same tick range. Population means match across ports.

### First: 1–600

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.7654 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.9727 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.9682 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.8879 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.7586 |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1933 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.2356 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.2131 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.2114 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1914 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1493 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.1687 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1757 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.1589 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1446 |
| thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0102 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0156 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0156 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0128 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0128 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0336 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0397 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0378 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0369 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0328 |
| weapons | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.0270 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0344 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0316 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0289 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0247 |
| carriers | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0010 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0712 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1431 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1231 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1287 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0823 |
| hits | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.2443 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.2852 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.3086 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.2673 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.2361 |
| lifecycle | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.0363 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0499 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0624 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0427 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0329 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 104.26 | 818.02 | 859.90 | 1558.18 | 48.44 |

### Second: 601–1200

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.8683 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.1630 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.1922 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 1.0274 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.8842 |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.2331 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.2912 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.2627 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.2438 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.2317 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1505 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1853 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1940 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.1710 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1481 |
| thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0102 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0167 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0170 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0141 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0135 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0299 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0351 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0380 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0338 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0305 |
| weapons | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0227 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0298 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0300 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0256 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0232 |
| carriers | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0006 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0007 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0008 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0007 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0008 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0798 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1723 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1538 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1542 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1010 |
| hits | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3002 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.3752 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.4112 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.3331 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.2882 |
| lifecycle | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0411 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0568 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0845 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0511 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0472 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 100.58 | 970.01 | 1513.26 | 1386.75 | 44.51 |

### Worst: 601–1200

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.8683 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.1630 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.1922 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 1.0274 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.8842 |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.2331 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.2912 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.2627 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.2438 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.2317 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1505 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1853 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1940 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.1710 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1481 |
| thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0102 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0167 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0170 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0141 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0135 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0299 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0351 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0380 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0338 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0305 |
| weapons | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0227 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0298 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0300 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0256 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0232 |
| carriers | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0006 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0007 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0008 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0007 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0008 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0798 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1723 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1538 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1542 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1010 |
| hits | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3002 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.3752 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.4112 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.3331 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.2882 |
| lifecycle | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0411 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0568 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0845 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0511 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0472 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 100.58 | 970.01 | 1513.26 | 1386.75 | 44.51 |

## Gameplay parity, census and tactical counters

Checksum: `7EE0B6054181F4AE`.

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
| SKECS | 39.71 | 80.19 | 62.71 | 7.04 | 0.00 | 3820 | 81 | 119 |
| LeoECSLite | 45.61 | 78.98 | 70.51 | n/a | n/a | 3820 | n/a | n/a |
| LeoECS Classic | 45.38 | 77.53 | 70.81 | n/a | n/a | 3820 | n/a | n/a |
| Morpeh | 44.86 | 85.88 | 80.02 | n/a | n/a | 3820 | n/a | n/a |
| StaticEcs 2.2.9 | 42.23 | 83.80 | 58.20 | n/a | n/a | 3820 | n/a | n/a |

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

Base commit: `9063e9d780526dc937d949a6709b33241b47a092`. Source and loaded DLL hashes describe the measured working tree.


Local lossless gzip archives retain the full tick CSV; archive and decompressed SHA256 hashes are recorded in the local manifest. Analysis reads gzip directly and requires only .NET 8.



```sh
dotnet run --project tools/BenchmarkTools -- report space-battle --data artifacts/benchmarks/current/space-battle --output docs/space-battle/current
```

[Desktop timing](DESKTOP_TIMING.md) · [Architecture](ARCHITECTURE.md) · [Historical finite battle](REPORT_2026-10-14_FINITE.md)

Local measurement inputs: `artifacts/benchmarks/current/space-battle`.
Base commit: `9063e9d780526dc937d949a6709b33241b47a092`.
RAM: 61.49 GiB.
CPU: AMD Ryzen 9 9900X 12-Core Processor            .
