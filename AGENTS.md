# PixelForge Studio

- Keep desktop UI, deterministic conversion, and external-tool integration separate.
- Never save over a source Blender file or previous generated/edited output.
- No asset-specific drawing logic. Geometry and source colors define the asset.
- Validate exact dimensions, palette membership, binary alpha and native layer compositing.
- Test real Blender/Aseprite integration separately from unit checks; inspect actual pixels.
- Technical validity is not artistic approval. Report unsupported model features clearly.
- Keep dependencies and configuration minimal. Build with `./Build.ps1 -Check`.
- Comparison is read-only: retain file references, never regenerate/export artwork or delete outputs when history rotates. Keep numeric input modes in UI preferences, outside asset settings.

- Use the main branch for normal development and commits. Do not create additional branches unless the user explicitly requests one. Never automatically push, force-push, or rewrite repository history.
