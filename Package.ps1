param([Parameter(Mandatory)][string]$PublishedFolder, [Parameter(Mandatory)][string]$SdkDirectory)
$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
$published = (Resolve-Path -LiteralPath $PublishedFolder).Path
$releaseRoot = Join-Path $sourceRoot 'artifacts'
$zipPath = Join-Path $releaseRoot 'PixelForge-Studio-v0.3.3-win-x64.zip'
if (Test-Path -LiteralPath $zipPath) { throw 'Release ZIP already exists; it will not be overwritten.' }
$config = Get-Content -LiteralPath (Join-Path $published 'PixelForge.Desktop.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($required in @('PixelForge.Desktop.exe','PixelForge.Desktop.dll','PixelForge.Core.dll','hostfxr.dll','coreclr.dll','PresentationFramework.dll','bridge/render.py','bridge/studio.py','bridge/surfaces.py','bridge/outline_groups.py')) {
    if (!(Test-Path -LiteralPath (Join-Path $published $required))) { throw "Missing published resource: $required" }
}
if (!$config.runtimeOptions.includedFrameworks) { throw 'The publish output must be self-contained.' }
foreach ($name in @('PixelForge.Core.dll','PixelForge.Desktop.dll')) {
    $binaryText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $published $name)))
    if ($binaryText -match '[A-Za-z]:\\Users\\') { throw "Private build path in $name; publish without debug symbols." }
}

# Assemble a separate, fresh distribution; never remove publish/artwork files.
$bundle = Join-Path $releaseRoot ('package/v0.3.3-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $bundle -Force | Out-Null
Get-ChildItem -LiteralPath $published -Recurse -File | Where-Object { $_.Extension -ne '.pdb' -and $_.FullName -notmatch '[\\/]__pycache__[\\/]|\.pyc$' } | ForEach-Object {
    $relative = $_.FullName.Substring($published.Length + 1)
    $target = Join-Path $bundle $relative
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $target
}
foreach ($file in @('Launch.bat','README.md','CHANGELOG.md','LICENSE','THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination $bundle }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'docs') -Destination $bundle -Recurse
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
if ($files | Where-Object { $_.Name -match '^(blender|aseprite)\.exe$' -or $_.Extension -in @('.pdb','.cs','.csproj','.sln','.blend','.log') }) { throw 'Unexpected development/tool/asset files in release.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($bundle.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath "$zipPath.sha256" -Encoding ascii
Write-Host "Packaged $($files.Count) files: $zipPath"
