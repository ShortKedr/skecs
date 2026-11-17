param(
    [ValidateSet('vulkan', 'd3d12')][string[]]$Backend = @('vulkan', 'd3d12'),
    [ValidateRange(0, 2147483647)][int[]]$DeviceIndex = @(0),
    [string]$ProbeDirectory,
    [string[]]$AdditionalPackageSource,
    [switch]$VulkanValidation,
    [switch]$CpuOnly
)

& "$PSScriptRoot/../Run-GameplayConformance.ps1" -FixtureDirectory $PSScriptRoot -SystemId planar-service `
    -KernelSources PlanarKernel.cs -AdapterClass PlanarAdapter -ProjectName PlanarServiceConformance @PSBoundParameters
