[English](README.md) | Русский

**[Промо Space Battle: четыре записанных ролика](docs/promo/space-battle/README.ru.md)**

<p align="center">
  <img src="docs/branding/logo/shortkedr-ecs-200.png" width="200" height="200" alt="SKECS — белка и кедровая шишка">
</p>

# SKECS — ShortKedr ECS

Небольшая библиотека «сущность — компонент — система» (ECS) со сборками для
.NET Standard 2.0/2.1 и .NET 8. SKECS
группирует сущности по набору компонентов и хранит компоненты без управляемых ссылок в чанках.
Системы изменяют их через типизированные запросы с `ref` и выполняются по
очереди: `PreUpdate`, `Update`, `PostUpdate`. В репозитории есть тесты,
бенчмарки и автономный пример Space Battle.

## С чего начать

| Ваша задача | Куда перейти |
| --- | --- |
| Использовать библиотеку | [Документация релизов](https://shortkedr.github.io/skecs/ru/) · [Быстрый старт](#быстрый-старт) · [API и правила времени жизни](https://shortkedr.github.io/skecs/0.2.0/ru/guide/using.html) |
| Развивать SKECS | [Сборка из исходников](#сборка-из-исходников) · [Примеры модулей](SKECS.Samples/README.ru.md) · [Тесты](SKECS.Tests) |
| Прочитать или воспроизвести измерения | [Бенчмарки ECS](SKECS.Benchmarks/README.ru.md) · [Бенчмарки SpaceBattle](SpaceBattle.Benchmarks/README.ru.md) · [Отчёт Space Battle](docs/space-battle/REPORT.ru.md) |
| Запустить игру | [Desktop](SpaceBattle.Game/README.ru.md) · [Web](SpaceBattle.Web/README.ru.md) |

`SKECS.Benchmarks/` объединяет бенчмарки SKECS, сравнение пяти ECS и анализ результатов. `SpaceBattle.Benchmarks/` объединяет замеры и анализ игровой нагрузки. Сырые данные и кеши находятся в игнорируемом `artifacts/`; публикуются итоговые отчёты и графики.

## Быстрый старт

Установите `ShortKedr.SKECS` 0.2.0 из релиза на GitHub и запустите пример
библиотеки в консольном приложении .NET. В руководстве по установке также
описаны Unity и Godot. Среды выполнения, проверенные при выпуске 0.2.0,
перечислены в примечаниях к релизу. [Установить 0.2.0](https://shortkedr.github.io/skecs/0.2.0/ru/guide/installation.html) · [Запустить пример](https://shortkedr.github.io/skecs/0.2.0/ru/guide/sample.html) · [Проверенные среды](https://shortkedr.github.io/skecs/0.2.0/ru/guide/release-notes.html#проверенные-среды-выполнения)

## Сборка из исходников

Нужен .NET SDK с поддержкой .NET 8. Запускайте из корня репозитория. Эти
настройки PowerShell сохраняют кеши инструментов и пакетов внутри проекта:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/artifacts/tooling/dotnet-home"
$env:NUGET_PACKAGES = "$PWD/artifacts/tooling/nuget-packages"

dotnet run --project SKECS.Sample/SKECS.Sample.csproj -c Release
dotnet test SKECS.Tests/SKECS.Tests.csproj -c Release
```

## Space Battle

**[Открыть в браузере](https://shortkedr.github.io/skecs/demo/?lang=ru)** ·
**[Читать отчёт о бенчмарках](docs/space-battle/REPORT.ru.md)**

Автономные флоты используют управление через AI, стрельбу с упреждением,
турели, щиты, производство, стыковку и ремонт на авианосцах. У SKECS, LeoECS Lite, LeoECS Classic, Morpeh и StaticEcs
отдельные ядра симуляции и соответствующие desktop-, browser- и headless-реализации.
Headless-замеры проверяют каждый тик против отдельного эталона SKECS вне таймера
симуляции; см. [архитектуру](docs/space-battle/ARCHITECTURE.ru.md).

[![Геймплей Space Battle](docs/space-battle/cover.png)](docs/space-battle/README.ru.md)

*Измеренная нагрузка, конфигурация и версии исходников указаны в отчёте.*

## Статус и подтверждения

Ядро релиза 0.2.0 работает на CPU с последовательными системами и запросами.
Результаты бенчмарков относятся к конкретным машинам, версиям и нагрузкам;
методика и ограничения описаны в каждом отчёте.

[Лицензия: MIT для собственных материалов проекта](LICENSE) ·
[Карта документации](docs/README.ru.md) ·
[Внутренние планы (доступ по приглашению)](https://github.com/ShortKedr/skecs-private) ·
[Лицензии и закреплённые сторонние исходники](THIRD_PARTY_NOTICES.ru.md)
