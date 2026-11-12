param([switch]$Publish, [switch]$Check, [ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$localSdk = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
$sdk = if (Test-Path $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
& $sdk build PixelForgeStudio.sln -c $Configuration -m:1
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Check) {
    & $sdk run --no-build -c $Configuration --project src/PixelForge.Checks
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
}
if ($Publish) {
    & $sdk publish src/PixelForge.Desktop -c Release -r win-x64 --self-contained true -o app-v0.3.3 -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
