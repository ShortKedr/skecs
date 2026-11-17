param(
    [Parameter(Mandatory)][string]$Bundle,
    [Parameter(Mandatory)][string]$NativeArm64,
    [Parameter(Mandatory)][string]$NativeX64,
    [Parameter(Mandatory)][string]$AndroidSdk,
    [Parameter(Mandatory)][string]$JavaSdk,
    [Parameter(Mandatory)][string]$AndroidNdk,
    [string]$ArtifactsDirectory = "$PSScriptRoot/../../../artifacts/ga/android-movement",
    [string]$GlesBundle,
    [string]$GlesNativeArm64,
    [string]$GlesNativeX64
)

$ErrorActionPreference = 'Stop'
$bundlePath = (Resolve-Path -LiteralPath $Bundle).Path
$arm64Library = (Resolve-Path -LiteralPath $NativeArm64).Path
$x64Library = (Resolve-Path -LiteralPath $NativeX64).Path
$glesProperties = @()
if ($GlesBundle -or $GlesNativeArm64 -or $GlesNativeX64) {
    if (!$GlesBundle -or !$GlesNativeArm64 -or !$GlesNativeX64) {
        throw 'Provide the GLES bundle and both GLES native libraries together.'
    }
    $glesPath = (Resolve-Path -LiteralPath $GlesBundle).Path
    $glesArm64 = Get-Item -LiteralPath $GlesNativeArm64
    $glesX64 = Get-Item -LiteralPath $GlesNativeX64
    if ($glesArm64.Name -cne 'libga-opengl-native.so' -or $glesX64.Name -cne 'libga-opengl-native.so') {
        throw 'Both GLES libraries must be named libga-opengl-native.so.'
    }
    $vulkanManifest = Get-Content -LiteralPath "$bundlePath/movement.manifest.json" -Raw | ConvertFrom-Json
    $glesManifest = Get-Content -LiteralPath "$glesPath/movement.manifest.json" -Raw | ConvertFrom-Json
    $shaderHash = (Get-FileHash -LiteralPath "$glesPath/movement.glsl").Hash
    if ($glesManifest.Toolchain.TargetProfile -ne 'glsl_es_310' -or
        ($glesManifest.Toolchain.Options -cnotcontains 'SKECS.GA GLSL ES codegen=2' -and
         $glesManifest.Toolchain.Options -cnotcontains 'SKECS.GA GLSL ES codegen=3') -or
        $glesManifest.ArtifactDigest -ne $shaderHash) {
        throw 'GLES packaging requires a current Movement GLSL ES 310 bundle with a matching shader digest.'
    }
    if (($vulkanManifest.Contract | ConvertTo-Json -Depth 10 -Compress) -cne
        ($glesManifest.Contract | ConvertTo-Json -Depth 10 -Compress)) {
        throw 'Movement Vulkan and GLES bundle contracts differ.'
    }
    foreach ($file in 'ga-adapter.g.cs', 'ga-row-codec.g.cs') {
        if ((Get-FileHash -LiteralPath "$glesPath/$file").Hash -ne (Get-FileHash -LiteralPath "$bundlePath/$file").Hash) {
            throw "Movement Vulkan and GLES generated $file differs."
        }
    }
    $glesProperties = @("-p:GlesBundle=$glesPath", "-p:GlesNativeArm64=$($glesArm64.FullName)", "-p:GlesNativeX64=$($glesX64.FullName)")
}
$packageFeed = (Resolve-Path -LiteralPath "$env:USERPROFILE/.nuget/packages").Path
$cache = (New-Item -ItemType Directory -Force $ArtifactsDirectory).FullName
$temp = (New-Item -ItemType Directory -Force "$cache/temp").FullName
$javaHome = (New-Item -ItemType Directory -Force "$cache/java").FullName
$environment = @{
    TEMP = $temp
    TMP = $temp
    APPDATA = $temp
    LOCALAPPDATA = $temp
    DOTNET_CLI_HOME = "$cache/dotnet"
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    ANDROID_USER_HOME = "$cache/android"
    JAVA_TOOL_OPTIONS = "-Duser.home=$javaHome"
}
$previous = @{}
foreach ($setting in $environment.GetEnumerator()) {
    $previous[$setting.Key] = [Environment]::GetEnvironmentVariable($setting.Key)
    [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value)
}

try {
    $keyStore = "$cache/movement-conformance.keystore"
    if (!(Test-Path -LiteralPath $keyStore)) {
        & "$JavaSdk/bin/keytool.exe" -genkeypair -keystore $keyStore -storetype JKS -storepass android -keypass android `
            -alias skecs-ga-diagnostic -dname 'CN=SKECS GA Android diagnostics' -keyalg RSA -keysize 2048 -validity 3650
        if ($LASTEXITCODE) { throw 'Could not create the diagnostic APK signing key.' }
    }

    $dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
    $sdk = (Resolve-Path -LiteralPath "$(Split-Path $dotnet)/sdk/10.0.204/dotnet.dll").Path
    $properties = @(
        "-p:AndroidSdkDirectory=$AndroidSdk", "-p:JavaSdkDirectory=$JavaSdk", "-p:AndroidNdkDirectory=$AndroidNdk",
        "-p:RestoreSources=$packageFeed", "-p:RestorePackagesPath=$cache/packages", '-p:NuGetAudit=false',
        "-p:Bundle=$bundlePath", "-p:NativeArm64=$arm64Library", "-p:NativeX64=$x64Library", '-p:UseSharedCompilation=false',
        '-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$keyStore", '-p:AndroidSigningKeyAlias=skecs-ga-diagnostic',
        '-p:AndroidSigningStorePass=android', '-p:AndroidSigningKeyPass=android'
    ) + $glesProperties
    & $dotnet $sdk publish "$PSScriptRoot/AndroidMovementConformance.csproj" -c Release `
        --artifacts-path "$cache/build" -m:1 -nr:false @properties
    if ($LASTEXITCODE) { throw "Android Movement publish failed with exit code $LASTEXITCODE." }
} finally {
    foreach ($setting in $previous.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value)
    }
}
