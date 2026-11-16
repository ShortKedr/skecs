param([string]$ConformanceDirectory)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$output = Join-Path $workspace 'artifacts/ga/probe'
if (-not $ConformanceDirectory) {
    $ConformanceDirectory = Join-Path $workspace 'artifacts/ga/conformance'
}
$ConformanceDirectory = (Resolve-Path $ConformanceDirectory).Path
$tool = Join-Path $output 'ga-slang-probe.exe'
if (-not (Test-Path -LiteralPath $tool)) {
    throw 'Run build-probe.ps1 first.'
}
if (-not $env:VULKAN_SDK) {
    throw 'Set VULKAN_SDK to the local SDK used by build-probe.ps1.'
}
$sdkBin = Join-Path (Resolve-Path $env:VULKAN_SDK).Path 'Bin'

function Compare-Dispatch {
    param($Case, $Kind, $Binary, $Bindings, $InputPath, $ExpectedOutput, $ExpectedFaults)

    $rowCount = [BitConverter]::ToUInt32([IO.File]::ReadAllBytes($InputPath), 0)
    $outputPath = Join-Path $output "$Kind-$Case.d3d12.out"
    $faultPath = Join-Path $output "$Kind-$Case.d3d12.fault"
    $initial = [byte[]]::new((Get-Item -LiteralPath $ExpectedOutput).Length)
    if ($Kind -eq 'integer') {
        for ($i = 0; $i -lt $initial.Length; $i += 4) {
            $initial[$i] = 0x98
            $initial[$i + 1] = 0xba
            $initial[$i + 2] = 0xdc
            $initial[$i + 3] = 0xfe
        }
    }
    [IO.File]::WriteAllBytes($outputPath, $initial)
    [IO.File]::WriteAllBytes($faultPath, [byte[]]::new((Get-Item -LiteralPath $ExpectedFaults).Length))

    $dispatch = @('dispatch-dxil', $Binary, [string][Math]::Ceiling($rowCount / 64))
    $paths = @{ gaInput = $InputPath; gaOutput = $outputPath; gaFaults = $faultPath }
    foreach ($binding in $Bindings) {
        $dispatch += @($binding[2], $binding[3], $binding[6], $paths[$binding[1]])
    }
    & $tool @dispatch
    if ($LASTEXITCODE -ne 0) {
        throw "D3D12 dispatch failed for $Kind with $rowCount rows."
    }
    if ((Get-FileHash -LiteralPath $outputPath).Hash -ne (Get-FileHash -LiteralPath $ExpectedOutput).Hash -or
        (Get-FileHash -LiteralPath $faultPath).Hash -ne (Get-FileHash -LiteralPath $ExpectedFaults).Hash) {
        throw "D3D12 readback differs from checked Vulkan/CPU output for $Kind with $rowCount rows."
    }
    Write-Output "$Kind with $rowCount rows and fault records matches checked Vulkan/CPU output."
}

$oldPath = $env:Path
try {
    $env:Path = $sdkBin + [IO.Path]::PathSeparator + $oldPath
    $dxcVersion = (& (Join-Path $sdkBin 'dxc.exe') --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $dxcVersion -ne 'dxcompiler.dll: 1.9 - 1.8.0.4806 (75a029d95)') {
        throw 'The generated D3D12 probe requires DXC 1.8.0.4806 (75a029d95).'
    }
    foreach ($kind in 'integer', 'fixed') {
        $shader = Join-Path $ConformanceDirectory "$kind.slang"
        $binary = Join-Path $output "$kind.generated.dxil"
        $layout = Join-Path $output "$kind.generated.dxil.layout"
        & $tool compile-dxil $shader main $binary $layout $sdkBin
        if ($LASTEXITCODE -ne 0) {
            throw "Could not compile generated $kind Slang to DXIL."
        }
        $lines = @(Get-Content -LiteralPath $layout)
        $bindings = @($lines | Where-Object { $_.StartsWith("binding`t") } | ForEach-Object { ,($_ -split "`t") })
        $access = @{ gaInput = 'r'; gaOutput = 'rw'; gaFaults = 'rw' }
        if ($bindings.Count -ne 3 -or ($bindings[0..2] | ForEach-Object { $_[1] } | Sort-Object -Unique).Count -ne 3 -or
            $lines -cnotcontains "workgroup`t64`t1`t1" -or $lines -cnotcontains "target`tdxil`tsm_6_0") {
            throw 'Unexpected reflected D3D12 compute layout.'
        }
        foreach ($binding in $bindings) {
            if ($binding.Count -ne 10 -or $binding[4] -ne '1' -or $binding[5] -ne 'storage' -or
                $binding[6] -cne $access[$binding[1]] -or $binding[7] -ne 'used' -or $binding[8] -ne '4' -or $binding[9] -ne '4') {
                throw 'Unexpected reflected D3D12 storage binding.'
            }
        }
        if ($kind -eq 'integer') {
            foreach ($case in 1, 65, 131) {
                $prefix = Join-Path $ConformanceDirectory "device-0-$case"
                Compare-Dispatch $case $kind $binary $bindings "$prefix.in" "$prefix.out" "$prefix.fault"
            }
        } else {
            $prefix = Join-Path $ConformanceDirectory 'fixed'
            Compare-Dispatch 'vectors' $kind $binary $bindings "$prefix.in" "$prefix-0.out" "$prefix-0.fault"
        }
    }
}
finally {
    $env:Path = $oldPath
}
