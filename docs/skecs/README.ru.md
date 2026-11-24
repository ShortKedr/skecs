[English](README.md) | Русский

# Использование SKECS 0.2.0

[Обзор проекта](../../README.ru.md) · [Карта документации](../README.ru.md)

Установите [`ShortKedr.SKECS` 0.2.0](INSTALL.ru.md) и используйте пространство
имён `SKECS`. В [исходном коде примера](../../SKECS.Sample/Program.cs) показаны
системы и типизированный обход чанков. Если вы используете SECS 0.1.0, перед
сменой пакета и пространства имён прочитайте [руководство по миграции](MIGRATION.ru.md).

## Что входит в 0.2.0

В релизный пакет входят миры и сущности, архетипное хранилище, последовательные
системы и запросы на CPU, типизированный доступ к чанкам, пакетное создание,
аксессоры компонентов и счётчики хранилища. Компоненты должны удовлетворять
ограничению C# `unmanaged`. В пакете есть сборки для .NET Standard 2.0,
.NET Standard 2.1 и .NET 8. Проверенные с этими артефактами среды выполнения
перечислены в [примечаниях к релизу](RELEASE_NOTES_0.2.0.ru.md#проверенные-среды-выполнения).

`new World()` использует ёмкость чанка 256 с выключенной диагностикой. Эти
параметры задаются через `new World(new WorldOptions(chunkCapacity: 128, diagnosticsEnabled: true))`.
Неположительная ёмкость заменяется на 256. ID типов компонентов назначаются
в порядке первого обращения отдельно в каждом мире. Для формата хранения
задайте отдельные стабильные идентификаторы компонентов.

## Минимальный запрос

<!-- docs-example: Position: (3, 1) -->
```csharp
using SKECS;

using var world = new World();
var entity = world.CreateEntity(new Position { X = 0, Y = 0 },
                                new Velocity { X = 3, Y = 1 });
var movement = new Movement();
world.Query<Position, Velocity>().ForEach(ref movement);

var position = world.Get<Position>(entity);
Console.WriteLine($"Position: ({position.X}, {position.Y})");

struct Position { public float X, Y; }
struct Velocity { public float X, Y; }
struct Movement : IQueryProcessor<Position, Velocity>
{
    public void Execute(Entity entity, ref Position position, ref Velocity velocity)
    {
        position.X += velocity.X;
        position.Y += velocity.Y;
    }
}
```

Процессор — структура, передаваемая по ссылке. Запросы с одним, двумя или тремя
типами компонентов обходят все сущности с этими типами, включая сущности с
дополнительными компонентами. Изменения через `ref` обновляют хранимые значения.
Этот пример выводит `Position: (3, 1)`.

## Сущности и компоненты

| Операция | Назначение |
| --- | --- |
| `CreateEntity()` / `DestroyEntity(entity)` | Создать пустую сущность или уничтожить живую. |
| `CreateEntity(component1, ...)` | Создать сразу с одним, двумя или тремя различными типами компонентов. |
| `Add(entity, component)` | Добавить отсутствующий компонент; существующее значение не меняется. |
| `Set(entity, component)` | Заменить значение или добавить отсутствующий компонент. |
| `Get<T>(entity)` | Получить существующий компонент по `ref`. |
| `TryGet<T>(entity, out value)` | Получить копию значения или вернуть `false`, если дескриптор недействителен или компонента нет. |
| `Has<T>(entity)` / `Remove<T>(entity)` | Проверить наличие или удалить компонент у живой сущности. |
| `IsAlive(entity)` | Проверить владельца, ID, поколение и существование сущности. |
| `BindEntity(id, version)` | Привязать координаты к этому миру перед проверкой существования сущности. |

Сохраняйте привязанный дескриптор `Entity`, полученный от мира или запроса.
Сравнение и хеширование учитывают мир-владелец. Дескрипторы из разных миров
не равны даже при совпадении ID и поколения. После уничтожения сущности её ID
может использоваться снова с новым поколением.

Чтобы восстановить дескриптор из ID и поколения строки чанка, вызовите
`world.BindEntity(id, version)` на мире, которому принадлежит чанк, а затем
проверьте `world.IsAlive(entity)`. Конструктор `Entity` с двумя аргументами
создаёт непривязанный дескриптор. `Entity.IsValid` проверяет привязку и допустимость
значений полей. Он может возвращать `true` после уничтожения сущности или
освобождения мира. Существование сущности проверяйте через `World.IsAlive`.

В действующем мире `World.IsAlive` и `World.TryGet` возвращают `false` для
чужого, непривязанного или устаревшего дескриптора. Строгие операции, например
`World.Has`, `World.Get` и `World.DestroyEntity`, выбрасывают для таких
дескрипторов `InvalidOperationException` в Debug и Release. Поведение
аксессоров описано в [руководстве по оптимизациям](../SKECS_OPTIMIZATION.ru.md#cached-random-access).

Создание, уничтожение и изменение состава компонентов применяются сразу.
Выполняйте их между обходами запросов. Запрос читает текущее хранилище;
структурные изменения внутри процессора не имеют безопасного контракта
отложенных команд или снимков.

Структурное изменение или освобождение мира делает ссылки `ref` на компоненты
и указатели чанков недействительными. Кешируйте дескрипторы и запросы, а ссылки
получайте заново. Используйте мир и его запросы, фабрики и аксессоры из одного
потока в каждый момент времени.

Освобождайте мир после завершения запросов, чтобы вернуть нативные буферы.
Вызов `Dispose` внутри `Query.ForEach` выбрасывает `InvalidOperationException`.
После освобождения операции, которым нужен действующий мир, включая
`World.IsAlive` и `World.TryGet`, выбрасывают `ObjectDisposedException`.
Повторный вызов `Dispose` безопасен.

Этот пример проверяет принадлежность и существование сущностей в двух мирах.

<!-- docs-example: Handle lifetime verified. -->
```csharp
using SKECS;

using var world = new World();
using var other = new World();
var entity = world.CreateEntity(12);
var foreign = other.CreateEntity(16);
var rebound = world.BindEntity(entity.Id, entity.Version);
var accessor = world.GetAccessor<int>();

if (entity == foreign || world.IsAlive(foreign) || accessor.Has(foreign))
    throw new InvalidOperationException("A foreign handle was accepted.");
if (rebound != entity || !world.IsAlive(rebound) || accessor.Get(rebound) != 12)
    throw new InvalidOperationException("The rebound handle should be live.");

world.DestroyEntity(entity);
if (!rebound.IsValid || world.IsAlive(rebound) || accessor.TryGet(rebound, out _))
    throw new InvalidOperationException("A destroyed entity should be absent.");

Console.WriteLine("Handle lifetime verified.");
```

## Системы и запросы

Регистрируйте `Action<World>` через `AddSystem(stage, system)`. `Run(stage)`
выполняет стадию в порядке регистрации; `RunAll()` выполняет `PreUpdate`,
`Update`, `PostUpdate` в этом порядке. Цикл тиков и входное время задаёт хост.

`QueryBuilder().With<T>().Without<U>().Build()` создаёт фильтр включения и
исключения компонентов. Его `MatchCount` считает подходящие архетипы.
Типизированные запросы предоставляют `Chunks()` с указателями на
компоненты и `Count`; для доступа нужен `unsafe`-контекст. Обходите строки
до `Count`, как в примере. Типы компонентов хранятся отдельными колонками,
но поля внутри структуры компонента сохраняют её layout.

Обход следует порядку создания архетипов, порядку чанков и текущих строк.
Удаление с перестановкой последней строки может изменить порядок строк.
API не обещает сортировку по ID сущности или независимость порядка от
истории хранилища.

## Массовое создание и произвольный доступ

Для повторного создания с двумя компонентами используйте `PrepareEntity<T1, T2>()`
и методы фабрики `Create` или `CreateBatch`. Пакетное создание присваивает
каждой сущности одинаковые входные значения. Для частого произвольного доступа
`GetAccessor<T>()` поддерживает таблицу адресов и поколений при структурных
изменениях. Обе возможности включаются явно и имеют свою цену; примеры и
диагностика хранилища описаны в [оптимизациях](../SKECS_OPTIMIZATION.ru.md).

## Развитие библиотеки

[Тесты](../../SKECS.Tests) проверяют текущий API.
[Бенчмарки](../../SKECS.Benchmarks/README.ru.md) измеряют конкретные нагрузки.
