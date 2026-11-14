# Using PixelForge Studio

## External tools

Install Blender and Aseprite separately. Standard Steam `steamapps/common` locations are detected. For other locations, launch from PowerShell with full executable paths:

```powershell
$env:PIXELFORGE_BLENDER = 'D:\Apps\Blender\blender.exe'
$env:PIXELFORGE_ASEPRITE = 'D:\Apps\Aseprite\Aseprite.exe'
./"Launch PixelForge.cmd"
```

These paths are placeholders. Both tools are required for generation/native verification. Missing tools produce an error naming the relevant variable. Reopening frames, comparison and timing-only changes require neither tool.

`PIXELFORGE_PROFILE` selects an isolated preferences/history/storage directory. The default is `%LOCALAPPDATA%/PixelForgeStudio/`. The desktop no longer uses the legacy `PIXELFORGE_OUTPUT` variable to permanently save every generation.

## Temporary sessions and saving

Generate writes to a unique leased `Temp/` session under the application profile. Recent comparison renders remain available for that session. **Save Current PNG** exports the displayed frame; **Save Spritesheet** exports PNG and timing JSON. Each operation remembers its own directory, independently of models, settings, palette imports, native files and frame exports. Canceling a dialog does not change its directory preference.

**More export actions → Preserve editable render** and persistent comparison favorites keep the full editable working folder in the permanent `Library/`. Files move without duplication when possible; a safe copy is used if moving is unavailable or the render belongs to another instance. Removing a favorite removes its history flag, not its saved artwork. Opening Aseprite or reloading edits first offers explicit preservation; use Aseprite **Save As** for a chosen destination.

Closing offers Discard temporary renders, Preserve renders, or Cancel. Explicit exports and Library files are never swept. Startup cleans only marked, unchanged abandoned sessions whose lease is not held by another instance. Changed, added, linked, protected-input or unrecognized files are retained for review; a crash during an unfinished/unregistered operation can therefore leave recoverable data in `Temp/`. No unsaved external Aseprite-edit detection is claimed. Save/close Aseprite before timing updates.

Do not use `Temp/` as a permanent save destination. Export dialogs require a destination outside it. Original models, imported palettes and older pre-v0.3.4 output folders remain untouched.

## Color controls and preview

Swatches remain visible with **Manage Colors** collapsed; expansion is remembered. Select a swatch to see/copy its HEX value. Ctrl+C copies it while palette controls have focus, without replacing normal text-field copying. Removal requires Remove/Cancel confirmation.

Outline color has a swatch, editable HEX, RGB picker and visual palette choices. A custom color stays selected; **Add outline color to palette** approves it explicitly. This application supports strict palettes only: generation with outlines requires an approved dark color. There is no new non-strict rendering mode.

Checkerboard/Ground Preview is one mutually exclusive, remembered toggle. It changes only preview backgrounds. Disabled feature details collapse without resetting their values. **Inspect Animation Range** stays beside the bottom frame/playback controls.

## Grouping and framing

Padding accepts 0–0.4 in slider and numeric modes. Exactly zero adds no framing margin; proportions and the shared animation camera stay unchanged. Outlines draw inward at canvas edges. Fully occupied tiles are valid at zero; reaching an edge still prompts a clipping review, and Object Only may clip ground shadows rather than shrink the object.

Put related objects into Blender `PF_Outline_…` collections, including nested collections, and enable **Respect Blender Outline Groups**. **Unified** reduces enclosed/narrow internal contours without filling transparent artwork. **Conservative** keeps the earlier contours. Layers, materials and independent motion remain separate. Ambiguous memberships choose the deepest named collection, then the lexicographically first path, with a warning. Grouping is off by default for ordinary assets.

**View and Scale → Fit bounds source → Object Only** excludes ground shadows from fit bounds, keeping model scale stable. Shadows can clip with a warning. **Object + Ground Shadow** reserves shadow space and can shrink the model. Fixed density reports insufficient canvas space rather than silently rescaling.

## Animation and manual edits

Frame sampling selects which Blender frames to render. **Playback FPS**, next to Play/Pause, retimes cached frames immediately. **Apply / Save timing** persists distributed integer-millisecond durations without changing pixels or spritesheet PNGs.

Save and close Aseprite before applying timing: unsaved external edits cannot be detected. Timing patches the latest saved native document and keeps backups. Reopen it afterward. Use **Reload saved edits** to import saved pixel edits into PNG previews/exports.

Each generation has a separate session working folder. Preserve it explicitly before relying on it across restarts. Keep preserved settings, native file, frames and metadata together. Comparison rotation itself never regenerates or deletes artwork; only the managed session lifecycle cleans unchanged unsaved renders.

## Controls and limits

Numeric edits apply on Enter or focus loss; Escape restores the last valid value. Display formatting and input-mode switches preserve untouched internal precision. The built-in starter palette is also supplied in `examples/PixelForge-default.gpl`.

- Canvas: 1–2048 pixels per axis; animation: 1–256 frames and 16 million total pixels; spritesheet: at most 8192 pixels per axis.
- Open trusted models. Embedded auto-run is disabled, but this is not an OS sandbox.
- Complex linked/procedural shaders use explicit fallback colors. Collection instances, uncached simulations and arbitrary external dependencies are not guaranteed; realize instances in a separate model copy.
- Palette contrast, resolution and geometry limit binary-alpha shadow softness. Inspect warnings and pixels at game resolution; technical validation is not artistic approval.
- Preserve palette membership and binary alpha in manual edits. Inspect **Technical details** and output logs for failed conversions; incomplete outputs do not enter successful history.
