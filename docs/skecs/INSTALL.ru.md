[English](INSTALL.md) | Русский

# Установка SKECS 0.2.0 Branch

SKECS 0.2.0 Branch включает несовместимое переименование SECS 0.1.0. Здесь
описана локальная сборка пакета SKECS из корня репозитория:

```powershell
dotnet pack SKECS/SKECS.csproj -c Release -o artifacts/release
```

Полученный `ShortKedr.SKECS.0.2.0.nupkg` — локальный кандидат. Эта инструкция
использует локальный источник пакетов. Сохраните копию вместе с проектом для
повторяемого восстановления. См. [заметки к выпуску 0.2.0 Branch](RELEASE_NOTES_0.2.0.ru.md)
с шагами миграции.

## Переход с SECS 0.1.0

Опубликованный выпуск [`secs-v0.1.0` Cone](https://github.com/ShortKedr/skecs/releases/tag/secs-v0.1.0)
по-прежнему доступен с пакетом `ShortKedr.SECS`, пространством имён `SECS` и
исходными файлами выпуска. В SKECS 0.2.0 переименование несовместимое. Обновите
потребителей:

- Замените ссылку на пакет `ShortKedr.SECS` на `ShortKedr.SKECS` версии `0.2.0`.
- Замените `using SECS;` на `using SKECS;`.
- При сборке из исходников измените `SECS/SECS.csproj` на `SKECS/SKECS.csproj`.
- При прямом импорте DLL в Unity замените `SECS.dll` на `SKECS.dll`.

Старые имена пакета, пространства имён, сборки и пути проектов не являются
псевдонимами совместимости. Оставайтесь на них только при использовании
исторического выпуска 0.1.0.

## Приложения .NET

Используйте локально собранный `ShortKedr.SKECS.0.2.0.nupkg` как источник
NuGet. Поместите его, например, в каталог `packages/` и создайте рядом с
проектом формата SDK файл `NuGet.Config`:

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

Перед выбором целевой среды выполните [проверки потребителей](../../tools/release-consumers) для собранного пакета SKECS.

## Unity

Локальный [скрипт создания Unity ZIP](../../tools/release/make_unity_zip.py)
упаковывает те же DLL, что и `.nupkg`. В проекте Unity 2022.3 LTS или Unity 6 выберите
профиль совместимости **.NET Standard 2.1**. Скопируйте все DLL из
`lib/netstandard2.1/` ZIP в `Assets/Plugins/` и дождитесь импорта. В ZIP также
входят лицензия MIT и эта инструкция. Подключите пространство имён `SKECS` в
C#-скрипте. Базовые `World`,
`Query<T>().ForEach` и `Get<T>` не требуют
включения `unsafe` в проекте-потребителе. Для прямого доступа к указателям
чанков нужны `unsafe` и соответствующие настройки компиляции. Освобождайте
каждый `World`: он владеет нативной памятью.

Проверяйте Editor и Windows Player с Mono и IL2CPP отдельно через release-consumer harness. Экспорты WebGL и мобильных платформ требуют отдельной проверки.

## Godot C#

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

Компоненты SKECS должны быть unmanaged-структурами. `World` не поддерживает
одновременный доступ из нескольких потоков; структурные изменения и
освобождение мира инвалидируют ссылки на компоненты и указатели чанков.
Подробности — в [руководстве по API](README.ru.md).
