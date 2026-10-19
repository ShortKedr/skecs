English | [Русский](SUPPLEMENT.ru.md)

# Supplemental SKECS measurements

These measurements apply to the measured sources and this machine. Timings are milliseconds per complete operation. These are separate workloads; tables do not form an overall ranking of APIs or systems.

Local raw artifacts: `artifacts/benchmarks/current/micro`: `optimized-api.json`, `storage.json`, `bdn-skecs.json`, `bdn-legacy.json`, `bdn-raw/`.

Local `supplement.json` records revision, source/assembly provenance and input hashes; original BDN full-json exports are verified by saved bytes and content. The [five-ECS comparison](REPORT.md) uses a separate shared workload.

## API

Eight samples per case. Creation uses a fresh world per repetition; setup, validation, disposal and forced GC are outside timing. Lookup uses warmed storage and a shared seeded order. Colour compares only methods of the same operation at the same N using raw precision; it does not establish statistical significance.

### Create

| N | EmptyAddAdd | CreateEntityComponents | PreparedFactoryBatch |
| --- | --- | --- | --- |
| 100,000 | ![Lag from 30%](../heatmap/worst.svg) 10.328587 | ![Lag from 30%](../heatmap/worst.svg) 6.111083 | ![Lag up to 5%](../heatmap/best.svg) 4.626843 |
| 1,000,000 | ![Lag from 30%](../heatmap/worst.svg) 96.912250 | ![Lag from 30%](../heatmap/worst.svg) 57.388813 | ![Lag up to 5%](../heatmap/best.svg) 43.075837 |

### Lookup

| N | WorldGet | CachedAccessorGet |
| --- | --- | --- |
| 100,000 | ![Lag from 30%](../heatmap/worst.svg) 0.857416 | ![Lag up to 5%](../heatmap/best.svg) 0.415045 |
| 1,000,000 | ![Lag from 30%](../heatmap/worst.svg) 48.014775 | ![Lag up to 5%](../heatmap/best.svg) 27.900531 |

| Operation | Method | N | SD ms | Managed allocated bytes | Retained native bytes |
| --- | --- | --- | --- | --- | --- |
| Create | CreateEntityComponents | 100,000 | 0.111982 | 7154080.000 | 2402304 |
| Create | CreateEntityComponents | 1,000,000 | 0.540865 | 58934045.000 | 24004608 |
| Create | EmptyAddAdd | 100,000 | 0.074916 | 7159680.000 | 2405377 |
| Create | EmptyAddAdd | 1,000,000 | 0.528346 | 58940486.000 | 24007681 |
| Create | PreparedFactoryBatch | 100,000 | 0.094531 | 7154080.000 | 2402304 |
| Create | PreparedFactoryBatch | 1,000,000 | 0.599935 | 58934184.000 | 24004608 |
| Lookup | CachedAccessorGet | 100,000 | 0.003973 | 0.000 | 5245953 |
| Lookup | CachedAccessorGet | 1,000,000 | 5.007868 | 0.000 | 41946113 |
| Lookup | WorldGet | 100,000 | 0.009043 | 0.000 | 5245953 |
| Lookup | WorldGet | 1,000,000 | 6.933352 | 0.000 | 41946113 |

### Colour scale

![Lag up to 5%](../heatmap/best.svg) up to 5% · ![Lag between 5% and 30%](../heatmap/middle.svg) 15% · ![Lag from 30%](../heatmap/worst.svg) from 30% · ![Neutral](../heatmap/equal.svg) neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](../COLORS.md).

## Storage

Untimed retained native column buffers after adding/removing a 256-byte payload, N=100000. Managed arrays, headers and allocator overhead are excluded; these are not total process memory.

| Pass | Chunks | Native bytes |
| --- | --- | --- |
| 0 | 393 | 2405377 |
| 1 | 784 | 30432257 |
| 2 | 784 | 30432257 |
| 3 | 784 | 30432257 |
| 4 | 784 | 30432257 |
| 5 | 784 | 30432257 |
| 6 | 784 | 30432257 |
| 7 | 784 | 30432257 |
| 8 | 784 | 30432257 |
| 9 | 784 | 30432257 |
| 10 | 784 | 30432257 |
| 11 | 784 | 30432257 |
| 12 | 784 | 30432257 |
| 13 | 784 | 30432257 |
| 14 | 784 | 30432257 |
| 15 | 784 | 30432257 |
| 16 | 784 | 30432257 |
| 17 | 784 | 30432257 |
| 18 | 784 | 30432257 |
| 19 | 784 | 30432257 |
| 20 | 784 | 30432257 |

## BenchmarkDotNet

| Suite | Runtime | Configuration | Toolchain |
| --- | --- | --- | --- |
| SKECS | .NET 8.0.32 (8.0.3226.50405) | Release | InProcess |
| Legacy | .NET 8.0.32 (8.0.3226.50405) | Release | InProcess |

Release, Short, InProcess: explicitly 3 warmup and 8 actual iterations, rather than Short’s default 3 actual iterations. Actual / retained distinguishes collected observations from Statistics after outlier processing. Setup/cleanup are outside operations; natural GC within operations is included. InProcess shares the host process without child-runtime isolation. Allocation diagnostics exclude native columns.

Exact keys cover 34 SKECS and 51 legacy cases. Different operations are not ranked: all cells are neutral. MixedFrames is a 120-frame batch. Padding 10 changes the fraction of matching entities and is not compared with Padding 0 as a speedup of one workload.

Mutable cases invoke one operation per iteration to preserve reset semantics. Many receive BDN MinIterationTime warnings below its recommended 100 ms: eight short observations have limited precision. These values do not replace a longer default job or the shared five-ECS matrix.

### SKECS: 34

| Class | Method | N | Padding | Mean ms | SD ms | Actual / retained |
| --- | --- | --- | --- | --- | --- | --- |
| ComparisonBenchmarks | Churn | 1,000 | — | ![Neutral](../heatmap/equal.svg) 0.083916 | 0.000092 | 8 / 6 |
| ComparisonBenchmarks | Churn | 100,000 | — | ![Neutral](../heatmap/equal.svg) 8.618690 | 0.136386 | 8 / 8 |
| ComparisonBenchmarks | Create | 1,000 | — | ![Neutral](../heatmap/equal.svg) 0.093257 | 0.001405 | 8 / 7 |
| ComparisonBenchmarks | Create | 100,000 | — | ![Neutral](../heatmap/equal.svg) 9.414450 | 0.091286 | 8 / 8 |
| ComparisonBenchmarks | Destroy | 1,000 | — | ![Neutral](../heatmap/equal.svg) 0.023043 | 0.000412 | 8 / 7 |
| ComparisonBenchmarks | Destroy | 100,000 | — | ![Neutral](../heatmap/equal.svg) 2.304400 | 0.056393 | 8 / 8 |
| ComparisonBenchmarks | Iterate | 1,000 | — | ![Neutral](../heatmap/equal.svg) 0.000598 | 0.000011 | 8 / 8 |
| ComparisonBenchmarks | Iterate | 100,000 | — | ![Neutral](../heatmap/equal.svg) 0.058398 | 0.000515 | 8 / 6 |
| EcsBenchmarks | AddRemoveStructuralChurn | 100,000 | — | ![Neutral](../heatmap/equal.svg) 7.948717 | 0.225609 | 8 / 8 |
| EcsBenchmarks | LinearIteration | 100,000 | — | ![Neutral](../heatmap/equal.svg) 0.061057 | 0.001885 | 8 / 8 |
| OptimizedApiCreationBenchmarks | CreateEntityComponents | 100,000 | — | ![Neutral](../heatmap/equal.svg) 5.610975 | 0.143364 | 8 / 8 |
| OptimizedApiCreationBenchmarks | CreateEntityComponents | 1,000,000 | — | ![Neutral](../heatmap/equal.svg) 58.177075 | 1.223098 | 8 / 8 |
| OptimizedApiCreationBenchmarks | EmptyAddAdd | 100,000 | — | ![Neutral](../heatmap/equal.svg) 9.479463 | 0.104788 | 8 / 8 |
| OptimizedApiCreationBenchmarks | EmptyAddAdd | 1,000,000 | — | ![Neutral](../heatmap/equal.svg) 101.873800 | 2.308518 | 8 / 8 |
| OptimizedApiCreationBenchmarks | PreparedFactoryBatch | 100,000 | — | ![Neutral](../heatmap/equal.svg) 4.059571 | 0.079446 | 8 / 7 |
| OptimizedApiCreationBenchmarks | PreparedFactoryBatch | 1,000,000 | — | ![Neutral](../heatmap/equal.svg) 40.632188 | 1.286636 | 8 / 8 |
| OptimizedApiLookupBenchmarks | CachedAccessorGet | 100,000 | — | ![Neutral](../heatmap/equal.svg) 0.436337 | 0.039930 | 8 / 8 |
| OptimizedApiLookupBenchmarks | CachedAccessorGet | 1,000,000 | — | ![Neutral](../heatmap/equal.svg) 24.128914 | 1.162728 | 8 / 7 |
| OptimizedApiLookupBenchmarks | WorldGet | 100,000 | — | ![Neutral](../heatmap/equal.svg) 0.858263 | 0.014826 | 8 / 8 |
| OptimizedApiLookupBenchmarks | WorldGet | 1,000,000 | — | ![Neutral](../heatmap/equal.svg) 60.714813 | 4.117298 | 8 / 8 |
| StressBenchmarks | DenseIteration | 100,000 | — | ![Neutral](../heatmap/equal.svg) 0.087471 | 0.002415 | 8 / 8 |
| StressBenchmarks | DenseIteration | 1,000,000 | — | ![Neutral](../heatmap/equal.svg) 1.818945 | 0.047186 | 8 / 7 |
| StressBenchmarks | MixedFrames | 100,000 | — | ![Neutral](../heatmap/equal.svg) 196.930612 | 2.858862 | 8 / 8 |
| StressBenchmarks | MixedFrames | 1,000,000 | — | ![Neutral](../heatmap/equal.svg) 4906.900350 | 113.632575 | 8 / 8 |
| StressBenchmarks | PayloadChurn | 100,000 | — | ![Neutral](../heatmap/equal.svg) 38.083809 | 0.978601 | 8 / 7 |
| StressBenchmarks | PayloadChurn | 1,000,000 | — | ![Neutral](../heatmap/equal.svg) 638.248687 | 27.738115 | 8 / 8 |
| StressBenchmarks | RandomLookup | 100,000 | — | ![Neutral](../heatmap/equal.svg) 0.881889 | 0.017804 | 8 / 8 |
| StressBenchmarks | RandomLookup | 1,000,000 | — | ![Neutral](../heatmap/equal.svg) 44.365342 | 1.955535 | 8 / 8 |
| StressBenchmarks | Recycle10Percent | 100,000 | — | ![Neutral](../heatmap/equal.svg) 3.585243 | 0.090843 | 8 / 8 |
| StressBenchmarks | Recycle10Percent | 1,000,000 | — | ![Neutral](../heatmap/equal.svg) 69.303797 | 2.605689 | 8 / 8 |
| StressBenchmarks | SparseIteration | 100,000 | — | ![Neutral](../heatmap/equal.svg) 0.001551 | 0.000023 | 8 / 7 |
| StressBenchmarks | SparseIteration | 1,000,000 | — | ![Neutral](../heatmap/equal.svg) 0.007476 | 0.000201 | 8 / 8 |
| StressBenchmarks | WideIteration | 100,000 | — | ![Neutral](../heatmap/equal.svg) 0.181994 | 0.001772 | 8 / 7 |
| StressBenchmarks | WideIteration | 1,000,000 | — | ![Neutral](../heatmap/equal.svg) 7.622745 | 0.083599 | 8 / 8 |

### Legacy: 51

| Class | Method | N | Padding | Mean ms | SD ms | Actual / retained |
| --- | --- | --- | --- | --- | --- | --- |
| Add1Component | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 3.307914 | 0.045955 | 8 / 7 |
| Add1ComponentRandomOrder | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 4.862257 | 0.148120 | 8 / 7 |
| Add1RandomComponent | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 4.757183 | 0.034868 | 8 / 6 |
| Add1RandomComponentRandomOrder | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 6.668538 | 0.091321 | 8 / 8 |
| Add2Components | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 6.332250 | 0.050406 | 8 / 6 |
| Add2ComponentsRandomOrder | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 8.625163 | 0.228380 | 8 / 8 |
| Add2RandomComponents | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 8.822038 | 0.151305 | 8 / 8 |
| Add2RandomComponentsRandomOrder | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 11.579038 | 0.560120 | 8 / 8 |
| Add3Components | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 9.672213 | 0.068706 | 8 / 8 |
| Add3ComponentsRandomOrder | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 11.744988 | 0.264644 | 8 / 8 |
| Add3RandomComponents | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 13.188007 | 0.388870 | 8 / 7 |
| Add3RandomComponentsRandomOrder | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 15.850700 | 0.193687 | 8 / 7 |
| Add4Components | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 12.522412 | 0.073736 | 8 / 8 |
| Add4ComponentsRandomOrder | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 15.873238 | 0.313107 | 8 / 8 |
| CreateEmptyEntity | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 2.194671 | 0.003759 | 8 / 7 |
| CreateEntityWith1Component | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 5.655386 | 0.094876 | 8 / 7 |
| CreateEntityWith1RandomComponent | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 6.013986 | 0.257184 | 8 / 7 |
| CreateEntityWith2Components | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 8.846412 | 0.213342 | 8 / 8 |
| CreateEntityWith2RandomComponents | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 8.694525 | 0.131524 | 8 / 8 |
| CreateEntityWith3Components | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 13.522462 | 1.161670 | 8 / 8 |
| CreateEntityWith3RandomComponents | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 13.861425 | 0.269642 | 8 / 8 |
| CreateEntityWith4Components | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 15.941650 | 0.849598 | 8 / 8 |
| DeleteEntity | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 2.244525 | 0.933628 | 8 / 8 |
| FilterMismatchSystems | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 0.014212 | 0.001056 | 8 / 8 |
| FourRemoveOneComponent | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 4.936750 | 0.648895 | 8 / 8 |
| FourRemoveThreeComponents | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 9.635629 | 0.360915 | 8 / 7 |
| FourRemoveTwoComponents | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 7.299957 | 0.201175 | 8 / 7 |
| MultiSystems | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 0.055214 | 0.002451 | 8 / 8 |
| OneAddOneComponent | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 3.647714 | 0.035249 | 8 / 7 |
| OneAddThreeComponents | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 10.278225 | 0.186893 | 8 / 8 |
| OneAddTwoComponents | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 7.205075 | 0.316675 | 8 / 8 |
| Remove1Component | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 3.013075 | 0.054055 | 8 / 8 |
| Remove1ComponentRandomOrder | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 5.167937 | 0.699374 | 8 / 8 |
| Remove2Components | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 5.707586 | 0.119666 | 8 / 7 |
| Remove2ComponentsRandomOrder | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 9.822057 | 1.936721 | 8 / 7 |
| Remove3Components | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 9.222400 | 0.319738 | 8 / 7 |
| Remove3ComponentsRandomOrder | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 11.617900 | 0.567640 | 8 / 7 |
| Remove4Components | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 11.702029 | 0.189902 | 8 / 7 |
| Remove4ComponentsRandomOrder | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 16.957763 | 0.823145 | 8 / 8 |
| SystemWith1Component | Run | 100,000 | 0 | ![Neutral](../heatmap/equal.svg) 0.032653 | 0.000962 | 8 / 8 |
| SystemWith1Component | Run | 100,000 | 10 | ![Neutral](../heatmap/equal.svg) 0.003058 | 0.000055 | 8 / 8 |
| SystemWith2Components | Run | 100,000 | 0 | ![Neutral](../heatmap/equal.svg) 0.036160 | 0.001458 | 8 / 8 |
| SystemWith2Components | Run | 100,000 | 10 | ![Neutral](../heatmap/equal.svg) 0.003231 | 0.000122 | 8 / 8 |
| SystemWith3Components | Run | 100,000 | 0 | ![Neutral](../heatmap/equal.svg) 0.049782 | 0.000716 | 8 / 8 |
| SystemWith3Components | Run | 100,000 | 10 | ![Neutral](../heatmap/equal.svg) 0.004365 | 0.000134 | 8 / 8 |
| ThreeAddOneComponent | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 4.010771 | 0.097875 | 8 / 7 |
| ThreeRemoveOneComponent | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 3.528225 | 0.106265 | 8 / 8 |
| ThreeRemoveTwoComponents | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 6.480686 | 0.178542 | 8 / 7 |
| TwoAddOneComponent | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 3.871614 | 0.164310 | 8 / 7 |
| TwoAddTwoComponents | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 6.978271 | 0.137534 | 8 / 7 |
| TwoRemoveOneComponent | Run | 100,000 | — | ![Neutral](../heatmap/equal.svg) 3.596017 | 0.050620 | 8 / 6 |
