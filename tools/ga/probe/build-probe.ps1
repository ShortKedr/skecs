param([switch]$BuildOnly)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$output = (New-Item -ItemType Directory -Force (Join-Path $workspace 'artifacts/ga/probe')).FullName
if (-not $env:VULKAN_SDK) {
    throw 'Set VULKAN_SDK to the local Vulkan SDK with Slang 2025.6.1.'
}
$sdk = (Resolve-Path $env:VULKAN_SDK).Path
$slangVersion = (& (Join-Path $sdk 'Bin/slangc.exe') -version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $slangVersion -ne '2025.6.1') {
    throw 'Slang 2025.6.1 is required for this probe.'
}
$dxcVersion = (& (Join-Path $sdk 'Bin/dxc.exe') --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $dxcVersion -ne 'dxcompiler.dll: 1.9 - 1.8.0.4806 (75a029d95)') {
    throw 'The Windows D3D12 probe requires DXC 1.8.0.4806 (75a029d95).'
}
$windowsKits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$windowsVersion = Get-ChildItem (Join-Path $windowsKits 'Include') -Directory |
    Where-Object {
        (Test-Path (Join-Path $_.FullName 'ucrt/stddef.h')) -and
        (Test-Path (Join-Path $windowsKits "Lib/$($_.Name)/ucrt/x64/ucrt.lib")) -and
        (Test-Path (Join-Path $windowsKits "Lib/$($_.Name)/um/x64/kernel32.lib"))
    } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 -ExpandProperty Name
if (-not $windowsVersion) {
    throw 'A Windows SDK with x64 libraries is required.'
}
$windowsInclude = Join-Path $windowsKits "Include/$windowsVersion"
$windowsLib = Join-Path $windowsKits "Lib/$windowsVersion"
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vcvars = & $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'VC\Auxiliary\Build\vcvars64.bat' | Select-Object -First 1
if (-not $vcvars) {
    throw 'Visual Studio C++ x64 tools are required.'
}

$runner = Join-Path $output 'build.cmd'
$common = '/nologo /std:c++20 /EHsc /W4 /O2 /MT /utf-8 /DNOMINMAX'
Set-Content -LiteralPath $runner -Encoding ascii -Value @(
    '@echo off',
    "call `"$vcvars`" || exit /b 1",
    "set `"TEMP=$output`"",
    "set `"TMP=$output`"",
    "set `"INCLUDE=%INCLUDE%;$windowsInclude\ucrt;$windowsInclude\shared;$windowsInclude\um;$windowsInclude\winrt`"",
    "set `"LIB=%LIB%;$windowsLib\ucrt\x64;$windowsLib\um\x64`"",
    "cd /d `"$output`" || exit /b 1",
    "cl $common /I`"$sdk\Include`" /c `"$PSScriptRoot\VulkanDispatch.cpp`" || exit /b 1",
    "cl $common /c `"$PSScriptRoot\D3D12Dispatch.cpp`" || exit /b 1",
    "cl $common /I`"$sdk\Include`" /Fega-slang-probe.exe `"$PSScriptRoot\Probe.cpp`" D3D12Dispatch.obj VulkanDispatch.obj /link /LIBPATH:`"$sdk\Lib`" vulkan-1.lib slang.lib d3d12.lib dxgi.lib || exit /b 1",
    "cl $common /I`"$sdk\Include`" /Fega-vulkan.exe `"$PSScriptRoot\VulkanCli.cpp`" VulkanDispatch.obj /link /LIBPATH:`"$sdk\Lib`" vulkan-1.lib || exit /b 1",
    "`"$sdk\Bin\slangc.exe`" `"$PSScriptRoot\StartupProbe.slang`" -entry main -target spirv -profile spirv_1_3 -source-embed-name startupSpirv -source-embed-style u32 -o StartupSpirv.h || exit /b 1",
    "cl $common /DSKECS_GA_VK_BUILD /I`"$sdk\Include`" /I`"$output`" /LD /Fega-vulkan-native.dll `"$PSScriptRoot\GaVulkanNative.cpp`" VulkanDispatch.obj /link /LIBPATH:`"$sdk\Lib`" vulkan-1.lib || exit /b 1",
    "cl $common /Fega-native-probe.exe `"$PSScriptRoot\NativeProbe.cpp`" ga-vulkan-native.lib || exit /b 1",
    "`"$sdk\Bin\slangc.exe`" `"$PSScriptRoot\StartupProbe.slang`" -entry main -target dxil -profile sm_6_0 -source-embed-name startupDxil -source-embed-style u32 -o StartupDxil.h || exit /b 1",
    "cl $common /DSKECS_GA_D3D12_BUILD /I`"$output`" /LD /Fega-d3d12-native.dll `"$PSScriptRoot\GaD3D12Native.cpp`" D3D12Dispatch.obj /link d3d12.lib dxgi.lib || exit /b 1",
    "cl $common /Fega-d3d12-native-probe.exe `"$PSScriptRoot\D3D12NativeProbe.cpp`" ga-d3d12-native.lib || exit /b 1"
)
& $runner
if ($LASTEXITCODE -ne 0) {
    throw 'Native probe build failed.'
}
if ($BuildOnly) {
    return
}

$oldPath = $env:Path
try {
    $env:Path = (Join-Path $sdk 'Bin') + [IO.Path]::PathSeparator + $oldPath
    $shader = Join-Path $PSScriptRoot 'Probe.slang'
    & (Join-Path $output 'ga-slang-probe.exe') $shader $output
    if ($LASTEXITCODE -ne 0) {
        throw 'Slang API or Vulkan readback probe failed.'
    }
    Write-Output $dxcVersion
    & (Join-Path $output 'ga-slang-probe.exe') d3d12 $shader $output (Join-Path $sdk 'Bin')
    if ($LASTEXITCODE -ne 0) {
        throw 'Slang DXIL or D3D12 readback probe failed.'
    }
    $startupArtifact = Join-Path $output 'startup.api.spv'
    & (Join-Path $output 'ga-slang-probe.exe') compile (Join-Path $PSScriptRoot 'StartupProbe.slang') main $startupArtifact (Join-Path $output 'startup.api.layout')
    if ($LASTEXITCODE -ne 0) {
        throw 'Slang API storage compiler probe failed.'
    }
    foreach ($artifact in 'initial.api.spv', 'permuted.api.spv', 'startup.api.spv') {
        & (Join-Path $sdk 'Bin/spirv-val.exe') --target-env vulkan1.1 (Join-Path $output $artifact)
        if ($LASTEXITCODE -ne 0) {
            throw "SPIR-V validation failed for $artifact."
        }
    }
    & (Join-Path $output 'ga-native-probe.exe') $startupArtifact
    if ($LASTEXITCODE -ne 0) {
        throw 'Native Vulkan ABI probe failed.'
    }
    $startupDxil = Join-Path $output 'startup.api.dxil'
    & (Join-Path $output 'ga-slang-probe.exe') compile-dxil (Join-Path $PSScriptRoot 'StartupProbe.slang') main $startupDxil (Join-Path $output 'startup.dxil.api.layout') (Join-Path $sdk 'Bin')
    if ($LASTEXITCODE -ne 0) {
        throw 'Slang API DXIL storage compiler probe failed.'
    }
    & (Join-Path $output 'ga-d3d12-native-probe.exe') $startupDxil
    if ($LASTEXITCODE -ne 0) {
        throw 'Native D3D12 ABI probe failed.'
    }
    $cliLayout = Join-Path $output 'initial.cli.json'
    & (Join-Path $sdk 'Bin/slangc.exe') $shader -entry main -DPERMUTE_BINDINGS=0 -DPROBE_D3D12=0 -target spirv -profile spirv_1_3 -reflection-json $cliLayout -o (Join-Path $output 'initial.cli.spv')
    if ($LASTEXITCODE -ne 0) {
        throw 'CLI reflection probe failed.'
    }
    $cliData = Get-Content -LiteralPath $cliLayout -Raw | ConvertFrom-Json
    if ($cliData.version -and $cliData.globalScope -and $cliData.entryPoints[0].scope) {
        throw 'The pinned CLI schema changed. Reassess CLI/API reflection parity.'
    }
    Write-Output 'Slang CLI JSON omits scope/container metadata required by this probe.'
}
finally {
    $env:Path = $oldPath
}
