English | [Русский](REPORT.ru.md)

# Space Battle benchmarks — real-game high

Preset: `game`; profile: `high`; 2026-10-19T02:24:31.9630390+00:00 — 2026-10-19T02:35:04.3039312+00:00.

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
| SKECS | `30817034464d5cd82b7495e8fff9a07c187c26ef` | 0.2.0 |
| LeoECSLite | `5053141d7d663c6b1855c8409ac93dfac807b239` | 2024.5.22 |
| LeoECS Classic | `1213c31c9ef8325d48e74a52b345c4db242d038b` | 2023.6.22 |
| Morpeh | `9f6369384792a01d74df24efd35b08ae313e4f12` | 2024.1.1 |
| StaticEcs 2.2.9 | `23b1cf36d89497e8a126d38459d688e2b0a5dea3` | 2.2.9 |

StaticPack: `7bfb6cf925901b9366dda884258d727a89fff76e`.

## Tick time

| ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick | Total outer s |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SKECS | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.5508 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.9099 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 1.1657 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 3.5657 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 584.1 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 59.490215 |
| LeoECSLite | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.5924 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.9639 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 1.2237 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 3.8661 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 666.8 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 63.975975 |
| LeoECS Classic | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.5692 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.9216 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 1.1699 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 3.4681 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 648.7 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 61.476601 |
| Morpeh | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.5702 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.9269 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 1.1865 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 3.8976 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 644.9 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 61.578307 |
| StaticEcs 2.2.9 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.4968 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.8308 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 1.0710 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 3.6386 | ![Neutral](../benchmarks/heatmap/equal.svg) 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 605.5 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 53.650141 |

![Mean tick time](current/MeanMs.svg)

![P99 tick time](current/P99Ms.svg)

## Opening windows

The first 600 and 2,000 windows are cumulative; the remainder is disjoint.

| ECS | 1–600 ms | 1–2000 ms | 2001–108000 ms | Full ms |
| --- | --- | --- | --- | --- |
| SKECS | 0.6296 | 0.5724 | 0.5504 | 0.5508 |
| LeoECSLite | 0.6715 | 0.6150 | 0.5919 | 0.5924 |
| LeoECS Classic | 0.6711 | 0.5966 | 0.5687 | 0.5692 |
| Morpeh | 0.6353 | 0.5858 | 0.5699 | 0.5702 |
| StaticEcs 2.2.9 | 0.5902 | 0.5462 | 0.4958 | 0.4968 |

![Opening windows](current/InitialWindows.svg)

[Summary CSV](current/summary.csv)

## Full-run stage means

| Stage | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1417 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1468 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.1565 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.1574 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1431 |
| ai | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0895 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1031 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0827 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0889 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0828 |
| thermal-inventory | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0066 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0080 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.0061 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.0060 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0055 |
| steering | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0195 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0223 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0185 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0194 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0173 |
| weapons | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0128 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0173 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0115 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0127 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0101 |
| carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0006 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0005 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0570 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0663 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0591 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0820 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0609 |
| hits | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1848 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1957 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2060 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1753 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1377 |
| lifecycle | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0382 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0321 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0285 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0279 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0388 |
| Battle.LastTickMs | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.5508 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.5923 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.5692 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.5701 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.4967 |
| Outer tick | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.5508 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.5924 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.5692 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.5702 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.4968 |

### SKECS opening stages

| Stage | 1–600 ms | 1–2000 ms | 2001–108000 ms | Full ms |
| --- | --- | --- | --- | --- |
| collect | 0.1567 | 0.1453 | 0.1417 | 0.1417 |
| ai | 0.1216 | 0.0979 | 0.0894 | 0.0895 |
| thermal-inventory | 0.0071 | 0.0064 | 0.0066 | 0.0066 |
| steering | 0.0246 | 0.0201 | 0.0195 | 0.0195 |
| weapons | 0.0165 | 0.0133 | 0.0128 | 0.0128 |
| carriers | 0.0002 | 0.0003 | 0.0006 | 0.0006 |
| physics | 0.0640 | 0.0569 | 0.0570 | 0.0570 |
| hits | 0.1971 | 0.1933 | 0.1846 | 0.1848 |
| lifecycle | 0.0417 | 0.0388 | 0.0382 | 0.0382 |

## HUD windows: first, second and common worst

The common worst window has the largest HUD mean among all five ports; each ECS uses that same tick range. Population means match across ports.

### First: 1–600

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.6295 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.6714 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.6710 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.6353 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5902 |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1567 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1616 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.1818 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.1718 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1591 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1216 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.1349 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1208 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1185 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1199 |
| thermal-inventory | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0071 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0082 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0069 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0062 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0061 |
| steering | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0246 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0283 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0243 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0246 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0229 |
| weapons | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0165 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0211 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.0148 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.0150 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0136 |
| carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0002 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0002 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0001 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0002 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0003 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0640 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0741 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0684 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0856 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0718 |
| hits | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1971 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2081 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2176 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.1849 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1557 |
| lifecycle | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0417 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0348 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0361 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0286 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0408 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 104.26 | 818.02 | 859.90 | 1558.18 | 48.44 |

### Second: 601–1200

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.7159 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.7800 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.7457 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.7508 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.7223 |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1891 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.1986 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.2105 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.2106 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.2042 |
| ai | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.1203 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.1408 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1137 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.1224 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.1255 |
| thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0072 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.0091 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0073 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0076 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.0080 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0228 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0265 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0221 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0240 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0234 |
| weapons | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.0144 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0191 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0132 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg) 0.0145 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0132 |
| carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0002 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0007 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0737 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0884 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0787 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1070 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0904 |
| hits | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.2386 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.2581 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2638 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.2297 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1993 |
| lifecycle | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0494 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0389 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0362 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0347 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0576 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 100.58 | 970.01 | 1513.26 | 1386.75 | 44.51 |

### Worst: 75601–76200

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.6081 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.8055 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.6504 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.6805 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.5741 |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1475 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.1737 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.1688 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.1728 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.1553 |
| ai | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.1111 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1539 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1052 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.1199 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1074 |
| thermal-inventory | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0074 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0124 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg) 0.0075 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0073 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0063 |
| steering | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0245 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0339 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0246 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg) 0.0257 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0230 |
| weapons | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0138 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0252 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0128 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0153 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0112 |
| carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0006 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0002 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0004 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0702 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1019 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0742 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1032 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg) 0.0758 |
| hits | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg) 0.1922 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2472 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.2239 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.1981 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.1510 |
| lifecycle | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg) 0.0409 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0568 | ![Lag up to 5%](../benchmarks/heatmap/best.svg) 0.0331 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg) 0.0377 | ![Lag from 30%](../benchmarks/heatmap/worst.svg) 0.0437 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 98.75 | 777.02 | 1179.82 | 1219.71 | 46.11 |

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
| SKECS | 42.90 | 90.62 | 73.23 | 7.04 | 0.00 | 3820 | 81 | 119 |
| LeoECSLite | 47.78 | 86.41 | 72.56 | n/a | n/a | 3820 | n/a | n/a |
| LeoECS Classic | 47.66 | 91.75 | 68.54 | n/a | n/a | 3820 | n/a | n/a |
| Morpeh | 47.38 | 96.93 | 75.75 | n/a | n/a | 3820 | n/a | n/a |
| StaticEcs 2.2.9 | 44.22 | 91.67 | 66.03 | n/a | n/a | 3820 | n/a | n/a |

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

Base commit: `30817034464d5cd82b7495e8fff9a07c187c26ef`. Source and loaded DLL hashes describe the measured working tree.


Local lossless gzip archives retain the full tick CSV; archive and decompressed SHA256 hashes are recorded in the local manifest. Analysis reads gzip directly and requires only .NET 8.



```sh
dotnet run --project SpaceBattle.Benchmarks -- report space-battle --data artifacts/benchmarks/current/space-battle --output docs/space-battle/current
```

[Desktop timing](DESKTOP_TIMING.md) · [Architecture](ARCHITECTURE.md)

Local measurement inputs: `artifacts/benchmarks/current/space-battle`.
Base commit: `30817034464d5cd82b7495e8fff9a07c187c26ef`.
GC total available memory limit; not physical RAM: 61.49 GiB.
CPU: AMD Ryzen 9 9900X 12-Core Processor.
