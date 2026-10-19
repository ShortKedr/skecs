[English](README.md) | Русский

# Поддерживаемые инструменты

Поддерживаемый набор обслуживает CI, запуск и анализ текущих бенчмарков либо документированный программный harness проверки. Тесты и импортируемые помощники относятся к тому же набору, что их точка входа. Фильтр путей workflow сам по себе не означает использование инструмента.

## Точки входа

| Назначение | Точка входа | Потребитель |
| --- | --- | --- |
| Генерация бенчмарков, измерения и отчёты | [SKECS.Benchmarks](../SKECS.Benchmarks/README.ru.md), [SpaceBattle.Benchmarks](../SpaceBattle.Benchmarks/README.ru.md), [общая поддержка](BenchmarkSupport/README.ru.md), [тесты](BenchmarkSupport.Tests) | [CI библиотеки](../.github/workflows/skecs-ci.yml), CLI каждого проекта |
| Переносимые метаданные измерений | [BenchmarkMachineInfo.cs](BenchmarkMachineInfo.cs), [BenchmarkMachineLabel.cs](BenchmarkMachineLabel.cs) | Проекты бенчмарков и игры |
| Проверка двуязычной документации | [check_docs.py](check_docs.py), [тесты](test_check_docs.py) | CI библиотеки и Pages |
| Проверка пакетов и релизов | [release](release), [consumer fixtures](release-consumers) | CI библиотеки/релизов и harness проверки релиза |
| Сборка и упаковка Web | [build-space-battle-web.ps1](build-space-battle-web.ps1), [Web-зависимости](web/package.json) | [Pages CI](../.github/workflows/space-battle-pages.yml), сборки обложки и сцен |
| Browser smoke и диагностика iOS | [check-web.mjs](web/check-web.mjs), [run-ios-smoke.mjs](web/run-ios-smoke.mjs) | Pages CI |
| Снимок и проверка обложки | [cover.py](space-battle/cover.py) | [CI обложки](../.github/workflows/space-battle-cover.yml) |
| Поиск и запись сцен | [Инструменты сцен](space-battle/scenes/README.ru.md) | Программный harness записи; unit-тесты выполняются в Pages CI |
| Профилирование браузера | [profile-web.mjs](web/profile-web.mjs) | [Руководство разработки Web](../SpaceBattle.Web/DEVELOPMENT.ru.md#метрики-и-воспроизведение) |
| Анализ Mono compiler | [Инструменты Mono](web/README.ru.md) | Тесты Pages CI и браузерный анализ |

К зависимостям Web-сборки относятся `prepare-web-atlases.mjs`, `finalize-web-site.mjs`, проверки scalar-WASM, runtime-тесты и их тесты. Снимок обложки также использует `web/capture-cover.mjs`. Инструментам сцен нужны обёртки `space-battle/prepare-scenes.ps1`, `find-scenes.ps1` и `capture-scene.ps1`, каталог `space-battle/scenes/` и закреплённые Playwright/pngjs из `web/package*.json`.

Mono CI охватывает модули и тесты compiler-study, CPU-profile, build-audit и summary, а также PowerShell-скрипты сборки/кеша.

## Программный harness проверки

Отдельной конфигурации AI harness в репозитории нет. Поддерживаемый harness состоит из документированных проверок браузера, сцен и ассетов ниже и их общих помощников. Эти проверки валидируют текущее поведение приложения; они не измеряют производительность ECS.

| Контракт | Проверки |
| --- | --- |
| Вооружение и тяжёлые попадания | `web/check-armament.mjs`, `web/check-heavy-cadence.mjs`, `web/check-heavy-impact.mjs` |
| Развёртывание и desktop-рендеринг | `web/check-deployment.mjs`, `web/check-desktop-scene.mjs` |
| Эффекты и подкрепления | `web/check-effects.mjs`, `web/check-effect-contrast.mjs`, `web/check-warp-reinforcements.mjs`, `web/effect-contrast-metrics.mjs` |
| Пиксели исходных атласов | `space-battle/test_effect_atlas.py`, `space-battle/test_gun_atlas.py`, `space-battle/test_warp_atlas.py` |
| Контракты frontend-модулей | `web/test-collider-debug.mjs`, `web/test-observer-camera.mjs`, `web/test-effect-contrast.mjs` |
| Проверки ассетов/физики в браузере | `web/physics-study.mjs --verify`, `web/physics-study.mjs --validate-assets-live` |

Контракты описаны в [руководстве ассетов](../SpaceBattle.Game/Assets/PROVENANCE.ru.md), [вооружения](../docs/space-battle/ARMAMENT.ru.md), [развёртывания](../docs/space-battle/DEPLOYMENT.ru.md), [подкреплений](../docs/space-battle/REINFORCEMENTS.ru.md) и [разработки desktop](../SpaceBattle.Game/DEVELOPMENT.ru.md). Тесты frontend-модулей импортируют production JavaScript напрямую. Python-проверкам атласов нужен Pillow; браузерные проверки используют закреплённые зависимости `web`. Сравнение сборок требует соответствующих локальных сборок и настроек нагрузки.

Текущие benchmark CLI сохраняют промежуточные данные только в игнорируемом `artifacts/`; в репозиторий входят отчёты, графики и сводные таблицы.
