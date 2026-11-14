# Release preparation

Work on `master`. Do not create branches, rewrite history, push, tag or publish automatically.

## Build and package

From the source root with the Windows .NET 10 SDK:

```powershell
./Build.ps1 -Check -Publish
./Build.ps1 -Check -Package
```

Publishing prepares the local self-contained `artifacts/current/PixelForge Studio.exe` and external bridge scripts. Packaging also creates `artifacts/releases/PixelForge-Studio-v0.4.0-win-x64.zip` and its SHA-256 checksum. Neither command publishes to GitHub. The ZIP is never overwritten. Existing current builds are replaced only after their ownership inventory verifies that no user files were added or changed.

Extract a ZIP into a new folder and verify startup, generation, saved-frame export and missing-tool messages. Testing on a development machine is not a clean-machine test.

## Integration checks

Configure Blender and Aseprite as in [USAGE.md](../USAGE.md#external-tools). Run these in an interactive Windows session with clipboard access. Fixture `.blend` files come from the tracked `tests/create_*.py` scripts.

```powershell
dotnet run -c Release --project src/PixelForge.Checks -- --modern-ui <static-model.blend> <12-frame-animation.blend> outputs/modern-ui
dotnet run -c Release --project src/PixelForge.Checks -- --workflow <existing-12-frame-output> outputs/workflow
dotnet run -c Release --project src/PixelForge.Checks -- --timing <existing-12-frame-output> outputs/timing
dotnet run -c Release --project src/PixelForge.Checks -- --padding outputs/padding
dotnet run -c Release --project src/PixelForge.Checks -- --v033 - outputs/outline-shadow-regression
```

The modern UI suite exercises the actual WPF dispatcher, Light/Dark persistence, controls, Blender inspection/static/animation generation, cached playback/FPS, timing confirmations, exports, comparison and resize behavior. It captures real WPF surfaces. Inspect the pixels and use the packaged EXE too; a successful build does not establish visual quality. Fractional DPI scale calculations are tested separately from physical multi-monitor/display testing.

## Review and GitHub preparation

Review `git status`, `git diff --check` and the source diff. Commit only tested source, documentation and intentional screenshots. Keep binaries, output assets, profiles and caches ignored. Preserve the MIT License and tool/runtime notices.

Push, release tagging and GitHub publication require an explicit owner request. When preparing a release manually, attach the ZIP/checksum and use [v0.4.0 notes](v0.4.0.md), replacing relative documentation links with the repository's versioned URLs. See [verification](v0.4.0-verification.md) for this candidate's executed checks and limits.
