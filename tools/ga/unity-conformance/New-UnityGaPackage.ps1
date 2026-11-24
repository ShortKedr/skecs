[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [string] $NuGetPackages = $env:NUGET_PACKAGES,
    [string] $Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$scratch = Join-Path $workspace 'artifacts/ga/unity-package'
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $scratch 'com.shortkedr.skecs.ga'
}
$package = [IO.Path]::GetFullPath($OutputDirectory)
$relative = [IO.Path]::GetRelativePath($workspace, $package)
if ($relative -eq '.' -or $relative -match '^\.\.([\\/]|$)' -or [IO.Path]::IsPathRooted($relative)) {
    throw 'The generated Unity package must stay inside the workspace.'
}
$source = Join-Path $workspace 'packages/com.shortkedr.skecs.ga'
if ([IO.Path]::GetRelativePath($source, $package) -notmatch '^\.\.([\\/]|$)') {
    throw 'Use a different directory for the assembled package.'
}
if (-not $NuGetPackages) {
    $NuGetPackages = Join-Path $env:USERPROFILE '.nuget/packages'
}
$NuGetPackages = (Resolve-Path -LiteralPath $NuGetPackages).Path
if (Test-Path -LiteralPath $package) {
    if (Get-ChildItem -LiteralPath $package -Force | Select-Object -First 1) {
        throw "The package output directory must be empty: $package"
    }
}

New-Item -ItemType Directory -Force -Path $scratch, $package, (Join-Path $scratch 'tmp') | Out-Null
$env:TEMP = Join-Path $scratch 'tmp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_HOME = Join-Path $scratch 'dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_NOLOGO = '1'
$build = Join-Path $scratch 'build'
$compiler = Join-Path $workspace 'SKECS.GA.Compiler/SKECS.GA.Compiler.csproj'
$restoreCache = Join-Path $scratch 'nuget-cache'
& $Dotnet build $compiler -c Release -f netstandard2.1 --artifacts-path $build -p:NuGetAudit=false "-p:RestorePackagesPath=$restoreCache" --source $NuGetPackages /m:1 /nr:false -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) {
    throw "Unity GA managed build failed with exit code $LASTEXITCODE."
}

Copy-Item -Path (Join-Path $source '*') -Destination $package -Recurse
Copy-Item -LiteralPath (Join-Path $workspace 'LICENSE') -Destination $package
$runtime = Join-Path $package 'Runtime/Plugins'
$editor = Join-Path $package 'Editor/Plugins'
$licenses = Join-Path $package 'Third Party Notices'
New-Item -ItemType Directory -Path $runtime, $editor, $licenses | Out-Null

function Add-Plugin([string] $Path, [string] $Destination, [bool] $EditorOnly) {
    $name = Split-Path -Leaf $Path
    $target = Join-Path $Destination $name
    Copy-Item -LiteralPath $Path -Destination $target
    $platform = if ($EditorOnly) { 'Editor' } else { 'Runtime' }
    $identity = [Text.Encoding]::UTF8.GetBytes("com.shortkedr.skecs.ga/$platform/$name")
    $guid = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($identity)).Substring(0, 32).ToLowerInvariant()
    $anyEnabled = if ($EditorOnly) { 0 } else { 1 }
    $explicit = if ($EditorOnly) { 1 } else { 0 }
    @"
fileFormatVersion: 2
guid: $guid
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: $explicit
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: $anyEnabled
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        DefaultValueInitialized: true
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content -LiteralPath ($target + '.meta') -Encoding utf8NoBOM
}

foreach ($name in @('SKECS', 'SKECS.GA')) {
    Add-Plugin (Join-Path $build "bin/$name/release_netstandard2.1/$name.dll") $runtime $false
}
Add-Plugin (Join-Path $build 'bin/SKECS.GA.Compiler/release_netstandard2.1/SKECS.GA.Compiler.dll') $editor $true
Add-Plugin (Join-Path $build 'bin/SKECS.GA.Analyzers/release/SKECS.GA.Analyzers.dll') $editor $true

# Unity provides the .NET Standard memory, buffers and task extension facade assemblies.
$dependencies = @(
    @('System.Text.Json',                 '9.0.9',  'netstandard2.0', $runtime, $false),
    @('System.Text.Encodings.Web',        '9.0.9',  'netstandard2.0', $runtime, $false),
    @('System.IO.Pipelines',              '9.0.9',  'netstandard2.0', $runtime, $false),
    @('Microsoft.Bcl.AsyncInterfaces',   '9.0.9',  'netstandard2.1', $runtime, $false),
    @('System.Runtime.CompilerServices.Unsafe', '6.0.0', 'netstandard2.0', $runtime, $false),
    @('Microsoft.CodeAnalysis.Common',   '4.11.0', 'netstandard2.0', $editor, $true, 'Microsoft.CodeAnalysis'),
    @('Microsoft.CodeAnalysis.CSharp',   '4.11.0', 'netstandard2.0', $editor, $true),
    @('System.Collections.Immutable',    '8.0.0',  'netstandard2.0', $editor, $true),
    @('System.Reflection.Metadata',      '8.0.0',  'netstandard2.0', $editor, $true),
    @('System.Text.Encoding.CodePages',   '7.0.0',  'netstandard2.0', $editor, $true)
)
foreach ($dependency in $dependencies) {
    $name, $version, $tfm, $destination, $editorOnly, $assembly = $dependency
    if (-not $assembly) { $assembly = $name }
    $directory = Join-Path $NuGetPackages "$($name.ToLowerInvariant())/$version"
    Add-Plugin (Join-Path $directory "lib/$tfm/$assembly.dll") $destination $editorOnly
    foreach ($notice in (Get-ChildItem -LiteralPath $directory -File | Where-Object Name -Match 'LICENSE|THIRD.?PARTY')) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenses "$name.$version.$($notice.Name)")
    }
}

Write-Output "Unity GA UPM package $package"
