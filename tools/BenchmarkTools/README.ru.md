[English](README.md) | Русский

# Переносимые инструменты бенчмарков

CLI-приложение .NET 8 генерирует исходники, проверяет vendor-код и лицензии, собирает и проверяет нагрузки, последовательно выполняет замеры и создаёт двуязычные Markdown/SVG-отчёты. Инструмент использует стандартную библиотеку .NET без внешних пакетов, Python, PowerShell, системных команд управления или графических библиотек. Необязательный desktop-замер запускает графический клиент игры.

Запускайте из корня репозитория. Измеряемому решению нужны runtime .NET 8 и SDK с поддержкой C# 13 (9.0 или новее). Проекты сохраняют объявленные NuGet-зависимости; для восстановления отсутствующих в кеше пакетов нужна сеть. Обычный клон уже содержит закреплённые vendor-исходники и upstream-лицензии.

```sh
dotnet run --project tools/BenchmarkTools -c Release -- generate --check
dotnet run --project tools/BenchmarkTools -c Release -- verify-vendor
dotnet run --project tools/BenchmarkTools -c Release -- validate all
dotnet run --project tools/BenchmarkTools -c Release -- run all
dotnet run --project tools/BenchmarkTools -c Release -- report all
dotnet run --project tools/BenchmarkTools -c Release -- verify all
```

`run all` работает без графики: последовательно выполняет базовое/стресс-сравнение пяти ECS, полный SpaceBattle и дополнительные наборы SKECS. Перед замерами проверяются контракты, сохраняются хеши исходников/сборок/данных, после создаются отчёты. Добавьте `--include-desktop` или выберите `run desktop` для исследования старта desktop; требуется рабочее графическое устройство. Перед измерением завершите другие замеры, сборки, игры и профилировщики.

## Команды и наборы

| Команда | Назначение |
| --- | --- |
| `generate` | Создать десять C#-файлов из модели/шаблонов; `--check` проверяет актуальность без записи |
| `verify-vendor` | Проверить закреплённые исходники и точные лицензии по манифесту |
| `restore-vendor` | Загрузить закреплённые архивы и восстановить только отсутствующие проверенные vendor-файлы |
| `hardware` | Сохранить переносимые сведения о CPU, OS и лимите памяти |
| `validate [suite]` | Собрать Release и проверить контракты без кампании замеров |
| `run [suite]` | Собрать, проверить, последовательно измерить и создать отчёты |
| `report [suite]` | Проверить локальные данные и пересоздать Markdown/SVG |
| `verify [suite]` | Проверить локальные исходные данные без изменения публикаций |

Наборы: `all` (по умолчанию), `comparison`, `supplement`, `space-battle`, `desktop`. `report all` и `verify all` обрабатывают desktop при наличии данных; `--include-desktop` требует их. Явные `report desktop` и `verify desktop` всегда требуют свой набор данных. Отсутствующие или несогласованные запрошенные файлы вызывают ошибку; клон только с опубликованными отчётами не восстанавливает сырые данные.

| Набор | Локальные данные по умолчанию | Публикация |
| --- | --- | --- |
| `comparison`, `supplement` | `artifacts/benchmarks/current/micro` | `docs/benchmarks/current` |
| `space-battle` | `artifacts/benchmarks/current/space-battle` | `docs/space-battle/REPORT.md`, русский вариант и `current/*.svg` |
| `desktop` | `artifacts/benchmarks/current/desktop` | `docs/space-battle/DESKTOP_TIMING.md`, русский вариант и `desktop-current/*.svg` |

Машинные JSON, сырые выборки времени/аллокаций, CSV/gzip, полные экспорты BenchmarkDotNet и манифесты — игнорируемые локальные артефакты. Markdown и SVG хранятся в репозитории. Числа отчёта выводятся из проверенных данных, а не вводятся вручную.

## Пути и параметры

`run --output` задаёт каталог сырых данных внутри игнорируемого `artifacts/`; при отдельном выводе отчёты находятся в его `reports/`. `report`/`verify --data` выбирает сохранённые данные. `report --output` задаёт любой каталог публикации внутри workspace, включая `docs/`. Для одного набора путь указывает непосредственно на него; `all` использует подкаталоги `micro`, `space-battle` и необязательный `desktop` внутри заданного корня.

```sh
dotnet run --project tools/BenchmarkTools -c Release -- run all --include-desktop --output artifacts/benchmarks/repro
dotnet run --project tools/BenchmarkTools -c Release -- report all --data artifacts/benchmarks/repro --output artifacts/benchmarks/repro-published
dotnet run --project tools/BenchmarkTools -c Release -- verify all --data artifacts/benchmarks/repro
```

Все пути должны оставаться внутри workspace. `--root` заменяет автоматический поиск корня. Инструмент хранит .NET, NuGet и временные файлы внутри проекта. `--no-build` использует только подтверждённую сборку с совпадающими хешами исходников и сборок, содержащую все нужные проекты.

Smoke-параметры SpaceBattle `--ticks N --warmup N` требуют отдельного вывода; короткий запуск не является полным отчётом. `--skip-ticks` пропускает пересчёт CSV при создании/проверке отчёта, сохраняя проверку целостности архивов; для публикации используйте полную проверку по умолчанию.

Нейтральная метка машины по умолчанию — `local-machine`. Для разных компьютеров задавайте разные `SKECS_BENCHMARK_MACHINE_LABEL`. Метаданные оборудования/runtime/ревизий сохраняются.

## Восстановление и диагностика

Обычный клон не требует восстановления vendor. Если закреплённые файлы отсутствуют:

```sh
dotnet run --project tools/BenchmarkTools -c Release -- restore-vendor
dotnet run --project tools/BenchmarkTools -c Release -- verify-vendor
```

[VendorRestore.cs](VendorRestore.cs) выполняет только чтение удалённых архивов и устанавливает локальные файлы из манифеста после проверки размеров и SHA-256. Изменённые локальные исходники не перезаписываются. Требуется сеть; лицензионные условия сохраняются.

```sh
dotnet run --project tools/BenchmarkTools -c Release -- hardware --output artifacts/benchmarks/repro/hardware.json
```

[HardwareCapture.cs](HardwareCapture.cs) сохраняет нейтральную метку машины, CPU/число доступных логических процессоров, OS и доступный лимит памяти GC. Последний показатель не равен физической RAM. По умолчанию результат — `artifacts/benchmarks/current/hardware.json`; `--output` также должен находиться в `artifacts/`.

Сохранённая диагностика состава сущностей проверяется и публикуется отдельно от временных замеров; она не входит в `run all`:

```sh
dotnet run --project tools/BenchmarkTools -c Release -- report composition --data artifacts/benchmarks/history/docs/space-battle/composition --output docs/space-battle/composition
dotnet run --project tools/BenchmarkTools -c Release -- verify composition --data artifacts/benchmarks/history/docs/space-battle/composition
```

[CompositionReports.cs](CompositionReports.cs) читает `low.json`, `nominal.json` и `high.json`, проверяет сохранение численности и компонентов, а также доступные исходные CSV/результаты по хешам и агрегатам. Историческая диагностика описывает записанную ревизию, а не производительность текущего геймплея. Указанные пути являются значениями по умолчанию.

## Исходники и проверки

[Модель/шаблоны](../../Benchmarks.Local/CodeGen/README.ru.md) создают нативный код адаптеров без диспетчеризации в измеряемых циклах. [SKECS.Benchmarks.Workloads](../../benchmarks/SKECS.Benchmarks.Workloads/SKECS.Benchmarks.Workloads.csproj) — общая .NET 8-библиотека раннера сравнения и BenchmarkDotNet.

```sh
dotnet test tools/BenchmarkTools.Tests/BenchmarkTools.Tests.csproj -c Release
```

Нагрузки, ограничения интерпретации и лицензии описаны в руководствах [раннера сравнения](../../Benchmarks.Local/README.ru.md), [наборов SKECS](../../SKECS.Benchmarks/README.ru.md), [SpaceBattle](../../docs/space-battle/README.ru.md) и [уведомлениях о сторонних компонентах](../../THIRD_PARTY_NOTICES.ru.md).
