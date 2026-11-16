# skecs-migration

Native C#/.NET consumer migration CLI. Requires .NET SDK 8 or later. The first
`dotnet run` restores its pinned Roslyn dependency through NuGet. Later SDKs can
run it using their installed .NET runtime. No separate language runtime is required.

The CLI migrates the package and source identities from SECS 0.1.0 to SKECS
0.2.0. Obtain `ShortKedr.SKECS.0.2.0.nupkg` or the Unity DLL separately. Keep
the SECS package and names in projects staying on 0.1.0.

From the repository root, preview changes to a consumer project:

```powershell
dotnet run --project skecs-migration -- C:/Projects/MyGame --from 0.1.0 --to 0.2.0
```

The default is a dry run. Save the consumer in version control or a backup,
review the diff, then explicitly apply:

```powershell
dotnet run --project skecs-migration -- C:/Projects/MyGame --from 0.1.0 --to 0.2.0 --apply
```

The CLI updates C# namespace imports and `SECS.` qualifications, and
`ShortKedr.SECS` package identities with literal `0.1.0` versions in MSBuild
projects and props/targets. Include `Directory.Packages.props` in the selected
directory for central version management. In Unity `.asmdef` files it updates
explicit `SECS.dll` entries under `precompiledReferences`. UTF-8 and BOM-marked
UTF-16 encoding and line endings are preserved. A second run is idempotent.
Generated/cache directories and symlinks are skipped. Unsupported package
versions/properties or malformed input stop the preflight before writes.

Roslyn parses C# and preserves comments, string text and formatting. Namespace
references inside interpolation expressions are migrated too. Local declarations
or aliases named `SECS`, inactive preprocessor branches containing `SECS`, and
invalid syntax require manual review and stop the preflight before writes.
Review shadowed names from other files/imports, reflection and assembly-name
strings, serialization metadata, source project/solution paths and custom
assembly references. In Unity, replace the imported `SECS.dll` with
`SKECS.dll` from the 0.2.0 Unity ZIP and rebuild. An I/O failure during apply
may leave earlier files applied. Review the diff before rerunning.

To analyze entity ownership as well, add
`--old-package C:/packages/ShortKedr.SECS.0.1.0.nupkg` to both preview and
apply. The CLI reads the old assembly metadata and analyzes the selected C#
sources with .NET runtime references. It changes
`new Entity(chunk.Entities[row], chunk.Versions[row])` to
`world.BindEntity(chunk.Entities[row], chunk.Versions[row])` when it can
resolve the old constructor, establish that both coordinates come from the
same row of a direct `world.Query<T>().Chunks()` iteration, and identify a
readonly `using var world` receiver. Scope the run to one project for the
best analysis. Projects with extra references or compilation settings may
require manual changes. Without `--old-package`, preview reports likely
construction and entity uses as unverified and leaves binding to you.

Review the reported constructor, `Entity.IsValid`, equality and hash sites.
For stored coordinates or other reconstruction patterns, call `BindEntity`
on the world the coordinates came from and verify liveness with
`world.IsAlive(handle)`. Review uses of entities, accessors and chunk views
across world disposal or structural changes.

Restore, build and run the consumer's tests against the local 0.2.0 package
after migration. Run the Godot .NET scene or Unity Editor and player builds
for the backends your project uses.

Run the module's regression tests from the repository root:

```powershell
dotnet test skecs-migration/Skecs.Migration.Tests/Skecs.Migration.Tests.csproj -c Release
```

Test fixtures are created under the workspace's ignored
`artifacts/tooling/tmp/migration-dotnet-tests/` directory.

For release qualification, pass the published SECS 0.1.0 package and the
locally built SKECS 0.2.0 candidate to the smoke runner:

```powershell
pwsh -NoProfile -File skecs-migration/Invoke-MigrationSmoke.ps1 `
  -OldPackage C:/packages/ShortKedr.SECS.0.1.0.nupkg `
  -CandidatePackage C:/packages/ShortKedr.SKECS.0.2.0.nupkg
```

The runner extends the C# sample from the local `secs-v0.1.0` tag with a
chunk handle reconstruction. It uses .NET SDK 8 and a fresh NuGet cache for
each consumer. Supply `-ToolPackagesDirectory` with a populated NuGet package
directory if the CLI must be built offline. Check for `SKECS_MIGRATION_OK`
and retain the logs under `artifacts/release-migration/`.
