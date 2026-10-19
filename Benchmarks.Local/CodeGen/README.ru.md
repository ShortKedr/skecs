[English](README.md) | Русский

# Генерация исходников бенчмарков

Запускайте из корня рабочего пространства:

```sh
dotnet run --project tools/BenchmarkTools -c Release -- generate
dotnet run --project tools/BenchmarkTools -c Release -- generate --check
dotnet test tools/BenchmarkTools.Tests/BenchmarkTools.Tests.csproj -c Release
```

[Генератор .NET 8](../../tools/BenchmarkTools/BenchmarkCodeGenerator.cs) использует только стандартную библиотеку. `benchmark-model.json` задаёт реестр ECS, операции базовых и стрессовых сценариев и число битов состава компонентов. `Templates/` содержит реализации на нативных API каждой ECS. Меняйте эти входные файлы, запускайте генерацию и проверяйте полученный C#.

`--check` проверяет содержимое UTF-8, допускает LF или CRLF при checkout и завершается ошибкой для отсутствующих либо устаревших файлов без записи. Генерация всегда записывает UTF-8 без BOM с LF. Сгенерированный C# хранится в репозитории; обычная сборка не запускает генератор.

Восемь шаблонов, реестр и каталог сценариев образуют десять файлов. Общие `ComparisonWorkload`, `StressContract`, `SkecsStressBackend` и `BenchmarkModel.g.cs` находятся в [SKECS.Benchmarks.Workloads](../../benchmarks/SKECS.Benchmarks.Workloads/SKECS.Benchmarks.Workloads.csproj) рядом с написанным вручную `StressWorkload`. Исполняемый comparison harness и BenchmarkDotNet ссылаются на эту библиотеку .NET 8. Остальные выходные файлы находятся в `Benchmarks.Local` и `SKECS.Benchmarks`.

Генератор разворачивает инициализацию и движение position/velocity, стрессовые теги, регистрацию pool/stash, создание и проверку составов компонентов, фабрики ECS, списки сценариев и точки входа BenchmarkDotNet. Нативные query processors, доступ к pool, отложенные commit, пакетные операции и семантика entity handles остаются в шаблонах соответствующих ECS. Генерация не добавляет диспетчеризацию внутри измеряемых циклов.

Шаблон StaticEcs использует предоставленные автором API микробенчмарков: пакетное создание, типизированные read/write processors, отключённый lifecycle данных компонентов, пакетные churn/destruction, теги и hot entity type. Предоставленный `SpaceBattle.Adapters/StaticAdapters.cs` сохранён и исключён из компиляции того проекта; его интеграция для микробенчмарков генерируется в `Benchmarks.Local/StaticAdapters.cs`.
