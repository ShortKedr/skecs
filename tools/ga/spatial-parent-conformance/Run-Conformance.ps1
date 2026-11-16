param(
    [ValidateSet('vulkan', 'd3d12', 'd3d11', 'opengl')]
    [ValidateNotNullOrEmpty()]
    [string[]]$Backend = @('vulkan', 'd3d12'),
    [ValidateRange(0, 2147483647)]
    [ValidateNotNullOrEmpty()]
    [int[]]$DeviceIndex = @(0),
    [ValidateSet('smoke', 'matrix')]
    [ValidateNotNullOrEmpty()]
    [string[]]$Profile = @('matrix'),
    [ValidateRange(1, 1024)]
    [int]$WorkgroupX = 64,
    [string]$DotNetSdkVersion = '8.0.428',
    [string]$ProbeDirectory,
    [switch]$VulkanValidation,
    [switch]$CpuOnly,
    [switch]$BuildOnly,
    [string]$FxcDirectory,
    [string]$SpirvCrossExecutable
)

& "$PSScriptRoot/../Run-GameplayConformance.ps1" -FixtureDirectory $PSScriptRoot -SystemId spatial-parent -KernelSources SpatialKernel.cs `
    -AdapterClass SpatialAdapter -ProjectName SpatialParentConformance @PSBoundParameters
