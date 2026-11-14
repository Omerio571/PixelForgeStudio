@echo off
cd /d "%~dp0"
if exist "PixelForge Studio.exe" (
  start "" "PixelForge Studio.exe"
) else if exist "artifacts\current\PixelForge Studio.exe" (
  start "" "artifacts\current\PixelForge Studio.exe"
) else (
  echo Build PixelForge Studio with Build.ps1 -Publish first. See README.md.
  pause
)
