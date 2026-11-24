[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $UnityEditor,
    [Parameter(Mandatory)][string] $Package,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [ValidateSet('Editor', 'Mono', 'IL2CPP')][string] $Lane = 'Editor',
    [ValidateSet('Win64', 'Linux64', 'OSXUniversal', 'WebGL')][string] $Target = 'Win64',
    [string[]] $GraphicsApis = @(),
    [switch] $CpuOnly,
    [switch] $BuildOnly
)

$ErrorActionPreference = 'Stop'
if ($Lane -eq 'Editor' -and $BuildOnly) {
    throw '-BuildOnly requires a Player lane.'
}
if ($Target -ne 'Win64' -and -not $BuildOnly) {
    throw 'Use -BuildOnly for cross-target Player output.'
}
if ($Target -eq 'WebGL' -and $Lane -ne 'IL2CPP') {
    throw 'A WebGL Player uses IL2CPP.'
}

$editor = (Resolve-Path -LiteralPath $UnityEditor).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$project = & (Join-Path $PSScriptRoot 'New-UnityGaProject.ps1') -Package $Package -OutputDirectory (Join-Path $output 'project')
$temp = Join-Path $output 'tmp'
New-Item -ItemType Directory -Path $temp -Force | Out-Null
$env:TEMP = $temp
$env:TMP = $temp

function Invoke-UnityProcess([string] $Executable, [string[]] $Arguments, [string] $LogName, [string] $Marker, [int] $Timeout) {
    $path = Join-Path $output $LogName
    $command = @('-batchmode', '-logFile', $path) + $Arguments
    $quoted = $command | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $process = Start-Process -FilePath $Executable -ArgumentList $quoted -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit($Timeout * 1000)) {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        throw "Unity timed out. See $path"
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $path) -or
        -not (Select-String -LiteralPath $path -SimpleMatch $Marker -Quiet)) {
        if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Tail 70 }
        throw "Unity exited $($process.ExitCode) or missed '$Marker'. See $path"
    }
    Write-Output "PASS $Marker ($path)"
}

$editorArgs = @('-projectPath', $project, '-buildTarget', $Target,
    '-cacheServerEnableUpload', 'false', '-cacheServerEnableDownload', 'false')
$graphicsArgs = @()
if ($GraphicsApis.Count) {
    $graphicsArgs = @('-ga-graphics-api', ($GraphicsApis -join ','))
}
$backend = if ($Lane -eq 'IL2CPP') { 'Il2Cpp' } else { 'Mono' }
$kernelCount = @(Get-ChildItem -LiteralPath (Join-Path $project 'Assets') -Filter '*.skecsga.json' -File -Recurse).Count
$generateArgs = $editorArgs + $graphicsArgs + @('-quit', '-executeMethod', "GaUnityConformanceBuild.Generate$backend")
Invoke-UnityProcess $editor $generateArgs 'generate.log' "SKECS GA generated $kernelCount Unity kernel asset(s)" 300

Copy-Item -Path (Join-Path $PSScriptRoot 'Acceptance/*.cs') -Destination (Join-Path $project 'Assets')
$runArgs = @()
if ($CpuOnly) {
    $runArgs = @('-nographics', '-ga-cpu')
}
if ($Lane -eq 'Editor') {
    Invoke-UnityProcess $editor ($editorArgs + $runArgs + @('-executeMethod', 'GaUnityConformance.RunEditor')) 'editor.log' 'SKECS_GA_UNITY_OK' 300
    return
}

$location = switch ($Target) {
    'Win64' { 'player/GaUnityConformance.exe' }
    'Linux64' { 'player/GaUnityConformance.x86_64' }
    'OSXUniversal' { 'player/GaUnityConformance.app' }
    'WebGL' { 'player/web' }
}
$player = Join-Path $output $location
$buildArgs = $editorArgs + $graphicsArgs + @('-quit', '-executeMethod', "GaUnityConformanceBuild.Build$backend", '-ga-output', $player)
Invoke-UnityProcess $editor $buildArgs 'build.log' 'SKECS_GA_UNITY_BUILD_OK' 900
if (-not $BuildOnly) {
    Invoke-UnityProcess $player $runArgs 'player.log' 'SKECS_GA_UNITY_OK' 300
}
