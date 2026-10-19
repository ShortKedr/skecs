[English](README.md) | Русский

# Генерация исходников бенчмарков

Запускайте из корня рабочего пространства:

```sh
dotnet run --project SKECS.Benchmarks -c Release -- generate
dotnet run --project SKECS.Benchmarks -c Release -- generate --check
dotnet test tools/BenchmarkSupport.Tests/BenchmarkSupport.Tests.csproj -c Release
```

[Генератор .NET 8](../Analysis/BenchmarkCodeGenerator.cs) использует только стандартную библиотеку. `benchmark-model.json` задаёт реестр ECS, операции базовых и стрессовых сценариев и число битов состава компонентов. `Templates/` содержит реализации на нативных API каждой ECS. Меняйте эти входные файлы, запускайте генерацию и проверяйте полученный C#.

`--check` проверяет содержимое UTF-8, допускает LF или CRLF при checkout и завершается ошибкой для отсутствующих либо устаревших файлов без записи. Генерация всегда записывает UTF-8 без BOM с LF. Сгенерированный C# хранится в репозитории; обычная сборка не запускает генератор.

Восемь шаблонов, реестр и каталог сценариев образуют десять файлов. Общие `ComparisonWorkload`, `StressContract`, `SkecsStressBackend` и `BenchmarkModel.g.cs` находятся в [Workloads](../Workloads/) рядом с написанным вручную `StressWorkload`. Comparison runner и BenchmarkDotNet компилируют их один раз внутри общего исполняемого проекта. Остальные выходные файлы находятся в `SKECS.Benchmarks/Comparison` и `SKECS.Benchmarks/Suites/Standard`.

Генератор разворачивает инициализацию и движение position/velocity, стрессовые теги, регистрацию pool/stash, создание и проверку составов компонентов, фабрики ECS, списки сценариев и точки входа BenchmarkDotNet. Нативные query processors, доступ к pool, отложенные commit, пакетные операции и семантика entity handles остаются в шаблонах соответствующих ECS. Генерация не добавляет диспетчеризацию внутри измеряемых циклов.

Шаблон StaticEcs использует предоставленные автором API микробенчмарков: пакетное создание, типизированные read/write processors, отключённый lifecycle данных компонентов, пакетные churn/destruction, теги и hot entity type. Предоставленный `SpaceBattle.Adapters/StaticAdapters.cs` сохранён и исключён из компиляции того проекта; его интеграция для микробенчмарков генерируется в `SKECS.Benchmarks/Comparison/StaticAdapters.cs`.
