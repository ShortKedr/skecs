[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $BuildCli,
    [Parameter(Mandatory)][string] $DotnetSdk,
    [Parameter(Mandatory)][string] $FrameworkReferences,
    [string] $SlangAdapter,
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $workspace 'artifacts/ga/build-cli-sample'
}
$scratch = [IO.Path]::GetFullPath($OutputDirectory)
$relative = [IO.Path]::GetRelativePath($workspace, $scratch).Replace('\', '/')
if ($relative -eq '.' -or $relative -eq '..' -or $relative.StartsWith('../') -or [IO.Path]::IsPathRooted($relative)) {
    throw 'The sample output directory must be inside the SKECS workspace.'
}
$cli = (Resolve-Path -LiteralPath $BuildCli).Path
$sdk = (Resolve-Path -LiteralPath $DotnetSdk).Path
$refs = (Resolve-Path -LiteralPath $FrameworkReferences).Path
$skecs = (Resolve-Path -LiteralPath (Join-Path (Split-Path $cli) 'SKECS.dll')).Path
$ga = (Resolve-Path -LiteralPath (Join-Path (Split-Path $cli) 'SKECS.GA.dll')).Path
$pinnedAdapter = if ($SlangAdapter) { (Resolve-Path -LiteralPath $SlangAdapter).Path }

New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $scratch 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_NOLOGO = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
$requestPath = Join-Path $scratch 'request.json'
$movementPath = Join-Path $scratch 'Movement.cs'
@'
using SKECS.GA;

namespace Consumer;

public struct Position { public int X; }
public struct Step { public int Amount; }

[SecsSystem(SystemId = "samples.checked-movement")]
public partial struct Movement
{
    public void Execute(ref Position position, in Step step)
    {
#if GA_SAMPLE
        position.X += step.Amount;
#else
#error The GA sample expects the consumer's preprocessor options.
#endif
    }
}
'@ | Set-Content -LiteralPath $movementPath -Encoding utf8

$request = [ordered]@{
    sources = @('Movement.cs')
    references = @($skecs, $ga)
    referenceDirectories = @($refs)
    systemId = 'samples.checked-movement'
    simulationContractId = 'samples.movement/1'
    outputDirectory = 'packages'
    artifactName = 'movement'
    adapterClass = 'ConsumerMovement'
    policy = 'auto'
    checkOverflow = $true
    languageVersion = '12.0'
    defines = @('GA_SAMPLE')
}

function Invoke-GaSample([int] $ExpectedExit) {
    $request | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $requestPath -Encoding utf8
    $json = & dotnet $cli $requestPath
    if ($LASTEXITCODE -ne $ExpectedExit) {
        throw "Unexpected GA CLI exit $LASTEXITCODE."
    }
    return $json | ConvertFrom-Json
}

$cpu = Invoke-GaSample 0
$again = Invoke-GaSample 0
if (-not $cpu.report.succeeded -or $cpu.report.gpu.code -cne 'SlangAdapterMissing' -or
    $again.contentId -cne $cpu.contentId) {
    throw 'The Auto build failed its CPU fallback or reproducibility check.'
}
$request.policy = 'requireGpu'
$strict = Invoke-GaSample 1
if ($strict.report.succeeded -or $strict.report.gpu.code -cne 'SlangAdapterMissing') {
    throw 'RequireGpu failed to report a missing GPU compiler.'
}
if ($pinnedAdapter) {
    $request.slangAdapter = $pinnedAdapter
    $gpu = Invoke-GaSample 0
    if ($gpu.report.gpu.status -cne 'emitted' -or -not (Test-Path -LiteralPath (Join-Path $gpu.outputDirectory $gpu.report.gpu.manifest))) {
        throw 'The strict GPU build did not publish the manifested shader package.'
    }
    Write-Output "Vulkan artifact $($gpu.report.gpu.contentId)"
}

$hostDirectory = Join-Path $scratch 'consumer'
New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
$adapterPath = Join-Path $cpu.outputDirectory $cpu.report.cpuAdapter
$skecsXml = [Security.SecurityElement]::Escape($skecs)
$gaXml = [Security.SecurityElement]::Escape($ga)
$movementXml = [Security.SecurityElement]::Escape($movementPath)
$adapterXml = [Security.SecurityElement]::Escape($adapterPath)
$project = Join-Path $hostDirectory 'Consumer.csproj'
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>
    <DefineConstants>GA_SAMPLE</DefineConstants>
    <RestoreSources>.</RestoreSources>
    <RestorePackagesPath>obj/packages</RestorePackagesPath>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="SKECS" HintPath="$skecsXml" />
    <Reference Include="SKECS.GA" HintPath="$gaXml" />
    <Compile Include="$movementXml" />
    <Compile Include="$adapterXml" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project -Encoding utf8
@'
using System;
using Consumer;
using SKECS;
using SKECS.GA;
using SKECS.Numerics;

using var world = new World();
var entity = world.CreateEntity(new Position { X = int.MaxValue - 1 }, new Step { Amount = 1 });
var workload = ConsumerMovement.Register(new DeterministicWorkload<byte>(), SystemStage.Update);
using var runner = new DeterministicRunner<byte>(world, workload);

runner.BeginTick(0, 0, Fixed.One);
if (runner.Advance(1) != DeterministicTickStatus.Completed || world.Get<Position>(entity).X != int.MaxValue)
    throw new InvalidOperationException("The generated adapter did not apply movement.");

runner.BeginTick(1, 0, Fixed.One);
if (runner.Advance(1) != DeterministicTickStatus.Faulted ||
    runner.Fault is not GaExecutionException { KernelFault.Kind: GaFaultKind.Overflow })
    throw new InvalidOperationException("The generated adapter did not preserve checked overflow.");

Console.WriteLine("Generated CPU adapter moved an entity and reported a checked overflow.");
'@ | Set-Content -LiteralPath (Join-Path $hostDirectory 'Program.cs') -Encoding utf8

& dotnet $sdk build $project -m:1
if ($LASTEXITCODE -ne 0) { throw 'The consumer build failed.' }
& dotnet (Join-Path $hostDirectory 'bin/Debug/net8.0/Consumer.dll')
if ($LASTEXITCODE -ne 0) { throw 'The consumer did not execute successfully.' }
Write-Output "CPU artifact $($cpu.contentId)"
