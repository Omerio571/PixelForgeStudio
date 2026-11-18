# PFA asset export and viewer

Open **Tools → Manage Export Profiles**. Set an Assets root, category, enabled layers and direction preference. Expand **Palettes and encoding** to choose GPL palettes and compression. **Save Changes to Profile** persists the selected profile in PixelForge's existing application preferences. Choose an existing profile to switch defaults. Changes made for one export stay in the dialog until explicitly saved.

In everyday use, generate or reopen a render, then choose **Tools → Export Asset / Export All**. The active profile fills in the export settings. Edit the asset name if needed and click **Export**. Each direction gets its own folder and one independent file per enabled layer and checked export format. Existing assets get a new version folder. PixelForge verifies staged files before moving the completed asset into place.

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
- **4 Index8:** one byte per index, values 0–31. Transparency and GPL lookup are identical to Index5.
- **5 PlanarBgra:** all blue bytes, then green, red, and alpha planes. Each plane contains all frame pixels in their normal order.
- **6 PlanarGrayAlpha:** one intensity plane followed by one alpha plane, both in frame order.

Codec IDs are **0 raw**, **1 standard Brotli**, and **2 frame XOR followed by standard Brotli**. All use codec version 1. Codec 2 requires at least two frames and frame-aligned storage IDs 0, 1, 3, or 4. Frame zero is literal. Each subsequent stored byte is XOR with the corresponding previous frame byte before compression. After bounded Brotli decoding, reverse the XOR from the second frame forward using the already restored previous frame. Existing IDs retain their original meanings. Reject unsupported versions. For Brotli, require all encoded bytes to be consumed and an exact decoded storage length before expanding channels. Then validate the metadata-and-pixels digest. Auto compares all eligible raw, Brotli and frame-XOR Brotli candidates by full serialized size, including palette references. The Brotli-only setting considers both Brotli codecs.

An indexed file stores the slash-separated relative palette path and uppercase hexadecimal SHA-256 of concatenated RGB triplets in GPL row order. It stores no palette colors. Match the RootId by searching ancestor directories for .pixelforge-assets.json, a small JSON object with an Id GUID. Non-indexed files decode without the marker or a palette. Reject malformed paths and out-of-range indices. Normal map RGB is world-space data with +X East, +Y North and +Z Up.

No manifest is required. New codecs can be introduced independently of the profile and viewer workflow. JPEG XL is optional for comparison. Local remapping and additional codecs remain separate experiments.

## PNG and WebP Export All

New export profiles default to **WebP Lossless (No Metadata)**. Existing saved profiles keep their selected formats. PNG and PFA are still optional. WebP writes one flat asset folder with names such as MineNorthBase.webp or MineSEBase.webp. The three image layers and different directions remain independent. WebP-only export does not use GPLs, create a PFA root marker, or write another image format.

Static layers produce one image each. Animated layers use the original untrimmed frame grid. **Include Game Metadata** is off by default. Turning it on writes one asset-level JSON with the direction layouts, frame durations, anchors, and camera values. Without it, supply frame rectangles and timing in the game. PixelForge retains those data internally. PNG-only and PFA export keep their existing metadata conventions.

WebP uses the official [cwebp and dwebp utilities](https://developers.google.com/speed/webp/docs/precompiled). Configure executable paths under **Palettes and encoding**, or add them to PATH. They are not bundled. Effort defaults to 9 and can be saved with the profile. The exporter requires VP8L, rejects unexpected RIFF chunks, and decodes every atlas with dwebp to compare all four channels, including RGB behind alpha zero. It never quantizes WebP pixels to GPL colors. External base PNGs with embedded profiles or non-sRGB color hints must be converted to sRGB first. Technical layers retain their exact data channels. See the [cwebp option reference](https://developers.google.com/speed/webp/docs/cwebp).

Enable **Include detailed compression comparison** to view exact results in PixelForge. The exporter shows file sizes and total bytes without writing a report into the asset. Comparison intermediates are staged and removed. Each shared GPL and root marker used by a PFA comparison is counted once. Atlas benchmarks also count the JSON needed to describe timing, separately from the bytes written by image-only export.

## Initial sample

The original Mine and PowerStrip exports contain six PFA files totaling 16,631 bytes. With their referenced Base32 GPL (374 bytes) and root marker (45 bytes), the collection is 17,050 bytes. The same render outputs stored as PNG atlases with timing and placement JSON are 29,369 bytes. This two-asset measurement uses an illustrative GPL created for the acceptance test. The requested Assets.zip was not present. JPEG XL was not measured, and these results do not establish a general advantage over it.

## Optimization measurements

The 12-frame, 128×128 Mine10 export was rendered again using the local test settings. Exact baseline sizes were reproduced for the existing four-frame Mine artifacts. The full 12-frame run did not reproduce the reported 4,191 versus 2,987 bytes for plain Brotli. Here plain Index5 was 4,667 bytes and plain Index8 was 4,959. Index8 with frame XOR dropped to 4,093 payload bytes and won on total base-layer size. On the four-frame Mine, plain Index8 beat plain Index5 (2,554 versus 2,922 bytes), which is why Auto tests both.

| Exact collection, including required shared files | Previous PFA | Optimized PFA | RGBA PNG | WebP |
|---|---:|---:|---:|---:|
| Mine10, 12 frames | 32,670 | 26,365 | 56,154 | 18,925 |
| PowerStrip, 2 frames | 3,128 | 2,707 | 2,725 | 1,724 |
| Four generated directions, 12 independent layers | 18,793 | 17,690 | 21,398 | 10,780 |

For the full Mine10, normal was the largest previous layer (22,718 bytes before shared overhead) and improved to 19,773. Emission fell from 4,158 to 1,372 bytes. Planar technical storage helped the PowerStrip and some other samples. Sparse trials and Deflate remain diagnostic only, and JPEG XL was unmeasured because cjxl and djxl were unavailable. The exact WebP samples used libwebp 1.6.0. Timing in the reports is local wall-clock time and includes file I/O; native WebP timings also include external process startup.
