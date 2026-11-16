param(
    [ValidateSet('vulkan', 'd3d12')]
    [string[]]$Backend = @('vulkan', 'd3d12'),
    [ValidateRange(0, 2147483647)]
    [int[]]$DeviceIndex = @(0),
    [ValidateSet('boundaries', 'short', 'long')]
    [ValidateNotNullOrEmpty()]
    [string[]]$Profile = @('boundaries', 'short'),
    [ValidateRange(1, 1024)]
    [int]$WorkgroupX = 64,
    [switch]$CpuOnly,
    [string]$DotNetSdkVersion = '8.0.428',
    [string]$ProbeDirectory,
    [switch]$VulkanValidation
)

$PSBoundParameters['Profile'] = $Profile
& "$PSScriptRoot/../Run-GameplayConformance.ps1" -FixtureDirectory $PSScriptRoot -SystemId carrier-capacity -KernelSources CarrierKernel.cs `
    -AdapterClass CarrierAdapter -ProjectName CarrierCapacityConformance @PSBoundParameters
