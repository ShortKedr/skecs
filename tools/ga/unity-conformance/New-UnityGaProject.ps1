[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Package,
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$packagePath = (Resolve-Path -LiteralPath $Package).Path.Replace([IO.Path]::DirectorySeparatorChar, '/')
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $workspace 'artifacts/ga/unity-package/project'
}
$project = [IO.Path]::GetFullPath($OutputDirectory)
$relative = [IO.Path]::GetRelativePath($workspace, $project)
if ($relative -eq '.' -or $relative -match '^\.\.([\\/]|$)' -or [IO.Path]::IsPathRooted($relative)) {
    throw 'The generated Unity project must stay inside the workspace.'
}
if (Test-Path -LiteralPath $project) {
    if (Get-ChildItem -LiteralPath $project -Force | Select-Object -First 1) {
        throw "The Unity project directory must be empty: $project"
    }
}
$manifest = Join-Path $project 'Packages'
New-Item -ItemType Directory -Path $manifest -Force | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'Project/*') -Destination $project -Recurse
$kernels = Join-Path $project 'Assets/Kernels'
$scenarios = Join-Path $project 'Assets/Scenarios'
New-Item -ItemType Directory -Path $scenarios -Force | Out-Null
$ga = Split-Path -Parent $PSScriptRoot
foreach ($file in @('movement-conformance/MovementKernel.cs', 'target-read-conformance/TargetKernel.cs', 'shotburst-conformance/ShotKernels.cs')) {
    Copy-Item -LiteralPath (Join-Path $ga $file) -Destination $kernels
}
foreach ($file in @('target-read-conformance/TargetScenario.cs', 'shotburst-conformance/ShotBurstScenario.cs')) {
    Copy-Item -LiteralPath (Join-Path $ga $file) -Destination $scenarios
}
@{ dependencies = @{ 'com.shortkedr.skecs.ga' = "file:$packagePath" } } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $manifest 'manifest.json') -Encoding utf8NoBOM
Write-Output $project
