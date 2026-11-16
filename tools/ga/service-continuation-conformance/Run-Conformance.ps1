param(
    [ValidateSet('vulkan', 'd3d12')]
    [string[]]$Backend = @('vulkan', 'd3d12'),
    [ValidateRange(0, 2147483647)]
    [int[]]$DeviceIndex = @(0),
    [string]$DotNetSdkVersion = '8.0.428',
    [string]$ProbeDirectory,
    [string[]]$AdditionalPackageSource,
    [switch]$VulkanValidation,
    [switch]$CpuOnly
)

& "$PSScriptRoot/../Run-GameplayConformance.ps1" -FixtureDirectory $PSScriptRoot -SystemId service-continuation -KernelSources ServiceKernel.cs `
    -AdapterClass ServiceAdapter -ProjectName ServiceContinuationConformance @PSBoundParameters
