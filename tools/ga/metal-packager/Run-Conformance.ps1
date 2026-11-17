# VulkanArtifacts contains <system>/bundles/<bundle> for each SG fixture.
# MetalExports contains <system>-<platform> directories from Export-MetalSource.ps1.
param(
    [Parameter(Mandatory)][string]$VulkanArtifacts,
    [Parameter(Mandatory)][string]$MetalExports,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('macos','ios')][string]$Platform = 'macos',
    [ValidateRange(0,2147483647)][int]$DeviceIndex = 0,
    [string[]]$PackageSource,
    [string]$DotNetSdkVersion = '8.0.428',
    [switch]$ValidateOnly,
    [switch]$Endurance
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (!$ValidateOnly -and (!$IsMacOS -or $Platform -ne 'macos')) {
    throw 'Metal conformance requires macOS with the Apple SDK. Use -ValidateOnly to verify the input packages.'
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory for the conformance run.' }
$artifacts = (Resolve-Path -LiteralPath $VulkanArtifacts).Path
$exports = (Resolve-Path -LiteralPath $MetalExports).Path
$tools = (Resolve-Path -LiteralPath "$PSScriptRoot/..").Path
$source = (Resolve-Path -LiteralPath "$tools/../..").Path
$dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
$sdkPattern = '^' + [regex]::Escape($DotNetSdkVersion) + ' \[(.+)\]$'
$installed = @(& $dotnet --list-sdks | Select-String -Pattern $sdkPattern)
if ($installed.Count -ne 1) { throw "Install .NET SDK $DotNetSdkVersion or select an installed version." }
$sdk = Join-Path $installed[0].Matches[0].Groups[1].Value "$DotNetSdkVersion/dotnet.dll"
$projectSources = @($PackageSource | ForEach-Object { if ($_) { '--source'; (Resolve-Path -LiteralPath $_).Path } })
$fixtures = @(
    @{ Id='movement'; Folder='movement'; Project='MovementConformance'; Profile='boundaries'; Rows=48030; LongRows=6500000 }
    @{ Id='target-read'; Folder='target-read'; Project='TargetReadConformance'; Profile='matrix'; Rows=64040; LongRows=6500000 }
    @{ Id='thermal-inventory'; Folder='thermal-inventory'; Project='ThermalInventoryConformance'; Profile='matrix'; Rows=128080; LongRows=6500000 }
    @{ Id='shot-burst-capacity'; Folder='shotburst'; Project='ShotBurstConformance'; Profile='matrix'; Rows=96060; LongRows=13000000 }
    @{ Id='multi-hit-damage'; Folder='multihit'; Project='MultiHitConformance'; Profile='matrix'; Rows=48030; LongRows=6500000 }
    @{ Id='carrier-capacity'; Folder='carrier-capacity'; Project='CarrierCapacityConformance'; Profile='boundaries'; Rows=48300; LongRows=1700000 }
    @{ Id='spatial-parent'; Folder='spatial-parent'; Project='SpatialParentConformance'; Profile='matrix'; Rows=64040; LongRows=6500000 }
    @{ Id='service-continuation'; Folder='service-continuation'; Project='ServiceContinuationConformance'; Profile=''; Rows=111; LongRows=56832 }
    @{ Id='host-lifecycle'; Folder='host-lifecycle'; Project='HostLifecycleConformance'; Profile=''; Rows=588; LongRows=150528 }
)
foreach ($fixture in $fixtures) {
    $id = $fixture.Id
    $vulkan = @(Get-ChildItem -LiteralPath "$artifacts/$id/bundles" -Directory |
        Where-Object { Test-Path -LiteralPath "$($_.FullName)/$id.spv" -PathType Leaf })
    if ($vulkan.Count -ne 1) { throw "Expected exactly one Vulkan bundle for $id." }
    $fixture.Vulkan = $vulkan[0].FullName
    $fixture.Export = (Resolve-Path -LiteralPath "$exports/$id-$Platform").Path
}

$output = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
$temp = (New-Item -ItemType Directory -Path "$output/temp").FullName
$environment = @{
    TMPDIR=$temp; TMP=$temp; TEMP=$temp; APPDATA=$temp; LOCALAPPDATA=$temp
    DOTNET_CLI_HOME="$output/dotnet-home"; DOTNET_CLI_TELEMETRY_OPTOUT='1'
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'; DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'
}
$previous = @{}

function Invoke-Logged([string]$Executable, [string[]]$Arguments, [string]$Log) {
    $lines = @(& $Executable @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $status = $LASTEXITCODE
    Set-Content -LiteralPath $Log -Value $lines -Encoding utf8
    if ($status) { throw "$Executable exited with status $status. See $Log" }
    $lines
}

function Get-Digest([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

try {
    foreach ($setting in $environment.GetEnumerator()) {
        $previous[$setting.Key] = [Environment]::GetEnvironmentVariable($setting.Key)
        [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value)
    }
    Invoke-Logged $dotnet @('--list-runtimes') "$output/dotnet-runtimes.txt" | Out-Null
    $build = "$output/build"
    $buildArgs = @('-c','Release','-m:1','-nr:false','--artifacts-path',$build,'-p:NuGetAudit=false','-p:UseSharedCompilation=false',
        "-p:RestorePackagesPath=$output/packages") + $projectSources
    if ($IsWindows) {
        $buildArgs += "-p:TargetPlatformSdkPath=$temp/", '-p:TargetPlatformDisplayName=Windows'
    }
    Invoke-Logged $dotnet (@($sdk,'build',"$PSScriptRoot/Ga.MetalPackager.csproj") + $buildArgs) "$output/build-packager.txt" | Out-Null
    $packager = "$build/bin/Ga.MetalPackager/release/Ga.MetalPackager.dll"
    $records = [System.Collections.Generic.List[object]]::new()
    foreach ($fixture in $fixtures) {
        $id = $fixture.Id
        $log = "$output/$id-input.json"
        $inputBundle = (Invoke-Logged $dotnet @($packager,'validate',$Platform,$fixture.Vulkan,$fixture.Export) $log) |
            ConvertFrom-Json
        if ($inputBundle.Platform -cne $Platform -or $inputBundle.ArtifactName -cne $id) {
            throw "Metal input $id has a mismatched platform or system ID."
        }
        $records.Add([ordered]@{ Id=$id; VulkanContentId=$inputBundle.ContentId; Contract=$inputBundle.Contract })
    }

    if (!$ValidateOnly) {
        $cmake = (Get-Command cmake -CommandType Application | Select-Object -First 1).Source
        $native = "$output/native"
        Invoke-Logged $cmake @('-S',"$tools/metal-native",'-B',$native,'-DCMAKE_BUILD_TYPE=Release') "$output/configure-native.txt" | Out-Null
        Invoke-Logged $cmake @('--build',$native,'--config','Release') "$output/build-native.txt" | Out-Null
        $bridge = @(Get-ChildItem -LiteralPath $native -Recurse -File -Filter 'libga-metal-native.dylib')
        if ($bridge.Count -ne 1) { throw 'Expected one built Metal bridge.' }
        $bridgePath = $bridge[0].FullName
        for ($i=0; $i -lt $fixtures.Count; $i++) {
            $fixture = $fixtures[$i]
            $id = $fixture.Id
            $record = $records[$i]
            $bundle = "$output/$id"
            $content = Invoke-Logged $dotnet @($packager,'pack',$Platform,$fixture.Vulkan,$fixture.Export,$bundle) "$output/pack-$id.txt"
            if (@($content).Count -ne 1 -or $content -cnotmatch '^[0-9a-f]{64}$' -or
                (Get-Digest "$bundle/$id.manifest.json") -cne $content) {
                throw "Metal packaging did not return the canonical content ID for $id."
            }
            $project = $fixture.Project
            $projectFile = "$tools/$($fixture.Folder)-conformance/$project.csproj"
            $assembly = "$build/bin/$project/release/$project.dll"
            $projectArgs = @($sdk,'build',$projectFile,"-p:GaSource=$source","-p:Bundle=$($fixture.Vulkan)") + $buildArgs
            Invoke-Logged $dotnet $projectArgs "$output/build-$id.txt" | Out-Null

            $profile = $fixture.Profile
            if ($Endurance) { $profile = if ($profile) { 'long' } else { 'endurance' } }
            if ($id -eq 'movement') {
                $cpuLog = "$output/movement-cpu.txt"
                $cpu = Invoke-Logged $dotnet @($assembly,'cpu','boundaries') $cpuLog
                if (!($cpu -match 'deviceRows=0\b.*exactQ32_32=passed allocator=passed')) {
                    throw "Movement CPU parity evidence is missing. See $cpuLog"
                }
                $record.CpuTraceSha256 = Get-Digest $cpuLog
            }
            $arguments = @($assembly,'metal',$bundle,$bridgePath,[string]$DeviceIndex)
            if ($profile) { $arguments += $profile }
            $trace = "$output/run-$id.txt"
            $run = Invoke-Logged $dotnet $arguments $trace
            $device = @($run | Where-Object {
                $_ -cmatch '\bapi=Metal\b' -and $_ -cmatch "\b(contentId|artifact)=$content\b"
            })
            $rows = if ($Endurance) { $fixture.LongRows } else { $fixture.Rows }
            if (!$device.Count -or !($run -cmatch "\bdeviceRows=$rows\b")) {
                throw "Metal did not attest the expected device, artifact and applied rows for $id. See $trace"
            }
            $record.Profile = $profile
            $record.GpuRows = $rows
            $record.Device = $device[0]
            $record.ContentId = $content
            $record.ShaderSha256 = Get-Digest "$bundle/$id.metallib"
            $record.LayoutSha256 = Get-Digest "$bundle/$id.layout"
            $record.AssemblySha256 = Get-Digest $assembly
            $record.TraceSha256 = Get-Digest $trace
            Write-Host "$id passed on Metal ($rows rows)"
        }
    }

    [ordered]@{
        Platform=$Platform
        OS=[Runtime.InteropServices.RuntimeInformation]::OSDescription
        OSArchitecture=[Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        PowerShellArchitecture=[Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        DotNetSdkVersion=$DotNetSdkVersion
        DotNetRuntimeInventorySha256=Get-Digest "$output/dotnet-runtimes.txt"
        ValidationOnly=[bool]$ValidateOnly
        DeviceIndex=if (!$ValidateOnly) { $DeviceIndex }
        NativeBridgeSha256=if (!$ValidateOnly) { Get-Digest $bridgePath }
        Fixtures=$records
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath "$output/receipt.json" -Encoding utf8
    Write-Host "Saved conformance evidence in $output/receipt.json"
}
finally {
    foreach ($setting in $previous.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($setting.Key,$setting.Value)
    }
}
