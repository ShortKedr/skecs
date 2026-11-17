param(
    [Parameter(Mandatory)][string]$VulkanBundle,
    [Parameter(Mandatory)][string]$MetalExport,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$CodesignKey,
    [string]$CodesignProvision
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (!$IsMacOS) { throw 'Building the iOS app requires macOS, Xcode and the .NET 10 iOS workload.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory for the iOS build.' }
$sign = [bool]($CodesignKey -or $CodesignProvision)
$sourceBundle = (Resolve-Path -LiteralPath $VulkanBundle).Path
$metalSource = (Resolve-Path -LiteralPath $MetalExport).Path
$dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
$cmake = (Get-Command cmake -CommandType Application | Select-Object -First 1).Source
$root = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
$temp = (New-Item -ItemType Directory -Path "$root/temp").FullName
$settings = @{
    TMPDIR=$temp; DOTNET_CLI_HOME="$root/dotnet-home"; DOTNET_CLI_TELEMETRY_OPTOUT='1'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'; DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
}
$previous = @{}

function Invoke-Logged([string]$Executable, [string[]]$Arguments, [string]$Log) {
    $lines = @(& $Executable @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $status = $LASTEXITCODE
    Set-Content -LiteralPath $Log -Value $lines -Encoding utf8
    if ($status) { throw "$Executable failed with status $status. See $Log" }
    $lines
}

function Get-Digest([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

try {
    foreach ($setting in $settings.GetEnumerator()) {
        $previous[$setting.Key] = [Environment]::GetEnvironmentVariable($setting.Key)
        [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value)
    }
    $sdkVersion = Invoke-Logged $dotnet @('--version') "$root/dotnet-sdk.txt"
    $appleSdk = Invoke-Logged '/usr/bin/xcrun' @('--sdk','iphoneos','--show-sdk-version') "$root/apple-sdk.txt"
    $native = "$root/native"
    Invoke-Logged $cmake @('-S',"$PSScriptRoot/../metal-native",'-B',$native,'-DCMAKE_SYSTEM_NAME=iOS',
        '-DCMAKE_OSX_SYSROOT=iphoneos','-DCMAKE_OSX_ARCHITECTURES=arm64','-DCMAKE_BUILD_TYPE=Release') "$root/configure-native.txt" | Out-Null
    Invoke-Logged $cmake @('--build',$native,'--config','Release') "$root/build-native.txt" | Out-Null
    $frameworks = @(Get-ChildItem -LiteralPath $native -Directory -Recurse -Filter 'ga-metal-native.framework')
    if ($frameworks.Count -ne 1) { throw 'Expected exactly one arm64 Metal framework.' }
    $framework = $frameworks[0].FullName

    $packagerBuild = "$root/packager"
    Invoke-Logged $dotnet @('build',"$PSScriptRoot/../metal-packager/Ga.MetalPackager.csproj",
        '-c','Release','--artifacts-path',$packagerBuild,"-p:RestorePackagesPath=$root/packages") "$root/build-packager.txt" | Out-Null
    $packager = "$packagerBuild/bin/Ga.MetalPackager/release/Ga.MetalPackager.dll"
    $bundle = "$root/bundle"
    $content = Invoke-Logged $dotnet @($packager,'pack','ios',$sourceBundle,$metalSource,$bundle) "$root/pack-metal.txt"
    if (@($content).Count -ne 1 -or $content -cnotmatch '^[0-9a-f]{64}$' -or
        (Get-Digest "$bundle/movement.manifest.json") -cne $content) {
        throw 'The packager did not publish a canonical Metal content ID.'
    }

    $build = "$root/build"
    $arguments = @('build',"$PSScriptRoot/IosMovementConformance.csproj",'-c','Release',
        '--artifacts-path',$build,"-p:RestorePackagesPath=$root/packages",
        "-p:VulkanBundle=$sourceBundle","-p:MetalBundle=$bundle","-p:MetalFramework=$framework")
    if ($sign) {
        if ($CodesignKey) { $arguments += "-p:CodesignKey=$CodesignKey" }
        if ($CodesignProvision) { $arguments += "-p:CodesignProvision=$CodesignProvision" }
    } else {
        $arguments += '-p:EnableCodeSigning=false'
    }
    Invoke-Logged $dotnet $arguments "$root/build-app.txt" | Out-Null
    $apps = @(Get-ChildItem -LiteralPath "$build/bin/IosMovementConformance" -Directory -Recurse -Filter 'IosMovementConformance.app')
    if ($apps.Count -ne 1) { throw 'Expected exactly one Movement app bundle.' }
    $app = $apps[0].FullName
    $embedded = "$app/Frameworks/ga-metal-native.framework/ga-metal-native"
    $executable = Invoke-Logged '/usr/bin/plutil' @('-extract','CFBundleExecutable','raw',"$app/Info.plist") "$root/app-executable.txt"
    foreach ($binary in "$app/$executable",$embedded) {
        $label = Split-Path $binary -Leaf
        $architectures = Invoke-Logged '/usr/bin/xcrun' @('lipo','-archs',$binary) "$root/$label-arch.txt"
        $platform = Invoke-Logged '/usr/bin/xcrun' @('vtool','-show-build',$binary) "$root/$label-build-platform.txt"
        if ($architectures -cne 'arm64' -or !($platform -cmatch '\bplatform IOS\s*$')) {
            throw "$binary does not target arm64 iOS."
        }
    }
    $symbols = Invoke-Logged '/usr/bin/xcrun' @('nm','-gU',$embedded) "$root/bridge-exports.txt"
    foreach ($operation in 'open','close','device_count','get_device','probe','create_kernel','release_kernel','dispatch') {
        if (!($symbols -cmatch "\b_skecs_ga_metal_$operation\s*$")) { throw "The embedded bridge lacks $operation." }
    }

    $hashes=[ordered]@{}
    foreach ($extension in 'manifest.json','content-id','metallib','layout','source-map.json') {
        $name="movement.$extension"
        $actual=Get-Digest "$app/bundle/$name"
        if ($actual -cne (Get-Digest "$bundle/$name")) { throw "The app changed packaged asset $name." }
        $hashes[$name]=$actual
    }
    if ($sign) {
        Invoke-Logged '/usr/bin/codesign' @('--verify','--deep','--strict',$app) "$root/codesign.txt" | Out-Null
    }
    [ordered]@{
        App=$app; Architecture='arm64'; DotNetSdk=$sdkVersion; AppleSdk=$appleSdk
        CodeSigningRequested=$sign; ContentId=$content; ArtifactHashes=$hashes
        BridgeSha256=Get-Digest $embedded; ExecutableSha256=Get-Digest "$app/$executable"
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$root/receipt.json" -Encoding utf8
    Write-Host "Built $app"
}
finally {
    foreach ($setting in $previous.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value)
    }
}
