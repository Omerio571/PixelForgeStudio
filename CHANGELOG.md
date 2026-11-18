# Changelog

Meaningful development changes, newest first. Historical entries follow saved versioned source/documentation and verification records; no historical release dates are assumed.

## Experimental — PFA asset workflow

- Added reusable export profiles, independent lossless PFA layers, and direct asset viewing with decoded PNG export.

## v0.5.0 — Emission maps and lighting previews

- Export material-level grayscale emission, geometry normals and neutral palette art in matching static and animated atlases.
- Preview adjustable glow or diffuse studio light without regenerating art. Compare color, normals and emission at linked frame indices.
- Put Dynamic Lighting first in Lighting and Shadows, with independent Studio Lighting and Ground Shadows controls.
- Keep palette actions together at the wider default sidebar size, remember sidebar width and display camera elevation in degrees.

## v0.4.0 — Modern desktop workspace

### Dynamic lighting assets

- Added an optional neutral material pass and aligned world-space normal maps for static sprites and animations.
- Added playback map switching, side-by-side map comparison, and versioned lighting asset export.
- Added a standalone raylib validator and documented the world-space convention and saved-edit behavior.

### Camera perspectives

- Added Isometric, Top, Bottom and Side families with direction-specific orthographic presets and adjustable 40–85° Top-Down 3/4.
- Added four-direction rendering with shared scale and ground alignment, separate previews, and named PNG frame, spritesheet and editable Aseprite exports.
- Retained original camera settings compatibility and added camera preset, elevation and world scale to export metadata.

### UI polish and fixes

- Clearly separated Add Colors (HEX / RGB) from collapsed advanced Source Color Indices. Multi-add accepts semicolon/newline/whitespace HEX, RGB and JSON, validates complete batches, preserves order and reports skipped duplicates without partial edits.
- Replaced forced narrow columns with responsive lighting, camera and outline cards; clarified Key Light Intensity as the existing raw Blender Sun strength and kept complete percentage labels and numeric precision.
- Added full-width bottom diagnostics with a resize boundary, wrapping/scrolling, errors, warnings, recent progress, build details and clipboard copying.
- Retained independent disk-backed dialog directories, normalized relative selections and added graceful fallback for missing or unreadable locations.
- Simplified the title/header to PixelForge Studio and moved the automatic assembly version to the status bar.

### Workspace overhaul

- Implemented the approved mint-accent workspace with a compact header, resizable settings sidebar, seven remembered sections, prominent pixel preview and separate Generate/export areas.
- Added complete live Light and Dark themes, remembered across restarts, and reusable WPF templates for inputs, dropdowns, segments, sliders, scrollbars, menus, tooltips, progress and application-owned confirmations.
- Moved playback FPS, duration, timeline and transport together; secondary exports are available from the preview's ••• menu. Settings load/save is in the header.
- Retained palette editing, strict outline-color approval, exact numeric values, render history/favorites, saved-frame comparison and all rendering/export services.
- Kept warnings in a stable scrollable area and made preview zoom use integer device pixels at fractional Windows display scaling.
- Centered startup placement and capped initial window dimensions to the available desktop work area.
- Normal development now uses master. No branch creation, automatic push or release publication.

## v0.3.4 — Workflow and UI cleanup

- Exact zero framing padding in slider/numeric modes, including fully occupied terrain tiles and inward canvas-edge outlines.
- Windows distribution bundles the application/runtime into PixelForge Studio.exe, with external bridge scripts; stable current build, releases directory and automatic staging cleanup.

### Added
- Independent persistent directories for models, shared settings load/save, PNG, spritesheet, palette, native, frame and saved-render dialogs.
- Compact, remembered Manage Colors section with selected HEX copying and removal confirmation.
- Visual outline swatch/palette picker, RGB picker, editable HEX and explicit approval of custom colors in the strict palette.
- Leased temporary render sessions, normal-close/stale-session cleanup, protected changed/input files and permanent preservation of favorites/editable renders.

### Fixed
- Removed the hidden minimum camera span so very small world-unit models fit the requested canvas at zero padding.
- Kept the preview viewport stable when edge/shadow warnings change, preventing misleading jumps in pixel-perfect Fit zoom.
- Corrected the padding range to 0–0.40, retained compact numeric formatting, and exposed the build revision in the title to distinguish stale v0.3.4 copies.
- Established local main as the normal development branch and recorded the project rule without rewriting history or pushing.
### Changed
- Mutually exclusive remembered Checkerboard/Ground Preview segments; dependent controls collapse without resetting values.
- Inspect Animation Range sits beside playback; primary bottom-left actions are Save Current PNG, Save Spritesheet and Open in Aseprite.
- Generating no longer permanently saves to Documents. Native editing first offers Library preservation; advanced exports remain available.

### Fixed
- Temporary history is not persisted across sessions. Other live instances, explicit exports, source inputs and saved edits are excluded from cleanup.
- Palette-import and spritesheet-metadata helper files are cleaned without touching their sources or saved exports.

Rendering, palette mapping, camera, outlines, highlights and animation-generation algorithms are unchanged. See release verification for tested scope and conservative recovery retention.

## v0.3.3 — Grouped silhouettes and stable shadow fit

### Added
- Unified/Conservative grouped-outline choice and explicit Object Only/Object + Ground Shadow fit bounds.
- GitHub preparation: original README, this changelog, targeted ignore rules, a starter palette example and selected real application/sprite images.
- MIT license for original source, third-party notices, portable Windows x64 ZIP packaging and release notes.

### Changed
- Shorter introductory README; usage and release instructions moved to focused guides.
- Source and portable launchers share one launch path; packaging includes runtime notices and required bridge resources.
- Published assemblies omit debug symbols and their embedded local build paths.

### Fixed
- Unified mode outlines the combined group exterior rather than giving enclosed members heavy individual borders. Transparent gaps, shading, highlights, component layers and independent animation remain intact.
- Object Only is now the default: ground-shadow on/off, softness, strength and light-direction edits no longer change model fit scale or position. Shadows may clip with a warning instead of silently shrinking the object.
- Optional historical compiled comparisons no longer require private backup/output folders for normal source builds.

Verified with real Blender/Aseprite conversion, all four isometric directions, four animated poses, shadow variants, fixed density, true isometric and source-file preservation. See the release verification report for packaged-build checks. GitHub publication remains pending owner approval.

## v0.3.2 — Comparison and interface improvements

### Added
- Persistent history of four successful renders, favorites, read-only reveal/side-by-side comparison and independent animation-frame comparison.
- Six square resolution presets alongside arbitrary manual dimensions; slider/numeric input modes with separate UI preferences.
- Optional selective outlines and geometry/light-facing palette edge highlights in editable native effect layers.
- `PF_Outline_` collection groups, nested membership, deterministic ambiguity reporting, and shared contours without joining objects.

### Fixed
- Ground-shadow softness retained stepped palette penumbras rather than collapsing sampled coverage into a flat shade.
- Clean numeric formatting and validation while retaining untouched internal rendering precision.

Shadow-inclusive fitting remained the default in this version; v0.3.3 addresses its effect on object scale.

## v0.3.1 — Editable animation FPS

### Added
- Immediate playback-FPS changes and duration display for cached frames, separately from Blender frame sampling.
- Reopening generated assets and timing-only native/metadata updates with cumulative integer-millisecond rounding.

### Changed
- Timing applies to the latest saved Aseprite document with backups, preserving artwork bytes, layers, tags and manual edits; unsaved external edits receive a warning.

## v0.3 — Animation and exports

### Added
- Inclusive Blender frame ranges, frame stepping, shared motion framing, animation playback/scrubbing and editable native frames/tags.
- Fixed-grid spritesheet PNG/JSON, ordered PNG-frame exports and per-frame timing metadata.
- Optional separate binary-alpha ground-shadow layers and explicit reload of saved Aseprite edits.

## v0.2 — Rendering and workflow improvements

### Added
- Configurable studio light, ambient/fill and surface-shadow controls; optional outer/component outlines at one/two pixels.
- Palette swatch PNG import and editing, expanded palette-input formats, and saved appearance controls.
- Fixed-density scale checks, clearer camera/framing behavior, export destination handling and actionable diagnostics.

## v0.1 — Initial development

### Added
- Native Windows WPF Blender-to-pixel conversion with evaluated geometry, visible object masks, stepped palette mapping and binary-alpha validation.
- Orthographic/isometric views, custom canvas sizes, starter/custom palettes and nearest-neighbor preview.
- Non-destructive output folders, PNG and layered Aseprite generation with native-composite reopening checks.
- Blender Python and Aseprite Lua/CLI integration, build/launch scripts and base/integration checks.
