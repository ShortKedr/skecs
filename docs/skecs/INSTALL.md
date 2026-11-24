English | [Русский](INSTALL.ru.md)

# Install SKECS 0.2.0 Branch

Download [`ShortKedr.SKECS.0.2.0.nupkg`](https://github.com/ShortKedr/skecs/releases/download/skecs-v0.2.0/ShortKedr.SKECS.0.2.0.nupkg)
from the [SKECS 0.2.0 Branch release](https://github.com/ShortKedr/skecs/releases/tag/skecs-v0.2.0) and keep it beside
your project for repeatable restores. To upgrade from SECS 0.1.0, see the
[migration guide](MIGRATION.md) and [release notes](RELEASE_NOTES_0.2.0.md).

## .NET applications

Use the released `ShortKedr.SKECS.0.2.0.nupkg` as a NuGet source. For an
SDK-style project, place the package in a directory such as `packages/` and put
this `NuGet.Config` beside the project:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/>
  <add key="release" value="./packages" />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources></configuration>
```

Then add the package:

```powershell
dotnet add package ShortKedr.SKECS --version 0.2.0
```

The `netstandard2.0` asset used by .NET Framework 4.8 depends on
`System.Memory`; NuGet.org supplies that dependency unless it is also available
in an offline package source.

Run the [consumer checks](../../tools/release-consumers) against the SKECS 0.2.0 package before relying on a target runtime.

## Unity

Download [`SKECS.Unity.0.2.0.zip`](https://github.com/ShortKedr/skecs/releases/download/skecs-v0.2.0/SKECS.Unity.0.2.0.zip).
Its `netstandard2.1` DLL matches the NuGet package. In a Unity 2022.3 LTS or Unity 6 project,
choose the **.NET Standard 2.1** API Compatibility Level. Copy all DLLs from
`lib/netstandard2.1/` in the ZIP into `Assets/Plugins/` and allow Unity to
reimport. The ZIP also includes the MIT license and installation notes. Use the `SKECS`
namespace in a C# script. The basic
`World`, `Query<T>().ForEach`, and `Get<T>`
API requires no `unsafe` setting in the consuming project. Direct chunk-pointer
iteration requires an `unsafe` context and project/compiler support for unsafe
code. Dispose every `World` to release its native buffers.

Use the release-consumer harness to check Editor and Windows players with Mono and IL2CPP separately. WebGL and mobile exports require their own validation.

## Godot .NET (C#)

Use Godot **4.5 .NET** on desktop. Put the `.nupkg` in a local source and add
the package to your C# project:

```xml
<ItemGroup>
  <PackageReference Include="ShortKedr.SKECS" Version="0.2.0" />
</ItemGroup>
```

Use the same `NuGet.Config` shown above so restore can find both the local SKECS
package and Godot's SDK on NuGet.org.

```powershell
dotnet restore YourGodotProject.csproj --configfile NuGet.Config
```

Use `SKECS` from a C# node or other managed class. The local
[Godot consumer fixture](../../tools/release-consumers/Godot) checks a package-backed
scene. Godot C# Web and mobile exports require separate validation.

## Minimal API check

The same check is used by the consumer fixtures: create an entity, run a query,
read its changed component, then dispose the world.

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

SKECS components must be unmanaged structs. A `World` is not thread-safe;
structural changes and world disposal invalidate component references and chunk
pointers. See the [API and lifetime guide](README.md) before using those APIs.
