@echo off
cd /d "%~dp0"
if exist "app-v0.3.3\PixelForge.Desktop.exe" (
  start "" "app-v0.3.3\PixelForge.Desktop.exe"
) else (
  echo Build the standalone application with Build.ps1 first. See README.md.
  pause
)
