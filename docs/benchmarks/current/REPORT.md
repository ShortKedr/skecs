English | [Русский](REPORT.ru.md)

# ECS comparison — 2026-10-18

[Benchmark overview](../../../SKECS.Benchmarks/README.md) | [Harness and reproduction](../../../SKECS.Benchmarks/README.md)

[Complete SKECS suites and API measurements](SUPPLEMENT.md) include 34 current and 51 legacy BenchmarkDotNet cases, creation/accessor and retained-storage diagnostics.

All five frameworks were measured afresh in one sequential campaign: 40 base cases and 70 stress cases. Cells are mean milliseconds per complete operation; lower is better. Colour compares only the same scenario and entity count, and describes effect size, not statistical significance.

Environment: .NET 8.0.32, X64, AMD Ryzen 9 9900X 12-Core Processor, 24 logical processors. Release; one thread; tiered compilation off. `9063e9d780526dc937d949a6709b33241b47a092`; working tree modified: `True`.

Local artifacts in `artifacts/benchmarks/current/micro`: `campaign.json` pins the measured revision and hashes; `base.json` and `stress.json` retain every observation. Source and assembly hashes describe the measured snapshot.

## Framework revisions used

Third-party rows show exact upstream commits from the hash-verified `SKECS.Benchmarks/Vendor/manifest.json`, rather than guessed release numbers. The SKECS row records the campaign repository revision.

StaticEcs **2.2.9**; StaticPack **1.2.6** (`7bfb6cf925901b9366dda884258d727a89fff76e`). The manifest records SHA-256 for every included upstream source file.

| Framework | Source revision |
| --- | --- |
| SKECS | `9063e9d780526dc937d949a6709b33241b47a092` |
| LeoECS Lite | `5053141d7d663c6b1855c8409ac93dfac807b239` |
| LeoECS Classic | `1213c31c9ef8325d48e74a52b345c4db242d038b` |
| Morpeh | `9f6369384792a01d74df24efd35b08ae313e4f12` |
| StaticEcs | `23b1cf36d89497e8a126d38459d688e2b0a5dea3` |

## Recorded means

| Scenario | N | SKECS | LeoECS Lite | LeoECS Classic | Morpeh | StaticEcs |
| --- | --- | --- | --- | --- | --- | --- |
| Churn | 1,000 | ![Lag from 30%](../heatmap/worst.svg) 0.0899 | ![Lag from 30%](../heatmap/worst.svg) 0.009551 | ![Lag from 30%](../heatmap/worst.svg) 0.0157 | ![Lag from 30%](../heatmap/worst.svg) 0.0199 | ![Lag up to 5%](../heatmap/best.svg) 0.000494 |
| Create | 1,000 | ![Lag from 30%](../heatmap/worst.svg) 0.1159 | ![Lag from 30%](../heatmap/worst.svg) 0.0256 | ![Lag from 30%](../heatmap/worst.svg) 0.0387 | ![Lag from 30%](../heatmap/worst.svg) 0.0298 | ![Lag up to 5%](../heatmap/best.svg) 0.004055 |
| Destroy | 1,000 | ![Lag from 30%](../heatmap/worst.svg) 0.0254 | ![Lag from 30%](../heatmap/worst.svg) 0.0299 | ![Lag from 30%](../heatmap/worst.svg) 0.0337 | ![Lag from 30%](../heatmap/worst.svg) 0.0182 | ![Lag up to 5%](../heatmap/best.svg) 0.002500 |
| Iterate | 1,000 | ![Lag up to 5%](../heatmap/best.svg) 0.000556 | ![Lag from 30%](../heatmap/worst.svg) 0.001268 | ![Lag from 30%](../heatmap/worst.svg) 0.001251 | ![Lag from 30%](../heatmap/worst.svg) 0.003119 | ![Lag between 5% and 30%](../heatmap/bad.svg) 0.000701 |
| Churn | 100,000 | ![Lag from 30%](../heatmap/worst.svg) 8.995 | ![Lag from 30%](../heatmap/worst.svg) 0.9391 | ![Lag from 30%](../heatmap/worst.svg) 1.590 | ![Lag from 30%](../heatmap/worst.svg) 2.051 | ![Lag up to 5%](../heatmap/best.svg) 0.0466 |
| Create | 100,000 | ![Lag from 30%](../heatmap/worst.svg) 9.913 | ![Lag from 30%](../heatmap/worst.svg) 4.850 | ![Lag from 30%](../heatmap/worst.svg) 10.295 | ![Lag from 30%](../heatmap/worst.svg) 7.964 | ![Lag up to 5%](../heatmap/best.svg) 0.2019 |
| Destroy | 100,000 | ![Lag from 30%](../heatmap/worst.svg) 2.432 | ![Lag from 30%](../heatmap/worst.svg) 3.140 | ![Lag from 30%](../heatmap/worst.svg) 3.532 | ![Lag from 30%](../heatmap/worst.svg) 1.719 | ![Lag up to 5%](../heatmap/best.svg) 0.1220 |
| Iterate | 100,000 | ![Lag up to 5%](../heatmap/best.svg) 0.0582 | ![Lag from 30%](../heatmap/worst.svg) 0.1275 | ![Lag from 30%](../heatmap/worst.svg) 0.1302 | ![Lag from 30%](../heatmap/worst.svg) 0.3127 | ![Lag between 5% and 30%](../heatmap/fair.svg) 0.0651 |

| Scenario | N | SKECS | LeoECS Lite | LeoECS Classic | Morpeh | StaticEcs |
| --- | --- | --- | --- | --- | --- | --- |
| DenseIteration | 100,000 | ![Lag between 5% and 30%](../heatmap/strong.svg) 0.0965 | ![Lag from 30%](../heatmap/worst.svg) 0.1377 | ![Lag from 30%](../heatmap/worst.svg) 0.1285 | ![Lag from 30%](../heatmap/worst.svg) 0.5066 | ![Lag up to 5%](../heatmap/best.svg) 0.0913 |
| MixedFrames | 100,000 | ![Lag from 30%](../heatmap/worst.svg) 180.459 | ![Lag between 5% and 30%](../heatmap/bad.svg) 81.917 | ![Lag from 30%](../heatmap/worst.svg) 133.770 | ![Lag from 30%](../heatmap/worst.svg) 138.522 | ![Lag up to 5%](../heatmap/best.svg) 65.421 |
| PayloadChurn | 100,000 | ![Lag from 30%](../heatmap/worst.svg) 38.008 | ![Lag from 30%](../heatmap/worst.svg) 7.610 | ![Lag from 30%](../heatmap/worst.svg) 17.249 | ![Lag from 30%](../heatmap/worst.svg) 14.983 | ![Lag up to 5%](../heatmap/best.svg) 5.425 |
| RandomLookup | 100,000 | ![Lag from 30%](../heatmap/worst.svg) 0.9123 | ![Lag between 5% and 30%](../heatmap/weak.svg) 0.2618 | ![Lag from 30%](../heatmap/worst.svg) 1.001 | ![Lag from 30%](../heatmap/worst.svg) 0.7469 | ![Lag up to 5%](../heatmap/best.svg) 0.2202 |
| Recycle10Percent | 100,000 | ![Lag from 30%](../heatmap/worst.svg) 4.108 | ![Lag from 30%](../heatmap/worst.svg) 2.394 | ![Lag from 30%](../heatmap/worst.svg) 3.061 | ![Lag from 30%](../heatmap/worst.svg) 2.542 | ![Lag up to 5%](../heatmap/best.svg) 1.055 |
| SparseIteration | 100,000 | ![Lag from 30%](../heatmap/worst.svg) 0.002021 | ![Lag from 30%](../heatmap/worst.svg) 0.002132 | ![Lag from 30%](../heatmap/worst.svg) 0.002031 | ![Lag from 30%](../heatmap/worst.svg) 0.008843 | ![Lag up to 5%](../heatmap/best.svg) 0.001055 |
| WideIteration | 100,000 | ![Lag from 30%](../heatmap/worst.svg) 0.2946 | ![Lag from 30%](../heatmap/worst.svg) 0.8159 | ![Lag from 30%](../heatmap/worst.svg) 0.4414 | ![Lag from 30%](../heatmap/worst.svg) 4.862 | ![Lag up to 5%](../heatmap/best.svg) 0.1996 |
| DenseIteration | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg) 2.093 | ![Lag between 5% and 30%](../heatmap/fair.svg) 1.588 | ![Lag up to 5%](../heatmap/best.svg) 1.412 | ![Lag from 30%](../heatmap/worst.svg) 8.967 | ![Lag between 5% and 30%](../heatmap/good.svg) 1.542 |
| MixedFrames | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg) 4866.246 | ![Lag from 30%](../heatmap/worst.svg) 2445.866 | ![Lag from 30%](../heatmap/worst.svg) 4721.919 | ![Lag from 30%](../heatmap/worst.svg) 4734.567 | ![Lag up to 5%](../heatmap/best.svg) 1313.287 |
| PayloadChurn | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg) 663.326 | ![Lag from 30%](../heatmap/worst.svg) 226.729 | ![Lag from 30%](../heatmap/worst.svg) 531.399 | ![Lag from 30%](../heatmap/worst.svg) 336.882 | ![Lag up to 5%](../heatmap/best.svg) 82.633 |
| RandomLookup | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg) 43.921 | ![Lag up to 5%](../heatmap/best.svg) 4.857 | ![Lag from 30%](../heatmap/worst.svg) 68.160 | ![Lag from 30%](../heatmap/worst.svg) 34.294 | ![Lag up to 5%](../heatmap/best.svg) 4.886 |
| Recycle10Percent | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg) 64.304 | ![Lag from 30%](../heatmap/worst.svg) 69.308 | ![Lag from 30%](../heatmap/worst.svg) 90.520 | ![Lag from 30%](../heatmap/worst.svg) 79.243 | ![Lag up to 5%](../heatmap/best.svg) 36.925 |
| SparseIteration | 1,000,000 | ![Lag up to 5%](../heatmap/best.svg) 0.0102 | ![Lag from 30%](../heatmap/worst.svg) 0.0314 | ![Lag from 30%](../heatmap/worst.svg) 0.0328 | ![Lag from 30%](../heatmap/worst.svg) 0.1199 | ![Lag up to 5%](../heatmap/best.svg) 0.0107 |
| WideIteration | 1,000,000 | ![Lag up to 5%](../heatmap/best.svg) 8.096 | ![Lag from 30%](../heatmap/worst.svg) 34.024 | ![Lag from 30%](../heatmap/worst.svg) 17.793 | ![Lag from 30%](../heatmap/worst.svg) 101.590 | ![Lag up to 5%](../heatmap/best.svg) 8.247 |

### Colour scale

![Lag up to 5%](../heatmap/best.svg) up to 5% · ![Lag between 5% and 30%](../heatmap/middle.svg) 15% · ![Lag from 30%](../heatmap/worst.svg) from 30% · ![Neutral](../heatmap/equal.svg) neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](../COLORS.md).

## Charts and interpretation

![Base comparison](comparison.svg)

![Stress at 100,000 entities](stress-100k.svg)

![Stress at 1,000,000 entities](stress-1m.svg)

Error bars show sample standard deviation, not confidence intervals. Base timings retain 20 samples; stress retains 8. Setup, validation, disposal and forced GC are excluded; natural collections and Morpeh commits during operations are included. Create/Destroy use fresh worlds; other workloads reuse warmed storage. MixedFrames is a complete 120-frame batch, not one frame. Allocation fields measure managed thread allocations and exclude native memory.

Base validation passed for all five frameworks at 1,000/100,000 entities before timing. Stress validation passed at 257/4,097 entities: seven scenarios repeated three times, payload round-trip and retired handles. Each measured stress case passed final full-population validation. Exact matrices, input hashes, sample counts, means, standard deviations and allocations were checked before report generation.

These are equivalent logical operations through selected adapters, not identical storage or each framework’s best possible API. Results apply to this machine, runtime, snapshots and workload. They do not establish a universal ECS ranking, Unity/IL2CPP performance, total-memory efficiency or cross-platform determinism. Close means should not be treated as reliable wins without further experiments.
