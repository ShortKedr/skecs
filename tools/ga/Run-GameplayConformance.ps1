param(
    [Parameter(Mandatory)][string]$FixtureDirectory,
    [Parameter(Mandatory)][string]$SystemId,
    [Parameter(Mandatory)][string[]]$KernelSources,
    [Parameter(Mandatory)][string]$AdapterClass,
    [Parameter(Mandatory)][string]$ProjectName,
    [ValidateSet('vulkan', 'd3d12', 'd3d11', 'opengl', 'gles')]
    [ValidateNotNullOrEmpty()]
    [string[]]$Backend = @('vulkan', 'd3d12'),
    [ValidateRange(0, 2147483647)]
    [ValidateNotNullOrEmpty()]
    [int[]]$DeviceIndex = @(0),
    [string[]]$Profile = @(''),
    [ValidateRange(1, 1024)]
    [int]$WorkgroupX = 64,
    [string]$DotNetSdkVersion = '8.0.428',
    [string]$ProbeDirectory,
    [string[]]$AdditionalPackageSource,
    [switch]$VulkanValidation,
    [switch]$CpuOnly,
    [switch]$BuildOnly,
    [string]$FxcDirectory,
    [string]$SpirvCrossExecutable,
    [string]$AngleDirectory,
    [ValidateSet('D3D11', 'Vulkan', 'OpenGl')]
    [string]$AngleRenderer
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path "$PSScriptRoot/../..").Path
$dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
$dotnetRoot = Split-Path $dotnet
$sdk = (Resolve-Path "$dotnetRoot/sdk/$DotNetSdkVersion/dotnet.dll").Path
$framework = Get-ChildItem "$dotnetRoot/packs/Microsoft.NETCore.App.Ref" -Directory |
    Where-Object Name -Match '^8\.0\.\d+$' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$framework) { throw 'The .NET 8 reference pack is required.' }
if (!$CpuOnly -and !$env:VULKAN_SDK) { throw 'The Windows Vulkan SDK is required for GPU routes.' }
if (!$CpuOnly -and $Backend -contains 'd3d11') {
    if (!$FxcDirectory) { throw 'Set FxcDirectory to the pinned Windows SDK x64 compiler directory for D3D11.' }
    $FxcDirectory = (Resolve-Path -LiteralPath $FxcDirectory).Path
}
if (!$CpuOnly -and ($Backend -contains 'opengl' -or $Backend -contains 'gles')) {
    if (!$SpirvCrossExecutable) { throw 'Set SpirvCrossExecutable to the pinned translator for OpenGL and OpenGL ES.' }
    $SpirvCrossExecutable = (Resolve-Path -LiteralPath $SpirvCrossExecutable).Path
}
$angle = $null
if (!$CpuOnly -and !$BuildOnly -and $Backend -contains 'gles') {
    if (!$AngleDirectory -or !$AngleRenderer) { throw 'Set AngleDirectory and AngleRenderer to run OpenGL ES conformance.' }
    $angle = Get-Item -LiteralPath $AngleDirectory
    if (!$angle.PSIsContainer) { throw 'AngleDirectory must be a directory.' }
}

$artifacts = (New-Item -ItemType Directory -Force "$source/artifacts/ga/$SystemId").FullName
$build = "$artifacts/build"
$scratch = (New-Item -ItemType Directory -Force "$artifacts/temp").FullName
$nativeTools = (New-Item -ItemType Directory -Force "$artifacts/native-tools").FullName
$nugetConfig = "$artifacts/NuGet.Config"
'<configuration><packageSources><clear /></packageSources></configuration>' |
    Set-Content -LiteralPath $nugetConfig -Encoding utf8
$environment = @{
    APPDATA = $scratch
    LOCALAPPDATA = $scratch
    TEMP = $scratch
    TMP = $scratch
    DOTNET_CLI_HOME = "$artifacts/dotnet-home"
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    Path = if ($CpuOnly) { $env:Path } else { "$env:VULKAN_SDK/Bin;$env:Path" }
    VK_INSTANCE_LAYERS = $env:VK_INSTANCE_LAYERS
}
if ($angle) {
    $environment.SKECS_GA_ANGLE_DIRECTORY = $angle.FullName
    $environment.SKECS_GA_ANGLE_RENDERER = $AngleRenderer
}
$previous = @{}
foreach ($setting in $environment.GetEnumerator()) {
    $previous[$setting.Key] = [Environment]::GetEnvironmentVariable($setting.Key)
    [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value)
}

try {
    if (!$CpuOnly) {
        if (!$ProbeDirectory) {
            & "$PSScriptRoot/probe/build-probe.ps1" -BuildOnly -FxcDirectory $FxcDirectory -SpirvCrossExecutable $SpirvCrossExecutable
            if ($LASTEXITCODE) { throw 'GA native probe build failed.' }
            $ProbeDirectory = "$source/artifacts/ga/probe"
        }
        $binaries = (Resolve-Path -LiteralPath $ProbeDirectory).Path
        Copy-Item -LiteralPath "$binaries/ga-slang-probe.exe" -Destination $nativeTools -Force
        foreach ($name in 'slang.dll', 'slang-glsl-module.dll', 'slang-glslang.dll', 'dxcompiler.dll', 'dxil.dll') {
            $library = Join-Path "$env:VULKAN_SDK/Bin" $name
            if (Test-Path -LiteralPath $library) {
                Copy-Item -LiteralPath $library -Destination $nativeTools -Force
            }
        }
    }

    $flags = @('-c', 'Release', '--artifacts-path', $build, '-m:1', '-nr:false',
        '-p:UseSharedCompilation=false', "-p:TargetPlatformSdkPath=$scratch/", '-p:TargetPlatformDisplayName=Windows',
        '-p:NuGetAudit=false', '--source', "$env:USERPROFILE/.nuget/packages",
        "-p:RestoreConfigFile=$nugetConfig", "-p:RestorePackagesPath=$artifacts/packages")
    foreach ($packageSource in $AdditionalPackageSource) {
        $flags += @('--source', $packageSource)
    }
    & $dotnet $sdk build "$source/tools/ga/build/Ga.Build.csproj" @flags
    if ($LASTEXITCODE) { throw 'GA compiler build failed.' }

    $cli = "$build/bin/Ga.Build/release"
    $bundles = @{}
    $targets = @(if ($CpuOnly) { 'cpu' } else { $Backend })
    foreach ($target in $targets) {
        $request = "$artifacts/$target.json"
        @{
            sourceRoot = $FixtureDirectory
            sources = $KernelSources
            references = @("$cli/SKECS.dll", "$cli/SKECS.GA.dll")
            referenceDirectories = @("$($framework.FullName)/ref/net8.0")
            systemId = $SystemId
            simulationContractId = "$SystemId/1"
            outputDirectory = 'bundles'
            artifactName = $SystemId
            adapterClass = $AdapterClass
            policy = if ($CpuOnly) { 'cpuOnly' } else { 'requireGpu' }
            slangAdapter = if (!$CpuOnly) { "$nativeTools/ga-slang-probe.exe" }
            target = if ($CpuOnly) { 'vulkan' } else { $target }
            workgroupX = $WorkgroupX
            dxcDirectory = if ($target -eq 'd3d12') { $nativeTools }
            fxcDirectory = if ($target -eq 'd3d11') { $FxcDirectory }
            spirvCrossExecutable = if ($target -in 'opengl', 'gles') { $SpirvCrossExecutable }
        } | ConvertTo-Json | Set-Content -LiteralPath $request -Encoding utf8
        $report = & $dotnet "$cli/Ga.Build.dll" $request
        if ($LASTEXITCODE) { throw "GA bundle build failed for $target. $report" }
        $bundles[$target] = ($report | ConvertFrom-Json).outputDirectory
    }

    $bundle = $bundles[$targets[0]]
    foreach ($file in 'ga-adapter.g.cs', 'ga-row-codec.g.cs') {
        $expected = (Get-FileHash -LiteralPath "$bundle/$file").Hash
        foreach ($target in $targets) {
            if ((Get-FileHash -LiteralPath "$($bundles[$target])/$file").Hash -ne $expected) {
                throw "Generated adapters differ for $file on $target."
            }
        }
    }
    & $dotnet $sdk build "$FixtureDirectory/$ProjectName.csproj" @flags "-p:GaSource=$source" "-p:Bundle=$bundle"
    if ($LASTEXITCODE) { throw "$SystemId harness build failed." }
    if ($BuildOnly) { return }

    $assembly = "$build/bin/$ProjectName/release/$ProjectName.dll"
    if ($CpuOnly) {
        foreach ($run in $Profile) {
            $arguments = @('cpu')
            if ($run) { $arguments += $run }
            & $dotnet $assembly @arguments
            if ($LASTEXITCODE) { throw "$SystemId $run CPU conformance failed." }
        }
        return
    }

    foreach ($target in $Backend) {
        $nativeTarget = if ($target -eq 'gles') { 'opengl' } else { $target }
        $env:VK_INSTANCE_LAYERS = if ($target -eq 'vulkan' -and $VulkanValidation) {
            'VK_LAYER_KHRONOS_validation'
        } else {
            $previous.VK_INSTANCE_LAYERS
        }
        foreach ($index in $DeviceIndex) {
            foreach ($run in $Profile) {
                $arguments = @($target, $bundles[$target], "$binaries/ga-$nativeTarget-native.dll", $index)
                if ($run) { $arguments += $run }
                & $dotnet $assembly @arguments
                if ($LASTEXITCODE) { throw "$SystemId $run failed on $target device $index." }
            }
        }
    }
}
finally {
    foreach ($setting in $previous.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value)
    }
}
