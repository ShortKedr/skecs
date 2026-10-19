[English](README.md) | Русский

**[Посмотреть четыре промо-ролика](../promo/space-battle/README.ru.md)**

# SpaceBattle

SpaceBattle — бой флотов под управлением ИИ и реалистичная нагрузка для сравнения ECS. Desktop и браузер используют общую C#-симуляцию; headless запускает её на SKECS, LeoECS Lite, LeoECS Classic, Morpeh или StaticEcs.

## С чего начать

| Задача | Руководство |
| --- | --- |
| Посмотреть бой в браузере или запустить локально | [Web-клиент](../../SpaceBattle.Web/README.ru.md) |
| Запустить desktop и разобраться с управлением | [Desktop-клиент](../../SpaceBattle.Game/README.ru.md) |
| Прочитать сравнение единственной игровой нагрузки high | [Текущий отчёт](REPORT.ru.md) |
| Прочитать предыдущую игровую кампанию | [Предыдущий игровой отчёт](GAME_REPORT.ru.md) |
| Понять текущие стадии, компоненты и границы изменений | [Текущая архитектура](ARCHITECTURE.ru.md) |
| Понять контракт ECS-адаптеров | [Native-адаптеры](../../SpaceBattle.Adapters/README.ru.md) |
| Найти и записать реальные боевые моменты | [Поиск и запись сцен](../../tools/space-battle/scenes/README.ru.md) |
| Разобраться в прежних экспериментах | [Индекс исследований](RESEARCH.ru.md) |

## Текущий геймплей

Интерактивные клиенты начинают с 96 эскортных кораблей и трёх носителей на сторону. Корабли сражаются сами; пользователь управляет камерой, скоростью и инструментами наблюдения. Истощённые флоты получают warp-подкрепления без сброса выживших. У headless два режима: `game` использует игровой состав, расстановку и подкрепления; `benchmark` сохраняет конечный бой для исторических сравнений. Основная кампания производительности использует `game` с высокой интенсивностью боя.

| Правило | Актуальное описание |
| --- | --- |
| Состав LightSwarm | [Флоты](FLEETS.ru.md) |
| Разреженная стартовая расстановка | [Развёртывание](DEPLOYMENT.ru.md) |
| Выбор целей, малые группы и полосы подхода | [Тактика групп v3](TACTICS_V3.ru.md) |
| Главные орудия и прикреплённые турели | [Вооружение](ARMAMENT.ru.md) |
| Множители урона щиту и корпусу | [Послойный урон](DAMAGE.ru.md) |
| Контакты кругов и поиск столкновений снарядов | [Физика](PHYSICS.ru.md) |
| Бесконечные угловые прибытия | [Подкрепления](REINFORCEMENTS.ru.md) |

Настройки — решения реализации, а не доказательство баланса. Утверждение о производительности требует правил, конфигурации, бинарников и среды, записанных в отчёте.

## Воспроизведение бенчмарка

Выполняйте команды из корня репозитория с runtime .NET 8 и SDK с поддержкой C# 13 (9.0 или новее). [Переносимый .NET CLI](../../tools/BenchmarkTools/README.ru.md) выполняет сборку Release, проверку адаптеров, последовательные замеры и создание двуязычных отчётов/SVG. Python и PowerShell не нужны; сборка восстанавливает NuGet-зависимости измеряемых проектов.

```sh
dotnet run --project tools/BenchmarkTools -c Release -- generate --check
dotnet run --project tools/BenchmarkTools -c Release -- verify-vendor
dotnet run --project tools/BenchmarkTools -c Release -- validate space-battle
dotnet run --project tools/BenchmarkTools -c Release -- run space-battle
dotnet run --project tools/BenchmarkTools -c Release -- report space-battle
dotnet run --project tools/BenchmarkTools -c Release -- verify space-battle
```

Текущий набор измеряет пять ECS на одной реальной игровой нагрузке: профиль `high`, режим `game`, игровой `ChurnMultiplier=2`, 108000 измеряемых тиков после отдельного прогрева на 600 тиков. Искусственный churn и диагностические пулы отсутствуют. Отчёт содержит время Step, девять стадий, окна HUD по 600 тиков и короткие начальные фазы.

Сырые JSON, tick CSV/gzip, метаданные и хеши сохраняются локально в игнорируемом `artifacts/benchmarks/current/space-battle`. Публикуются Markdown-отчёты и SVG в `docs/space-battle`. `report` и `verify` проверяют архивы и пересчитывают полные tick-данные через .NET; без локальных исходных данных новый клон может читать публикации, но не пересоздавать их.

Полный набор без графики запускается через `run all`: сравнение пяти ECS, полный headless SpaceBattle и дополнительные микроизмерения. Desktop включается явно:

```sh
dotnet run --project tools/BenchmarkTools -c Release -- run desktop
dotnet run --project tools/BenchmarkTools -c Release -- run all --include-desktop
dotnet run --project tools/BenchmarkTools -c Release -- report all
dotnet run --project tools/BenchmarkTools -c Release -- verify all
```

Desktop требует рабочего графического устройства. `report all` и `verify all` включают desktop при наличии сохранённых данных; `--include-desktop` требует их. Перед замерами завершите другие сборки, игры, рендеринг, профилировщики и браузерные проверки. Не смешивайте конфигурации и ревизии.

Для отдельного воспроизведения используйте `run space-battle --output artifacts/benchmarks/repro/space-battle`, затем `report space-battle --data artifacts/benchmarks/repro/space-battle --output artifacts/benchmarks/repro/report` и `verify space-battle --data artifacts/benchmarks/repro/space-battle` с тем же префиксом `dotnet run --project tools/BenchmarkTools -c Release --`. Все пути остаются внутри workspace. `--no-build` принимает только неизменённую подтверждённую сборку. Короткие `--ticks`/`--warmup` предназначены для smoke-проверок с отдельным `--output`; они не заменяют полный отчёт.

Поле `Machine` использует обезличенную метку `local-machine`; оборудование, runtime и ревизии сохраняются для интерпретации. Для разных компьютеров задавайте разные нейтральные `SKECS_BENCHMARK_MACHINE_LABEL`: значение по умолчанию не устанавливает тождество хостов.

Исторические результаты сохраняют исходные метки `SECS` и хеши. [Предыдущий игровой отчёт](GAME_REPORT.ru.md) и [отчёт конечного боя](REPORT_2026-10-14_FINITE.ru.md) меняли множитель игровых правил для регулирования churn, поэтому их профили относятся к другим траекториям боя.

[Отчёт 2026-10-11](REPORT_2026-10-11.ru.md) сохраняет кампанию `predictive-neutral-v1` на Aether, её измерения и область анализа. Ссылки на исходники в нём ведут к текущим файлам; они не восстанавливают историческую реализацию. Проверенный снимок исходников и команда точного исторического прогона здесь не установлены; текущие команды запускают другие нагрузки. Измеренный состав описан в [историческом каталоге систем](SYSTEMS.ru.md). Эти результаты не подтверждают производительность текущих исходников. [Обложка](COVER.ru.md) — иллюстрация, а не замер.
