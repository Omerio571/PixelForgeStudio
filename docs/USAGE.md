# Using PixelForge Studio

## External tools

Install Blender and Aseprite separately. Standard Steam `steamapps/common` locations are detected. For other locations, launch from PowerShell with full executable paths:

```powershell
$env:PIXELFORGE_BLENDER = 'D:\Apps\Blender\blender.exe'
$env:PIXELFORGE_ASEPRITE = 'D:\Apps\Aseprite\Aseprite.exe'
./Launch.bat
```

These paths are placeholders. Both tools are required for generation/native verification. Missing tools produce an error naming the relevant variable. Reopening frames, comparison and timing-only changes require neither tool.

`PIXELFORGE_OUTPUT` changes the initial output directory; `PIXELFORGE_PROFILE` selects an isolated preferences/history directory. Defaults use Documents and `%LOCALAPPDATA%/PixelForgeStudio/`.

## Grouping and framing

Put related objects into Blender `PF_Outline_…` collections, including nested collections, and enable **Respect Blender Outline Groups**. **Unified** reduces enclosed/narrow internal contours without filling transparent artwork. **Conservative** keeps the earlier contours. Layers, materials and independent motion remain separate. Ambiguous memberships choose the deepest named collection, then the lexicographically first path, with a warning. Grouping is off by default for ordinary assets.

**View and Scale → Fit bounds source → Object Only** excludes ground shadows from fit bounds, keeping model scale stable. Shadows can clip with a warning. **Object + Ground Shadow** reserves shadow space and can shrink the model. Fixed density reports insufficient canvas space rather than silently rescaling.

## Animation and manual edits

Frame sampling selects which Blender frames to render. **Playback FPS**, next to Play/Pause, retimes cached frames immediately. **Apply / Save timing** persists distributed integer-millisecond durations without changing pixels or spritesheet PNGs.

Save and close Aseprite before applying timing: unsaved external edits cannot be detected. Timing patches the latest saved native document and keeps backups. Reopen it afterward. Use **Reload saved edits** to import saved pixel edits into PNG previews/exports.

Each generation writes a separate folder. Keep settings, native file, frames and metadata together. Comparison retains references and never regenerates or deletes artwork.

## Controls and limits

Numeric edits apply on Enter or focus loss; Escape restores the last valid value. Display formatting and input-mode switches preserve untouched internal precision. The built-in starter palette is also supplied in `examples/PixelForge-default.gpl`.

- Canvas: 1–2048 pixels per axis; animation: 1–256 frames and 16 million total pixels; spritesheet: at most 8192 pixels per axis.
- Open trusted models. Embedded auto-run is disabled, but this is not an OS sandbox.
- Complex linked/procedural shaders use explicit fallback colors. Collection instances, uncached simulations and arbitrary external dependencies are not guaranteed; realize instances in a separate model copy.
- Palette contrast, resolution and geometry limit binary-alpha shadow softness. Inspect warnings and pixels at game resolution; technical validation is not artistic approval.
- Preserve palette membership and binary alpha in manual edits. Inspect **Technical details** and output logs for failed conversions; incomplete outputs do not enter successful history.
