param([switch]$Publish, [switch]$Check, [switch]$Package, [ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$localSdk = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
$sdk = if (Test-Path $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$zip = Join-Path $PSScriptRoot 'artifacts/releases/PixelForge-Studio-v0.4.0-win-x64.zip'
if ($Package -and (Test-Path -LiteralPath $zip)) { throw 'Release ZIP already exists; archive the previous candidate explicitly before packaging a replacement.' }
& $sdk build PixelForgeStudio.sln -c $Configuration -m:1
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Check) {
    & $sdk run --no-build -c $Configuration --project src/PixelForge.Checks
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
}
if ($Publish -or $Package) {
    $workRoot = Join-Path $PSScriptRoot 'artifacts/work'
    New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
    $staging = Join-Path $workRoot ('publish-' + [Guid]::NewGuid().ToString('N'))
    try {
        & $sdk publish src/PixelForge.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $staging -m:1
        if ($LASTEXITCODE -ne 0) { throw 'Single-file publish failed.' }
        Rename-Item -LiteralPath (Join-Path $staging 'PixelForge.Desktop.exe') -NewName 'PixelForge Studio.exe'
        Copy-Item -LiteralPath 'src/PixelForge.Desktop/bin/Release/net10.0-windows/win-x64/PixelForge.Desktop.runtimeconfig.json' -Destination (Join-Path $staging '.runtime-manifest.json')
        $sdkVersion = (& $sdk --version).Trim()
        $sdkDirectory = Join-Path (Split-Path $sdk) "sdk/$sdkVersion"
        & "$PSScriptRoot/Package.ps1" -PublishedFolder $staging -SdkDirectory $sdkDirectory -CreateZip:$Package
    } finally {
        if (Test-Path -LiteralPath $staging) {
            $resolved = (Resolve-Path -LiteralPath $staging).Path
            if (!$resolved.StartsWith((Resolve-Path $workRoot).Path + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid staging cleanup boundary.' }
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
