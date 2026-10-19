[English](README.md) | Русский

# Бенчмарки SKECS

[Обзор проекта](../README.ru.md) | [Отчёт SpaceBattle](../docs/space-battle/REPORT.ru.md) | [Раннер сравнения](../Benchmarks.Local/README.ru.md) | [Переносимые инструменты](../tools/BenchmarkTools/README.ru.md)

## Текущее сравнение

[Текущий отчёт](../docs/benchmarks/current/REPORT.ru.md) сравнивает SKECS, LeoECS Lite, LeoECS Classic, Morpeh и StaticEcs: 40 базовых и 70 стресс-кейсов. Все пять фреймворков используют общий логический контракт нагрузки.

![Базовое сравнение](../docs/benchmarks/current/comparison.svg)

Create, Iterate, Churn и Destroy выполняются при 1,000 и 100,000 сущностей. Стресс включает плотную/разреженную/широкую итерацию, случайный доступ, churn 256-байтового payload, переработку 10% и смешанный пакет из 120 кадров при 100,000 и 1,000,000 сущностей. Сценарии и проверки описаны в [руководстве раннера](../Benchmarks.Local/README.ru.md).

Эти Stopwatch-замеры характеризуют записанную машину и снимки исходников. Они не устанавливают универсальный рейтинг или производительность Unity/IL2CPP. Погрешности — стандартное отклонение выборки; управляемые аллокации исключают нативную память. Сырые выборки, метаданные и хеши хранятся в игнорируемом `artifacts/benchmarks/current/micro`; Markdown и SVG публикуются в `docs/benchmarks/current`.

## Наборы BenchmarkDotNet SKECS

[ComparisonBenchmarks](ComparisonBenchmarks.cs) и [StressBenchmarks](StressBenchmarks.cs), как и раннер пяти ECS, ссылаются на .NET 8 [библиотеку общих нагрузок](../benchmarks/SKECS.Benchmarks.Workloads/SKECS.Benchmarks.Workloads.csproj). Общие [базовая нагрузка](../benchmarks/SKECS.Benchmarks.Workloads/ComparisonWorkload.cs) и [стресс-нагрузка](../benchmarks/SKECS.Benchmarks.Workloads/StressWorkload.cs) имеют одну скомпилированную реализацию.

[OptimizedApiBenchmarks](OptimizedApiBenchmarks.cs) проверяет отдельные factory/accessor API. [Program.cs](Program.cs) сохраняет исходные бенчмарки итерации/churn, а `SKECS.OtherBenchmarks` содержит legacy-набор. BenchmarkDotNet здесь измеряет SKECS; результаты оптимизированных API отделены от общего сравнения.

Запускайте из корня репозитория с runtime .NET 8 и SDK, поддерживающим C# 13 (9.0 или новее):

```sh
dotnet run --project tools/BenchmarkTools -c Release -- generate --check
dotnet run --project tools/BenchmarkTools -c Release -- verify-vendor
dotnet run --project tools/BenchmarkTools -c Release -- validate all
dotnet run --project tools/BenchmarkTools -c Release -- run supplement
dotnet run --project tools/BenchmarkTools -c Release -- report supplement
dotnet run --project tools/BenchmarkTools -c Release -- verify supplement
```

[Дополнительный отчёт](../docs/benchmarks/current/SUPPLEMENT.ru.md) покрывает 34 стандартных кейса этого проекта и 51 legacy-кейс с Short/InProcess, тремя прогревочными и восемью измеряемыми итерациями, прямое создание/аксессоры и диагностику сохранённого хранилища. Переносимый .NET-инструмент собирает и запускает их последовательно и сохраняет полные экспорты BenchmarkDotNet локально. Отчётам и SVG не нужны Python или PowerShell.

`run all` также измеряет сравнение пяти ECS и полный headless SpaceBattle. Замер старта desktop включается явно: `run desktop` или `run all --include-desktop`. `report all` и `verify all` включают сохранённые desktop-данные при их наличии; `--include-desktop` требует их.

Для отдельного вывода используйте `run supplement --output artifacts/benchmarks/repro/micro`, затем `report supplement --data artifacts/benchmarks/repro/micro --output artifacts/benchmarks/repro/report` и `verify supplement --data artifacts/benchmarks/repro/micro`. Перед каждым суффиксом укажите `dotnet run --project tools/BenchmarkTools -c Release --`. Все параметры — в [руководстве CLI](../tools/BenchmarkTools/README.ru.md).

Матрица `SKECS_STRESS_EXTREME=1` на пять миллионов сущностей остаётся отдельным необязательным режимом. Изоляция InProcess отличается от стандартного toolchain BenchmarkDotNet. Dry/smoke проверяют запуск и выполнение, но не являются полным рейтингом производительности.

## Исторические эксперименты

[Методология оптимизации 2026-10-11](../docs/benchmarks/optimization-methodology.ru.md) описывает отдельный эксперимент SECS до/после и сохранённые бинарники для его интерпретации. [Прежние графики](../docs/benchmarks/updated/comparison.png) сохраняют исторический контекст. Сырые исторические замеры являются локальными артефактами; пересборка текущих исходников не воспроизводит старый baseline-бинарник.
