param([switch]$Publish, [switch]$Check, [switch]$Package, [ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$localSdk = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
$sdk = if (Test-Path $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
if ($Package -and (Test-Path 'artifacts/PixelForge-Studio-v0.3.3-win-x64.zip')) { throw 'Release ZIP already exists; preserve or move it before building another candidate.' }
& $sdk build PixelForgeStudio.sln -c $Configuration -m:1
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Check) {
    & $sdk run --no-build -c $Configuration --project src/PixelForge.Checks
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
}
if ($Publish -or $Package) {
    $destination = if ($Package) { Join-Path $PSScriptRoot ('artifacts/staging/v0.3.3-' + [Guid]::NewGuid().ToString('N').Substring(0,8)) } else { 'app-v0.3.3' }
    # Rebuild so cached debug assemblies cannot leak local PDB paths into a release.
    & $sdk build src/PixelForge.Desktop -c Release -r win-x64 --no-incremental -p:SelfContained=true -p:DebugType=None -p:DebugSymbols=false -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Release rebuild failed.' }
    & $sdk publish src/PixelForge.Desktop --no-build -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $destination -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if ($Package) {
        $sdkVersion = (& $sdk --version).Trim()
        $sdkDirectory = Join-Path (Split-Path $sdk) "sdk/$sdkVersion"
        & "$PSScriptRoot/Package.ps1" -PublishedFolder $destination -SdkDirectory $sdkDirectory
    }
}
