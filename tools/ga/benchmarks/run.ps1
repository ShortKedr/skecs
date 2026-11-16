param(
    [Parameter(Mandatory)] [ValidateRange(0, 2147483647)] [int]$DeviceIndex,
    [ValidateSet('smoke', 'dry', 'benchmark')] [string]$Mode = 'smoke',
    [ValidateRange(1, 2147483647)] [int]$Rows = 65,
    [string]$DotNetSdkVersion = '8.0.428'
)

$ErrorActionPreference = 'Stop'
if ($Mode -ne 'smoke' -and $PSBoundParameters.ContainsKey('Rows')) {
    throw 'Rows can be set only in smoke mode.'
}

$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
$sdk = Join-Path (Split-Path $dotnet) "sdk/$DotNetSdkVersion/dotnet.dll"
if (-not (Test-Path -LiteralPath $sdk -PathType Leaf)) {
    throw "Missing installed .NET 8 SDK $sdk. Set DotNetSdkVersion to a local version."
}
if (-not $env:VULKAN_SDK) {
    throw 'Set VULKAN_SDK to the local Vulkan SDK with Slang 2025.6.1.'
}

$build = Join-Path $workspace 'artifacts/ga/benchmarks-build'
$native = Join-Path $workspace 'artifacts/ga/probe'
$temp = (New-Item -ItemType Directory -Force (Join-Path $build 'temp')).FullName
$settings = @{
    DOTNET_CLI_HOME = Join-Path $build 'dotnet-home'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    NUGET_HTTP_CACHE_PATH = Join-Path $build 'nuget-http'
    NUGET_PLUGINS_CACHE_PATH = Join-Path $build 'nuget-plugins'
    Path = (Join-Path $env:VULKAN_SDK 'Bin') + [IO.Path]::PathSeparator + $env:Path
    TEMP = $temp
    TMP = $temp
}
$previous = @{}
Push-Location $workspace
try {
    foreach ($entry in $settings.GetEnumerator()) {
        $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key)
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }

    foreach ($file in 'ga-slang-probe.exe', 'ga-vulkan.exe', 'ga-vulkan-native.dll') {
        if (-not (Test-Path -LiteralPath (Join-Path $native $file) -PathType Leaf)) {
            & (Join-Path $PSScriptRoot '../probe/build-probe.ps1') -BuildOnly
            break
        }
    }

    $project = Join-Path $PSScriptRoot 'Ga.Benchmarks.csproj'
    $packageSource = Join-Path $env:USERPROFILE '.nuget/packages'
    $packages = Join-Path $build 'packages'
    & $dotnet $sdk build $project -c Release --artifacts-path $build -m:1 -nr:false `
        -p:UseSharedCompilation=false -p:NuGetAudit=false "-p:RestoreSources=$packageSource" "-p:RestorePackagesPath=$packages"
    if ($LASTEXITCODE -ne 0) { throw 'GA benchmarks build failed.' }

    $arguments = @((Join-Path $build 'bin/Ga.Benchmarks/release/Ga.Benchmarks.dll'), $Mode, $DeviceIndex)
    if ($Mode -eq 'smoke') { $arguments += $Rows }
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'GA benchmark or its parity verification failed.' }
}
finally {
    foreach ($entry in $previous.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }
    Pop-Location
}
