[English](DESKTOP_TIMING.md) | Русский

# Почему HUD Mean отличается от headless

Холодный Release с tiering off: 4.002 ms HUD Mean к тику 30 и 0.736 ms в окне 601–1200. Обычный runtime: 5.582 ms к тику 10 и 0.707 ms в окне 601–1200.

### Цветовая шкала

![Отставание до 5%](../benchmarks/heatmap/best.svg) до 5% · ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 15% · ![Отставание от 30%](../benchmarks/heatmap/worst.svg) от 30% · ![Нейтрально](../benchmarks/heatmap/equal.svg) нейтрально.

Цвет показывает отставание от лучшего значения в сопоставимой группе: зелёный → жёлтый → красный. Если вся группа укладывается в 5%, она серая. Цвет описывает размер различия, а не статистическую значимость. [Правила сравнения](../benchmarks/COLORS.ru.md).

Пять независимых Release-процессов повторяют один nominal game-бой: seed 7331, 96 эскортов и 3 носителя на сторону, LightSwarm, Tactical, подкрепления включены. Хеши Core, SKECS и адаптера совпадают; итоговые checksum, счётчики, тактика и численность совпали.

Checksum: `4876EB52A0D4AFC3`.

Headless отдельно прогревает 600 тиков в другом экземпляре боя. Desktop начинается без прогрева Step, включая стартовые JIT и ленивую инициализацию. Default убирает DOTNET_TieredCompilation, tiering0 отключает tiered compilation. Paced desktop ограничен 60 кадрами/с, выполняет один Step на кадр, VSync выключен. Это контролируемый replay; FPS обычного игрового накопителя не измеряется. Процессы запускаются последовательно.

HUD Mean усредняет последние min(600, число тиков) выборок. Первые 10/30/60 тиков включают старт; после 600 это скользящее окно. Capture, рендеринг и ожидание кадра не входят в Battle.LastTickMs; их нагрузка может повлиять на следующий Step.

## Mean HUD в момент тика, ms

| Tick | Headless прогрет | Headless прогрет / 60 Hz | Desktop старт / tiering off | Desktop старт / default | Desktop старт / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| 10 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.6025 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8712 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 9.5218 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 5.5824 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 5.5468 |
| 30 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3894 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.7016 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 4.0021 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2.8311 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2.8843 |
| 60 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3366 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.6384 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2.2158 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2.1258 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.9331 |
| 120 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3870 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.7113 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.4359 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2.0304 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.4868 |
| 600 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.6385 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.0712 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.9610 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2.0620 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.9683 |
| 1200 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.6458 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.0769 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.7362 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.7067 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.7578 |

## Среднее стадий: 1–600, ms

| Stage | Headless прогрет | Headless прогрет / 60 Hz | Desktop старт / tiering off | Desktop старт / default | Desktop старт / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1394 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2525 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1712 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.4684 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1971 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1423 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2442 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2209 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.4465 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2132 |
| thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0113 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0217 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0257 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0476 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0203 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0378 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0594 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0555 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1386 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0585 |
| weapons | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0259 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0480 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0587 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0961 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0540 |
| carriers | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0018 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0085 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0049 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0055 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0788 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1441 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1282 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2887 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1633 |
| hits | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1730 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2426 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.2087 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.4377 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.1892 |
| lifecycle | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0297 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0569 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0836 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1334 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0670 |
| Total HUD Step | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.6385 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.0712 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.9610 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2.0620 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.9683 |

## Среднее стадий: 601–1200, ms

| Stage | Headless прогрет | Headless прогрет / 60 Hz | Desktop старт / tiering off | Desktop старт / default | Desktop старт / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1581 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2926 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.1780 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.1877 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2212 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1251 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2201 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.1466 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.1405 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.1431 |
| thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0091 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0170 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0111 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0112 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0117 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0250 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0423 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0308 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0286 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.0285 |
| weapons | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0220 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0406 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0290 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0303 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0287 |
| carriers | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0005 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0021 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0013 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0021 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0030 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0657 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1243 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0823 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0813 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0793 |
| hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.2050 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2784 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.2153 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1858 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg) 0.1985 |
| lifecycle | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0353 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0596 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0417 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.0394 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0438 |
| Total HUD Step | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.6458 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.0769 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.7362 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.7067 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.7578 |

Это один запуск каждой конфигурации. Для сравнения должны совпадать настройки, runtime, диапазоны тиков, прогрев и границы таймера. Эти данные не устанавливают причину устойчивого значения около 4 ms в другой фазе боя. RawCsvPath содержит имя файла без локального пути пользователя.

[Full high-profile game run and all five ECS](REPORT.ru.md).

Полные CSV сохранены локально без потерь в gzip. Локальный манифест хранит SHA256 архивов и распакованных CSV; анализатор читает gzip напрямую с .NET 8.



```sh
dotnet run --project tools/BenchmarkTools -- report desktop --data artifacts/benchmarks/current/desktop --output docs/space-battle/desktop-current
```

Checksum: `4876EB52A0D4AFC3`. Локальные входы измерения: `artifacts/benchmarks/current/desktop`.
