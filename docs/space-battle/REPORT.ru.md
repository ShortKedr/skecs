[English](REPORT.md) | Русский

# Бенчмарки Space Battle — игровой high

Preset: `game`; profile: `high`; 2026-10-18T15:27:07.5787241+05:00 — 2026-10-18T15:34:23.4895992+05:00.

Пять ECS, по 108 000 измеренных тиков и 600 отдельных тиков прогрева. Seed 7331, 96 эскортов и 3 носителя на сторону; ChurnMultiplier 2; бесконечные подкрепления включены.

### Цветовая шкала

![Отставание до 5%](../benchmarks/heatmap/best.svg) до 5% · ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 15% · ![Отставание от 30%](../benchmarks/heatmap/worst.svg) от 30% · ![Нейтрально](../benchmarks/heatmap/equal.svg) нейтрально.

Цвет показывает отставание от лучшего значения в сопоставимой группе: зелёный → жёлтый → красный. Если вся группа укладывается в 5%, она серая. Цвет описывает размер различия, а не статистическую значимость. [Правила сравнения](../benchmarks/COLORS.ru.md).

## Проверка и методика

Проверка пройдена: 540 000 исходных тиков, хеши архивов/CSV/входов, происхождение, время, стадии, аллокации, интервалы GC, счётчики, census и потиковая игровая трасса.

Внешний тик включает игровые системы, физику и ECS commit; HUD и стадии имеют более узкие границы. Рендеринг, snapshots и отчёт исключены. Один прогон описывает эту машину и не устанавливает устойчивый рейтинг или доверительный интервал. Managed allocation не включает нативное хранилище ECS.

## Ревизии фреймворков

| ECS | Ревизия | Версия |
| --- | --- | --- |
| SKECS | `9063e9d780526dc937d949a6709b33241b47a092` | 0.2.0 |
| LeoECSLite | `5053141d7d663c6b1855c8409ac93dfac807b239` | 2024.5.22 |
| LeoECS Classic | `1213c31c9ef8325d48e74a52b345c4db242d038b` | 2023.6.22 |
| Morpeh | `9f6369384792a01d74df24efd35b08ae313e4f12` | 2024.1.1 |
| StaticEcs 2.2.9 | `23b1cf36d89497e8a126d38459d688e2b0a5dea3` | 2.2.9 |

StaticPack: `7bfb6cf925901b9366dda884258d727a89fff76e`.

## Время тика

| ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick | Total outer s |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SKECS | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.6662 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 1.0452 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 1.3122 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 3.9294 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 601.6 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 71.954668 |
| LeoECSLite | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8767 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 1.3194 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 1.6137 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 4.2005 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 681.0 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 94.678999 |
| LeoECS Classic | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.8412 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 1.3188 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.7073 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 4.4115 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 667.5 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 90.848046 |
| Morpeh | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.7588 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 1.1732 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 1.4542 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 5.1162 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 655.8 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 81.947525 |
| StaticEcs 2.2.9 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.6536 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 1.0177 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 1.2704 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 4.1364 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 621.6 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 70.592594 |

![Mean tick time](current/MeanMs.svg)

![P99 tick time](current/P99Ms.svg)

## Окна начала прогона

Окна 1–600 и 1–2000 накопительные; остаток с ними не пересекается.

| ECS | 1–600 ms | 1–2000 ms | 2001–108000 ms | Full ms |
| --- | --- | --- | --- | --- |
| SKECS | 0.7655 | 0.6998 | 0.6656 | 0.6662 |
| LeoECSLite | 0.9728 | 0.9107 | 0.8760 | 0.8767 |
| LeoECS Classic | 0.9683 | 0.8982 | 0.8401 | 0.8412 |
| Morpeh | 0.8880 | 0.8239 | 0.7575 | 0.7588 |
| StaticEcs 2.2.9 | 0.7587 | 0.6957 | 0.6528 | 0.6536 |

![Opening windows](current/InitialWindows.svg)

[Summary CSV](current/summary.csv)

## Средние стадий за полный прогон

| Stage | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1798 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.2207 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.1911 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.1847 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1732 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1089 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1317 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1313 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.1175 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1055 |
| thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0093 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0139 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0132 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0107 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0105 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0249 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0292 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0295 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.0271 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0247 |
| weapons | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0202 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0264 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0240 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0225 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0195 |
| carriers | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0008 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0009 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0008 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0008 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0009 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0614 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1323 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1054 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1131 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0757 |
| hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.2287 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2771 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2939 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.2456 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.2102 |
| lifecycle | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0322 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0443 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0518 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0365 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0333 |
| Battle.LastTickMs | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.6662 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8766 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.8411 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.7587 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.6536 |
| Outer tick | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.6662 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8767 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.8412 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.7588 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.6536 |

### SKECS: стадии начала

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

## Окна HUD: первое, второе и общее худшее

Общее худшее окно выбрано по максимальному HUD Mean среди пяти портов. Границы общие для всех ECS. Средняя численность совпадает.

### Первое: 1–600

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.7654 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.9727 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.9682 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.8879 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.7586 |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1933 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.2356 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.2131 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.2114 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1914 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1493 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.1687 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1757 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.1589 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1446 |
| thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0102 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0156 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0156 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0128 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0128 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0336 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0397 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0378 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0369 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0328 |
| weapons | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.0270 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0344 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0316 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0289 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0247 |
| carriers | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0010 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0712 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1431 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1231 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1287 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0823 |
| hits | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.2443 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.2852 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.3086 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.2673 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.2361 |
| lifecycle | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.0363 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0499 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0624 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0427 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0329 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 104.26 | 818.02 | 859.90 | 1558.18 | 48.44 |

### Второе: 601–1200

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.8683 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.1630 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.1922 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 1.0274 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.8842 |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.2331 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.2912 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.2627 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.2438 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.2317 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1505 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1853 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1940 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.1710 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1481 |
| thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0102 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0167 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0170 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0141 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0135 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0299 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0351 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0380 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0338 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0305 |
| weapons | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0227 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0298 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0300 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0256 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0232 |
| carriers | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0006 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0007 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0008 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0007 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0008 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0798 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1723 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1538 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1542 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1010 |
| hits | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3002 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.3752 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.4112 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.3331 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.2882 |
| lifecycle | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0411 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0568 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0845 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0511 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0472 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 100.58 | 970.01 | 1513.26 | 1386.75 | 44.51 |

### Худшее: 601–1200

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.8683 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.1630 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.1922 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 1.0274 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.8842 |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.2331 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.2912 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.2627 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.2438 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.2317 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1505 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1853 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1940 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.1710 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1481 |
| thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0102 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0167 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0170 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0141 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0135 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0299 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0351 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0380 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0338 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0305 |
| weapons | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0227 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0298 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0300 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0256 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0232 |
| carriers | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0006 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0007 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0008 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0007 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0008 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0798 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1723 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1538 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1542 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1010 |
| hits | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3002 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.3752 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.4112 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.3331 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.2882 |
| lifecycle | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0411 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0568 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0845 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0511 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0472 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 100.58 | 970.01 | 1513.26 | 1386.75 | 44.51 |

## Игровое совпадение, census и тактические счётчики

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

## Память и census хранилища

Пики по 30 минутным checkpoint; census содержит только игровые сущности. Память процесса включает буферы стенда.

| ECS | Managed heap MiB | Working set MiB | Private MiB | Native MiB | Accessor MiB | Entities final | Archetypes final | Chunks final |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SKECS | 39.71 | 80.19 | 62.71 | 7.04 | 0.00 | 3820 | 81 | 119 |
| LeoECSLite | 45.61 | 78.98 | 70.51 | n/a | n/a | 3820 | n/a | n/a |
| LeoECS Classic | 45.38 | 77.53 | 70.81 | n/a | n/a | 3820 | n/a | n/a |
| Morpeh | 44.86 | 85.88 | 80.02 | n/a | n/a | 3820 | n/a | n/a |
| StaticEcs 2.2.9 | 42.23 | 83.80 | 58.20 | n/a | n/a | 3820 | n/a | n/a |

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

Runtime: .NET 8.0.32; OS: Microsoft Windows 10.0.26200; tiered compilation `0`.

Base commit: `9063e9d780526dc937d949a6709b33241b47a092`. Source and loaded DLL hashes describe the measured working tree.


Полные CSV сохранены локально без потерь в gzip; SHA256 архива и распакованного файла записаны в локальном манифесте. Анализатор читает gzip напрямую и требует только .NET 8.



```sh
dotnet run --project tools/BenchmarkTools -- report space-battle --data artifacts/benchmarks/current/space-battle --output docs/space-battle/current
```

[Desktop timing](DESKTOP_TIMING.ru.md) · [Architecture](ARCHITECTURE.ru.md) · [Historical finite battle](REPORT_2026-10-14_FINITE.ru.md)

Локальные входы измерения: `artifacts/benchmarks/current/space-battle`.
Базовый commit: `9063e9d780526dc937d949a6709b33241b47a092`.
RAM: 61.49 GiB.
CPU: AMD Ryzen 9 9900X 12-Core Processor            .
