# skecs-migration

Native C#/.NET consumer migration CLI. Requires .NET SDK 8 or later. The first
`dotnet run` restores its pinned Roslyn dependency through NuGet. Later SDKs can
run it using their installed .NET runtime. No separate language runtime is required.

The prepared SECS 0.1.0 → SKECS 0.2.0 rename targets the **unreleased current-source
identity**. It is not an upgrade to a published release. This module does not
provide, download or publish `ShortKedr.SKECS.0.2.0.nupkg`. SECS 0.1.0 remains the
released version; retain its package and names when staying on that release.

From the repository root, preview changes to a consumer project:

```powershell
dotnet run --project skecs-migration -- C:/Projects/MyGame --from 0.1.0 --to 0.2.0
```

The default is a dry run. Save the consumer in version control or a backup,
review the diff, then explicitly apply:

```powershell
dotnet run --project skecs-migration -- C:/Projects/MyGame --from 0.1.0 --to 0.2.0 --apply
```

The CLI updates C# namespace imports and `SECS.` qualifications, and exact
`ShortKedr.SECS` package identities with literal `0.1.0` versions in MSBuild
projects and props/targets. Include `Directory.Packages.props` in the selected
directory for central version management. UTF-8 and BOM-marked UTF-16 encoding
and line endings are preserved; a second run is idempotent. Generated/cache
directories and symlinks are skipped. Unsupported package versions/properties
or malformed input stop the preflight before writes.

Roslyn parses C# and preserves comments, string text and formatting. Namespace
references inside interpolation expressions are migrated too. Local declarations
or aliases named `SECS`, inactive preprocessor branches containing `SECS`, and
invalid syntax require manual review and stop the preflight before writes.
The tool uses syntax rather than a full semantic compilation of the consumer;
review shadowed names from other files/imports, reflection/assembly-name strings,
serialization metadata, source project/solution paths and Unity DLL/assembly
definition imports. It does not restore consumer packages, build the consumer,
rename files, import DLLs or contact remote services during migration. An I/O failure during
application may leave earlier files applied; review the diff before rerunning.

Restore, build and run the consumer's tests against the intended local source or
package after migration. Unity player backends and Godot .NET scenes need their
own validation. A consumer migration article must accompany the future release
once the actual release and migration requirements have been verified.

Run the module's regression tests from the repository root:

```powershell
dotnet test skecs-migration/Skecs.Migration.Tests/Skecs.Migration.Tests.csproj -c Release
```

Test fixtures are created under the workspace's ignored
`artifacts/tooling/tmp/migration-dotnet-tests/` directory.
