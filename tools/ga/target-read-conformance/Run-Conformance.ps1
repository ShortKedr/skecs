param(
    [ValidateSet('vulkan', 'd3d12', 'd3d11', 'opengl', 'gles')]
    [ValidateNotNullOrEmpty()]
    [string[]]$Backend = @('vulkan', 'd3d12'),
    [ValidateRange(0, 2147483647)]
    [ValidateNotNullOrEmpty()]
    [int[]]$DeviceIndex = @(0),
    [ValidateSet('smoke', 'matrix', 'short', 'long')]
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
    [string]$SpirvCrossExecutable,
    [string]$AngleDirectory,
    [ValidateSet('D3D11', 'Vulkan', 'OpenGl')]
    [string]$AngleRenderer,
    [string]$AngleDevice
)

& "$PSScriptRoot/../Run-GameplayConformance.ps1" -FixtureDirectory $PSScriptRoot -SystemId target-read -KernelSources TargetKernel.cs `
    -AdapterClass TargetAdapter -ProjectName TargetReadConformance @PSBoundParameters
