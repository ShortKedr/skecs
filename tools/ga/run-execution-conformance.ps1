param(
    [ValidateSet('cpu-only', 'auto', 'require-gpu')]
    [string]$Policy = 'require-gpu',
    [ValidateSet('vulkan', 'd3d12')]
    [string]$Backend = 'vulkan',
    [int[]]$DeviceIndex = @(0),
    [ValidateRange(1, [int]::MaxValue)]
    [int]$Entities = 257,
    [ValidateRange(1, [int]::MaxValue)]
    [int]$Ticks = 8,
    [ValidateRange(1, [int]::MaxValue)]
    [int]$ChunkCapacity = 63,
    [ValidateRange(1, [int]::MaxValue)]
    [int]$JobBudget = 1,
    [string]$DotNetSdkVersion = '8.0.428',
    [string[]]$PackageSource,
    [switch]$VulkanValidation,
    [switch]$Measure,
    [ValidateRange(0, [int]::MaxValue)]
    [int]$WarmupTicks = 8
)

$ErrorActionPreference = 'Stop'
$Policy = $Policy.ToLowerInvariant()
$Backend = $Backend.ToLowerInvariant()
$configuration = if ($Measure) { 'Release' } else { 'Debug' }
$buildConfiguration = $configuration.ToLowerInvariant()
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
$dotnetRoot = Split-Path $dotnet
$sdk = Join-Path $dotnetRoot "sdk/$DotNetSdkVersion/dotnet.dll"
if (-not (Test-Path -LiteralPath $sdk -PathType Leaf)) {
    throw "An installed .NET 8 SDK is required. Missing $sdk."
}
$framework = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'packs/Microsoft.NETCore.App.Ref') -Directory |
    Where-Object { $_.Name -match '^8\.0\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $framework) { throw 'The .NET 8 reference assemblies are required.' }

$ga = Join-Path $workspace 'artifacts/ga'
$build = Join-Path $ga 'execution-conformance-build'
$native = Join-Path $ga 'probe'
$nativeLibrary = Join-Path $native "ga-$Backend-native.dll"
$scratch = (New-Item -ItemType Directory -Force (Join-Path $ga 'execution-conformance')).FullName
$temp = (New-Item -ItemType Directory -Force (Join-Path $ga 'temp')).FullName
$environment = @{
    DOTNET_CLI_HOME = Join-Path $ga 'dotnet-home'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    NUGET_HTTP_CACHE_PATH = Join-Path $ga 'nuget-http'
    NUGET_PLUGINS_CACHE_PATH = Join-Path $ga 'nuget-plugins'
    TEMP = $temp
    TMP = $temp
    Path = $env:Path
    VK_INSTANCE_LAYERS = $env:VK_INSTANCE_LAYERS
}
if ($VulkanValidation) { $environment.VK_INSTANCE_LAYERS = 'VK_LAYER_KHRONOS_validation' }
if ($Policy -ne 'cpu-only' -and $env:VULKAN_SDK) {
    $environment.Path = (Join-Path $env:VULKAN_SDK 'Bin') + [IO.Path]::PathSeparator + $env:Path
}
$previous = @{}
foreach ($entry in $environment.GetEnumerator()) {
    $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key)
    [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
}

try {
    $slang = $null
    if ($Policy -ne 'cpu-only' -and $env:VULKAN_SDK) {
        & (Join-Path $PSScriptRoot 'probe/build-probe.ps1') -BuildOnly
        $slang = Join-Path $native 'ga-slang-probe.exe'
    }
    $buildFlags = @(
        '--artifacts-path', $build, '-c', $configuration, '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
        "-p:RestorePackagesPath=$(Join-Path $ga 'packages')"
    )
    if (-not $PackageSource) {
        $PackageSource = @(Join-Path $env:USERPROFILE '.nuget/packages')
        $checkpointFeed = Join-Path $workspace 'artifacts/serialization/feed'
        if (Test-Path -LiteralPath $checkpointFeed -PathType Container) {
            $PackageSource += $checkpointFeed
        }
    }
    foreach ($source in $PackageSource) { $buildFlags += @('--source', $source) }
    & $dotnet $sdk build (Join-Path $PSScriptRoot 'build/Ga.Build.csproj') @buildFlags
    if ($LASTEXITCODE -ne 0) { throw 'GA execution conformance compiler build failed.' }
    $cli = Join-Path $build "bin/Ga.Build/$buildConfiguration"
    $request = [ordered]@{
        sourceRoot = Join-Path $workspace 'SKECS.ExecutionConformance'
        sources = @('GaMovement.cs')
        references = @((Join-Path $cli 'SKECS.dll'), (Join-Path $cli 'SKECS.GA.dll'))
        referenceDirectories = @((Join-Path $framework.FullName 'ref/net8.0'))
        systemId = 'execution-conformance.fixed-movement'
        simulationContractId = 'execution-conformance.fixed-movement/1'
        outputDirectory = 'packages'
        artifactName = 'ga-movement'
        policy = @{ 'cpu-only' = 'cpuOnly'; 'auto' = 'auto'; 'require-gpu' = 'requireGpu' }[$Policy]
        slangAdapter = $slang
        target = $Backend
        dxcDirectory = if ($Backend -eq 'd3d12' -and $env:VULKAN_SDK) { Join-Path $env:VULKAN_SDK 'Bin' }
    }
    $requestPath = Join-Path $scratch 'request.json'
    $request | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $requestPath -Encoding utf8
    $json = & $dotnet (Join-Path $cli 'Ga.Build.dll') $requestPath
    if ($LASTEXITCODE -ne 0) {
        if ($json) { Write-Output $json }
        throw 'GA execution conformance package build failed.'
    }
    $bundle = ($json | ConvertFrom-Json).outputDirectory
    & $dotnet $sdk build (Join-Path $workspace 'SKECS.ExecutionConformance/SKECS.ExecutionConformance.csproj') @buildFlags "-p:GaExecutionConformanceBundleDirectory=$bundle"
    if ($LASTEXITCODE -ne 0) { throw 'GA execution conformance build failed.' }
    $conformanceExecutable = Join-Path $build "bin/SKECS.ExecutionConformance/$buildConfiguration/SKECS.ExecutionConformance.dll"
    $mode = @('ga')
    if ($Measure) { $mode += @('--measure', $WarmupTicks) }
    foreach ($index in $DeviceIndex) {
        & $dotnet $conformanceExecutable @mode $Policy $bundle $nativeLibrary $index $Entities $Ticks $ChunkCapacity $JobBudget $Backend
        if ($LASTEXITCODE -ne 0) { throw "GA execution conformance failed on device index $index." }
    }
}
finally {
    foreach ($entry in $previous.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }
}
