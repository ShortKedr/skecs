English | [Русский](REPORT.ru.md)

# Space Battle benchmarks — real-game high

Preset: `game`; profile: `high`; 2026-11-24T08:27:13.8070421+00:00 — 2026-11-24T08:38:43.3254183+00:00.

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
| SKECS | `a18443101ca1a27191745b979c32518eab4d058d` | 0.2.0 |
| LeoECSLite | `5053141d7d663c6b1855c8409ac93dfac807b239` | 2024.5.22 |
| LeoECS Classic | `1213c31c9ef8325d48e74a52b345c4db242d038b` | 2023.6.22 |
| Morpeh | `9f6369384792a01d74df24efd35b08ae313e4f12` | 2024.1.1 |
| StaticEcs 2.2.9 | `23b1cf36d89497e8a126d38459d688e2b0a5dea3` | 2.2.9 |

StaticPack: `7bfb6cf925901b9366dda884258d727a89fff76e`.

## Tick time

| ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick | Total outer s |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SKECS | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.3875 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.7655 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 1.0737 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 9.0559 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 616.6 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 41.848539 |
| LeoECSLite | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.6381 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.1754 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.5861 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 5.2985 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 699.1 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 68.915440 |
| LeoECS Classic | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.5366 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.0276 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.4231 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 8.8131 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 681.3 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 57.952254 |
| Morpeh | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.6569 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.2288 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.8793 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 34.9796 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 8 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 677.5 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 70.944696 |
| StaticEcs 2.2.9 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3058 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.6618 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.9256 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 6.0490 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 606.0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 33.028393 |

![Mean tick time](current/MeanMs.svg)

![P99 tick time](current/P99Ms.svg)

## Opening windows

The first 600 and 2,000 windows are cumulative; the remainder is disjoint.

| ECS | 1–600 ms | 1–2000 ms | 2001–108000 ms | Full ms |
| --- | --- | --- | --- | --- |
| SKECS | 0.3915 | 0.3657 | 0.3879 | 0.3875 |
| LeoECSLite | 0.7793 | 0.6548 | 0.6378 | 0.6381 |
| LeoECS Classic | 0.7391 | 0.6046 | 0.5353 | 0.5366 |
| Morpeh | 0.6631 | 0.6091 | 0.6578 | 0.6569 |
| StaticEcs 2.2.9 | 0.3033 | 0.2549 | 0.3068 | 0.3058 |

![Opening windows](current/InitialWindows.svg)

[Summary CSV](current/summary.csv)

## Full-run stage means

| Stage | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| collect | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0174 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0253 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0341 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0407 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0048 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1178 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1804 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1401 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1711 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1130 |
| thermal-inventory | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0090 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0152 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0104 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0124 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0044 |
| steering | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0274 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0392 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0311 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0366 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0233 |
| weapons | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0197 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0323 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0217 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0265 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0108 |
| carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0011 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0012 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0007 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0016 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0008 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0426 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0788 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0618 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1188 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0498 |
| hits | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1238 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1971 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1765 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1837 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0840 |
| lifecycle | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0286 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0683 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0601 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0653 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0147 |
| Battle.LastTickMs | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.3874 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.6379 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.5364 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.6567 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3057 |
| Outer tick | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.3875 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.6381 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.5366 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.6569 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3058 |

### SKECS opening stages

| Stage | 1–600 ms | 1–2000 ms | 2001–108000 ms | Full ms |
| --- | --- | --- | --- | --- |
| collect | 0.0173 | 0.0162 | 0.0175 | 0.0174 |
| ai | 0.1399 | 0.1185 | 0.1177 | 0.1178 |
| thermal-inventory | 0.0074 | 0.0075 | 0.0091 | 0.0090 |
| steering | 0.0295 | 0.0260 | 0.0274 | 0.0274 |
| weapons | 0.0196 | 0.0178 | 0.0197 | 0.0197 |
| carriers | 0.0002 | 0.0005 | 0.0011 | 0.0011 |
| physics | 0.0385 | 0.0378 | 0.0427 | 0.0426 |
| hits | 0.1127 | 0.1155 | 0.1240 | 0.1238 |
| lifecycle | 0.0263 | 0.0257 | 0.0287 | 0.0286 |

## HUD windows: first, second and common worst

The common worst window has the largest HUD mean among all five ports; each ECS uses that same tick range. Population means match across ports.

### First: 1–600

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.3914 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.7791 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.7389 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.6629 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3032 |
| collect | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0173 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0313 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0396 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0409 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0043 |
| ai | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.1399 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2457 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2225 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1995 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1303 |
| thermal-inventory | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0074 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0152 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0137 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0117 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0038 |
| steering | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0295 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0468 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0440 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0386 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0227 |
| weapons | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0196 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0397 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0323 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0271 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0108 |
| carriers | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0002 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0007 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0008 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0004 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0385 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0903 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0808 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1101 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0413 |
| hits | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1127 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2232 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2198 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1695 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0767 |
| lifecycle | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0263 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0863 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0855 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0645 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0128 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 104.26 | 818.02 | 859.90 | 1558.18 | 48.44 |

### Second: 601–1200

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.5015 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8275 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.7161 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.7222 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.3217 |
| collect | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0212 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0344 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0439 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0473 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0048 |
| ai | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1615 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2468 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1954 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2007 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1213 |
| thermal-inventory | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0100 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0187 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0126 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0124 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0039 |
| steering | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0340 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0465 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0375 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0375 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0221 |
| weapons | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0224 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0365 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0256 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0257 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0107 |
| carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0008 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0011 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0008 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0014 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0006 |
| physics | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0556 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1050 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0847 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1272 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0518 |
| hits | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1582 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2505 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2314 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1974 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0885 |
| lifecycle | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0377 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0881 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0842 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0725 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0180 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 100.58 | 970.01 | 1513.26 | 1386.75 | 44.51 |

### Worst: 25201–25800

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.4581 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8318 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.7115 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 1.2909 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.4400 |
| collect | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0186 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0294 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0412 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0600 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0068 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1664 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2760 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2211 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.3713 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.1853 |
| thermal-inventory | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0102 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0207 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0139 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0240 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0061 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0356 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0600 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0474 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0725 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0362 |
| weapons | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0207 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0397 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0268 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0494 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0134 |
| carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0010 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0011 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0007 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0034 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0010 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0490 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0961 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0794 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2276 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0659 |
| hits | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1267 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2296 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2078 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.3203 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1047 |
| lifecycle | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0299 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0791 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0732 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1624 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0206 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 115.89 | 756.05 | 1239.28 | 1417.06 | 53.13 |

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
| SKECS | 46.95 | 98.19 | 76.41 | 7.04 | 0.00 | 3820 | 81 | 119 |
| LeoECSLite | 51.82 | 94.32 | 78.78 | n/a | n/a | 3820 | n/a | n/a |
| LeoECS Classic | 51.74 | 98.70 | 82.70 | n/a | n/a | 3820 | n/a | n/a |
| Morpeh | 51.44 | 105.07 | 90.97 | n/a | n/a | 3820 | n/a | n/a |
| StaticEcs 2.2.9 | 45.23 | 92.77 | 66.05 | n/a | n/a | 3820 | n/a | n/a |

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

Runtime: .NET 8.0.34; OS: Microsoft Windows 10.0.26200; tiered compilation `0`.

Base commit: `a18443101ca1a27191745b979c32518eab4d058d`. Source and loaded DLL hashes describe the measured working tree.


Local lossless gzip archives retain the full tick CSV; archive and decompressed SHA256 hashes are recorded in the local manifest. Analysis reads gzip directly and requires only .NET 8.



```sh
dotnet run --project SpaceBattle.Benchmarks -- report space-battle --data artifacts/benchmarks/current/space-battle --output docs/space-battle/current
```

[Desktop timing](DESKTOP_TIMING.md) · [Architecture](ARCHITECTURE.md)

Local measurement inputs: `artifacts/benchmarks/current/space-battle`.
Base commit: `a18443101ca1a27191745b979c32518eab4d058d`.
GC total available memory limit; not physical RAM: 61.49 GiB.
CPU: AMD Ryzen 9 9900X 12-Core Processor.
