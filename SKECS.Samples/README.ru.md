[English](README.md) | Русский

# Примеры SKECS

Примеры проверяют модули и инструменты из текущей копии исходного кода. Выполняйте команды из корня репозитория с .NET 10 SDK и средой выполнения .NET 8. Для работы с выпущенным ядром используйте [документацию SKECS 0.2.0](https://shortkedr.github.io/skecs/0.2.0/ru/).

```sh
dotnet run --project SKECS.Samples -c Release -- list
```

Запускайте пример с помощью `dotnet run --project SKECS.Samples -c Release -- <пример>`.

| Пример | Аргумент | Ожидаемый результат |
| --- | --- | --- |
| [Ядро](Core/Sample.cs) | `core` | Через пять тиков система перемещения приводит сущность в точку `(15, 5)`. |
| [Сериализация](Serialization/Sample.cs) | `serialization` | Двоичная контрольная точка восстанавливает мир, номер тика и состояние генератора случайных чисел. Исходная и восстановленная симуляции совпадают до тика 24. |
| [Сеть](Network/Sample.cs) | `network` | Два узла Loopback применяют полный снимок на тике 0 и дельту на тике 1. Реплика достигает точки `(3, 1)`. |
| [GA](Ga/Sample.cs) | `ga` | Переносимое ядро перемещения работает на CPU и достигает точек `(15, 5)` и `(5, 6)`. |
| [Профилировщик](Profiler/Sample.cs) | `profiler` | XML-трасса содержит три кадра с четырьмя созданными, перемещёнными и удалёнными строками, без потерь событий. |
| [Профили среды выполнения](RuntimeProfiles/Sample.cs) | `runtime-profiles` | Подходящий профиль устанавливает ёмкость чанка 64. При несовпадении идентификатора нагрузки пример использует настройки по умолчанию. |
| [Детерминизм](Determinism/Sample.cs) | `determinism` | Два независимых запуска совпадают. Сравнение находит намеренно добавленное расхождение на шаге 1 третьей трассы. |
| [Agentic](Agentic/Sample.cs) | `agentic` | Локальный инструмент находит и запускает операцию тестирования ядра. Пример выводит код завершения и пути к JSON-результату и отчёту TRX. |

Каждый пример проверяет результат и завершает работу с ненулевым кодом при ошибке. Трассы детерминизма сохраняются в `artifacts/samples/determinism/`.

## GA на Vulkan

Режим `ga auto` демонстрирует переход на CPU при отсутствии устройства. Для запуска на GPU укажите адаптер Slang и нативный мост Vulkan. Пример использует экспериментальный профиль Vulkan и после каждого тика сравнивает считанное с GPU состояние с состоянием CPU.

На Windows x64 соберите инструменты с установленными Visual Studio C++ Tools и Vulkan SDK. Оставьте каталог `Bin` из SDK в `PATH` при запуске. [Скрипт сборки](../tools/ga/probe/build-probe.ps1)

```powershell
./tools/ga/probe/build-probe.ps1 -BuildOnly
$env:Path = "$env:VULKAN_SDK\Bin;$env:Path"
dotnet run --project SKECS.Samples -c Release -- ga vulkan artifacts/ga/probe/ga-slang-probe.exe artifacts/ga/probe/ga-vulkan-native.dll artifacts/samples/ga
```

## Экспорт трассы профилировщика

Передайте `--output <новый-файл.xml>`. Родительский каталог должен существовать. Команда создаёт новый файл.

```sh
dotnet run --project SKECS.Samples -c Release -- profiler --output artifacts/sample-trace.xml
```

## Подготовка операции Agentic

Инструмент запускается локально в Git-репозитории. Восстановите зависимости тестов ядра перед первым запуском и повторите восстановление после изменения зависимостей. Команды требуют Git и работают без учётных данных для доступа к моделям.

```sh
dotnet restore SKECS.Tests/SKECS.Tests.csproj --artifacts-path artifacts/agentic-build --packages artifacts/tooling/nuget-packages
dotnet run --project SKECS.Samples -c Release -- agentic
```
