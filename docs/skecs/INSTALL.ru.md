[English](INSTALL.md) | Русский

# Установка SKECS 0.2.0 Branch

Скачайте [`ShortKedr.SKECS.0.2.0.nupkg`](https://github.com/ShortKedr/skecs/releases/download/skecs-v0.2.0/ShortKedr.SKECS.0.2.0.nupkg)
из [релиза SKECS 0.2.0 Branch](https://github.com/ShortKedr/skecs/releases/tag/skecs-v0.2.0) и сохраните
его рядом с проектом для повторяемого восстановления пакетов. При обновлении с
SECS 0.1.0 см. [руководство по миграции](MIGRATION.ru.md) и
[примечания к релизу](RELEASE_NOTES_0.2.0.ru.md).

## Приложения .NET

Для быстрой проверки создайте консольный проект с помощью установленного .NET SDK.

```powershell
dotnet new console -o SkecsQuickstart
cd SkecsQuickstart
```

Поместите скачанный `ShortKedr.SKECS.0.2.0.nupkg` в каталог `packages/` внутри
проекта. Сохраните эту конфигурацию в файле `NuGet.Config` рядом с файлом проекта.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/>
  <add key="release" value="./packages" />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources></configuration>
```

Затем добавьте пакет:

```powershell
dotnet add package ShortKedr.SKECS --version 0.2.0
```

Используемая .NET Framework 4.8 сборка `netstandard2.0` зависит от
`System.Memory`. NuGet.org предоставляет эту зависимость, если её нет в
локальном офлайн-источнике.

Перед выбором целевой среды выполните [проверки потребителей](../../tools/release-consumers) для пакета SKECS 0.2.0.
Точные версии сред, проверенных с этим выпуском, указаны в
[примечаниях к релизу](RELEASE_NOTES_0.2.0.ru.md#проверенные-среды-выполнения).
Актуальные сроки поддержки приведены в [политике поддержки .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

## Unity

Скачайте [`SKECS.Unity.0.2.0.zip`](https://github.com/ShortKedr/skecs/releases/download/skecs-v0.2.0/SKECS.Unity.0.2.0.zip).
Архив содержит ту же сборку `netstandard2.1`, что и пакет NuGet. В проекте Unity 2022.3 LTS или Unity 6 выберите
профиль совместимости **.NET Standard 2.1**. Скопируйте все DLL из
`lib/netstandard2.1/` ZIP в `Assets/Plugins/` и дождитесь импорта. В ZIP также
входят лицензия MIT и инструкция по установке. Подключите пространство имён `SKECS` в
C#-скрипте. Базовые `World`,
`Query<T>().ForEach` и `Get<T>` не требуют
включения `unsafe` в проекте-потребителе. Для прямого доступа к указателям
чанков нужны `unsafe` и соответствующие настройки компиляции. Освобождайте
каждый `World`: он владеет нативной памятью.

Проверяйте Editor и Windows Player с Mono и IL2CPP отдельно через release-consumer harness. Экспорты WebGL и мобильных платформ требуют отдельной проверки.

## Godot .NET (C#)

Используйте Godot **4.5 .NET** на компьютере. Поместите `.nupkg` в локальный
источник и добавьте ссылку на пакет в C#-проект:

```xml
<ItemGroup>
  <PackageReference Include="ShortKedr.SKECS" Version="0.2.0" />
</ItemGroup>
```

Используйте тот же `NuGet.Config` из раздела выше, чтобы восстановление
находило локальный пакет SKECS и SDK Godot на NuGet.org.

```powershell
dotnet restore YourGodotProject.csproj --configfile NuGet.Config
```

Используйте `SKECS` в C#-узле или другом управляемом классе. Локальный
[проверочный Godot-проект](../../tools/release-consumers/Godot) проверяет сцену с установленным пакетом. Экспорты Godot C# Web и мобильных платформ требуют отдельной проверки.

## Минимальная проверка API

Проверочные проекты создают сущность, запускают запрос, читают изменённый
компонент и освобождают мир:

<!-- docs-example -->
```csharp
using SKECS;

using (var world = new World())
{
    var entity = world.CreateEntity(new Position { X = 2 });
    var processor = new Increment();
    world.Query<Position>().ForEach(ref processor);
    if (world.Get<Position>(entity).X != 3)
        throw new System.Exception("SKECS query failed");
}

struct Position { public int X; }
struct Increment : IQueryProcessor<Position>
{
    public void Execute(Entity entity, ref Position position) => position.X++;
}
```

Для быстрой проверки вставьте код в `Program.cs` и выполните
`dotnet run -c Release`. Проверка завершится успешно, если запрос увеличит
позицию с `2` до `3`.

Компоненты SKECS должны быть unmanaged-структурами. `World` не поддерживает
одновременный доступ из нескольких потоков; структурные изменения и
освобождение мира инвалидируют ссылки на компоненты и указатели чанков.
Подробности — в [руководстве по API](README.ru.md).
