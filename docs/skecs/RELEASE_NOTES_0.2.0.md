English | [Русский](RELEASE_NOTES_0.2.0.ru.md)

# SKECS 0.2.0 — Branch

SKECS 0.2.0 introduces the breaking product and public API identity change
from SECS 0.1.0. This rename happens before 1.0.0.

## Entity ownership and world lifetime

- Each bound entity handle belongs to a world. Equality and hashing include
  the owning world. Use `World.BindEntity` to reconstruct a handle from its ID
  and generation. Check liveness with `World.IsAlive`.
- Dispose worlds after queries return. Calling `Dispose` inside
  `Query.ForEach` throws `InvalidOperationException`. Operations that need a
  live world throw `ObjectDisposedException` after disposal.

## Migration

The [SECS 0.1.0 migration guide](MIGRATION.md) covers automated preview and
apply, remaining manual steps, and validation. The
[SECS 0.1.0 release](https://github.com/ShortKedr/skecs/releases/tag/secs-v0.1.0)
still provides its original artifacts for projects staying on that API.

## Verified runtimes

The package and Unity ZIP passed consumer checks on Windows 11. The release
source is commit [`98bf6b2`](https://github.com/ShortKedr/skecs/commit/98bf6b22ac8d9a122ce1baba8201ccad02c51465).

| Consumer | Verified runtime | Configuration |
| --- | --- | --- |
| .NET 8 | .NET 8.0.34 | .NET SDK 8.0.428 |
| .NET 10 | .NET 10.0.8 | .NET SDK 10.0.204 |
| .NET Framework (`net48`) | .NET Framework 4.8.1 | .NET SDK 8.0.428, Windows registry release 533509 |
| Godot .NET | Godot 4.5.stable.mono.official.876b29033 | .NET SDK 8.0.428, desktop |
| Unity 2022.3 LTS | 2022.3.60f1 | Editor, Windows Mono Player, Windows IL2CPP Player |
| Unity 6 | 6000.3.19f1 | Editor, Windows Mono Player, Windows IL2CPP Player |

Download the package, Unity ZIP, checksums, and verification report from the
[0.2.0 release](https://github.com/ShortKedr/skecs/releases/tag/skecs-v0.2.0).
Follow the [installation guide](INSTALL.md) to get started.
