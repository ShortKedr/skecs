param(
    [Parameter(Mandatory)][string]$Bundle,
    [Parameter(Mandatory)][string]$NativeArm64,
    [Parameter(Mandatory)][string]$NativeX64,
    [Parameter(Mandatory)][string]$AndroidSdk,
    [Parameter(Mandatory)][string]$JavaSdk,
    [Parameter(Mandatory)][string]$AndroidNdk,
    [string]$ArtifactsDirectory = "$PSScriptRoot/../../../artifacts/ga/android-movement"
)

$ErrorActionPreference = 'Stop'
$bundlePath = (Resolve-Path -LiteralPath $Bundle).Path
$arm64Library = (Resolve-Path -LiteralPath $NativeArm64).Path
$x64Library = (Resolve-Path -LiteralPath $NativeX64).Path
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
    )
    & $dotnet $sdk publish "$PSScriptRoot/AndroidMovementConformance.csproj" -c Release `
        --artifacts-path "$cache/build" -m:1 -nr:false @properties
    if ($LASTEXITCODE) { throw "Android Movement publish failed with exit code $LASTEXITCODE." }
} finally {
    foreach ($setting in $previous.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value)
    }
}
