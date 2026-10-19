[English](REPORT.md) | Русский

# Бенчмарки Space Battle — игровой high

Preset: `game`; profile: `high`; 2026-10-19T02:24:31.9630390+00:00 — 2026-10-19T02:35:04.3039312+00:00.

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
| SKECS | `30817034464d5cd82b7495e8fff9a07c187c26ef` | 0.2.0 |
| LeoECSLite | `5053141d7d663c6b1855c8409ac93dfac807b239` | 2024.5.22 |
| LeoECS Classic | `1213c31c9ef8325d48e74a52b345c4db242d038b` | 2023.6.22 |
| Morpeh | `9f6369384792a01d74df24efd35b08ae313e4f12` | 2024.1.1 |
| StaticEcs 2.2.9 | `23b1cf36d89497e8a126d38459d688e2b0a5dea3` | 2.2.9 |

StaticPack: `7bfb6cf925901b9366dda884258d727a89fff76e`.

## Время тика

| ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick | Total outer s |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SKECS | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.5508 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.9099 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 1.1657 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 3.5657 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 584.1 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 59.490215 |
| LeoECSLite | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.5924 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.9639 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 1.2237 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 3.8661 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 666.8 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 63.975975 |
| LeoECS Classic | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.5692 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.9216 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 1.1699 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 3.4681 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 648.7 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 61.476601 |
| Morpeh | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.5702 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.9269 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 1.1865 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 3.8976 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 644.9 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 61.578307 |
| StaticEcs 2.2.9 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.4968 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.8308 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 1.0710 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 3.6386 | ![Нейтрально](../benchmarks/heatmap/equal.svg) 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 605.5 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 53.650141 |

![Mean tick time](current/MeanMs.svg)

![P99 tick time](current/P99Ms.svg)

## Окна начала прогона

Окна 1–600 и 1–2000 накопительные; остаток с ними не пересекается.

| ECS | 1–600 ms | 1–2000 ms | 2001–108000 ms | Full ms |
| --- | --- | --- | --- | --- |
| SKECS | 0.6296 | 0.5724 | 0.5504 | 0.5508 |
| LeoECSLite | 0.6715 | 0.6150 | 0.5919 | 0.5924 |
| LeoECS Classic | 0.6711 | 0.5966 | 0.5687 | 0.5692 |
| Morpeh | 0.6353 | 0.5858 | 0.5699 | 0.5702 |
| StaticEcs 2.2.9 | 0.5902 | 0.5462 | 0.4958 | 0.4968 |

![Opening windows](current/InitialWindows.svg)

[Summary CSV](current/summary.csv)

## Средние стадий за полный прогон

| Stage | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1417 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1468 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.1565 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.1574 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1431 |
| ai | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0895 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1031 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0827 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0889 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0828 |
| thermal-inventory | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0066 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0080 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.0061 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.0060 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0055 |
| steering | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0195 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0223 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0185 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0194 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0173 |
| weapons | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0128 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0173 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0115 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0127 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0101 |
| carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0006 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0005 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0005 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0570 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0663 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0591 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0820 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0609 |
| hits | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1848 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1957 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2060 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1753 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1377 |
| lifecycle | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0382 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0321 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0285 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0279 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0388 |
| Battle.LastTickMs | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.5508 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.5923 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.5692 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.5701 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.4967 |
| Outer tick | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.5508 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.5924 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.5692 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.5702 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.4968 |

### SKECS: стадии начала

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

## Окна HUD: первое, второе и общее худшее

Общее худшее окно выбрано по максимальному HUD Mean среди пяти портов. Границы общие для всех ECS. Средняя численность совпадает.

### Первое: 1–600

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.6295 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.6714 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.6710 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.6353 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5902 |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1567 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1616 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.1818 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.1718 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1591 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1216 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.1349 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1208 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1185 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1199 |
| thermal-inventory | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0071 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0082 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0069 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0062 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0061 |
| steering | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0246 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0283 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0243 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0246 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0229 |
| weapons | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0165 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0211 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.0148 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.0150 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0136 |
| carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0002 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0002 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0001 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0002 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0003 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0640 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0741 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0684 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0856 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0718 |
| hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1971 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2081 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2176 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.1849 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1557 |
| lifecycle | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0417 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0348 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0361 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0286 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0408 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 104.26 | 818.02 | 859.90 | 1558.18 | 48.44 |

### Второе: 601–1200

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.7159 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.7800 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.7457 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.7508 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.7223 |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1891 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.1986 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.2105 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.2106 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.2042 |
| ai | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.1203 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1408 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1137 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.1224 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.1255 |
| thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0072 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0091 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0073 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0076 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.0080 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0228 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0265 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0221 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0240 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0234 |
| weapons | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.0144 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0191 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0132 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.0145 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0132 |
| carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0002 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0007 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0737 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0884 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0787 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1070 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0904 |
| hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.2386 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.2581 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2638 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.2297 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1993 |
| lifecycle | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0494 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0389 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0362 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0347 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0576 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 100.58 | 970.01 | 1513.26 | 1386.75 | 44.51 |

### Худшее: 75601–76200

| HUD / stage ms | SKECS | LeoECSLite | LeoECS Classic | Morpeh | StaticEcs 2.2.9 |
| --- | --- | --- | --- | --- | --- |
| HUD total | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.6081 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8055 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.6504 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.6805 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5741 |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1475 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.1737 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.1688 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.1728 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.1553 |
| ai | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.1111 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1539 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1052 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.1199 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1074 |
| thermal-inventory | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0074 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0124 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0075 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0073 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0063 |
| steering | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0245 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0339 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0246 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0257 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0230 |
| weapons | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0138 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0252 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0128 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0153 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0112 |
| carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0006 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0002 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0004 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0004 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0702 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1019 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0742 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1032 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.0758 |
| hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1922 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2472 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2239 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1981 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1510 |
| lifecycle | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0409 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0568 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0331 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0377 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0437 |

| Ships | Projectiles | Debris | Particles | Turrets |
| --- | --- | --- | --- | --- |
| 98.75 | 777.02 | 1179.82 | 1219.71 | 46.11 |

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
| SKECS | 42.90 | 90.62 | 73.23 | 7.04 | 0.00 | 3820 | 81 | 119 |
| LeoECSLite | 47.78 | 86.41 | 72.56 | n/a | n/a | 3820 | n/a | n/a |
| LeoECS Classic | 47.66 | 91.75 | 68.54 | n/a | n/a | 3820 | n/a | n/a |
| Morpeh | 47.38 | 96.93 | 75.75 | n/a | n/a | 3820 | n/a | n/a |
| StaticEcs 2.2.9 | 44.22 | 91.67 | 66.03 | n/a | n/a | 3820 | n/a | n/a |

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

Base commit: `30817034464d5cd82b7495e8fff9a07c187c26ef`. Source and loaded DLL hashes describe the measured working tree.


Полные CSV сохранены локально без потерь в gzip; SHA256 архива и распакованного файла записаны в локальном манифесте. Анализатор читает gzip напрямую и требует только .NET 8.



```sh
dotnet run --project SpaceBattle.Benchmarks -- report space-battle --data artifacts/benchmarks/current/space-battle --output docs/space-battle/current
```

[Desktop timing](DESKTOP_TIMING.ru.md) · [Architecture](ARCHITECTURE.ru.md)

Локальные входы измерения: `artifacts/benchmarks/current/space-battle`.
Базовый commit: `30817034464d5cd82b7495e8fff9a07c187c26ef`.
GC total available memory limit; not physical RAM: 61.49 GiB.
CPU: AMD Ryzen 9 9900X 12-Core Processor.
