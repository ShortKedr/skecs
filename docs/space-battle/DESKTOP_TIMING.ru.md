[English](DESKTOP_TIMING.md) | Русский

# Почему HUD Mean отличается от headless

Холодный Release с tiering off: 4.384 ms HUD Mean к тику 30 и 0.618 ms в окне 601–1200. Обычный runtime: 4.805 ms к тику 10 и 0.580 ms в окне 601–1200.

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
| 10 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5315 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.6597 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 10.7380 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 4.8045 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 5.0207 |
| 30 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3177 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.4923 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 4.3845 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2.3991 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2.5706 |
| 60 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.2659 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.4582 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 2.3733 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.8037 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.7721 |
| 120 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.3071 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.5649 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.4961 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.7805 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.5312 |
| 600 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5273 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8794 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8719 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.8040 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8259 |
| 1200 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5239 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.9137 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.6180 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.5796 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.6198 |

## Среднее стадий: 1–600, ms

| Stage | Headless прогрет | Headless прогрет / 60 Hz | Desktop старт / tiering off | Desktop старт / default | Desktop старт / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1100 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1862 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1334 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.3659 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.1242 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1182 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2002 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2061 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.4154 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1972 |
| thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0083 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0152 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0233 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0350 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0170 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0292 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0478 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0467 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1180 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0536 |
| weapons | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0166 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0327 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0564 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0829 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0538 |
| carriers | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0001 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0010 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0074 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0033 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0032 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0728 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1325 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1257 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2848 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1731 |
| hits | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1385 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2089 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1840 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.3948 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.1584 |
| lifecycle | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0335 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0549 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0889 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1040 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0452 |
| Total HUD Step | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5273 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8794 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8719 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 1.8040 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.8259 |

## Среднее стадий: 601–1200, ms

| Stage | Headless прогрет | Headless прогрет / 60 Hz | Desktop старт / tiering off | Desktop старт / default | Desktop старт / default 60 Hz |
| --- | --- | --- | --- | --- | --- |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1239 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2279 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.1453 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1280 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg) 0.1433 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1009 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1807 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1240 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1223 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1367 |
| thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0063 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0126 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0078 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.0074 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0080 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0197 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0362 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.0244 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0247 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0251 |
| weapons | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0139 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0295 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0188 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0226 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0252 |
| carriers | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0003 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0015 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0006 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0015 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0016 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0587 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.1157 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0763 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0883 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg) 0.0739 |
| hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg) 0.1606 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.2461 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1747 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.1423 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg) 0.1755 |
| lifecycle | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0397 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0634 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0461 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.0426 | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.0305 |
| Total HUD Step | ![Отставание до 5%](../benchmarks/heatmap/best.svg) 0.5239 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg) 0.9137 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.6180 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg) 0.5796 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg) 0.6198 |

Это один запуск каждой конфигурации. Для сравнения должны совпадать настройки, runtime, диапазоны тиков, прогрев и границы таймера. Эти данные не устанавливают причину устойчивого значения около 4 ms в другой фазе боя. RawCsvPath содержит имя файла без локального пути пользователя.

[Full high-profile game run and all five ECS](REPORT.ru.md).

Полные CSV сохранены локально без потерь в gzip. Локальный манифест хранит SHA256 архивов и распакованных CSV; анализатор читает gzip напрямую с .NET 8.



```sh
dotnet run --project SpaceBattle.Benchmarks -- report desktop --data artifacts/benchmarks/current/desktop --output docs/space-battle/desktop-current
```

Checksum: `97D6C5AE0577ACA4`. Локальные входы измерения: `artifacts/benchmarks/current/desktop`.
