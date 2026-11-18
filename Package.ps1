param([Parameter(Mandatory)][string]$PublishedFolder, [Parameter(Mandatory)][string]$SdkDirectory, [switch]$CreateZip=$true)
$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
$published = (Resolve-Path -LiteralPath $PublishedFolder).Path
$releaseRoot = Join-Path $sourceRoot 'artifacts/releases'
$workRoot = Join-Path $sourceRoot 'artifacts/work'
New-Item -ItemType Directory -Path $releaseRoot,$workRoot -Force | Out-Null
$zipPath = Join-Path $releaseRoot 'PixelForge-Studio-v0.5.0-win-x64.zip'
if ($CreateZip -and (Test-Path -LiteralPath $zipPath)) { throw 'Release ZIP already exists; it will not be overwritten.' }
$config = Get-Content -LiteralPath (Join-Path $published '.runtime-manifest.json') -Raw | ConvertFrom-Json
if (!$config.runtimeOptions.includedFrameworks) { throw 'The publish output must be self-contained.' }
foreach ($required in @('PixelForge Studio.exe','bridge/render.py','bridge/studio.py','bridge/surfaces.py','bridge/outline_groups.py')) {
    if (!(Test-Path -LiteralPath (Join-Path $published $required))) { throw "Missing published resource: $required" }
}
if (Get-ChildItem -LiteralPath $published -File -Filter '*.dll') { throw 'Unexpected loose runtime DLLs in single-file publication.' }
$current = Join-Path $sourceRoot 'artifacts/current'
if (Test-Path -LiteralPath $current) {
    if ((Get-Item -LiteralPath $current).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Current build is a link; refusing replacement.' }
    if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $current 'PixelForge Studio.exe') }) { throw 'Close the current application before republishing.' }
    $inventoryPath = Join-Path $current '.distribution.json'
    if (!(Test-Path -LiteralPath $inventoryPath)) { throw 'Existing current build has no ownership inventory; preserve it manually.' }
    $inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
    $existing = @(Get-ChildItem -LiteralPath $current -Recurse -File | Where-Object Name -ne '.distribution.json')
    if ($existing.Count -ne $inventory.Count) { throw 'Current build contains added/removed files; preserve them before republishing.' }
    foreach ($file in $existing) {
        $relative = [IO.Path]::GetRelativePath($current,$file.FullName).Replace('\','/')
        $known = $inventory | Where-Object Path -eq $relative
        if (!$known -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $known.Sha256) { throw 'Current build contains changed files; preserve them before republishing.' }
    }
}
$bundle = Join-Path $workRoot ('package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $bundle | Out-Null
try {
Copy-Item -LiteralPath (Join-Path $published 'PixelForge Studio.exe') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $published 'bridge') -Destination $bundle -Recurse
foreach ($file in @('Launch PixelForge.cmd','README.md','CHANGELOG.md','LICENSE','THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination $bundle }
foreach ($file in @('docs/USAGE.md','docs/release/PREPARING.md','docs/release/VERIFICATION.md','docs/release/v0.5.0.md','docs/lighting-assets.md','docs/indexed-image-lab.md','docs/pfa-assets.md','docs/images/app.jpg','docs/images/ui-dark.png','docs/images/ui-light.png','docs/images/grouping.png','docs/images/shadow-fit.png','docs/images/grouping.gif','docs/images/mine.png')) {
    $target = Join-Path $bundle $file
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination $target
}
Copy-Item -LiteralPath (Join-Path $sourceRoot 'examples') -Destination $bundle -Recurse
# Use licenses accompanying the actual runtime; fail rather than omit notices.
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
$licenses = Join-Path $bundle 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
foreach ($framework in $config.runtimeOptions.includedFrameworks) {
    $id = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
    $packageRoot = Join-Path $packages "$id/$($framework.version)"
    $license = Get-ChildItem -LiteralPath $packageRoot -File | Where-Object { $_.Name -match '^LICENSE(\.txt)?$' } | Select-Object -First 1
    if (!$license) { throw "Runtime license not found: $id" }
    Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $licenses "$id-LICENSE.txt")
    if ($framework.name -eq 'Microsoft.NETCore.App') {
        Copy-Item -LiteralPath (Join-Path $packageRoot 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $licenses 'dotnet-THIRD-PARTY-NOTICES.txt')
    }
}
Copy-Item -LiteralPath (Join-Path $SdkDirectory 'Sdks/Microsoft.NET.Sdk.WindowsDesktop/THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $licenses 'windowsdesktop-THIRD-PARTY-NOTICES.txt')

$files = @(Get-ChildItem -LiteralPath $bundle -Recurse -File)
if ($files | Where-Object { $_.Name -match '^(blender|aseprite)\.exe$' -or $_.Extension -in @('.dll','.pdb','.cs','.csproj','.sln','.blend','.log','.pyc') }) { throw 'Unexpected loose runtime/development/tool files in release.' }
$inventory = @($files | ForEach-Object { @{Path=[IO.Path]::GetRelativePath($bundle,$_.FullName).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
$inventory | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundle '.distribution.json')
if ($CreateZip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    try { [IO.Compression.ZipFile]::CreateFromDirectory($bundle,$zipPath,[IO.Compression.CompressionLevel]::Optimal,$false) } catch { if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }; throw }
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath "$zipPath.sha256" -Encoding ascii
    Write-Host "Packaged $($files.Count + 1) files: $zipPath"
}
if (Test-Path -LiteralPath $current) {
    $resolved = (Resolve-Path -LiteralPath $current).Path
    if ($resolved -ne (Join-Path $sourceRoot 'artifacts/current')) { throw 'Invalid current build cleanup boundary.' }
    if (Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Current build contains a link; refusing cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Move-Item -LiteralPath $bundle -Destination $current
Write-Host "Current build: $current"
} finally {
    if (Test-Path -LiteralPath $bundle) {
        $resolved = (Resolve-Path -LiteralPath $bundle).Path
        if (!$resolved.StartsWith((Resolve-Path $workRoot).Path + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package cleanup boundary.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
