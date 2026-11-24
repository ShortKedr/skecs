[English](RELEASE_NOTES_0.2.0.md) | Русский

# SKECS 0.2.0 — Branch

В SKECS 0.2.0 несовместимо изменены имя продукта и идентификаторы публичного
API относительно SECS 0.1.0. Переименование происходит до версии 1.0.0.

## Владение сущностями и время жизни мира

- Дескриптор сущности принадлежит конкретному миру. Сравнение и хеширование учитывают мир-владелец.
  Для восстановления дескриптора из ID и поколения используйте `World.BindEntity`.
  Проверяйте существование сущности через `World.IsAlive`.
- Освобождайте мир после завершения запросов. Вызов `Dispose` внутри
  `Query.ForEach` выбрасывает `InvalidOperationException`. После освобождения
  операции, которым нужен рабочий мир, выбрасывают `ObjectDisposedException`.

## Миграция

В [руководстве по миграции с SECS 0.1.0](MIGRATION.ru.md) описаны
автоматический предварительный просмотр, применение изменений и ручные шаги.
Отдельный [релиз SECS 0.1.0](https://github.com/ShortKedr/skecs/releases/tag/secs-v0.1.0)
содержит исходные материалы для проектов, продолжающих использовать старый API.

## Проверенные среды выполнения

Проверки пакета и Unity ZIP прошли в Windows 11 на исходном коммите
[`98bf6b2`](https://github.com/ShortKedr/skecs/commit/98bf6b22ac8d9a122ce1baba8201ccad02c51465).

| Потребитель | Проверенная среда | Конфигурация |
| --- | --- | --- |
| .NET 8 | .NET 8.0.34 | .NET SDK 8.0.428 |
| .NET 10 | .NET 10.0.8 | .NET SDK 10.0.204 |
| .NET Framework (`net48`) | .NET Framework 4.8.1 | .NET SDK 8.0.428, номер выпуска в реестре Windows 533509 |
| Godot .NET | Godot 4.5.stable.mono.official.876b29033 | .NET SDK 8.0.428, настольный редактор |
| Unity 2022.3 LTS | 2022.3.60f1 | Editor, Windows Mono Player, Windows IL2CPP Player |
| Unity 6 | 6000.3.19f1 | Editor, Windows Mono Player, Windows IL2CPP Player |

Скачайте пакет, Unity ZIP, контрольные суммы и отчёт проверки на
[странице релиза 0.2.0](https://github.com/ShortKedr/skecs/releases/tag/skecs-v0.2.0).
Подключение описано в [руководстве по установке](INSTALL.ru.md).
