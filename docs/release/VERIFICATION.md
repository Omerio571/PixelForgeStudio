# v0.3.4 zero-padding follow-up

Verified locally on Windows x64 with Blender 5.3.0, Aseprite 1.3.18.6 and .NET SDK 10.0.404. This update was developed on local main and does not publish a release or push to GitHub.

## Reproduced causes and fixes

1. The retained old extraction (`PixelForge.Desktop.exe`, build 749bb24) displayed Padding (0.02–0.4) and rejected an actual zero-padding settings file in its GUI. The current application is `artifacts/current/PixelForge Studio.exe`; the source-root launchers select it. The title now shows the build revision, and the label is Padding (0–0.40). Old user-selected extractions are not overwritten.
2. The bridge imposed an undocumented 0.001 minimum camera span. A 0.0002-world-unit tile therefore could not fill its canvas. Fit now uses its actual positive projected extent, with an explicit error for nonfinite/degenerate extents. The GUI-rendered/exported tiny tile occupies all 32×32 pixels at padding zero; native layers and composites validate. Positive padding still yields the expected margin.
3. Canvas-edge/shadow warnings changed the preview height. Integer Fit zoom could drop from 3× to 2× at zero padding, making the larger exported sprite look smaller on screen. The existing warning area now has a stable scrollable height. Actual 128×128 GUI rerenders retain the same integer zoom at zero/0.10, and their PNGs remain byte-identical to the measured exports. A long-warning WPF regression checks viewport and zoom stability.

## Actual GUI pixel comparisons

The same copied Blender model, IsometricSE camera and Dimetric2To1 projection were used at padding 0, 0.02 and 0.10. Each row keeps its other settings identical. All six PNGs were saved through the WPF Save Current PNG action. Their hashes match the generated frame PNGs; saved settings and Blender jobs contain the exact padding values.

| Canvas / features | Padding 0 visible bounds | Padding 0.02 | Padding 0.10 |
|---|---|---|---|
| 32×32, outlines/shadows off | x0 y7, 32×25 | x1 y7, 30×24 | x3 y9, 26×20 |
| 128×128, selective outlines/shadows on | x0 y27, 128×101 | x3 y28, 125×97 | x13 y34, 107×81 |

Bounds include visible ground-shadow pixels. At 128×128 the model alone is respectively 128×101, 122×97 and 102×81. Decreasing padding does not shrink the fitted model. The model's projected width is 3.889087 world units and height is 3.077358: width limits a square canvas. The existing bottom/ground anchor leaves 7 or 27 empty top rows at zero; filling those rows would distort the aspect ratio. Zero adds no extra margin.

Both resolutions also rendered three-frame animations through the real GUI with padding zero; shared camera, source preservation and native compositing passed. A GUI spritesheet export retains 32×32 frame dimensions, source order and durations. Slider/numeric switches retained 0.1 and exactly zero, displaying zero as 0. The exported comparison contact sheet was inspected visually at equal canvas magnification. Final tiny-tile export bounds are x0 y0, 32×32.

Fit to Canvas and Consistent World Scale remain distinct. Fixed density keeps the requested pixels per unit, so zero does not force a small model to fill the canvas. Object Only ignores shadow bounds and may clip shadows; explicit Object + Shadow fit includes the shadow footprint/softness budget. Shared animation framing fits the motion union, so individual frames may leave space. No mode is silently switched.

## Executed checks and Git

| Suite | Passed |
|---|---:|
| Base | 25 |
| New padding comparison matrix | 218 |
| Zero padding, tiny geometry and stable preview layout | 43 |
| Existing rendering/grouping/framing | 237 |
| Timing/native edit preservation | 69 |
| Existing usability/comparison/export | 68 |
| Existing workflow/directory/preservation | 43 |
| **Distinct total** | **703** |

The comparison matrix renders 32/128, static/animated, outlines on/off, shadows on/off at all three paddings, plus shadow-inclusive/fixed-density cases. It measures model/composite bounds, exact serialized job values, camera span and exported pixels. Normal-model comparisons were followed by the tiny-scale floor fix and final 237-check framing regression. The 43-check padding suite tests the final bridge and warning layout. Two optional historical-assembly comparisons were skipped. `./Build.ps1 -Check -Publish` succeeded; publishing reported NU1900 when the vulnerability feed was unreachable. This is not a clean-machine test or an online vulnerability audit.

There was no local/remote main branch when inspected. Local main was created from existing master and safely fast-forwarded through the latest a0ef963 work, including the user's image commit. All old commits/branches remain. AGENTS.md now explicitly requires main for normal development/commits and prohibits automatic branches, pushes or history rewrites. The completed fix is committed locally on main; remote branches and existing release ZIPs remain unchanged. README and MIT license remain unchanged.

Evidence is retained under ignored `outputs/padding-fix/`: GUI exports, visible-bounds.json, comparison contact sheets, preserved GUI traces, exact settings/jobs, suite logs/results and native validation. Test artwork is preserved rather than overwritten. Historical verification reports follow.

---

# v0.3.4 final cleanup and zero-padding verification

Verified on Windows x64, 2026-11-13, with .NET SDK 10.0.404, .NET/Windows Desktop 10.0.15, Blender 5.3.0 and Aseprite 1.3.18.6. GitHub Releases was checked: only V0.3.3 is published, so these changes remain v0.3.4. No push, tag or publication is automatic. This is not a clean-machine test.

## Executed checks

| Suite | Passed |
|---|---:|
| Base | 25 |
| Rendering, grouping and framing | 237 |
| Animation and exports | 51 |
| Playback timing | 69 |
| Comparison and usability | 68 |
| Workflow, preservation and directories | 43 |
| Zero padding | 39 |
| **Total distinct checks** | **532** |

All suites were executed again for this cleanup. Two optional comparisons against historical compiled renderers were skipped. The padding suite renders square tiles at 8, 16, 32, 64 and 128 pixels with both sampling modes. It verifies exact canvases, complete edge outlines and no forced transparent border; a 2:1 rectangle retains its proportions. Positive padding retains margins. Animated zero-padding retains shared size, ground alignment and source motion. Actual WPF slider/numeric input and restart persistence are checked. The ordinary bridge framing equations, inward outline algorithms, shadow fit behavior and animation generation are unchanged. Validation now accepts padding zero and, only at zero, fully occupied canvases.

`./Build.ps1 -Check -Package` succeeded. A fresh source-only copy built with zero warnings/errors and passed the 25 base checks using the installed SDK on PATH. An earlier attempted concurrent build encountered locked check-harness assemblies; it was repeated successfully after those checks finished. Publishing reports NU1900 when the vulnerability feed is unreachable; no successful online vulnerability audit is claimed.

## Extracted single-file application

Microsoft's supported `PublishSingleFile` plus `IncludeNativeLibrariesForSelfExtract` produces one managed/native runtime bundle. The executable is renamed `PixelForge Studio.exe`. Native components use the standard .NET extraction cache. This is a bundled application/runtime executable, **not an entirely single-file distribution**: external Python bridge scripts and runtime notices remain alongside it. Blender and Aseprite are separate installations. No trimming, custom dependency resolver or new runtime dependency was introduced.

The new ZIP was extracted into an independent directory and started through `Launch PixelForge.cmd`. That actual EXE detected installed tools, rendered a 32×32 fully occupied zero-padding tile and a three-frame 64×64 animation with selective outlines, highlights and ground shadows, and validated native cels, exact dimensions, approved colors, binary alpha and unchanged sources. The tile PNG and animation spritesheet PNG/JSON exported successfully. The app compared static and animated renders. Open in Aseprite preserved the animation, and Aseprite displayed its three frames, tag and editable component/shadow layers. Opening/closing without edits left its native bytes unchanged. Bridge imports left no `__pycache__` in the installed folder. Input .blend copies were outside project source directories; runtime bridge lookup uses the executable directory.

## Cleanup performed

- Removed `app/`, `app-v0.3/`, `app-v0.3.1/`, `app-v0.3.2/`, `app-v0.3.2-groups/`, `app-v0.3.2-usability/` and `app-v0.3.3/` after inventorying all files. They contained generated runtimes, application binaries and bridge copies, with no artwork or settings. Distinct historical bridge sources were preserved under ignored `backups/retired-build-bridges/`; their file/hash inventory remains under `outputs/cleanup-inventory/`.
- Removed redundant `artifacts/checks/` extraction after checking its files against the preserved v0.3.3 ZIP; only generated Python cache files were extra. Removed the earlier task-generated `artifacts/v034-shipping-extracted/` and the single-file probe. Source-check/staging/package inputs are disposable and cleaned after verification.
- Kept v0.3.3 ZIP/checksum in `artifacts/releases/`; its SHA-256 matches the GitHub release digest. Replaced the explicitly superseded, unpublished flat-layout v0.3.4 candidate with the new bundled layout, rather than replacing a published release. Final releases have checksums beside them.
- Deliberately retained `.git/`, active `src/`, `bridge/`, `tests/`, `docs/`, intentional `examples/`, `.tools/`, all `outputs/`, `RenderSettings/` and existing `backups/`. Old output evidence may contain manual artwork or useful history, so it was not swept by age. The separately running `artifacts/PixelForge-Studio-v0.3.4-win-x64/` extraction is left intact for its owner.
- The preexisting unrelated `docs/images/BlenderToPixel.png` remains byte-identical and is excluded from this change and package. MIT licensing is unchanged. No generated builds/artwork were tracked; `.gitignore` already covers bin/obj, SDKs, publishes, artifacts, logs, local settings and render outputs without blanket image/JSON/Blender exclusions.

## Predictable layouts

Source: `src/`, `bridge/`, `tests/`, `docs/`, `examples/`, solution/project files, build/launch scripts, README, CHANGELOG, notices and MIT license.

Release: `PixelForge Studio.exe`, `Launch PixelForge.cmd`, README/CHANGELOG/LICENSE/notices, external `bridge/`, `docs/`, `examples/`, `licenses/`, and a build ownership inventory. No loose DLLs appear in the release root. `artifacts/current/` is the stable source-launcher target; `artifacts/releases/` holds ZIPs/checksums. `artifacts/work/` is temporary. An executed added-file guard check refused replacement and preserved its sentinel. Current builds with changed/added files or running processes block replacement; existing release ZIPs are never silently overwritten.

Raw evidence remains in ignored `outputs/cleanup-*.log`, suite result directories and desktop exports. Historical workflow-only v0.3.4 and v0.3.3 reports follow.

---

# v0.3.4 release verification

Verified locally on Windows x64, 2026-11-13, with .NET SDK 10.0.404, Blender 5.3.0 and Aseprite 1.3.18.6. The self-contained package includes .NET/Windows Desktop 10.0.15. This was **not a clean-machine test**.

## Functional checks

| Suite | Passed | Coverage |
|---|---:|---|
| Base | 25 | Settings, palette validation/imports, dimensions, deterministic pixels, binary alpha and missing-tool errors |
| Rendering/grouping/framing | 237 | Real static and animated Blender/Aseprite output, grouping, outlines/highlights, shadows, four directions, native compositing and preferences |
| Animation/export | 51 | Motion, sampling, tags, native edits/reload, cancellation and exports |
| Playback timing | 69 | 12→6→24 FPS, cumulative millisecond rounding, latest saved edits, unchanged pixel data, metadata and native reopening |
| Usability | 68 | Comparison, history, favorites, resolutions, numeric controls and persistence |
| Workflow | 43 | Independent directories, actual WPF palette/clipboard/removal dialogs, outline approval, visibility, preview modes, temporary ownership, live/stale process leases, manual edits and permanent preservation |
| **Total** | **493** | Repeated runs are counted once |

The final ZIP's Core/Desktop assemblies and bridge were copied byte-for-byte into a separate harness and passed the 43 workflow, 69 timing and 68 usability checks. Base checks also passed during the final build. The 237 rendering and 51 animation checks passed earlier in this update; subsequent changes were workflow/ownership safeguards and documentation. The two optional rendering comparisons against older compiled assemblies were skipped. Rendering algorithms and bridge scripts have no source changes. Actual output pixels and native composites were inspected; this does not establish artistic approval for arbitrary models.

## Actual desktop workflow

The extracted launcher's application generated and validated a 64×48 static sprite and a three-frame 64×64 animation using real Blender and Aseprite. Model, shared settings, PNG and spritesheet directory preferences persisted independently. PNG and spritesheet exports were saved into separate destinations. Closing with Cancel retained the session; closing with Discard cleaned its owned temporary directory while source and exported image hashes remained unchanged.

The relocated inspector read the real source range. Playback FPS changed 12→6→24 immediately without changing PNG hashes/write times or the Blender render log. Saving 24 FPS produced frame durations 42, 41, 42 ms (125 ms total for three frames). Switching the exclusive preview toggle left image bytes unchanged. Favoriting preserved the latest timing and all necessary files in Library. Open in Aseprite displayed three frames, the animation tag and component layers; closing without editing left the native hash unchanged. Reopening the saved asset restored 24 FPS without rendering. Normal close removed the remaining owned Temp session and retained the favorite and explicit exports. The documentation screenshot is from this actual v0.3.4 application.

## Build, packaging and limits

- `./Build.ps1 -Check -Package` succeeded. A fresh source-only copy also built with zero warnings/errors and passed 25 base checks using the installed SDK on PATH, without old binaries or a project SDK cache. The final Windows x64 ZIP is self-contained and excludes test harnesses, debug symbols, generated artwork, caches and local user paths in application assemblies.
- Publishing reported **NU1900** because the NuGet vulnerability feed was unreachable. No successful online vulnerability audit is claimed. No new dependencies were introduced.
- Temporary cleanup is conservative: changed, unrecognized, protected-input or incompletely registered interrupted-session data is retained and reported. Only unchanged, inventoried, owned session directories with no live lease are removed. Favorites preserve complete editable assets; history rotation remains read-only.
- Unsaved changes inside an external Aseprite process cannot be detected. The interface warns about this and preserves temporary native artwork before opening it externally.
- Strict palette validation remains enforced. The existing application has no non-strict rendering mode.
- MIT licensing is unchanged. The preexisting unrelated `docs/images/BlenderToPixel.png` is untouched and excluded from the package. No commit, push, tag or GitHub publication was performed for this update.

Raw local test evidence is retained under ignored `outputs/v034-*` paths. Historical v0.3.3 verification follows.

---

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
