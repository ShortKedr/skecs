[English](REPORT.md) | Русский

# Бенчмарки Space Battle — игровой high

Preset: `game`; profile: `high`; 2026-10-19T04:33:57.3983635+00:00 — 2026-10-19T04:44:30.9401893+00:00.

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
| SKECS | `48347501a0d41b453768c456e582c75f6b37af77` | 0.2.0 |
| LeoECSLite | `5053141d7d663c6b1855c8409ac93dfac807b239` | 2024.5.22 |
| LeoECS Classic | `1213c31c9ef8325d48e74a52b345c4db242d038b` | 2023.6.22 |
| Morpeh | `9f6369384792a01d74df24efd35b08ae313e4f12` | 2024.1.1 |
| StaticEcs 2.2.9 | `23b1cf36d89497e8a126d38459d688e2b0a5dea3` | 2.2.9 |

StaticPack: `7bfb6cf925901b9366dda884258d727a89fff76e`.

## Время тика

| ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick | Total outer s |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SKECS | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.5442 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.8947 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 1.1407 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 3.6522 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 584.1 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 58.770956 |
| LeoECSLite | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.5908 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.9544 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 1.2083 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 5.7411 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 666.8 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 63.808179 |
| LeoECS Classic | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.5715 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.9289 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 1.1744 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 3.5846 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 648.7 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 61.721136 |
| Morpeh | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.5784 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.9501 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 1.2273 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 3.8858 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 644.9 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 62.471811 |
| StaticEcs 2.2.9 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5084 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.8543 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 1.1080 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 3.9540 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 605.5 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 54.904581 |

![Mean tick time](current/MeanMs.svg)

![P99 tick time](current/P99Ms.svg)

## Окна начала прогона

Окна 1–600 и 1–2000 накопительные; остаток с ними не пересекается.

| ECS | 1–600 ms | 1–2000 ms | 2001–108000 ms | Full ms |
| --- | --- | --- | --- | --- |
| SKECS | 0.6207 | 0.5759 | 0.5436 | 0.5442 |
| LeoECSLite | 0.7145 | 0.6241 | 0.5902 | 0.5908 |
| LeoECS Classic | 0.6610 | 0.6004 | 0.5709 | 0.5715 |
| Morpeh | 0.6517 | 0.6083 | 0.5779 | 0.5784 |
| StaticEcs 2.2.9 | 0.5897 | 0.5274 | 0.5080 | 0.5084 |

![Opening windows](current/InitialWindows.svg)

[Summary CSV](current/summary.csv)

## Средние стадий за полный прогон

| Stage | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1400 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1453 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.1567 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.1586 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1455 |
| ai | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0886 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1032 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0833 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0904 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0847 |
| thermal-inventory | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0065 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0080 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0062 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0062 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0058 |
| steering | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0193 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0225 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0187 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0194 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0179 |
| weapons | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0126 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0176 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0117 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0132 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0104 |
| carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0005 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0564 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0660 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0598 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0836 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0628 |
| hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1824 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1954 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2050 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1767 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1408 |
| lifecycle | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0378 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0322 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0298 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0297 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0399 |
| Battle.LastTickMs | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.5441 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.5907 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.5714 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.5784 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5083 |
| Outer tick | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.5442 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.5908 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.5715 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.5784 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5084 |

### SKECS: стадии начала

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

## Окна HUD: первое, второе и общее худшее

Общее худшее окно выбрано по максимальному HUD Mean среди пяти портов. Границы общие для всех ECS. Средняя численность совпадает.

### Первое: 1–600

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.6206 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.7144 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.6609 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.6517 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5897 |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1541 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.1657 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.1787 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.1766 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1588 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1208 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1430 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1178 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1190 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1190 |
| thermal-inventory | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0070 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0089 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0076 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0064 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0057 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0242 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0301 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0241 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0250 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0233 |
| weapons | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0164 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0226 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0160 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.0149 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0134 |
| carriers | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0001 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0002 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0002 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0002 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0003 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0629 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0777 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0707 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0888 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0701 |
| hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1943 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2270 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2106 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.1912 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1588 |
| lifecycle | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0407 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0391 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0351 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0295 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0403 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 104.26 | 818.02 | 859.90 | 1558.18 | 48.44 |

### Второе: 601–1200

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.7225 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.7800 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.7668 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.7575 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.6641 |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1893 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1962 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.2140 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.2120 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1915 |
| ai | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.1212 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1438 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1158 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.1228 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1133 |
| thermal-inventory | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0072 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0093 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.0075 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0076 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0068 |
| steering | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0237 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0269 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.0232 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0235 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0210 |
| weapons | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0146 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0197 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0143 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0151 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0116 |
| carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0004 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0760 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0868 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0818 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1077 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0817 |
| hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.2404 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2579 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2675 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.2320 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1863 |
| lifecycle | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0499 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0390 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0424 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0363 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0514 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 100.58 | 970.01 | 1513.26 | 1386.75 | 44.51 |

### Худшее: 80401–81000

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.6399 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.6773 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.6743 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8305 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5797 |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1763 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1782 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.1929 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.2121 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1767 |
| ai | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.1009 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1166 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0979 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1290 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0959 |
| thermal-inventory | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0066 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0082 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0066 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0092 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0059 |
| steering | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0190 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0229 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0194 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0264 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0179 |
| weapons | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0150 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0185 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0128 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0213 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0107 |
| carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0012 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0005 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0627 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0722 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0662 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1258 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0674 |
| hits | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2138 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2241 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2395 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2487 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1592 |
| lifecycle | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0451 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0360 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0386 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0569 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0454 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 85.66 | 716.35 | 1336.79 | 1462.78 | 46.07 |

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
| SKECS | 42.92 | 91.98 | 70.48 | 7.04 | 0.00 | 3820 | 81 | 119 |
| LeoECSLite | 47.78 | 85.46 | 65.01 | n/a | n/a | 3820 | n/a | n/a |
| LeoECS Classic | 47.65 | 89.81 | 68.75 | n/a | n/a | 3820 | n/a | n/a |
| Morpeh | 47.38 | 98.11 | 76.66 | n/a | n/a | 3820 | n/a | n/a |
| StaticEcs 2.2.9 | 44.22 | 90.27 | 65.11 | n/a | n/a | 3820 | n/a | n/a |

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

Base commit: `48347501a0d41b453768c456e582c75f6b37af77`. Source and loaded DLL hashes describe the measured working tree.


Полные CSV сохранены локально без потерь в gzip; SHA256 архива и распакованного файла записаны в локальном манифесте. Анализатор читает gzip напрямую и требует только .NET 8.



```sh
dotnet run --project SpaceBattle.Benchmarks -- report space-battle --data artifacts/benchmarks/current/space-battle --output docs/space-battle/current
```

[Desktop timing](DESKTOP_TIMING.ru.md) · [Architecture](ARCHITECTURE.ru.md)

Локальные входы измерения: `artifacts/benchmarks/current/space-battle`.
Базовый commit: `48347501a0d41b453768c456e582c75f6b37af77`.
GC total available memory limit; not physical RAM: 61.49 GiB.
CPU: AMD Ryzen 9 9900X 12-Core Processor.
