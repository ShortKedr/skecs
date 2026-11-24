param(
    [string]$SourceRoot = "$PSScriptRoot/../../..",
    [string]$EvidenceDirectory = "$PSScriptRoot/../../../artifacts/ga/frontend-conformance",
    [switch]$DirectOnly,
    [switch]$PackageCpu,
    [ValidateSet('vulkan', 'd3d12')]
    [string[]]$GpuBackend,
    [string]$ProbeDirectory,
    [string]$VulkanSdk = $env:VULKAN_SDK,
    [ValidateRange(0, 2147483647)]
    [int]$DeviceIndex = 0
)

$ErrorActionPreference = 'Stop'
$fixture = $PSScriptRoot
$project = Join-Path $fixture 'FrontendConformance.csproj'
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$evidence = New-Item -ItemType Directory -Path $EvidenceDirectory -Force

function Build([string]$Project) {
    & dotnet build $Project -c Release -m:1 -v:minimal -p:NuGetAudit=false "-p:GaSourceRoot=$SourceRoot"
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $Project." }
}

Build $project
$binaries = Join-Path $fixture 'bin/Release/net8.0'
$runner = Join-Path $binaries 'FrontendConformance.dll'
[string[]]$runnerArgs = @(if ($DirectOnly) { '--direct' })
$logName = if ($DirectOnly) { 'direct-csharp.txt' } else { 'frontend-conformance.txt' }
& dotnet $runner @runnerArgs | Tee-Object -FilePath (Join-Path $evidence.FullName $logName)
if ($LASTEXITCODE -ne 0) { throw 'Frontend conformance failed.' }

if ($PackageCpu) {
    $buildDirectory = Join-Path $SourceRoot 'tools/ga/build'
    Build (Join-Path $buildDirectory 'Ga.Build.csproj')
    $buildTool = Join-Path $buildDirectory 'bin/Release/net8.0/Ga.Build.dll'
    $dotnetRoot = Split-Path -Parent (Get-Command dotnet -CommandType Application).Source
    $packRoot = Join-Path $dotnetRoot 'packs/Microsoft.NETCore.App.Ref'
    $referencePack = Get-ChildItem -LiteralPath $packRoot -Directory -Filter '8.*' |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if ($null -eq $referencePack) { throw '.NET 8 framework reference assemblies are required for packaging.' }

    $kernelPath = Join-Path $fixture 'Kernels.cs'
    $kernelSource = Get-Content -LiteralPath $kernelPath -Raw
    $systemIds = [regex]::Matches($kernelSource, 'SecsSystem\(SystemId = "([^"]+)"\)') |
        ForEach-Object { $_.Groups[1].Value }
    if (!$systemIds) { throw 'The fixture has no marked kernel system IDs.' }
    $template = Get-Content -LiteralPath (Join-Path $fixture 'ga-request.template.json') -Raw
    $packageDirectory = New-Item -ItemType Directory -Path (Join-Path $evidence.FullName 'packages') -Force
    $reports = foreach ($systemId in $systemIds) {
        $name = $systemId.Split('.')[-1]
        $request = $template | ConvertFrom-Json -AsHashtable
        $request.sourceRoot = $fixture
        $request.references = @((Join-Path $binaries 'SKECS.dll'), (Join-Path $binaries 'SKECS.GA.dll'))
        $request.referenceDirectories = @((Join-Path $referencePack.FullName 'ref/net8.0'))
        $request.systemId = $systemId
        $request.outputDirectory = $packageDirectory.FullName
        $request.artifactName = $name
        $requestPath = Join-Path $evidence.FullName "$name.request.json"
        $request | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $requestPath -Encoding utf8

        $output = & dotnet $buildTool $requestPath
        if ($LASTEXITCODE -ne 0) { throw "Ga.Build failed for $systemId." }
        $report = $output | ConvertFrom-Json
        if (!$report.report.succeeded -or $report.report.gpu.status -ne 'notRequested') {
            throw "Unexpected CPU packaging result for $systemId."
        }
        $report
    }
    $reports | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidence.FullName 'cpu-packages.json') -Encoding utf8
    Write-Host "CPU-only packaging passed for $($reports.Count) GA kernels."
}

if ($GpuBackend) {
    if ($DirectOnly) { throw 'GPU conformance requires the full frontend lane.' }
    if (!$ProbeDirectory) { $ProbeDirectory = Join-Path $SourceRoot 'artifacts/ga/probe' }
    if (!$VulkanSdk) { throw 'Set VulkanSdk to the SDK used to build the GA probes.' }
    $probes = (Resolve-Path -LiteralPath $ProbeDirectory).Path
    $gpuTools = (Resolve-Path -LiteralPath (Join-Path $VulkanSdk 'Bin')).Path
    $previousPath = $env:Path
    try {
        $env:Path = $gpuTools + [IO.Path]::PathSeparator + $previousPath
        foreach ($backend in $GpuBackend) {
            $gpuOutput = Join-Path $evidence.FullName $backend
            $gpuLog = Join-Path $evidence.FullName "$backend-conformance.txt"
            & dotnet $runner --gpu $backend $probes $gpuTools $gpuOutput $DeviceIndex |
                Tee-Object -FilePath $gpuLog
            if ($LASTEXITCODE -ne 0) { throw "$backend frontend conformance failed." }
        }
    }
    finally {
        $env:Path = $previousPath
    }
}
