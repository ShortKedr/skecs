param([switch]$BuildOnly, [string]$FxcDirectory, [string]$SpirvCrossExecutable)

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
if ($FxcDirectory) {
    $FxcDirectory = (Resolve-Path -LiteralPath $FxcDirectory).Path
    $fxcLibrary = Join-Path $FxcDirectory 'd3dcompiler_47.dll'
    $expectedFxcHash = '2B23EFDC8B45ADC4B79C1C0B940A906336628CD35ED80DEFD489D70D22FF6D74'
    if ((Get-FileHash -LiteralPath $fxcLibrary).Hash -ne $expectedFxcHash) {
        throw 'The D3D11 probe requires the pinned x64 FXC DLL from Windows SDK 10.0.26100.0.'
    }
}
if ($SpirvCrossExecutable) {
    $SpirvCrossExecutable = (Resolve-Path -LiteralPath $SpirvCrossExecutable).Path
    $expectedCrossHash = 'CC19353F15B3DB467D58D5E0A97ADD7A6797FBC52313C57D38B0F77BFA2F90A1'
    if ((Get-FileHash -LiteralPath $SpirvCrossExecutable).Hash -ne $expectedCrossHash) {
        throw 'The OpenGL probe requires the pinned SPIRV-Cross executable from Vulkan SDK 1.4.309.0.'
    }
    $spirvOptExecutable = Join-Path (Split-Path -Parent $SpirvCrossExecutable) 'spirv-opt.exe'
    $expectedOptHash = 'E8CED090E643AB277296039E61C19DE39C23535BDEBD31109E8E7DB23A86C303'
    if ((Get-FileHash -LiteralPath $spirvOptExecutable).Hash -ne $expectedOptHash) {
        throw 'The OpenGL probe requires the pinned SPIRV-Tools optimizer beside SPIRV-Cross.'
    }
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
    'if "%~1"=="d3d11" goto d3d11',
    'if "%~1"=="opengl" goto opengl',
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
if ($FxcDirectory -or $SpirvCrossExecutable) {
    Add-Content -LiteralPath $runner -Encoding ascii -Value 'exit /b 0'
}
if ($FxcDirectory) {
    Add-Content -LiteralPath $runner -Encoding ascii -Value @(
        ':d3d11',
        "cl $common /DSKECS_GA_D3D11_BUILD /I`"$output`" /LD /Fega-d3d11-native.dll `"$PSScriptRoot\GaD3D11Native.cpp`" /link d3d11.lib dxgi.lib d3dcompiler.lib dxguid.lib || exit /b 1",
        'exit /b 0'
    )
}
if ($SpirvCrossExecutable) {
    Add-Content -LiteralPath $runner -Encoding ascii -Value @(
        ':opengl',
        "cl $common /DSKECS_GA_GL_BUILD /I`"$output`" /LD /Fega-opengl-native.dll `"$PSScriptRoot\GaOpenGlNative.cpp`" /link opengl32.lib gdi32.lib user32.lib || exit /b 1",
        'exit /b 0'
    )
}
& $runner
if ($LASTEXITCODE -ne 0) {
    throw 'Native probe build failed.'
}
$oldPath = $env:Path
try {
    $env:Path = (Join-Path $sdk 'Bin') + [IO.Path]::PathSeparator + $oldPath
    if ($FxcDirectory) {
        $startupDxbc = Join-Path $output 'startup.api.dxbc'
        & (Join-Path $output 'ga-slang-probe.exe') compile-dxbc (Join-Path $PSScriptRoot 'StartupProbe.slang') main `
            $startupDxbc (Join-Path $output 'startup.dxbc.api.layout') $FxcDirectory
        if ($LASTEXITCODE -ne 0) {
            throw 'Slang API DXBC storage compiler probe failed.'
        }
        $bytes = [IO.File]::ReadAllBytes($startupDxbc)
        if ($bytes.Length -lt 32 -or $bytes.Length % 4 -or [BitConverter]::ToUInt32($bytes, 0) -ne 0x43425844) {
            throw 'Invalid startup DXBC container.'
        }
        @(
            'const uint32_t startupDxbc[] = {'
            for ($offset = 0; $offset -lt $bytes.Length; $offset += 4) {
                '    0x{0:x8}u,' -f [BitConverter]::ToUInt32($bytes, $offset)
            }
            '};'
        ) | Set-Content -LiteralPath (Join-Path $output 'StartupDxbc.h') -Encoding ascii
        & $runner d3d11
        if ($LASTEXITCODE -ne 0) {
            throw 'D3D11 native probe build failed.'
        }
    }
    if ($SpirvCrossExecutable) {
        $startupSpirv = Join-Path $output 'startup-gl.spv'
        $optimizedSpirv = Join-Path $output 'startup-gl.optimized.spv'
        $startupGlsl = Join-Path $output 'startup.gl430.glsl'
        & (Join-Path $output 'ga-slang-probe.exe') compile (Join-Path $PSScriptRoot 'StartupProbe.slang') main `
            $startupSpirv (Join-Path $output 'startup-gl.layout')
        if ($LASTEXITCODE -ne 0) { throw 'Slang failed to compile the OpenGL startup intermediate.' }
        & $spirvOptExecutable --target-env=vulkan1.1 -O $startupSpirv -o $optimizedSpirv
        if ($LASTEXITCODE -ne 0) { throw 'SPIRV-Tools failed to optimize the OpenGL startup intermediate.' }
        & (Join-Path $sdk 'Bin/spirv-val.exe') --target-env vulkan1.1 $optimizedSpirv
        if ($LASTEXITCODE -ne 0) { throw 'The optimized OpenGL startup intermediate is invalid.' }
        & $SpirvCrossExecutable $optimizedSpirv --version 430 --no-es --entry main --stage comp --output $startupGlsl
        if ($LASTEXITCODE -ne 0) { throw 'SPIRV-Cross failed to compile the OpenGL startup shader.' }
        $glsl = [IO.File]::ReadAllText($startupGlsl).ReplaceLineEndings("`n")
        if (!$glsl.StartsWith("#version 430`n", [StringComparison]::Ordinal) -or $glsl.Contains(')GA_STARTUP"')) {
            throw 'Invalid GLSL 430 startup shader.'
        }
        @('const char startupGlsl[] = R"GA_STARTUP(' + $glsl + ')GA_STARTUP";') |
            Set-Content -LiteralPath (Join-Path $output 'StartupGlsl.h') -Encoding ascii
        $startupGlslEs = Join-Path $output 'startup.es310.glsl'
        & (Join-Path $sdk 'Bin/spirv-val.exe') --target-env vulkan1.1 $startupSpirv
        if ($LASTEXITCODE -ne 0) { throw 'The OpenGL ES startup intermediate is invalid.' }
        & $SpirvCrossExecutable $startupSpirv --version 310 --es --entry main --stage comp --output $startupGlslEs
        if ($LASTEXITCODE -ne 0) { throw 'SPIRV-Cross failed to compile the OpenGL ES startup shader.' }
        $gles = [IO.File]::ReadAllText($startupGlslEs).ReplaceLineEndings("`n")
        if (!$gles.StartsWith("#version 310 es`n", [StringComparison]::Ordinal) -or $gles.Contains(')GA_STARTUP"')) {
            throw 'Invalid GLSL ES 310 startup shader.'
        }
        @('constexpr char startupGlslEs[] = R"GA_STARTUP(' + $gles + ')GA_STARTUP";') |
            Set-Content -LiteralPath (Join-Path $output 'StartupGlslEs.h') -Encoding ascii
        & $runner opengl
        if ($LASTEXITCODE -ne 0) { throw 'OpenGL native probe build failed.' }
    }
    if ($BuildOnly) {
        return
    }
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
    $layouts = @('startup.api.layout', 'startup.dxil.api.layout')
    if ($FxcDirectory) {
        $layouts += 'startup.dxbc.api.layout'
    }
    foreach ($layout in $layouts) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $output $layout))
        if ($bytes.Length -eq 0 -or $bytes[-1] -ne 10 -or $bytes -contains 13) {
            throw "Expected LF-only storage reflection in $layout."
        }
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
