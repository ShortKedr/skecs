English | [Русский](MIGRATION.ru.md)

# Migrate SECS 0.1.0 to SKECS 0.2.0

Upgrading to `ShortKedr.SKECS.0.2.0.nupkg` changes the package, namespace,
assembly and source project names. The old names are not compatibility
aliases. Projects remaining on SECS 0.1.0 should retain the original package and
its documentation.

| SECS 0.1.0 | SKECS 0.2.0 |
| --- | --- |
| `ShortKedr.SECS` version `0.1.0` | `ShortKedr.SKECS` version `0.2.0` |
| `using SECS;` | `using SKECS;` |
| `SECS.World` | `SKECS.World` |
| `SECS.dll` | `SKECS.dll` |
| `SECS/SECS.csproj` | `SKECS/SKECS.csproj` |
| `SECS.sln` | `SKECS.sln` |

## Review the automatic migration

The local [`skecs-migration` module](https://github.com/ShortKedr/skecs/tree/skecs-v0.2.0/skecs-migration) handles the mechanical
package and C# namespace changes. It is a native .NET CLI requiring SDK 8 or later;
its pinned Roslyn dependency is restored by `dotnet run` on the first build.
Use the migration tool from the `skecs-v0.2.0` source checkout. Run it from
that checkout's root, passing your **consumer project's** directory.
Include the directory containing `Directory.Packages.props` when using central
package management. Save your changes in version control or a backup first.
Download the original `ShortKedr.SECS.0.1.0.nupkg` from the
[SECS 0.1.0 release](https://github.com/ShortKedr/skecs/releases/tag/secs-v0.1.0)
if you want the tool to analyze entity ownership against the original assembly.

```powershell
dotnet run --project skecs-migration -- C:/Projects/MyGame --from 0.1.0 --to 0.2.0 --old-package "C:/packages/ShortKedr.SECS.0.1.0.nupkg"
```

The default is a dry run: it prints a diff without writing files. Review it,
especially any identifier named `SECS` that could refer to your own variable or
alias. The identity-only mode uses Roslyn syntax trees. Declarations or aliases
named `SECS` and inactive preprocessor
branches containing `SECS` stop the preflight for manual review.
To apply the reviewed transformation:

```powershell
dotnet run --project skecs-migration -- C:/Projects/MyGame --from 0.1.0 --to 0.2.0 --old-package "C:/packages/ShortKedr.SECS.0.1.0.nupkg" --apply
```

The CLI updates namespace imports and explicit `SECS.`/`global::SECS.`
qualifications in `.cs` files. It preserves comments, character literals and
string text, including verbatim, raw and interpolated strings. Namespace references
inside interpolation expressions are migrated too. It updates the exact
`ShortKedr.SECS` identity in `PackageReference` and `PackageVersion` items in
`.csproj`, `.props` and `.targets`, changing literal version `0.1.0` to `0.2.0`.
Versionless references remain versionless for central package management.
MSBuild properties, version ranges and other old-package versions stop the run
for manual resolution before any files are written.
In Unity `.asmdef` files, the CLI replaces exact `SECS.dll` entries in
`precompiledReferences`.

`--old-package` enables entity-handle analysis against the published SECS
assembly. Omit it from both commands to flag likely sites for manual review.
If Roslyn
can prove the owning world in a direct typed chunk loop, the CLI rewrites
`new Entity(chunk.Entities[row], chunk.Versions[row])` to
`world.BindEntity(chunk.Entities[row], chunk.Versions[row])`. The receiver
must be a read-only `using var` local. Review any remaining construction,
`IsValid`, equality or hash sites the analysis reports. Handle indirect
queries, ambiguous ownership and source with unresolved references by hand.

UTF-8 and BOM-marked UTF-16 files retain their encoding, BOM and line endings.
Malformed input stops the preflight. Generated/cache directories such as
`bin`, `obj`, Unity `Library`, `.godot`, `node_modules` and `artifacts`, and
symbolic links are skipped. The CLI does not restore consumer packages, build projects,
rename files, import DLLs or access remote services. A second run after applying
the migration produces no additional changes.

## Complete the manual steps

1. Make the target `ShortKedr.SKECS.0.2.0.nupkg` available to the consumer's
   NuGet source as described in the [installation guide](INSTALL.md). Review any central
   version properties or package versions outside the selected directory.
2. When referencing source projects, update project and solution paths only after
   the SKECS source exists at the new paths. The script does not rename a source
   checkout or rewrite `.sln` files.
3. For Unity DLL imports, remove the old `SECS.dll` and import
   `lib/netstandard2.1/SKECS.dll` from `SKECS.Unity.0.2.0.zip`.
   Review the assembly definition changes, importer settings and platform filters.
   Follow the [Unity installation steps](INSTALL.md#unity).
4. For Godot .NET (C#), restore the new package in the C# project and rebuild
   the scene. Follow the [Godot .NET installation steps](INSTALL.md#godot-net-c).
5. Review reflection/assembly-name strings, serialization metadata, aliases and
   inactive preprocessor branches. These cannot
   be safely renamed as arbitrary text. Recompile dependent assemblies against
   the new identity.

## Update entity handles and world lifetime

- Keep the bound handle returned by the world or passed to the query processor.
  To reconstruct a handle from saved coordinates or a chunk's raw ID and
  generation, use `owningWorld.BindEntity(id, version)` on the world that
  supplied those coordinates. `new Entity(id, version)` creates an unbound
  value. `BindEntity` binds the coordinates without checking liveness.
  Persist the owning world context separately from the ID and generation.
- Use `world.IsAlive(entity)` to check liveness. `entity.IsValid` checks that
  the handle is bound and its ID and generation have a valid shape. It can
  stay true after entity destruction or world disposal. Equality and hash
  codes include ownership. Review keys and caches that treated matching ID
  and generation from different worlds as equal.
- In a live world, `World.IsAlive`, `World.TryGet` and component accessor
  `Has`/`TryGet` return false for foreign, unbound or stale handles. Strict
  operations such as `World.Has` and `World.Get` throw
  `InvalidOperationException` for those handles.
- Dispose the world after the query returns, including on early exits.
  Calling `Dispose` inside `Query.ForEach` throws
  `InvalidOperationException`. Operations that require a live world throw
  `ObjectDisposedException` after disposal. Repeated `Dispose` is safe.

## Validate the consumer

After applying the changes, rerun the preview. It should propose no further
edits. Restore the consumer and check that it resolves `ShortKedr.SKECS` 0.2.0.
Build and run its tests using the consumer's package source configuration.

```powershell
dotnet restore C:/Projects/MyGame/MyGame.csproj
dotnet build C:/Projects/MyGame/MyGame.csproj -c Release --no-restore
dotnet test C:/Projects/MyGame/MyGame.csproj -c Release --no-restore
```

For Unity, check the Editor and each required player backend separately. For
Godot .NET, run the scene using the .NET editor. Verify world creation, queries,
component access and disposal in the actual consumer. Exercise any changed
handle path after entity destruction or ID reuse and across worlds that may
allocate the same ID and generation.
