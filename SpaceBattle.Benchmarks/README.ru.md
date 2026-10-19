[English](README.md) | Русский

# Бенчмарки SpaceBattle

[Руководство SpaceBattle](../docs/space-battle/README.ru.md) | [Текущий отчёт](../docs/space-battle/REPORT.ru.md) | [Desktop-замеры](../docs/space-battle/DESKTOP_TIMING.ru.md) | [Микробенчмарки ECS](../SKECS.Benchmarks/README.ru.md)

Этот исполняемый проект .NET 8 объединяет замеры SpaceBattle, проверки симуляции и анализ сохранённых данных. Он сравнивает SKECS, LeoECS Lite, LeoECS Classic, Morpeh и StaticEcs на общей C#-симуляции. Бенчмарки только SKECS и сравнения микросценариев находятся в `SKECS.Benchmarks`.

## Воспроизведение

Запускайте из корня репозитория с runtime .NET 8 и SDK, поддерживающим C# 13 (9.0 или новее). Нативный CLI собирает Release, проверяет контракты, последовательно измеряет и создаёт двуязычные Markdown/SVG. Анализу не нужны Python, PowerShell и внешние графические библиотеки. Desktop-замеру требуется рабочее графическое устройство. Сборки восстанавливают объявленные NuGet-зависимости; offline-сборка требует кеша пакетов.

```sh
dotnet run --project SpaceBattle.Benchmarks -c Release -- validate space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- run space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- report space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- verify space-battle
```

Полная кампания использует профиль `high`, preset `game`, игровой `ChurnMultiplier=2`, 108000 измеряемых тиков после 600 отдельных прогревочных тиков, seed 7331 и 96 эскортов на сторону. Синтетический churn и диагностические пулы сущностей не добавляются. Отчёт сохраняет полные времена Step, средние девяти стадий, окна HUD по 600 тиков и начальные фазы.

`validate` проверяет контракты симуляции и адаптеров без кампании замеров. `run` собирает, проверяет, измеряет и создаёт отчёты. `report` проверяет локальные данные и заново создаёт публикации; `verify` проверяет без записи отчётов. `all` — headless-набор SpaceBattle; микробенчмарки ECS в него не входят. Перед замерами завершите другие замеры, сборки, игры, рендеринг, профилировщики и браузерные тесты.

```sh
dotnet run --project SpaceBattle.Benchmarks -c Release -- run desktop
dotnet run --project SpaceBattle.Benchmarks -c Release -- run all --include-desktop
dotnet run --project SpaceBattle.Benchmarks -c Release -- report all
dotnet run --project SpaceBattle.Benchmarks -c Release -- verify all
```

Desktop сравнивает согласованные headless и графические случаи старта на 1200 тиках. `report all` и `verify all` включают сохранённые desktop-данные при наличии; `--include-desktop` требует их. Явный `desktop` всегда требует свой набор данных.

## Данные и пути

| Набор | Локальные сырые данные по умолчанию | Публикации |
| --- | --- | --- |
| `space-battle` | `artifacts/benchmarks/current/space-battle` | `docs/space-battle/REPORT.md`, русский вариант, `current/*.svg` и итоговый CSV |
| `desktop` | `artifacts/benchmarks/current/desktop` | `docs/space-battle/DESKTOP_TIMING.md`, русский вариант и `desktop-current/*.svg` |
| `composition` | Сохранённый локальный census, переданный через `--data` | Локальные отчёты в `artifacts/benchmarks/composition-reports` |

Сырые JSON, tick CSV/gzip, манифесты и метаданные — игнорируемые локальные артефакты. Публикуются только итоговые отчёты, графики и сводные таблицы. По умолчанию проверка отчётов пересчитывает все тики и проверяет хеши данных/архивов. `--skip-ticks` пропускает пересчёт CSV, сохраняя проверки целостности архивов; для публикации используйте полную проверку. Свежий клон содержит публикации, но для их генерации нужны сохранённые сырые данные.

```sh
dotnet run --project SpaceBattle.Benchmarks -c Release -- run space-battle --output artifacts/benchmarks/repro/space-battle
dotnet run --project SpaceBattle.Benchmarks -c Release -- report space-battle --data artifacts/benchmarks/repro/space-battle --output artifacts/benchmarks/repro/report
dotnet run --project SpaceBattle.Benchmarks -c Release -- verify space-battle --data artifacts/benchmarks/repro/space-battle
```

`run --output` должен находиться внутри игнорируемого `artifacts/`; отчёты отдельного запуска находятся в его подкаталоге `reports/`. `report --output` может выбрать любой каталог workspace. Пути отдельного набора указывают прямо на него. Для `all` заданные корни содержат подкаталоги `space-battle` и необязательный `desktop`. `--root` переопределяет поиск корня. Все пути остаются внутри workspace. Кеши .NET/NuGet и временные файлы находятся в `artifacts/tooling`. `--no-build` принимает только неизменённую подтверждённую сборку.

Сокращённые `--ticks N --warmup N` требуют отдельный `--output` и создают smoke-данные, которые не являются полноценными benchmark-отчётами.

## Диагностика и реализация

```sh
dotnet run --project SpaceBattle.Benchmarks -c Release -- hardware --output artifacts/benchmarks/repro/hardware.json
dotnet run --project SpaceBattle.Benchmarks -c Release -- report composition --data artifacts/benchmarks/repro/composition
dotnet run --project SpaceBattle.Benchmarks -c Release -- verify composition --data artifacts/benchmarks/repro/composition
```

Hardware capture записывает переносимые сведения о CPU/OS и лимит доступной памяти GC, а не физическую RAM. По умолчанию выводится `artifacts/benchmarks/current/hardware.json`. Нейтральная метка машины — `local-machine`; при объединении разных компьютеров задайте разные `SKECS_BENCHMARK_MACHINE_LABEL`. Метаданные оборудования/runtime/ревизии сохраняются.

[Measurement](Measurement/) содержит headless-хост и проверки симуляции. [Analysis](Analysis/) объединяет отчёты полной кампании, desktop и состава сущностей. Composition проверяет сохранённые `low.json`, `nominal.json` и `high.json`, сохранение числа сущностей/компонентов и доступные хеши источников; эта локальная диагностика не входит в `run all`. Её вывод остаётся в игнорируемых артефактах и не относится к текущей публикации бенчмарков.

[Нативные адаптеры](../SpaceBattle.Adapters/README.ru.md) используют закреплённые runtime-исходники из ECS-проекта. Там доступны проверка vendor и необязательное восстановление отсутствующих файлов:

```sh
dotnet run --project SKECS.Benchmarks -c Release -- verify-vendor
dotnet run --project SKECS.Benchmarks -c Release -- restore-vendor
dotnet test tools/BenchmarkSupport.Tests/BenchmarkSupport.Tests.csproj -c Release
```

Восстановление читает закреплённые удалённые архивы и записывает только проверенные отсутствующие локальные файлы; изменённые локальные исходники отклоняются. Обычный клон содержит vendor и не требует восстановления. Условия лицензий — в [уведомлениях о сторонних компонентах](../THIRD_PARTY_NOTICES.ru.md).
