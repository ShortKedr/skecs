param(
    [Parameter(Mandatory)][string]$GaBuild,
    [Parameter(Mandatory)][string]$ReferenceDirectory,
    [Parameter(Mandatory)][string]$SlangCompiler
)

$ErrorActionPreference = 'Stop'
$build = (Resolve-Path -LiteralPath $GaBuild).Path
$buildDirectory = Split-Path -Parent $build
$references = (Resolve-Path -LiteralPath $ReferenceDirectory).Path
$slang = (Resolve-Path -LiteralPath $SlangCompiler).Path
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$run = Join-Path $repository ".dotnet/spacebattle-ga-expiry/$([Guid]::NewGuid())"
New-Item -ItemType Directory -Path $run -Force | Out-Null
$request = Join-Path $run 'request.json'
@{
    policy = 'requireGpu'
    target = 'webgpu'
    simulationContractId = 'spacebattle-particle-expiry/1'
    systemId = 'particle-expiry'
    artifactName = 'particle-expiry'
    adapterClass = 'ParticleExpiryAdapter'
    workgroupX = 64
    sourceRoot = $PSScriptRoot
    sources = @('ParticleExpiry.cs')
    referenceDirectories = @($references)
    references = @((Join-Path $buildDirectory 'SKECS.dll'), (Join-Path $buildDirectory 'SKECS.GA.dll'))
    slangCompilerExecutable = $slang
    outputDirectory = Join-Path $run 'output'
} | ConvertTo-Json -Depth 8 | Set-Content -Encoding ascii -LiteralPath $request

$report = & dotnet $build $request
if ($LASTEXITCODE -ne 0) { throw "Ga.Build failed: $report" }
$result = $report | ConvertFrom-Json
if (!$result.report.succeeded) { throw "GA generation failed: $report" }

$files = @('ga-row-codec.g.cs', 'particle-expiry.content-id', 'particle-expiry.layout',
    'particle-expiry.manifest.json', 'particle-expiry.source-map.json', 'particle-expiry.wgsl')
foreach ($file in $files) {
    Copy-Item -LiteralPath (Join-Path $result.outputDirectory $file) -Destination (Join-Path $PSScriptRoot "Bundle/$file")
}
Write-Output $result.report.gpu.contentId
