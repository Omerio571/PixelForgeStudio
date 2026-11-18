# PFA asset export and viewer

Open **Tools → Manage Export Profiles**. Set an Assets root, category, enabled layers and direction preference. Expand **Palettes and encoding** to choose GPL palettes and compression. **Save Changes to Profile** persists the selected profile in PixelForge's existing application preferences. Choose an existing profile to switch defaults. Changes made for one export stay in the dialog until explicitly saved.

In everyday use, generate or reopen a render, then choose **Tools → Export Asset (.pfa)**. The active profile fills in the export settings. Edit the asset name if needed and click **Export**. Each direction gets its own folder and one independent file per enabled layer. Existing assets get a new version folder. PixelForge verifies staged files before moving the completed asset into place.

Leave the external PNG overrides blank to use the current render's frames automatically. Explicit overrides can be persisted in a profile. For animation, supply a PNG atlas with the render's existing frame grid. Overrides apply to one direction and must match the frame dimensions and, for technical layers, the base alpha. PixelForge cannot infer whether new opaque pixels describe the same geometry solely from one PNG.

GPL palettes can sit directly in Assets or in Assets/Palettes. Base GPL index 0 is a placeholder for transparent black and indices 1–31 are opaque. Technical layers preserve all channels and alpha. Normal palettes are considered only for an exact match. An opaque black emission sample remains opaque and has intensity zero. An incompatible palette falls back to lossless data storage. Palette conversion is a separate task in Indexed Image Lab.

Use **Tools → Open .pfa** for one independent file or **Open PFA folder** for one direction or an entire asset. Select the layer in the viewer, scrub or play its frame sequence, inspect a pixel, and export decoded PNGs. A moved Assets tree works with the same marker and palette locations. If necessary, **Locate Assets root** explicitly chooses a different folder. The selected palette must still match the encoded ordered color fingerprint.

## PFA container v1

Integers are little-endian. The 48-byte prefix contains:

| Offset | Bytes | Meaning |
|---:|---:|---|
| 0 | 4 | ASCII PFA1 |
| 4 | 1 | Container version, 1 |
| 5 | 1 | Storage ID |
| 6 | 1 | Codec ID |
| 7 | 1 | Codec version, 1 |
| 8 | 4 | Positive int32 metadata byte count, at most 65,536 |
| 12 | 4 | Positive int32 encoded payload length |
| 16 | 32 | SHA-256 of the exact metadata bytes followed by decoded BGRA bytes |
| 48 | variable | UTF-8 JSON, then exactly the encoded payload length |

Metadata JSON properties are Asset, Layer (0 Base, 1 Normal, 2 Emission), Direction, Width, Height, Durations (array of positive millisecond durations), RootId, Palette, PaletteHash, and Placement. Placement contains optional pivot, world scale, camera conventions and animation tags. Width and height are 1–4096, with 1–256 frames and at most 16,777,216 pixels in total. Duration is 1–600,000 ms. Frame boundaries follow from dimensions. Frames are ordered, each starting at the top-left and stored in row-major order.

Storage IDs:

- **0 BGRA:** byte order blue, green, red, alpha.
- **1 GrayAlpha:** one grayscale byte, then alpha, per pixel. Expand gray into all RGB channels.
- **2 Index5:** tightly packed 5-bit indices, least-significant bit first with zero-padded final high bits. No row or frame padding. Index zero is transparent black.
- **3 IndexAlpha:** one GPL row index (0–255) and one alpha byte per pixel. GPL row zero is a regular RGB color.

Codec IDs are **0 raw** and **1 standard Brotli**. Both use codec version 1. Reject unsupported versions. For Brotli, require all encoded bytes to be consumed and an exact decoded storage length before expanding channels. Then validate the metadata-and-pixels digest. Auto compares raw and Brotli candidates by full serialized size, including palette reference overhead.

An indexed file stores the slash-separated relative palette path and uppercase hexadecimal SHA-256 of concatenated RGB triplets in GPL row order. It stores no palette colors. Match the RootId by searching ancestor directories for .pixelforge-assets.json, a small JSON object with an Id GUID. Non-indexed files decode without the marker or a palette. Reject malformed paths and out-of-range indices. Normal map RGB is world-space data with +X East, +Y North and +Z Up.

No manifest is required. New codecs can be introduced independently of the profile and viewer workflow. Local remapping, inter-frame compression and collection comparison against JPEG XL remain follow-up experiments.

## Initial sample

The original Mine and PowerStrip exports contain six PFA files totaling 16,631 bytes. With their referenced Base32 GPL (374 bytes) and root marker (45 bytes), the collection is 17,050 bytes. The same render outputs stored as PNG atlases with timing and placement JSON are 29,369 bytes. This two-asset measurement uses an illustrative GPL created for the acceptance test. The requested Assets.zip was not present. JPEG XL was not measured, and these results do not establish a general advantage over it.
