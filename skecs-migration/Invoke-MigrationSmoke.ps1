param(
    [Parameter(Mandatory)][string] $OldPackage,
    [Parameter(Mandatory)][string] $CandidatePackage,
    [string] $ToolPackagesDirectory,
    [string] $SdkVersion
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$toolProject = Join-Path $PSScriptRoot 'Skecs.Migration.csproj'
$old = (Resolve-Path -LiteralPath $OldPackage).Path
$candidate = (Resolve-Path -LiteralPath $CandidatePackage).Path
$publishedHash = '0EAD3A5BAAABCB03718D0519C76A264EFDAA2C4D7572777D51850A5AEDC0783C'
if ((Get-FileHash -LiteralPath $old).Hash -ne $publishedHash) { throw 'Expected the published ShortKedr.SECS 0.1.0 package.' }
if ($ToolPackagesDirectory) {
    $ToolPackagesDirectory = (Resolve-Path -LiteralPath $ToolPackagesDirectory).Path
}
if (-not $SdkVersion) {
    $installedSdk = dotnet --list-sdks | ForEach-Object {
        if ($_ -match '^(8\.0\.\d+)\s') { [version]$Matches[1] }
    } | Sort-Object -Descending | Select-Object -First 1
    if (-not $installedSdk) { throw 'A .NET 8 SDK is required for the migration smoke.' }
    $SdkVersion = $installedSdk.ToString()
}

$run = Join-Path $repoRoot ('artifacts/release-migration/' + [guid]::NewGuid().ToString('N'))
$consumer = Join-Path $run 'consumer'
$toolBuild = Join-Path $run 'tool'
$tmp = Join-Path $run 'tmp'
New-Item -ItemType Directory -Path $consumer,$tmp | Out-Null
Set-Content -LiteralPath (Join-Path $run 'global.json') -Encoding utf8 -Value "{`"sdk`":{`"version`":`"$SdkVersion`",`"rollForward`":`"disable`"}}"

$env:DOTNET_CLI_HOME = Join-Path $run 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $run 'nuget-http'
$env:NUGET_PACKAGES = Join-Path $run 'tool-packages'
$env:TEMP = $tmp
$env:TMP = $tmp
$env:TMPDIR = $tmp

function Invoke-Logged([string] $Name, [string[]] $CommandArguments) {
    $log = Join-Path $run "$Name.log"
    $output = dotnet @CommandArguments 2>&1
    $code = $LASTEXITCODE
    Set-Content -LiteralPath $log -Value $output -Encoding utf8
    $output | Write-Host
    if ($code -ne 0) { throw "$Name failed (exit $code). See $log" }
}

function Get-ConsumerHashes {
    Get-ChildItem -LiteralPath $consumer -File -Recurse -Force | Sort-Object FullName | ForEach-Object {
        "$($_.FullName): $((Get-FileHash -LiteralPath $_.FullName).Hash)"
    }
}

function Test-ConsumerPackage([string] $Phase, [string] $Package, [string] $Id, [string] $Version, [string] $Assembly) {
    $feed = Join-Path $run "$Phase-feed"
    $build = Join-Path $run "$Phase-build"
    $env:NUGET_PACKAGES = Join-Path $run "$Phase-packages"
    New-Item -ItemType Directory -Path $feed | Out-Null
    Copy-Item -LiteralPath $Package -Destination (Join-Path $feed "$Id.$Version.nupkg")
    Invoke-Logged "$Phase-restore" @('restore', $project, '--source', $feed, '--artifacts-path', $build, '-p:NuGetAudit=false')

    $lowerId = $Id.ToLowerInvariant()
    $restored = Join-Path $env:NUGET_PACKAGES "$lowerId/$Version/$lowerId.$Version.nupkg"
    $sha = (Get-FileHash -LiteralPath $Package).Hash
    if ((Get-FileHash -LiteralPath $restored).Hash -ne $sha) { throw "Restored $Id package digest differs." }
    Write-Output "$Phase package $Id $Version SHA-256 $sha"

    Invoke-Logged "$Phase-build" @('build', $project, '-c', 'Release', '--no-restore', '--artifacts-path', $build)
    $output = Join-Path $build 'bin/Consumer/release'
    if (-not (Test-Path -LiteralPath (Join-Path $output "$Assembly.dll") -PathType Leaf)) {
        throw "The build did not include $Assembly.dll."
    }
    Invoke-Logged "$Phase-run" @((Join-Path $output 'Consumer.dll'))
    $runtimeLog = Join-Path $run "$Phase-run.log"
    if (-not (Select-String -LiteralPath $runtimeLog -SimpleMatch 'Tick 5: (15, 5)' -Quiet) -or
        -not (Select-String -LiteralPath $runtimeLog -SimpleMatch 'Matching archetypes: 1' -Quiet)) {
        throw "The tagged sample did not produce the expected $Phase result."
    }
}

Write-Output "Migration logs: $run"
Push-Location $consumer
try {
    Write-Output "Migration SDK: $(dotnet --version)"
    $sample = git -C $repoRoot show secs-v0.1.0:SECS.Sample/Program.cs
    if ($LASTEXITCODE -ne 0) { throw 'Could not read Program.cs from the local secs-v0.1.0 tag.' }
    $source = Join-Path $consumer 'Program.cs'
    $project = Join-Path $consumer 'Consumer.csproj'
    $unrelated = Join-Path $consumer 'Unrelated.cs'
    $coordinate = 'new Entity(chunk.Entities[row], chunk.Versions[row])'
    $extendedSample = foreach ($line in $sample) {
        if ($line.TrimStart().StartsWith('Console.WriteLine($"  Entity ')) {
            "            var reconstructed = $coordinate;"
            '            if (!world.IsAlive(reconstructed) || world.Get<Position>(reconstructed).X != chunk.Component1[row].X)'
            '                throw new InvalidOperationException("The reconstructed chunk entity must resolve in its world.");'
        }
        $line
    }
    Set-Content -LiteralPath $source -Encoding utf8 -Value $extendedSample
    Add-Content -LiteralPath $source -Encoding utf8 -Value @'

internal static class SavedEntity
{
    internal static Entity FromCoordinates(int id, int version) => new Entity(id, version);
    internal static bool HasShape(Entity entity) => entity.IsValid;
    internal static bool Equals(Entity left, Entity right) => left == right;

    internal static unsafe Entity? MixedCoordinates(World first, World second)
    {
        foreach (var a in first.Query<Position>().Chunks())
        foreach (var b in second.Query<Position>().Chunks())
            return new Entity(a.Entities[0], b.Versions[0]);
        return null;
    }
}
'@
    Set-Content -LiteralPath $unrelated -Value 'internal static class Unrelated { internal const string Example = "SECS.World"; }' -Encoding utf8
    Set-Content -LiteralPath $project -Encoding utf8 -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ShortKedr.SECS" Version="0.1.0" />
  </ItemGroup>
</Project>
'@
    $toolArguments = @('build', $toolProject, '-c', 'Release', '--artifacts-path', $toolBuild, '-p:NuGetAudit=false')
    if ($ToolPackagesDirectory) {
        $toolArguments += @("-p:RestoreSources=$run", "-p:RestoreFallbackFolders=$ToolPackagesDirectory")
    }
    Invoke-Logged 'tool-build' $toolArguments
    Test-ConsumerPackage 'old' $old 'ShortKedr.SECS' '0.1.0' 'SECS'

    $migration = @((Join-Path $toolBuild 'bin/Skecs.Migration/release/Skecs.Migration.dll'), $consumer, '--from', '0.1.0', '--to', '0.2.0', '--old-package', $old)
    $before = Get-ConsumerHashes
    $sourceText = Get-Content -LiteralPath $source -Raw
    if (-not $sourceText.Contains($coordinate)) { throw 'The tagged sample did not contain the expected chunk loop.' }
    $expectedSource = $sourceText.Replace('using SECS;', 'using SKECS;').
        Replace($coordinate, 'world.BindEntity(chunk.Entities[row], chunk.Versions[row])')
    $expectedProject = (Get-Content -LiteralPath $project -Raw).Replace('Include="ShortKedr.SECS" Version="0.1.0"', 'Include="ShortKedr.SKECS" Version="0.2.0"')
    $unrelatedHash = (Get-FileHash -LiteralPath $unrelated).Hash
    Invoke-Logged 'preview' $migration
    $preview = Get-Content -LiteralPath (Join-Path $run 'preview.log') -Raw
    if ((Compare-Object $before (Get-ConsumerHashes)) -or
        -not $preview.Contains('Dry run: 2 file(s).') -or
        -not $preview.Contains('world.BindEntity(chunk.Entities[row], chunk.Versions[row])') -or
        ([regex]::Matches($preview, 'Review this Entity construction\.')).Count -ne 2 -or
        -not $preview.Contains('Review Entity.IsValid.') -or
        -not $preview.Contains('Review Entity equality or hashing.')) {
        throw 'Preview did not report the two changes without modifying the consumer.'
    }

    Invoke-Logged 'apply' ($migration + '--apply')
    if ((Get-Content -LiteralPath $source -Raw) -cne $expectedSource -or
        (Get-Content -LiteralPath $project -Raw) -cne $expectedProject -or
        (Get-FileHash -LiteralPath $unrelated).Hash -ne $unrelatedHash) {
        throw 'Apply did not make exactly the expected edits.'
    }
    Test-ConsumerPackage 'candidate' $candidate 'ShortKedr.SKECS' '0.2.0' 'SKECS'
    if (Compare-Object (Get-Content -LiteralPath (Join-Path $run 'old-run.log')) (Get-Content -LiteralPath (Join-Path $run 'candidate-run.log')) -SyncWindow 0) {
        throw 'The migrated sample output differs from the SECS 0.1.0 baseline.'
    }

    $after = Get-ConsumerHashes
    Invoke-Logged 'repeat' ($migration + '--apply')
    if ((Compare-Object $after (Get-ConsumerHashes)) -or
        -not (Select-String -LiteralPath (Join-Path $run 'repeat.log') -SimpleMatch 'Applied: 0 file(s).' -Quiet)) {
        throw 'Repeated apply was not idempotent.'
    }
    Write-Output 'SKECS_MIGRATION_OK'
} finally {
    Pop-Location
}
