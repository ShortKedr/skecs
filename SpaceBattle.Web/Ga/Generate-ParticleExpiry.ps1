[CmdletBinding(DefaultParameterSetName = 'Web')]
param(
    [Parameter(Mandatory)][string]$GaBuild,
    [Parameter(Mandatory)][string]$ReferenceDirectory,
    [Parameter(Mandatory, ParameterSetName = 'Web')][string]$SlangCompiler,
    [Parameter(Mandatory, ParameterSetName = 'Native')][ValidateSet('vulkan', 'd3d12')][string]$Target,
    [Parameter(Mandatory, ParameterSetName = 'Native')][string]$SlangAdapter,
    [Parameter(ParameterSetName = 'Native')][string]$DxcDirectory
)

$ErrorActionPreference = 'Stop'
$build = (Resolve-Path -LiteralPath $GaBuild).Path
$buildDirectory = Split-Path -Parent $build
$references = (Resolve-Path -LiteralPath $ReferenceDirectory).Path
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$native = $PSCmdlet.ParameterSetName -eq 'Native'
$buildTarget = if ($native) { $Target } else { 'webgpu' }
$compilerOptions = @{}
if ($native) {
    $compilerOptions.slangAdapter = (Resolve-Path -LiteralPath $SlangAdapter).Path
    if ($Target -eq 'd3d12') {
        if (!$DxcDirectory) { throw 'D3D12 generation requires a pinned DXC directory.' }
        $compilerOptions.dxcDirectory = (Resolve-Path -LiteralPath $DxcDirectory).Path
    }
} else {
    $compilerOptions.slangCompilerExecutable = (Resolve-Path -LiteralPath $SlangCompiler).Path
}
$run = Join-Path $repository ".dotnet/spacebattle-ga-expiry/$([Guid]::NewGuid())"
New-Item -ItemType Directory -Path $run -Force | Out-Null
$request = Join-Path $run 'request.json'
$settings = @{
    policy = 'requireGpu'
    target = $buildTarget
    simulationContractId = 'spacebattle-particle-expiry/1'
    systemId = 'particle-expiry'
    artifactName = 'particle-expiry'
    adapterClass = 'ParticleExpiryAdapter'
    workgroupX = 64
    sourceRoot = $PSScriptRoot
    sources = @('ParticleExpiry.cs')
    referenceDirectories = @($references)
    references = @((Join-Path $buildDirectory 'SKECS.dll'), (Join-Path $buildDirectory 'SKECS.GA.dll'))
    outputDirectory = Join-Path $run 'output'
} + $compilerOptions
$settings | ConvertTo-Json -Depth 8 | Set-Content -Encoding ascii -LiteralPath $request

$report = & dotnet $build $request
if ($LASTEXITCODE -ne 0) { throw "Ga.Build failed: $report" }
$result = $report | ConvertFrom-Json
if (!$result.report.succeeded) { throw "GA generation failed: $report" }

if ($native) {
    $canonical = Join-Path $PSScriptRoot 'Bundle'
    $name = 'particle-expiry.manifest.json'
    $webManifest = Get-Content -LiteralPath (Join-Path $canonical $name) -Raw | ConvertFrom-Json
    $generated = Get-Content -LiteralPath (Join-Path $result.outputDirectory $name) -Raw | ConvertFrom-Json
    if (($webManifest.Contract | ConvertTo-Json -Depth 10 -Compress) -cne
        ($generated.Contract | ConvertTo-Json -Depth 10 -Compress)) {
        throw 'Particle expiry native and Web contracts differ.'
    }
    $codec = 'ga-row-codec.g.cs'
    $webCodec = (Get-Content -LiteralPath (Join-Path $canonical $codec) -Raw).Replace("`r`n", "`n")
    $nativeCodec = (Get-Content -LiteralPath (Join-Path $result.outputDirectory $codec) -Raw).Replace("`r`n", "`n")
    if ($webCodec -cne $nativeCodec) {
        throw 'Particle expiry native row codec differs from the checked-in Web codec.'
    }
    $sourceMap = 'particle-expiry.source-map.json'
    if ((Get-FileHash -LiteralPath (Join-Path $canonical $sourceMap)).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $result.outputDirectory $sourceMap)).Hash) {
        throw 'Particle expiry native source map differs from the checked-in Web source map.'
    }
    Write-Output $result.outputDirectory
    return
}

$files = @('ga-row-codec.g.cs', 'particle-expiry.content-id', 'particle-expiry.layout',
    'particle-expiry.manifest.json', 'particle-expiry.source-map.json', 'particle-expiry.wgsl')
foreach ($file in $files) {
    Copy-Item -LiteralPath (Join-Path $result.outputDirectory $file) -Destination (Join-Path $PSScriptRoot "Bundle/$file")
}
Write-Output $result.report.gpu.contentId
