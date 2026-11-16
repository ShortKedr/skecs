param(
    [Parameter(Mandatory)][string]$Wheel,
    [string]$OutputDirectory = "$PSScriptRoot/../../artifacts/ga/angle/comfy-angle-0.1.1-win-x64",
    [string]$SteamRootForVulkan
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot/../..").Path
$prefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory).TrimEnd(
    [IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
if (!$destination.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The ANGLE output directory must be inside the checkout.'
}
if (Test-Path -LiteralPath $destination) { throw 'The ANGLE output directory already exists.' }
$parent = [IO.Path]::GetDirectoryName($destination)
for ($path = $parent; $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase); $path = [IO.Path]::GetDirectoryName($path)) {
    if (Test-Path -LiteralPath $path) {
        $folder = Get-Item -LiteralPath $path -Force
        if (!$folder.PSIsContainer -or ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "An ANGLE output parent is not a regular directory: $path"
        }
    }
}

# PyPI Trusted Publishing asset comfy_angle-0.1.1-py3-none-win_amd64.whl.
# https://pypi.org/project/comfy-angle/0.1.1/#comfy_angle-0.1.1-py3-none-win_amd64.whl
$expectedHash = 'BEDDD2E3B9A67E55F5AFAEF4DA73E3AC704FD88F0CBE6A6F67CE5718D7C7EDEB'
$files = [ordered]@{
    'comfy_angle/libs/libEGL.dll' = 'libEGL.dll'
    'comfy_angle/libs/libGLESv2.dll' = 'libGLESv2.dll'
    'comfy_angle/electron-LICENSE' = 'electron-LICENSE'
    'comfy_angle/LICENSES.chromium.html' = 'LICENSES.chromium.html'
}

Add-Type -AssemblyName System.IO.Compression
$inputFile = [IO.File]::OpenRead((Resolve-Path -LiteralPath $Wheel).Path)
$archive = $null
$staging = $null
$companions = @{}
try {
    if ((Get-FileHash -InputStream $inputFile).Hash -ne $expectedHash) {
        throw 'Expected the pinned comfy-angle 0.1.1 Windows x64 wheel SHA-256.'
    }
    $inputFile.Position = 0
    $archive = [IO.Compression.ZipArchive]::new($inputFile, [IO.Compression.ZipArchiveMode]::Read)
    $entries = @{}
    foreach ($name in $files.Keys) {
        $found = @($archive.Entries | Where-Object { $_.FullName -ceq $name })
        if ($found.Count -ne 1) { throw "Expected one wheel entry named $name." }
        $entries[$name] = $found[0]
    }
    if ($SteamRootForVulkan) {
        # Optional Vulkan loader from the locally verified Steam CEF x64 installation.
        $steam = (Resolve-Path -LiteralPath $SteamRootForVulkan).Path
        $inputs = @{
            'vulkan-1.dll' = @('bin/cef/cef.win64/vulkan-1.dll', '1A6C256AEFA935A09053957BE99D79FCDF94958D291EBC46593717222E1D440F')
            'vulkan-loader-notices.html' = @('ThirdPartyLegalNotices-Chromium.html', 'A8856FBD8D9D846B2B272063DE30B8569DC18D0B5B9024798272ADF8EDE0CAF7')
        }
        foreach ($name in $inputs.Keys) {
            $relative,$digest = $inputs[$name]
            $stream = [IO.File]::OpenRead((Join-Path $steam $relative))
            $companions[$name] = $stream
            if ((Get-FileHash -InputStream $stream).Hash -ne $digest) { throw "Unexpected Steam Vulkan dependency $name." }
            $stream.Position = 0
        }
    }

    $staging = Join-Path $parent ('.angle-import-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($staging)
    foreach ($name in $files.Keys) {
        $entry = $entries[$name].Open()
        try {
            $outputFile = [IO.File]::Open((Join-Path $staging $files[$name]), [IO.FileMode]::CreateNew)
            try { $entry.CopyTo($outputFile) }
            finally { $outputFile.Dispose() }
        }
        finally { $entry.Dispose() }
    }
    foreach ($name in $companions.Keys) {
        $outputFile = [IO.File]::Open((Join-Path $staging $name), [IO.FileMode]::CreateNew)
        try { $companions[$name].CopyTo($outputFile) }
        finally { $outputFile.Dispose() }
    }
    [IO.Directory]::Move($staging, $destination)
    $staging = $null
    $destination
}
finally {
    if ($archive) { $archive.Dispose() }
    $inputFile.Dispose()
    foreach ($stream in $companions.Values) { $stream.Dispose() }
    if ($staging -and [IO.Directory]::Exists($staging)) {
        foreach ($file in $files.Values) { [IO.File]::Delete((Join-Path $staging $file)) }
        foreach ($file in $companions.Keys) { [IO.File]::Delete((Join-Path $staging $file)) }
        [IO.Directory]::Delete($staging)
    }
}
