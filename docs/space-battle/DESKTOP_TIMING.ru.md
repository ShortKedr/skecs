[English](DESKTOP_TIMING.md) | Русский

# Почему HUD Mean отличается от headless

Холодный Release с tiering off: 4.381 ms HUD Mean к тику 30 и 0.618 ms в окне 601–1200. Обычный runtime: 5.075 ms к тику 10 и 0.610 ms в окне 601–1200.

### Цветовая шкала

![Отставание до 5%](../benchmarks/heatmap/best.svg) до 5% · ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 15% · ![Отставание от 30%](../benchmarks/heatmap/worst.svg) от 30% · ![Нейтрально](../benchmarks/heatmap/equal.svg) нейтрально.

Цвет показывает отставание от лучшего значения в сопоставимой группе: зелёный → жёлтый → красный. Если вся группа укладывается в 5%, она серая. Цвет описывает размер различия, а не статистическую значимость. [Правила сравнения](../benchmarks/COLORS.ru.md).

Пять независимых Release-процессов повторяют один nominal game-бой: seed 7331, 96 эскортов и 3 носителя на сторону, LightSwarm, Tactical, подкрепления включены. Хеши сборок симуляции и контрактов совпадают; итоговые checksum, счётчики, тактика и численность совпали.

Checksum: `97D6C5AE0577ACA4`.

Headless отдельно прогревает 600 тиков в другом экземпляре боя. Desktop начинается без прогрева Step, включая стартовые JIT и ленивую инициализацию. Default убирает DOTNET_TieredCompilation, tiering0 отключает tiered compilation. Paced desktop ограничен 60 кадрами/с, выполняет один Step на кадр, VSync выключен. Это контролируемый replay; FPS обычного игрового накопителя не измеряется. Процессы запускаются последовательно.

HUD Mean усредняет последние min(600, число тиков) выборок. Первые 10/30/60 тиков включают старт; после 600 это скользящее окно. Capture, рендеринг и ожидание кадра не входят в Battle.LastTickMs; их нагрузка может повлиять на следующий Step.

## Mean HUD в момент тика, ms

| Tick | Headless прогрет | Headless прогрет / 60 Hz | Desktop старт / tiering off | Desktop старт / default | Desktop старт / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| 10 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5211 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.7035 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 10.6390 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 5.0745 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 4.9649 |
| 30 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3131 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.5297 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 4.3811 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2.5248 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2.5492 |
| 60 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.2610 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.4946 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2.3680 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.8794 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.9094 |
| 120 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3018 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.5650 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.4915 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.8299 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.6990 |
| 600 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5171 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8850 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8733 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.9017 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8543 |
| 1200 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5255 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8917 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.6181 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.6095 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.6064 |

## Среднее стадий: 1–600, ms

| Stage | Headless прогрет | Headless прогрет / 60 Hz | Desktop старт / tiering off | Desktop старт / default | Desktop старт / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1087 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2011 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1334 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.4309 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.1248 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1160 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1990 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2021 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.4249 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2053 |
| thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0080 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0150 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0237 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0372 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0186 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0285 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0489 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0472 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1249 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0640 |
| weapons | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0165 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0321 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0575 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0859 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0561 |
| carriers | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0001 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0008 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0079 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0030 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0029 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0711 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1335 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1272 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2908 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1789 |
| hits | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1354 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2004 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1840 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.3950 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.1585 |
| lifecycle | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0329 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0541 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0903 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1090 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0451 |
| Total HUD Step | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5171 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8850 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8733 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.9017 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8543 |

## Среднее стадий: 601–1200, ms

| Stage | Headless прогрет | Headless прогрет / 60 Hz | Desktop старт / tiering off | Desktop старт / default | Desktop старт / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1233 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2264 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.1457 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1293 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.1401 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1015 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1789 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.1201 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.1212 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1341 |
| thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0061 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0127 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0080 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0078 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0084 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0196 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0359 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0246 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0255 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0242 |
| weapons | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0139 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0288 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0191 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0245 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0242 |
| carriers | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0012 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0006 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0015 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0014 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0591 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1138 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0768 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1160 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0773 |
| hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.1606 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2329 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.1769 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1417 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.1678 |
| lifecycle | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0410 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0610 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0463 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0420 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0288 |
| Total HUD Step | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5255 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8917 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.6181 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.6095 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.6064 |

Это один запуск каждой конфигурации. Для сравнения должны совпадать настройки, runtime, диапазоны тиков, прогрев и границы таймера. Эти данные не устанавливают причину устойчивого значения около 4 ms в другой фазе боя. RawCsvPath содержит имя файла без локального пути пользователя.

[Full high-profile game run and all five ECS](REPORT.ru.md).

Полные CSV сохранены локально без потерь в gzip. Локальный манифест хранит SHA256 архивов и распакованных CSV; анализатор читает gzip напрямую с .NET 8.



```sh
dotnet run --project SpaceBattle.Benchmarks -- report desktop --data artifacts/benchmarks/current/desktop --output docs/space-battle/desktop-current
```

Checksum: `97D6C5AE0577ACA4`. Локальные входы измерения: `artifacts/benchmarks/current/desktop`.
