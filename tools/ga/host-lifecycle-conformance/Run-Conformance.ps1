param(
    [ValidateSet('vulkan', 'd3d12')]
    [string[]]$Backend = @('vulkan', 'd3d12'),
    [ValidateRange(0, 2147483647)]
    [int[]]$DeviceIndex = @(0),
    [ValidateRange(1, 1024)]
    [int]$WorkgroupX = 64,
    [string]$DotNetSdkVersion = '8.0.428',
    [string]$ProbeDirectory,
    [switch]$BuildOnly,
    [switch]$VulkanValidation,
    [switch]$CpuOnly
)

& "$PSScriptRoot/../Run-GameplayConformance.ps1" -FixtureDirectory $PSScriptRoot -SystemId host-lifecycle -KernelSources HostKernel.cs `
    -AdapterClass HostAdapter -ProjectName HostLifecycleConformance @PSBoundParameters
