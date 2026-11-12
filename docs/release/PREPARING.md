# Release preparation

## Build and package

With a Windows .NET 10 SDK and PowerShell, run from the source root:

```powershell
./Build.ps1 -Check -Package
```

This publishes a fresh self-contained staging folder, adds documentation, palette and runtime notices, and creates `artifacts/PixelForge-Studio-v0.3.3-win-x64.zip` plus SHA-256 checksum. It refuses to replace an existing ZIP. Move an earlier candidate aside intentionally before rebuilding. `-Publish` alone retains the `app-v0.3.3/` workflow.

Extract to a new folder, run the launcher and verify generation, saved-frame export and missing-tool messages. Extraction on the development machine is not a clean-machine test.

## Integration checks

Configure tools as in [USAGE.md](../USAGE.md#external-tools).

```powershell
dotnet run -c Release --project src/PixelForge.Checks -- --v033 - outputs/release-checks
```

This creates its own fixture and covers static/animated grouping, shadows, all four isometric views, native compositing and persistence. Other suites are `--usability <static.blend> <animation.blend> <output-dir>`, `--edges <static.blend> <fixture.blend> <output-dir>` and `--timing <existing-12-frame-output> <output-dir>`. Generate fixtures with tracked `tests/create_*.py` scripts. Optional historical comparisons use `PIXELFORGE_LEGACY_CORE`; ordinary checks need no old builds.

## Review, commit and tag

Review changes and [verification](VERIFICATION.md), then intentionally commit. The build script performs no Git mutations.

```powershell
git status --short
git diff --check
git diff
git add .gitignore README.md CHANGELOG.md LICENSE THIRD_PARTY_NOTICES.md Build.ps1 Package.ps1 Launch.bat "Run PixelForge.cmd" docs
git diff --cached --check
git diff --cached --stat
git commit -m "chore(release): prepare PixelForge Studio v0.3.3"
git status --short
git show --stat HEAD
```

After verifying **that release commit**, create its tag only if absent:

```powershell
git show-ref --verify --quiet refs/tags/v0.3.3
if ($LASTEXITCODE -eq 0) { throw 'v0.3.3 already exists; do not overwrite it.' }
if ($LASTEXITCODE -ne 1) { throw 'Could not check existing tags.' }
git tag -a v0.3.3 -m "PixelForge Studio v0.3.3" HEAD
```

Keep existing branches/remotes intact. Push and publish only after owner approval, attaching the ZIP/checksum and [release notes](v0.3.3.md). The tag is deferred while these changes await the owner's commit.

When pasting the notes into GitHub Releases, replace their relative documentation links with the actual repository's `blob/v0.3.3/…` URLs. Do not invent a repository URL before the destination is selected.
