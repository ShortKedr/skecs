param(
    [Parameter(Mandatory = $true)][string] $PackageDirectory,
    [Parameter(Mandatory = $true)][string] $GodotExecutable,
    [string] $Version = '0.2.0',
    [string] $SdkVersion
)

$ErrorActionPreference = 'Stop'
$feed = (Resolve-Path -LiteralPath $PackageDirectory).Path
$package = Join-Path $feed "ShortKedr.SKECS.$Version.nupkg"
if (-not (Test-Path -LiteralPath $package)) {
    throw "Missing ShortKedr.SKECS.$Version.nupkg in $feed"
}
$godot = (Resolve-Path -LiteralPath $GodotExecutable).Path
$godotVersion = & $godot --version
if ($LASTEXITCODE -ne 0 -or $godotVersion -notmatch '^4\.5\..*\.mono(?:\.|$)') {
    throw "A Godot 4.5 .NET executable is required. Reported version: $godotVersion"
}
Write-Output "Godot version: $godotVersion"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$scratch = Join-Path $repoRoot ('artifacts/release-consumers/godot-' + [guid]::NewGuid().ToString('N'))
$projectDirectory = Join-Path $scratch 'project'
$packages = Join-Path $scratch 'nuget'
New-Item -ItemType Directory -Path $projectDirectory, $packages | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Godot') -File | Copy-Item -Destination $projectDirectory
$project = Join-Path $projectDirectory 'SKECSReleaseSmoke.csproj'
$config = Join-Path $projectDirectory 'NuGet.Config'
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
$godotFeed = Join-Path (Split-Path -Parent $godot) 'GodotSharp/Tools/nupkgs'
$godotSdk = ([xml](Get-Content -LiteralPath $project -Raw)).Project.Sdk -replace '/', '.'
$sdkSource = if (Test-Path -LiteralPath (Join-Path $godotFeed "$godotSdk.nupkg")) {
    '<add key="godot" value="' + [System.Security.SecurityElement]::Escape($godotFeed) + '"/>'
} else {
    '<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/>'
}
Set-Content -LiteralPath $config -Encoding utf8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="release" value="$escapedFeed"/>$sdkSource</packageSources></configuration>
"@

if (-not $SdkVersion) {
    $candidate = dotnet --list-sdks | ForEach-Object { if ($_ -match '^(8\.\d+\.\d+)\s') { [version]$Matches[1] } } | Sort-Object -Descending | Select-Object -First 1
    if ($null -ne $candidate) { $SdkVersion = $candidate.ToString() }
}
if (-not $SdkVersion) { throw 'A .NET 8 SDK is required for the Godot 4.5 smoke.' }
Set-Content -LiteralPath (Join-Path $projectDirectory 'global.json') -Encoding utf8 -Value "{`"sdk`":{`"version`":`"$SdkVersion`",`"rollForward`":`"disable`"}}"
$previousPackages = $env:NUGET_PACKAGES
$previousCliHome = $env:DOTNET_CLI_HOME
$env:NUGET_PACKAGES = $packages
$env:DOTNET_CLI_HOME = $scratch
Push-Location $projectDirectory
try {
    Write-Output "Godot smoke SDK: $(dotnet --version)"
    dotnet restore $project --configfile $config -p:SKECSVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw 'Godot package restore failed.' }
    $restoredPackage = Join-Path $env:NUGET_PACKAGES "shortkedr.skecs/$Version/shortkedr.skecs.$Version.nupkg"
    $packageHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    if (-not (Test-Path -LiteralPath $restoredPackage) -or
        $packageHash -ne (Get-FileHash -LiteralPath $restoredPackage -Algorithm SHA256).Hash) {
        throw 'Restored Godot package does not match the built release package.'
    }
    Write-Output "SKECS_PACKAGE_SHA256 $packageHash"
    dotnet build $project -c Debug --no-restore -p:SKECSVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw 'Godot C# build failed.' }
} finally {
    Pop-Location
    $env:NUGET_PACKAGES = $previousPackages
    $env:DOTNET_CLI_HOME = $previousCliHome
}

$importLog = & $godot --headless --editor --path $projectDirectory --import 2>&1
$importExitCode = $LASTEXITCODE
$importLog | ForEach-Object { Write-Output $_ }
if ($importExitCode -ne 0) { throw "Godot asset import failed (exit code $importExitCode)." }

$log = & $godot --headless --path $projectDirectory --log-file (Join-Path $scratch 'scene.log') --quit-after 120 2>&1
$exitCode = $LASTEXITCODE
$log | ForEach-Object { Write-Output $_ }
if ($exitCode -ne 0 -or -not ($log -match 'SKECS_GODOT_OK')) {
    throw "Godot scene smoke failed (exit code $exitCode)."
}
