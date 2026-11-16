param(
    [Parameter(Mandatory)][string]$Bundle,
    [Parameter(Mandatory)][string]$SpirvCrossExecutable,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidateSet('macos','ios')][string]$Platform
)

$ErrorActionPreference = 'Stop'
$Platform = $Platform.ToLowerInvariant()
$bundlePath = (Resolve-Path -LiteralPath $Bundle).Path
$cross = (Resolve-Path -LiteralPath $SpirvCrossExecutable).Path
$crossSha = 'CC19353F15B3DB467D58D5E0A97ADD7A6797FBC52313C57D38B0F77BFA2F90A1'
if ((Get-FileHash -LiteralPath $cross).Hash -ne $crossSha) {
    throw 'Expected SPIRV-Cross vulkan-sdk-1.4.304.0-40-g2c32b6bf from Vulkan SDK 1.4.309.0.'
}
$manifests = @(Get-ChildItem -LiteralPath $bundlePath -File -Filter '*.manifest.json')
if ($manifests.Count -ne 1) { throw 'Expected one GA Vulkan artifact manifest.' }
$manifestPath = $manifests[0].FullName
$stem = $manifestPath.Substring(0,$manifestPath.Length - '.manifest.json'.Length)
$contentId = (Get-Content -LiteralPath "$stem.content-id" -Raw).Trim()
if ((Get-FileHash -LiteralPath $manifestPath).Hash.ToLowerInvariant() -cne $contentId) {
    throw 'Vulkan manifest does not match its content ID.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.Toolchain.Format -ne 0 -or $manifest.Toolchain.TargetProfile -cne 'spirv_1_3' -or
    $manifest.Toolchain.SlangVersion -cne '2025.6.1' -or $manifest.EntryPoint -cne 'main' -or
    $manifest.Requirements.WorkgroupY -ne 1 -or $manifest.Requirements.WorkgroupZ -ne 1 -or
    ($manifest.Requirements.RequiredFeatures).Count -ne 0) {
    throw 'Expected a portable, one-dimensional Slang 2025.6.1 Vulkan GA kernel.'
}
$spirv = "$stem.spv"
if ((Get-FileHash -LiteralPath $spirv).Hash.ToLowerInvariant() -cne $manifest.ArtifactDigest) {
    throw 'SPIR-V does not match the Vulkan manifest.'
}
$layout = & $cross $spirv --reflect
if ($LASTEXITCODE) { throw 'SPIRV-Cross reflection failed.' }
$reflection = ($layout -join "`n") | ConvertFrom-Json
$entry = @($reflection.entryPoints)
if ($entry.Count -ne 1 -or $entry[0].name -cne 'main' -or $entry[0].mode -cne 'comp' -or
    ($entry[0].workgroup_size -join ',') -ne "$($manifest.Requirements.WorkgroupX),1,1" -or
    $entry[0].workgroup_size_is_spec_constant_id -contains $true -or
    @($reflection.PSObject.Properties.Name | Where-Object { $_ -cnotin 'entryPoints','types','ssbos' }).Count -ne 0) {
    throw 'Unexpected SPIR-V entry point, workgroup or resource kind.'
}

$logicalNames = @('gaInput','gaOutput','gaFaults')
if ($manifest.DeclaredBindings.Count -ne 3 -or $manifest.EntryPointUsage.Count -ne 3 -or $reflection.ssbos.Count -ne 3) {
    throw 'Expected exactly three Vulkan storage bindings.'
}
for ($slot = 0; $slot -lt $logicalNames.Count; $slot++) {
    $name = $logicalNames[$slot]
    $access = if ($slot -eq 0) { 0 } else { 2 }
    $binding = @($manifest.DeclaredBindings | Where-Object LogicalResourceId -CEQ $name)
    $usage = @($manifest.EntryPointUsage | Where-Object LogicalResourceId -CEQ $name)
    $physical = @($reflection.ssbos | Where-Object { $_.set -eq 0 -and $_.binding -eq $slot })
    if ($binding.Count -ne 1 -or $usage.Count -ne 1 -or !$usage[0].Used -or $physical.Count -ne 1 -or
        $binding[0].Namespace -ne 0 -or $binding[0].Kind -ne 0 -or $binding[0].Space -ne 0 -or
        $binding[0].Index -ne $slot -or $binding[0].Count -ne 1 -or $binding[0].Access -ne $access -or
        $binding[0].Layout.ElementStride -ne 4 -or [bool]$physical[0].readonly -ne ($slot -eq 0)) {
        throw "The Vulkan manifest and physical storage binding disagree at $name."
    }
    $member = @($reflection.types.PSObject.Properties[$physical[0].type].Value.members)
    if ($member.Count -ne 1 -or $member[0].type -cne 'uint' -or $member[0].array_stride -ne 4 -or
        $member[0].offset -ne 0 -or ($member[0].array -join ',') -ne '0') {
        throw "Expected a runtime array of uint32 words at $name."
    }
}

$options = @('--msl','--msl-version','20300','--msl-decoration-binding',
    '--rename-entry-point','main','skecs_ga_dispatch','comp')
if ($Platform -eq 'ios') { $options += '--msl-ios' }
$source = & $cross $spirv @options
if ($LASTEXITCODE) { throw 'SPIRV-Cross MSL translation failed.' }
$text = ($source -join "`n").TrimEnd() + "`n"
$kernel = [regex]::Match($text, '(?m)^kernel\s+void\s+skecs_ga_dispatch\s*\(([^\r\n]*)\)')
if (!$kernel.Success -or $text -cmatch '\.GetDimensions\s*\(') {
    throw 'MSL does not expose the expected compute entry point.'
}
$signature = $kernel.Groups[1].Value
for ($slot = 0; $slot -lt $logicalNames.Count; $slot++) {
    if ($signature -cnotmatch "\b$($logicalNames[$slot])\s+\[\[buffer\($slot\)\]\]") {
        throw "MSL did not preserve storage slot $slot."
    }
}
$sizeSlot = $null
$sizeUses = [regex]::Matches($text, '\bspvBufferSizeConstants\[(\d+)\]')
if ($sizeUses.Count -ne 0) {
    if ($signature -cnotmatch 'constant uint\* spvBufferSizeConstants \[\[buffer\(25\)\]\]' -or
        @($sizeUses | Where-Object { $_.Groups[1].Value -ne '0' }).Count -ne 0) {
        throw 'Unsupported MSL buffer-size table.'
    }
    $sizeSlot = 25
}
$physicalSlots = @([regex]::Matches($signature, '\[\[buffer\((\d+)\)\]\]') |
    ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object)
$expectedSlots = if ($null -eq $sizeSlot) { 0,1,2 } else { 0,1,2,25 }
if (($physicalSlots -join ',') -ne ($expectedSlots -join ',')) { throw 'Unexpected MSL buffer arguments.' }

$output = (New-Item -ItemType Directory -Force $OutputDirectory).FullName
$metal = Join-Path $output 'kernel.metal'
[IO.File]::WriteAllText($metal,$text,[Text.UTF8Encoding]::new($false))
$metadata = [ordered]@{
    Platform = $Platform
    MetalLanguageVersion = '2.3'
    EntryPoint = 'skecs_ga_dispatch'
    WorkgroupSize = $entry[0].workgroup_size
    StorageSlots = [ordered]@{ gaInput = 0; gaOutput = 1; gaFaults = 2 }
    InputLengthInBytesSlot = $sizeSlot
    VulkanContentId = $contentId
    SpirvSha256 = $manifest.ArtifactDigest
    SpirvCrossSha256 = $crossSha.ToLowerInvariant()
    MetalSourceSha256 = (Get-FileHash -LiteralPath $metal).Hash.ToLowerInvariant()
    Options = $options
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$output/metal-source.json" -Encoding utf8
