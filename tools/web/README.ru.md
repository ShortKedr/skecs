[English](README.md) | Русский

# Проверки Web и инструменты Mono

[Каталог инструментов](../README.ru.md) | [Техническое руководство Web](../../SpaceBattle.Web/DEVELOPMENT.ru.md)

Поддерживаемые инструменты собирают и проверяют Web-хост, записывают браузерные данные и анализируют вывод компилятора Mono. CI проверяет контракты инструментов; измерения производительности выполняются отдельными локальными запусками. Точки входа проверок браузера и ресурсов перечислены в каталоге инструментов.

## Сравнение компилятора Mono

`build-mono-study.ps1` фиксирует текущие исходники в новом игнорируемом каталоге и собирает изолированные варианты. Нужны отдельно установленные локальный Windows SDK и соответствующие пакеты wasm-tools. Скрипты рассчитаны на SDK 8.0.425 и runtime-пакеты 8.0.31; `prepare-mono-lto-cache.ps1` также ожидает Emscripten 3.1.34. Каталог SDK по умолчанию — `artifacts/tooling/tmp/dotnet-aot`.

| Вариант | Компиляция native C | AOT bitcode и финальная линковка |
| --- | --- | --- |
| A | -Oz | -O2 |
| B | -Oz | -O3 |
| C | -O3 | -O3 |
| D | -O3, -flto | -O3, -flto |

Это экспериментальные варианты сборки. Параметры production-публикации определяются Web-проектом и его обычным скриптом сборки.

```powershell
./tools/web/build-mono-study.ps1 -Study artifacts/tooling/tmp/my-mono-study -Variant Freeze
./tools/web/build-mono-study.ps1 -Study artifacts/tooling/tmp/my-mono-study -Variant A
./tools/web/build-mono-study.ps1 -Study artifacts/tooling/tmp/my-mono-study -Variant B
./tools/web/build-mono-study.ps1 -Study artifacts/tooling/tmp/my-mono-study -Variant C
./tools/web/prepare-mono-lto-cache.ps1 -Study artifacts/tooling/tmp/my-mono-study
./tools/web/build-mono-study.ps1 -Study artifacts/tooling/tmp/my-mono-study -Variant D -PrivateLtoCache
node tools/web/mono-compiler-study.mjs --help
node tools/web/profile-mono-cpu.mjs --help
```

Скрипты сборки и кеша отклоняют повторное использование каталогов вывода. Задайте `PLAYWRIGHT_BROWSERS_PATH` с существующим кешем браузера. Завершите сборки и остановите конкурирующие измерения до замеров.

`mono-compiler-study.mjs` принимает локальные пути сайтов, метки и соответствующие фиксированные исходники. Каждый replay использует новые процесс браузера, контекст и страницу. Отдельный прогревочный бой сбрасывается перед измеряемыми тиками. Отчёт объединяет средние независимых запусков и процентили отдельных запусков. Frontend timing включает обновление клиента, экспорт, DOM и передачу Canvas; он не измеряет GPU или ритм показа кадров. Сырые манифесты исходников, аудит команд и результаты браузера находятся в игнорируемом `artifacts/tooling/tmp`.

`audit-mono-build.py` проверяет фактические команды компиляции/линковки, response-файлы и форматы объектов. `summarize-mono-study.py` проверяет сохранённые запуски и эквивалентность. `profile-mono-cpu.mjs` классифицирует сэмплы CPU-профиля по symbol maps; доли показывают атрибуцию сэмплов, а не точные длительности стадий. Профилировщику нужна отдельно подготовленная сборка с символами.

## Проверки

```powershell
node --test tools/web/test-mono-compiler-study.mjs tools/web/test-profile-mono-cpu.mjs
python tools/web/test-audit-mono-build.py
python tools/web/test-summarize-mono-study.py
./tools/web/test-build-mono-study.ps1
```

Успешная проверка инструментов не подтверждает ускорение. Публикуйте текущие измерения после проверки исходников, эквивалентности нагрузки и полных сырых данных.
