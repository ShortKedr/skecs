param(
    [ValidateSet('vulkan', 'd3d12', 'd3d11', 'opengl', 'gles')]
    [string[]]$Backend = @('vulkan', 'd3d12'),
    [ValidateRange(0, 2147483647)]
    [int[]]$DeviceIndex = @(0),
    [ValidateSet('boundaries', 'short', 'long')]
    [ValidateNotNullOrEmpty()]
    [string[]]$Profile = @('boundaries', 'short'),
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
    [string]$AngleRenderer
)

$PSBoundParameters['Profile'] = $Profile
& "$PSScriptRoot/../Run-GameplayConformance.ps1" -FixtureDirectory $PSScriptRoot -SystemId movement -KernelSources MovementKernel.cs `
    -AdapterClass MovementAdapter -ProjectName MovementConformance @PSBoundParameters
