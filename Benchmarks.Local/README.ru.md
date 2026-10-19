[English](README.md) | Русский

# Локальные сравнения ECS

Эта папка — часть публичного репозитория. Здесь находятся воспроизводимый
раннер сравнения пяти ECS, адаптеры и опубликованные отчёты. `Local`
относится к запуску бенчмарков на машине; внутренние заметки по разработке
хранятся в `../skecs-private/`.

[Репозиторий](../README.ru.md) | [Отчёт о бенчмарках](../SKECS.Benchmarks/README.ru.md) | [Отчёт SpaceBattle](../docs/space-battle/REPORT.ru.md) | [Уведомления о сторонних компонентах](../THIRD_PARTY_NOTICES.ru.md)

Здесь находятся сравнительный Stopwatch-раннер пяти фреймворков, адаптеры и стресс-бэкенды. Переносимые [.NET-инструменты](../tools/BenchmarkTools/README.ru.md) выполняют сборку, проверки, замеры и публикацию отчётов. Наборы BenchmarkDotNet расположены отдельно в `SKECS.Benchmarks` и `SKECS.OtherBenchmarks` и измеряют SKECS, а не все пять фреймворков.

## Каталог исходников

| Фреймворк | Реализация базовой нагрузки | Стресс-реализация | Нативное хранилище SpaceBattle | Исходники runtime |
| --- | --- | --- | --- | --- |
| SKECS | [SkecsWorkload](Adapters.cs), обёртка [ComparisonWorkload](../benchmarks/SKECS.Benchmarks.Workloads/ComparisonWorkload.cs) | [SkecsStressBackend](../benchmarks/SKECS.Benchmarks.Workloads/SkecsStressBackend.cs) | [SkecsStore](../SpaceBattle.SKECS/SkecsStore.cs) | [SKECS](../SKECS/) |
| LeoECS Lite | [LiteWorkload](Adapters.cs) | [LiteStressBackend](StressAdapters.cs) | [LeoEcsLiteStore](../SpaceBattle.Adapters/LeoEcsLiteStore.cs) | [ecslite/src](vendor/ecslite/src/) |
| LeoECS Classic | [ClassicWorkload](Adapters.cs) | [ClassicStressBackend](StressAdapters.cs) | [LeoEcsClassicStore](../SpaceBattle.Adapters/LeoEcsClassicStore.cs) | [ecs/src](vendor/ecs/src/) |
| Morpeh | [MorpehWorkload](Adapters.cs) | [MorpehStressBackend](StressAdapters.cs) | [MorpehStore](../SpaceBattle.Adapters/MorpehStore.cs) | [Morpeh Core](vendor/morpeh/Scellecs.Morpeh/Core/) |
| StaticEcs | [StaticWorkload](StaticAdapters.cs) | [StaticStressBackend](StaticAdapters.cs) | [StaticEcsStore](../SpaceBattle.Adapters/StaticEcsStore.cs) | [StaticEcs/Src](vendor/StaticEcs/Src/), [StaticPack/Src](vendor/StaticPack/Src/) |

Проекты сборки: [Comparison.csproj](Comparison.csproj), [StaticLibrary.csproj](StaticLibrary.csproj), [MorpehLibrary.csproj](MorpehLibrary.csproj) и [SpaceBattle.Adapters.csproj](../SpaceBattle.Adapters/SpaceBattle.Adapters.csproj). StaticPack обслуживает StaticEcs и не является шестым сравниваемым фреймворком. Хранилища SpaceBattle используют общий контракт [IEcsStore](../SpaceBattle.Core/EcsContract.cs) и слой идентичности [NativeStore](../SpaceBattle.Core/NativeStore.cs); см. [контракт адаптеров](../SpaceBattle.Adapters/README.ru.md).

[Манифест vendor](vendor-manifest.json) фиксирует исходники; `dotnet run --project tools/BenchmarkTools -c Release -- verify-vendor` проверяет распространяемый состав. Поставка содержит подмножества исходников runtime и точные upstream-лицензии, но не upstream-документацию/ассеты, бинарные NuGet-кэши или результаты сборки. Обычный клон уже содержит эти исходники и не требует скачивания vendor. `dotnet run --project tools/BenchmarkTools -c Release -- restore-vendor` необязательно восстанавливает отсутствующие файлы из закреплённых архивов. Реализация [VendorRestore.cs](../tools/BenchmarkTools/VendorRestore.cs) проверяет размеры и хеши до установки и отказывается перезаписывать изменённые локальные исходники; загрузка требует сети.

Лицензии не являются единообразно неограниченными MIT: LeoECS Classic предлагает проприетарные условия или MIT-Red, LeoECS Lite использует MIT-Red, Morpeh, StaticEcs и StaticPack используют MIT. Перед распространением прочтите [лицензионный реестр](../THIRD_PARTY_NOTICES.ru.md) и точные лицензии [Classic](vendor/ecs/LICENSE.md), [Lite](vendor/ecslite/LICENSE.md), [Morpeh](vendor/morpeh/LICENSE.md), [StaticEcs](vendor/StaticEcs/LICENSE.md), [StaticPack](vendor/StaticPack/LICENSE.md). Этот каталог не является юридической гарантией.

## Общие нагрузки

Библиотека .NET 8 [SKECS.Benchmarks.Workloads](../benchmarks/SKECS.Benchmarks.Workloads/SKECS.Benchmarks.Workloads.csproj) предоставляет `ComparisonWorkload`, `StressContract`, `SkecsStressBackend`, `StressWorkload` и сгенерированный каталог сценариев раннеру и BenchmarkDotNet. Оба используют ссылки на проект.

[Program.cs](Program.cs) регистрирует все пять базовых адаптеров. На 1 000 и 100 000 сущностей выполняются Create, Iterate, Churn и Destroy: Position/Velocity содержат по два float; движение прибавляет скорость к позиции; churn добавляет и затем удаляет Mass с одним float.

Перед замерами проверяются все сущности. Подготовка, заполнение мира, его освобождение и принудительный GC не измеряются; commit Morpeh включён. Каждый случай прогревается минимум 400 мс и сохраняет 20 выборок. Create/Destroy выполняют один свежий пакет на выборку; Iterate/Churn стремятся к 30 мс, максимум 1 000 повторов.

[StressRunner.cs](StressRunner.cs), [StressContract.cs](../benchmarks/SKECS.Benchmarks.Workloads/StressContract.cs) и [StressWorkload.cs](../benchmarks/SKECS.Benchmarks.Workloads/StressWorkload.cs) используют одинаковые начальные данные, маски состава и порядки операций с фиксированным seed для всех пяти бэкендов. Обычная матрица: 100 000/1 000 000 сущностей, семь нагрузок, восемь выборок на случай, прогрев минимум 300 мс, до 16 повторов с целевыми 30 мс. MixedFrames выполняется один раз на выборку.

| Стресс-сценарий | Одна операция |
| --- | --- |
| DenseIteration | Перемещение всех сущностей во всех заполненных составах |
| SparseIteration | Перемещение выборки 1% с Hot |
| WideIteration | Перемещение и изменение первого/последнего полей 256-байтного компонента |
| RandomLookup | Чтение позиций в перемешанном порядке с фиксированным seed и контрольной суммой |
| PayloadChurn | Добавление и удаление 256-байтного компонента в перемешанном порядке |
| Recycle10Percent | Удаление/пересоздание 10% с восстановлением логических данных и состава |
| MixedFrames | 120 кадров: полное движение, 5% churn, 10% чтений и 1% пересозданий в каждом |

Стресс использует 256 базовых масок из восьми необязательных четырёхбайтных компонентов, 12-байтные Position/Velocity и четырёхбайтный Hot на каждой сотой сущности. Промежуточные переходы и Hot могут создавать дополнительные архетипы. MixedFrames повторяет фиксированные подмножества, а не равномерную случайную замену всей популяции.

RNG, сортировка, подготовка, проверки, освобождение и принудительный GC не измеряются. Естественный GC, вызванный измеряемыми операциями, включён.

Одинаковая логическая работа не означает одинаковую раскладку памяти, запросы или оптимальные API.

Поля аллокаций измеряют управляемые выделения в измеряемом потоке. Они исключают нативное хранилище и не отражают полный объём памяти. Погрешности на графиках показывают выборочное стандартное отклонение, не доверительные интервалы. Результаты относятся к записанной машине/runtime/ревизиям, а не к Unity/IL2CPP или универсальному рейтингу. Эксперименты с оптимизированным созданием/accessor SKECS выделены отдельно и не заменяют сравнение общих API.

Поле `machine` в JSON содержит обезличенную метку `local-machine`. Данные о
процессоре, ОС, runtime, ревизиях исходников и условиях замера сохранены для
интерпретации результатов без публикации имени компьютера.
При объединении замеров с разных компьютеров задавайте для каждого отдельную
нейтральную метку через `SKECS_BENCHMARK_MACHINE_LABEL`: значение по умолчанию
само по себе не доказывает, что запуски выполнены на одном хосте.

## Текущая кампания

[Текущий отчёт](../docs/benchmarks/current/REPORT.ru.md) содержит полную базовую/стресс-матрицу пяти ECS и SVG-графики. [Дополнение](../docs/benchmarks/current/SUPPLEMENT.ru.md) покрывает все стандартные и legacy-кейсы BenchmarkDotNet SKECS и диагностику API/storage. Машинные JSON, отдельные выборки, tick CSV/gzip и экспорты BenchmarkDotNet хранятся в игнорируемом `artifacts/benchmarks/current/`; Markdown и SVG публикуются в `docs/`.

## Воспроизведение

Запускайте из корня репозитория с runtime .NET 8 и SDK, поддерживающим C# 13 (9.0 или новее). Сам инструмент рассчитан на .NET 8 и использует только стандартную библиотеку. Python, PowerShell, matplotlib и NumPy не нужны. Сборка измеряемых проектов восстанавливает объявленные NuGet-зависимости; для offline-сборки пакеты должны быть в кеше.

```sh
dotnet run --project tools/BenchmarkTools -c Release -- generate --check
dotnet run --project tools/BenchmarkTools -c Release -- verify-vendor
dotnet run --project tools/BenchmarkTools -c Release -- validate all
dotnet run --project tools/BenchmarkTools -c Release -- run all
```

`run all` собирает Release, проверяет контракты и последовательно запускает сравнение пяти ECS, полный headless SpaceBattle и дополнительные наборы. После каждого набора создаются двуязычные отчёты и SVG. Добавьте `--include-desktop` для сопоставленного исследования старта desktop или выберите `run desktop`; desktop-замеру требуется рабочее графическое устройство. Перед замерами завершите другие сборки, бенчмарки, игры и профилировщики.

```sh
dotnet run --project tools/BenchmarkTools -c Release -- run all --include-desktop
dotnet run --project tools/BenchmarkTools -c Release -- report all
dotnet run --project tools/BenchmarkTools -c Release -- verify all
```

`report all` и `verify all` включают desktop при наличии локальных данных; `--include-desktop` требует их.

`report` заново создаёт публикации из локальных исходных данных; `verify` проверяет их без изменения отчётов. Обе команды отклоняют неполные и несогласованные данные. Инструменты сохраняют хеши исходников, сборок и входных файлов, настраивают локальные каталоги .NET/NuGet/temp. `--no-build` требует неизменённой сборки, подтверждённой предыдущим запуском инструмента.

Отдельные наборы: `run comparison`, `run supplement`, `run space-battle`. Для отдельного воспроизведения задайте `--output`, затем читайте сохранённые данные через `--data`:

```sh
dotnet run --project tools/BenchmarkTools -c Release -- run comparison --output artifacts/benchmarks/repro/micro
dotnet run --project tools/BenchmarkTools -c Release -- report comparison --data artifacts/benchmarks/repro/micro --output artifacts/benchmarks/repro/report
dotnet run --project tools/BenchmarkTools -c Release -- verify comparison --data artifacts/benchmarks/repro/micro
```

При `run all --output <root>` данные находятся в `<root>/micro`, `<root>/space-battle` и, если запрошен, `<root>/desktop`, а отчёты — в `reports/` внутри каждого набора. `report all --data <root> --output <published-root>` читает эти подкаталоги и создаёт соответствующие каталоги публикаций. Сырые данные `run --output` должны находиться в игнорируемом `artifacts/`; публикации `report --output` могут находиться в любом каталоге внутри workspace. Значения по умолчанию и параметры описаны в [руководстве CLI](../tools/BenchmarkTools/README.ru.md).

Команда `hardware --output artifacts/benchmarks/repro/hardware.json` сохраняет переносимые сведения о CPU/OS и доступном лимите памяти GC, а не физической RAM. `report composition` и `verify composition` обрабатывают сохранённую диагностику состава сущностей отдельно от замеров; полные команды и пути — в [руководстве CLI](../tools/BenchmarkTools/README.ru.md).

Общие исходники создаются из [модели](CodeGen/benchmark-model.json) и шаблонов нативных API в [CodeGen](CodeGen/README.ru.md). После изменения входных файлов выполните `dotnet run --project tools/BenchmarkTools -c Release -- generate`. `generate --check` принимает LF и CRLF, обнаруживает устаревшие файлы без записи и выполняется перед замерами. Сгенерированные циклы сохраняют нативные запросы и API фреймворков.

Базовая проверка проверяет данные и жизненный цикл при обоих размерах популяции. Стресс-проверка использует 257/4,097 сущностей, все семь сценариев по три повтора, добавление/удаление payload и устаревшие handles. Измеряемые стресс-кейсы проверяют всю популяцию после прогрева и выборок. Это проверки контракта нагрузки/адаптера, а не полные upstream-наборы тестов.

## Сохранённые эксперименты

[Методология оптимизации](../docs/benchmarks/optimization-methodology.ru.md) объясняет сохранённые baseline/optimized-бинарники и парные исторические данные. Прежние прогоны и графики сохраняют собственные метаданные и границы применимости; они не описывают текущий рейтинг. Не смешивайте наблюдения из разных кампаний и не используйте Dry/smoke как полный рейтинг.

Отдельные нагрузки описаны в руководствах [BenchmarkDotNet](../SKECS.Benchmarks/README.ru.md) и [SpaceBattle](../docs/space-battle/README.ru.md).
