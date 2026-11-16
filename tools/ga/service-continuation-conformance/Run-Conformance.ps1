param(
    [ValidateSet('vulkan', 'd3d12', 'd3d11', 'opengl', 'gles')]
    [string[]]$Backend = @('vulkan', 'd3d12'),
    [ValidateRange(0, 2147483647)]
    [int[]]$DeviceIndex = @(0),
    [string]$DotNetSdkVersion = '8.0.428',
    [string]$ProbeDirectory,
    [string[]]$AdditionalPackageSource,
    [switch]$VulkanValidation,
    [switch]$CpuOnly,
    [string]$FxcDirectory,
    [string]$SpirvCrossExecutable,
    [string]$AngleDirectory,
    [ValidateSet('D3D11', 'Vulkan', 'OpenGl')]
    [string]$AngleRenderer,
    [string]$AngleDevice
)

& "$PSScriptRoot/../Run-GameplayConformance.ps1" -FixtureDirectory $PSScriptRoot -SystemId service-continuation -KernelSources ServiceKernel.cs `
    -AdapterClass ServiceAdapter -ProjectName ServiceContinuationConformance @PSBoundParameters
