[English](REPORT.md) | Русский

# Бенчмарки Space Battle — игровой high

Preset: `game`; profile: `high`; 2026-11-24T08:27:13.8070421+00:00 — 2026-11-24T08:38:43.3254183+00:00.

Пять независимых нативных ECS-ядер, по 108 000 измеренных тиков и 600 отдельных тиков прогрева. Seed 7331, 96 эскортов и 3 носителя на сторону; ChurnMultiplier 2; бесконечные подкрепления включены.

### Цветовая шкала

![Отставание до 5%](../benchmarks/heatmap/best.svg) до 5% · ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 15% · ![Отставание от 30%](../benchmarks/heatmap/worst.svg) от 30% · ![Нейтрально](../benchmarks/heatmap/equal.svg) нейтрально.

Цвет показывает отставание от лучшего значения в сопоставимой группе: зелёный → жёлтый → красный. Если вся группа укладывается в 5%, она серая. Цвет описывает размер различия, а не статистическую значимость. [Правила сравнения](../benchmarks/COLORS.ru.md).

## Проверка и методика

Проверка пройдена: 540 000 исходных тиков, хеши архивов/CSV/входов, происхождение, время, стадии, аллокации, интервалы GC, счётчики, census и потиковое совпадение хеша состояния с SKECS.

Внешний тик включает игровые системы, физику и ECS commit; HUD и стадии имеют более узкие границы. Рендеринг, захват состояния, хеширование/сравнение replay и отчёт исключены. Начальное состояние и каждый тик сравниваются с повтором SKECS из отдельного процесса до прогрева в 600 тиков; хеши включают игровые компоненты, ожидаемые подкрепления и счётчики. Один прогон описывает эту машину и не устанавливает устойчивый рейтинг или доверительный интервал. Managed allocation не включает нативное хранилище ECS.

## Ревизии фреймворков

| ECS | Ревизия | Версия |
| --- | --- | --- |
| SKECS | `a18443101ca1a27191745b979c32518eab4d058d` | 0.2.0 |
| LeoECSLite | `5053141d7d663c6b1855c8409ac93dfac807b239` | 2024.5.22 |
| LeoECS Classic | `1213c31c9ef8325d48e74a52b345c4db242d038b` | 2023.6.22 |
| Morpeh | `9f6369384792a01d74df24efd35b08ae313e4f12` | 2024.1.1 |
| StaticEcs 2.2.9 | `23b1cf36d89497e8a126d38459d688e2b0a5dea3` | 2.2.9 |

StaticPack: `7bfb6cf925901b9366dda884258d727a89fff76e`.

## Время тика

| ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick | Total outer s |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SKECS | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.3875 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.7655 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 1.0737 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 9.0559 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 616.6 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 41.848539 |
| LeoECSLite | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.6381 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.1754 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.5861 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 5.2985 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 699.1 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 68.915440 |
| LeoECS Classic | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.5366 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.0276 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.4231 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 8.8131 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 681.3 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 57.952254 |
| Morpeh | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.6569 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.2288 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.8793 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 34.9796 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 8 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 677.5 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 70.944696 |
| StaticEcs 2.2.9 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3058 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.6618 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.9256 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 6.0490 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 606.0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 33.028393 |

![Mean tick time](current/MeanMs.svg)

![P99 tick time](current/P99Ms.svg)

## Окна начала прогона

Окна 1–600 и 1–2000 накопительные; остаток с ними не пересекается.

| ECS | 1–600 ms | 1–2000 ms | 2001–108000 ms | Full ms |
| --- | --- | --- | --- | --- |
| SKECS | 0.3915 | 0.3657 | 0.3879 | 0.3875 |
| LeoECSLite | 0.7793 | 0.6548 | 0.6378 | 0.6381 |
| LeoECS Classic | 0.7391 | 0.6046 | 0.5353 | 0.5366 |
| Morpeh | 0.6631 | 0.6091 | 0.6578 | 0.6569 |
| StaticEcs 2.2.9 | 0.3033 | 0.2549 | 0.3068 | 0.3058 |

![Opening windows](current/InitialWindows.svg)

[Summary CSV](current/summary.csv)

## Средние стадий за полный прогон

| Stage | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| collect | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0174 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0253 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0341 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0407 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0048 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1178 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1804 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1401 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1711 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1130 |
| thermal-inventory | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0090 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0152 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0104 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0124 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0044 |
| steering | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0274 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0392 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0311 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0366 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0233 |
| weapons | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0197 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0323 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0217 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0265 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0108 |
| carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0011 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0012 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0007 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0016 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0008 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0426 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0788 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0618 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1188 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0498 |
| hits | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1238 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1971 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1765 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1837 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0840 |
| lifecycle | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0286 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0683 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0601 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0653 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0147 |
| Battle.LastTickMs | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.3874 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.6379 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.5364 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.6567 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3057 |
| Outer tick | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.3875 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.6381 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.5366 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.6569 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3058 |

### SKECS: стадии начала

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

## Окна HUD: первое, второе и общее худшее

Общее худшее окно выбрано по максимальному HUD Mean среди пяти портов. Границы общие для всех ECS. Средняя численность совпадает.

### Первое: 1–600

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.3914 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.7791 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.7389 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.6629 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3032 |
| collect | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0173 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0313 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0396 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0409 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0043 |
| ai | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.1399 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2457 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2225 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1995 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1303 |
| thermal-inventory | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0074 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0152 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0137 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0117 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0038 |
| steering | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0295 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0468 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0440 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0386 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0227 |
| weapons | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0196 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0397 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0323 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0271 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0108 |
| carriers | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0002 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0007 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0008 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0004 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0385 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0903 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0808 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1101 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0413 |
| hits | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1127 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2232 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2198 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1695 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0767 |
| lifecycle | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0263 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0863 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0855 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0645 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0128 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 104.26 | 818.02 | 859.90 | 1558.18 | 48.44 |

### Второе: 601–1200

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.5015 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8275 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.7161 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.7222 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3217 |
| collect | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0212 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0344 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0439 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0473 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0048 |
| ai | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1615 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2468 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1954 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2007 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1213 |
| thermal-inventory | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0100 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0187 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0126 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0124 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0039 |
| steering | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0340 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0465 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0375 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0375 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0221 |
| weapons | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0224 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0365 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0256 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0257 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0107 |
| carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0008 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0011 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0008 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0014 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0006 |
| physics | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0556 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1050 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0847 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1272 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0518 |
| hits | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1582 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2505 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2314 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1974 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0885 |
| lifecycle | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0377 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0881 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0842 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0725 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0180 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 100.58 | 970.01 | 1513.26 | 1386.75 | 44.51 |

### Худшее: 25201–25800

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.4581 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8318 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.7115 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.2909 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.4400 |
| collect | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0186 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0294 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0412 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0600 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0068 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1664 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2760 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2211 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.3713 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.1853 |
| thermal-inventory | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0102 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0207 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0139 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0240 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0061 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0356 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0600 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0474 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0725 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0362 |
| weapons | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0207 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0397 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0268 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0494 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0134 |
| carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0010 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0011 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0007 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0034 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0010 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0490 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0961 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0794 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2276 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0659 |
| hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1267 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2296 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2078 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.3203 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1047 |
| lifecycle | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0299 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0791 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0732 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1624 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0206 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 115.89 | 756.05 | 1239.28 | 1417.06 | 53.13 |

## Игровое совпадение, census и тактические счётчики

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

## Память и census хранилища

Пики по 30 минутным checkpoint; census содержит только игровые сущности. Память процесса включает буферы стенда.

| ECS | Managed heap MiB | Working set MiB | Private MiB | Native MiB | Accessor MiB | Entities final | Archetypes final | Chunks final |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SKECS | 46.95 | 98.19 | 76.41 | 7.04 | 0.00 | 3820 | 81 | 119 |
| LeoECSLite | 51.82 | 94.32 | 78.78 | n/a | n/a | 3820 | n/a | n/a |
| LeoECS Classic | 51.74 | 98.70 | 82.70 | n/a | n/a | 3820 | n/a | n/a |
| Morpeh | 51.44 | 105.07 | 90.97 | n/a | n/a | 3820 | n/a | n/a |
| StaticEcs 2.2.9 | 45.23 | 92.77 | 66.05 | n/a | n/a | 3820 | n/a | n/a |

### Поминутная численность

| Tick | Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- | --- |
| 3600 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 49 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 547 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1426 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 747 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 44 |
| 7200 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 91 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 453 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1425 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2027 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 40 |
| 10800 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 66 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 755 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1345 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1164 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 44 |
| 14400 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 91 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 827 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1369 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 726 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 50 |
| 18000 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 100 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 609 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1253 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 578 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 46 |
| 21600 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 103 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 532 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1271 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1415 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 44 |
| 25200 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 84 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 354 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1188 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 393 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 52 |
| 28800 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 93 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 698 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1233 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1641 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 56 |
| 32400 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 65 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 478 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1440 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 880 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 48 |
| 36000 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 82 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 770 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1325 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1301 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 58 |
| 39600 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 61 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 314 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1243 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 496 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 52 |
| 43200 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 73 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 490 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1177 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 774 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 40 |
| 46800 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 113 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 418 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 858 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 790 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 70 |
| 50400 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 103 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 777 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1427 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1392 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 50 |
| 54000 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 110 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1429 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1211 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 3808 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 50 |
| 57600 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 98 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 254 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1138 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 471 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 54 |
| 61200 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 97 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1052 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1461 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1949 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 50 |
| 64800 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 104 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 798 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1265 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1420 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 48 |
| 68400 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 62 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 378 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1364 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 303 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 40 |
| 72000 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 100 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 617 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1567 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 916 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 40 |
| 75600 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 96 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 456 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1096 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1090 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 44 |
| 79200 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 98 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 559 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1345 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 907 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 50 |
| 82800 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 64 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 734 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1288 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1062 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 46 |
| 86400 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 66 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 450 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 997 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 496 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 38 |
| 90000 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 47 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 438 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1408 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 489 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 52 |
| 93600 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 83 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 953 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1417 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1936 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 36 |
| 97200 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 98 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 637 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1084 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1310 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 46 |
| 100800 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 72 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 432 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1363 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1932 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 48 |
| 104400 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 72 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 887 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1525 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 618 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 36 |
| 108000 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 89 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 918 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1340 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1425 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 48 |

## Происхождение и воспроизведение

Runtime: .NET 8.0.34; OS: Microsoft Windows 10.0.26200; tiered compilation `0`.

Base commit: `a18443101ca1a27191745b979c32518eab4d058d`. Source and loaded DLL hashes describe the measured working tree.


Полные CSV сохранены локально без потерь в gzip; SHA256 архива и распакованного файла записаны в локальном манифесте. Анализатор читает gzip напрямую и требует только .NET 8.



```sh
dotnet run --project SpaceBattle.Benchmarks -- report space-battle --data artifacts/benchmarks/current/space-battle --output docs/space-battle/current
```

[Desktop timing](DESKTOP_TIMING.ru.md) · [Architecture](ARCHITECTURE.ru.md)

Локальные входы измерения: `artifacts/benchmarks/current/space-battle`.
Базовый commit: `a18443101ca1a27191745b979c32518eab4d058d`.
GC total available memory limit; not physical RAM: 61.49 GiB.
CPU: AMD Ryzen 9 9900X 12-Core Processor.
