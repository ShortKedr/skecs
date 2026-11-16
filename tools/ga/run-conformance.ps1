param(
    [string]$DotNetSdkVersion = '8.0.428',
    [int[]]$DeviceIndex,
    [switch]$VulkanValidation
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$dotnet = Get-Command dotnet -CommandType Application | Select-Object -First 1
$sdkRoot = Join-Path (Split-Path $dotnet.Source) 'sdk'
$sdk = Join-Path $sdkRoot "$DotNetSdkVersion/dotnet.dll"
if (-not (Test-Path -LiteralPath $sdk -PathType Leaf)) {
    throw "Set DotNetSdkVersion to an installed .NET 8 SDK. Missing $sdk."
}

$artifacts = Join-Path $workspace 'artifacts/ga'
$previousEnvironment = @{
    DOTNET_CLI_HOME = $env:DOTNET_CLI_HOME
    DOTNET_CLI_TELEMETRY_OPTOUT = $env:DOTNET_CLI_TELEMETRY_OPTOUT
    DOTNET_GENERATE_ASPNET_CERTIFICATE = $env:DOTNET_GENERATE_ASPNET_CERTIFICATE
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE
    NUGET_HTTP_CACHE_PATH = $env:NUGET_HTTP_CACHE_PATH
    NUGET_PLUGINS_CACHE_PATH = $env:NUGET_PLUGINS_CACHE_PATH
    VK_INSTANCE_LAYERS = $env:VK_INSTANCE_LAYERS
    Path = $env:Path
    TEMP = $env:TEMP
    TMP = $env:TMP
}
try {
    $env:DOTNET_CLI_HOME = Join-Path $artifacts 'dotnet-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $artifacts 'nuget-http'
    $env:NUGET_PLUGINS_CACHE_PATH = Join-Path $artifacts 'nuget-plugins'
    $env:TEMP = (New-Item -ItemType Directory -Force (Join-Path $artifacts 'temp')).FullName
    $env:TMP = $env:TEMP
    if ($VulkanValidation) {
        $env:VK_INSTANCE_LAYERS = 'VK_LAYER_KHRONOS_validation'
    }
    & (Join-Path $PSScriptRoot 'probe/build-probe.ps1')
    $native = Join-Path $artifacts 'probe'
    $vulkan = Join-Path $native 'ga-vulkan.exe'
    if (-not $DeviceIndex) {
        $rows = & $vulkan devices
        if ($LASTEXITCODE -ne 0) { throw 'Vulkan device listing failed.' }
        $DeviceIndex = @($rows | ForEach-Object {
            $fields = $_ -split '\t'
            if ($fields.Count -eq 7 -and $fields[5] -in '1', '2') {
                [int]$fields[0]
            }
        })
    }
    if (-not $DeviceIndex) { throw 'No integrated or discrete Vulkan GPU was found.' }
    $build = Join-Path $artifacts 'conformance-build'
    $project = Join-Path $PSScriptRoot 'conformance/Ga.Conformance.csproj'
    $packageSource = Join-Path $env:USERPROFILE '.nuget/packages'
    $packages = Join-Path $artifacts 'packages'
    $buildArguments = @(
        $sdk, 'build', $project, '--artifacts-path', $build, '-m:1', '-nr:false',
        '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
        "-p:RestoreSources=$packageSource", "-p:RestorePackagesPath=$packages"
    )
    & $dotnet.Source @buildArguments
    if ($LASTEXITCODE -ne 0) { throw 'GA conformance build failed.' }

    $vulkanBin = Join-Path $env:VULKAN_SDK 'Bin'
    $env:Path = $vulkanBin + [IO.Path]::PathSeparator + $env:Path
    $conformanceArguments = @(
        (Join-Path $build 'bin/Ga.Conformance/debug/Ga.Conformance.dll'),
        (Join-Path $native 'ga-slang-probe.exe'), (Join-Path $vulkanBin 'spirv-val.exe'),
        $vulkan, (Join-Path $artifacts 'conformance'), (Join-Path $native 'ga-vulkan-native.dll')
    ) + $DeviceIndex
    & $dotnet.Source @conformanceArguments
    if ($LASTEXITCODE -ne 0) { throw 'GA hardware conformance failed.' }
}
finally {
    foreach ($entry in $previousEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }
}
