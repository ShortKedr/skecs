[English](README.md) | Русский

**[Посмотреть четыре промо-ролика](../promo/space-battle/README.ru.md)**

# SpaceBattle

SpaceBattle — бой флотов под управлением ИИ и реалистичная нагрузка для сравнения ECS. У SKECS, LeoECS Lite, LeoECS Classic, Morpeh и StaticEcs отдельные C#-ядра симуляции, desktop-клиенты, browser bridge/сцены и headless-runner. Хост выбирает фреймворк до запуска соответствующей реализации.

## С чего начать

| Задача | Руководство |
| --- | --- |
| Посмотреть бой в браузере или запустить локально | [Web-клиент](../../SpaceBattle.Web/README.ru.md) |
| Запустить desktop и разобраться с управлением | [Desktop-клиент](../../SpaceBattle.Game/README.ru.md) |
| Прочитать сравнение единственной игровой нагрузки high | [Текущий отчёт](REPORT.ru.md) |
| Понять текущие стадии, компоненты и границы изменений | [Текущая архитектура](ARCHITECTURE.ru.md) |
| Найти и записать реальные боевые моменты | [Поиск и запись сцен](../../tools/space-battle/scenes/README.ru.md) |

## Текущий геймплей

Интерактивные клиенты начинают с 96 эскортных кораблей и трёх носителей на сторону. Корабли сражаются сами; пользователь управляет камерой, скоростью и инструментами наблюдения. Истощённые флоты получают warp-подкрепления без сброса выживших. У headless два режима: `game` использует игровой состав, расстановку и подкрепления; `benchmark` использует конечный бой. Основная кампания производительности использует `game` с высокой интенсивностью боя.

| Правило | Актуальное описание |
| --- | --- |
| Состав LightSwarm | [Флоты](FLEETS.ru.md) |
| Разреженная стартовая расстановка | [Развёртывание](DEPLOYMENT.ru.md) |
| Выбор целей, малые группы и полосы подхода | [Тактика групп v3](TACTICS.ru.md) |
| Главные орудия и прикреплённые турели | [Вооружение](ARMAMENT.ru.md) |
| Множители урона щиту и корпусу | [Послойный урон](DAMAGE.ru.md) |
| Контакты кругов и поиск столкновений снарядов | [Физика](PHYSICS.ru.md) |
| Бесконечные угловые прибытия | [Подкрепления](REINFORCEMENTS.ru.md) |

Настройки — решения реализации, а не доказательство баланса. Утверждение о производительности требует правил, конфигурации, бинарников и среды, записанных в отчёте.

## Воспроизведение бенчмарка

Выполняйте команды из корня репозитория с runtime .NET 8 и SDK с поддержкой C# 13 (9.0 или новее). [Переносимый .NET CLI](../../SpaceBattle.Benchmarks/README.ru.md) выполняет сборку Release, проверку повторов конкретных ядер против SKECS, последовательные замеры и создание двуязычных отчётов/SVG. Python и PowerShell не нужны; сборка восстанавливает NuGet-зависимости измеряемых проектов.

```sh
dotnet run --project SKECS.Benchmarks -c Release -- verify-vendor
dotnet run --project SpaceBattle.Benchmarks -c Release -- validate space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- run space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- report space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- verify space-battle
```

Текущий набор измеряет пять ECS на одной реальной игровой нагрузке: профиль `high`, режим `game`, игровой `ChurnMultiplier=2`, 108000 измеряемых тиков после отдельного прогрева на 600 тиков. Искусственный churn и диагностические пулы отсутствуют. Отчёт содержит время Step, девять стадий, окна HUD по 600 тиков и короткие начальные фазы.

Каждый runner сравнивает нулевой и каждый измеренный тик с отдельным эталонным повтором SKECS. Внешний экспорт состояния, хеширование и сравнение выполняются после остановки таймера Step и счётчика выделений на тик. Границы симуляции и проверки описаны в [архитектуре](ARCHITECTURE.ru.md).

Сырые JSON, tick CSV/gzip, метаданные и хеши сохраняются локально в игнорируемом `artifacts/benchmarks/current/space-battle`. Публикуются Markdown-отчёты и SVG в `docs/space-battle`. `report` и `verify` проверяют архивы и пересчитывают полные tick-данные через .NET; без локальных исходных данных новый клон может читать публикации, но не пересоздавать их.

Полный headless-набор SpaceBattle запускается через `run all`. Микробенчмарки ECS выполняются отдельно через [SKECS.Benchmarks](../../SKECS.Benchmarks/README.ru.md). Desktop включается явно:

```sh
dotnet run --project SpaceBattle.Benchmarks -c Release -- run desktop
dotnet run --project SpaceBattle.Benchmarks -c Release -- run all --include-desktop
dotnet run --project SpaceBattle.Benchmarks -c Release -- report all
dotnet run --project SpaceBattle.Benchmarks -c Release -- verify all
```

Desktop требует рабочего графического устройства. `report all` и `verify all` включают desktop при наличии сохранённых данных; `--include-desktop` требует их. Перед замерами завершите другие сборки, игры, рендеринг, профилировщики и браузерные проверки. Не смешивайте конфигурации и ревизии.

Для отдельного воспроизведения используйте `run space-battle --output artifacts/benchmarks/repro/space-battle`, затем `report space-battle --data artifacts/benchmarks/repro/space-battle --output artifacts/benchmarks/repro/report` и `verify space-battle --data artifacts/benchmarks/repro/space-battle` с тем же префиксом `dotnet run --project SpaceBattle.Benchmarks -c Release --`. Все пути остаются внутри workspace. `--no-build` принимает только неизменённую подтверждённую сборку. Короткие `--ticks`/`--warmup` предназначены для smoke-проверок с отдельным `--output`; они не заменяют полный отчёт.

Поле `Machine` использует обезличенную метку `local-machine`; оборудование, runtime и ревизии сохраняются для интерпретации. Для разных компьютеров задавайте разные нейтральные `SKECS_BENCHMARK_MACHINE_LABEL`: значение по умолчанию не устанавливает тождество хостов.

[Создание обложки](COVER.ru.md) служит для игровых иллюстраций. Временные показатели отдельно записывает набор бенчмарков.
