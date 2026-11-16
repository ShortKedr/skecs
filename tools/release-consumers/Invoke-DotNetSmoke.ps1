param(
    [Parameter(Mandatory = $true)][string] $PackageDirectory,
    [ValidateSet('net8.0', 'net10.0', 'net48')][string] $Framework = 'net8.0',
    [string] $Version = '0.2.0',
    [string] $SdkVersion,
    [switch] $Offline
)

$ErrorActionPreference = 'Stop'
$feed = (Resolve-Path -LiteralPath $PackageDirectory).Path
$package = Join-Path $feed "ShortKedr.SKECS.$Version.nupkg"
if (-not (Test-Path -LiteralPath $package)) {
    throw "Missing ShortKedr.SKECS.$Version.nupkg in $feed"
}
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$scratch = Join-Path $repoRoot ('artifacts/release-consumers/dotnet-' + [guid]::NewGuid().ToString('N'))
$projectDirectory = Join-Path $scratch 'project'
$packages = Join-Path $scratch 'nuget'
New-Item -ItemType Directory -Path $projectDirectory, $packages | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DotNet/DotNet.csproj'), (Join-Path $PSScriptRoot 'DotNet/Program.cs') -Destination $projectDirectory
$project = Join-Path $projectDirectory 'DotNet.csproj'
$config = Join-Path $scratch 'NuGet.Config'
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
$extraSource = if ($Framework -eq 'net48' -and -not $Offline) { '<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/>' } else { '' }
Set-Content -LiteralPath $config -Encoding utf8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="release" value="$escapedFeed"/>$extraSource</packageSources></configuration>
"@

if (-not $SdkVersion) {
    $sdkMajor = if ($Framework -eq 'net10.0') { '10' } else { '8' }
    $candidate = dotnet --list-sdks | ForEach-Object { if ($_ -match "^($sdkMajor\.\d+\.\d+)\s") { [version]$Matches[1] } } | Sort-Object -Descending | Select-Object -First 1
    if ($null -ne $candidate) { $SdkVersion = $candidate.ToString() }
}
if (-not $SdkVersion) { throw "No suitable SDK found for $Framework. Supply -SdkVersion to select an installed SDK." }
Set-Content -LiteralPath (Join-Path $projectDirectory 'global.json') -Encoding utf8 -Value "{`"sdk`":{`"version`":`"$SdkVersion`",`"rollForward`":`"disable`"}}"
$previousPackages = $env:NUGET_PACKAGES
$previousCliHome = $env:DOTNET_CLI_HOME
$env:NUGET_PACKAGES = $packages
$env:DOTNET_CLI_HOME = $scratch
Push-Location $projectDirectory
try {
    Write-Output "Smoke SDK: $(dotnet --version)"

    dotnet restore $project --configfile $config -p:TargetFramework=$Framework -p:SKECSVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw "Package restore failed for $Framework" }
    $restoredPackage = Join-Path $env:NUGET_PACKAGES "shortkedr.skecs/$Version/shortkedr.skecs.$Version.nupkg"
    $packageHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    if (-not (Test-Path -LiteralPath $restoredPackage) -or
        $packageHash -ne (Get-FileHash -LiteralPath $restoredPackage -Algorithm SHA256).Hash) {
        throw 'Restored package does not match the built release package.'
    }
    Write-Output "SKECS_PACKAGE_SHA256 $packageHash"
    dotnet build $project -c Release -f $Framework --no-restore -p:SKECSVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw "Consumer build failed for $Framework" }

    if ($Framework -eq 'net48') {
        if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') { throw 'The net48 runtime smoke requires Windows.' }
        $app = Join-Path $projectDirectory 'bin/Release/net48/DotNet.exe'
        $runtimeOutput = & $app
    } else {
        $runtimeOutput = dotnet run --project $project -c Release -f $Framework --no-build --no-restore -p:SKECSVersion=$Version
    }
    $exitCode = $LASTEXITCODE
    $runtimeOutput | Write-Output
    if ($exitCode -ne 0 -or $runtimeOutput -notcontains 'SKECS_CONSUMER_OK') {
        throw "Consumer runtime failed for $Framework (exit code $exitCode)."
    }
} finally {
    Pop-Location
    $env:NUGET_PACKAGES = $previousPackages
    $env:DOTNET_CLI_HOME = $previousCliHome
}
