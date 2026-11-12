# v0.3.3 release verification

Verified locally on Windows x64, 2026-11-12, with .NET SDK 10.0.404, Blender 5.3.0 and Aseprite 1.3.18.6. The self-contained package contains .NET/Windows Desktop 10.0.15. This was **not a clean-machine test**.

## Functional checks

Existing automated suites ran against published Release `PixelForge.Core.dll` and `PixelForge.Desktop.dll`, copied byte-for-byte from a fresh ZIP extraction into the separate check harness. Published bridge scripts were used; the harness is not included in the ZIP.

| Suite | Passed | Coverage |
|---|---:|---|
| Base | 25 | Settings, palette imports, custom dimensions, deterministic pixels, binary alpha and missing-tool errors |
| v0.3.3 | 237 | Static/animated grouping, selective outlines/highlights, component/native composites, shadow variants, four isometric directions, fixed density, true isometric and preferences |
| Animation/export | 51 | Twelve-frame motion, tags, sampled/portrait output, native edits/reload, cancellation, spritesheets and PNG-frame exports |
| Playback timing | 69 | 12→6→24 FPS and other rates, rounding, latest saved edits, byte-preserved artwork, backups, metadata and reopening in real Aseprite |
| Usability | 68 | Render comparison, history, favorites, presets, numeric controls and saved preferences |
| **Total** | **450** | Repeated checks are not added to this total |

The two optional v0.3.3 comparisons against an older compiled renderer were skipped; historical assemblies are not required. Grouping and framing comparison pixels were visually inspected. These fixtures do not establish artistic approval for arbitrary models.

After removing published debug records/local build paths, the final symbol-free ZIP extraction passed the 25 base, 69 timing and 68 usability checks again. Its launcher opened successfully and its actual executable generated/validated a 128×128 native sprite with unchanged source, exact palette membership, binary alpha and matching layer composite. An earlier extracted candidate also generated a 64×48 sprite, and its Open in Aseprite handoff displayed the native artwork and component layers. Application/renderer source did not change during cleanup.

## Build and package

- `./Build.ps1 -Check -Package` succeeded. A fresh source-only copy, with no old binaries or SDK cache, also built with zero warnings/errors and passed 25 base checks using the installed SDK on PATH.
- Publishing reported **NU1900**: the NuGet vulnerability feed was unreachable. No successful online vulnerability audit is claimed. The original projects have no third-party NuGet dependencies; restored Microsoft runtime packs retain their notices.
- ZIP includes launcher, self-contained runtime, all four bridge scripts, starter palette, documentation, MIT license and upstream runtime notices. No Blender/Aseprite executable, PDB, source tree, logs, user profile or render results are shipped.
- Publishing performs a non-incremental symbol-free rebuild; packaging rejects local user build paths in PixelForge assemblies and refuses to overwrite an existing ZIP.
- Normal-permission launch/native-editor integration passed. Restricted test processes initially encountered denied Aseprite configuration and sandbox temporary-directory writes; normal GUI access and a workspace-local writable test TEMP resolved those environment restrictions.

## Cleanup and source review

- Removed only the three confirmed MSBuild `src/*/obj` cache trees before rebuilding. They regenerated normally and remain ignored. Temporary release staging/source-copy/check-harness duplicates were removed after verification.
- No active source, bridge, generated Lua integration, tests, palette, user model, saved artwork or historical source backup was removed.
- Existing `app*`, `backups/`, `outputs/`, `RenderSettings/` and generated fixture files are intentionally retained locally for owner review; age alone cannot establish that artwork or historical records are disposable. They are excluded from Git and the release ZIP.
- Source review found no hardcoded personal paths/tokens/private keys; generated outputs and SDK/build caches are ignored. MIT holder/year follow the owner's documented identity and current year. Bundled runtime licenses/notices are retained; external Blender/Aseprite code/binaries are not redistributed.
- Changes remain uncommitted for owner review. No remote changes, pushes, tags or GitHub release publication were performed. Create `v0.3.3` only after committing and verifying the release commit, using [PREPARING.md](PREPARING.md).

Raw logs, machine-readable results and rendered evidence stay local under ignored `outputs/release-final/`. The ZIP checksum is supplied beside the release asset.
