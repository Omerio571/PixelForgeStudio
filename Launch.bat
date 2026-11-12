@echo off
cd /d "%~dp0"
if exist "PixelForge.Desktop.exe" (
  start "" "PixelForge.Desktop.exe"
) else if exist "app-v0.3.3\PixelForge.Desktop.exe" (
  start "" "app-v0.3.3\PixelForge.Desktop.exe"
) else (
  echo Build PixelForge Studio with Build.ps1 -Publish first. See README.md.
  pause
)
