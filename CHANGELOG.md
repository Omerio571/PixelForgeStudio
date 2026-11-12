# Changelog

Meaningful development changes, newest first. Historical entries follow saved versioned source/documentation and verification records; no historical release dates are assumed.

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
