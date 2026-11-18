# Indexed Image Lab

Open **Tools → Indexed Image Lab** in PixelForge. Select an Assets root, import one or more images, and select a shared palette in the root's **Palettes** folder. Create a palette from HEX, JSON, GPL, PAL, or the current working palette. A 32-color source opens a review window where you can choose which opaque color to replace with transparency. Recommendations use the first selected image's color usage and perceptual similarity. Without an image they are provisional. Neither operation edits the source palette or image.

Run comparison to inspect the first image and see measurements for the entire selection. Save PFI writes the first converted image using the chosen encoding. Auto uses the smallest available payload and breaks ties in Packed, Runs, Brotli order. Open PFI resolves its palette against the selected root and verifies its checksums. Export reconstructed PNG writes the decoded image. Rebind writes a **new** file using the currently selected shared palette and the original indices. Its colors can change.

Input alpha below the selected threshold is transparent black. Other pixels become fully opaque and map to the nearest palette entry using the existing perceptual distance metric, without dithering. Discarded RGB at alpha zero counts as a changed RGBA pixel. Lossless storage means exact reconstruction of the converted reference.

## Files, version 1

All integers are unsigned little-endian except the PFI payload length, which is a positive signed int32. Hashes are raw SHA-256 bytes. No native struct padding is used.

### PFPAL

| Byte offset | Field |
|---:|---|
| 0 | Four ASCII bytes PFP1 |
| 4 | Version byte 1 |
| 5 | Transparent index byte 0 |
| 6 | 16 opaque identifier bytes. Newly created palettes use the first 16 bytes of SHA-256 over the ordered RGB triplets. |
| 22 | 31 distinct RGB triplets, one byte per channel, in index order 1–31 |

The file is exactly 115 bytes. Transparent index 0 has no RGB triplet. HEX export lists only opaque entries, in index order.

### PFI

| Byte offset | Field |
|---:|---|
| 0 | Four ASCII bytes PFI1 |
| 4 | Version byte 1 |
| 5 | Encoding byte: 0 Packed, 1 Runs, 2 Brotli |
| 6 | Width, uint16 |
| 8 | Height, uint16 |
| 10 | UTF-8 reference length, uint16, from 1 to 1024 |
| 12 | Payload length, int32 |
| 16 | Palette identifier bytes, copied from PFPAL |
| 32 | SHA-256 of the entire 115-byte PFPAL file |
| 64 | SHA-256 of the canonical packed pixel data |
| 96 | Exact UTF-8 palette path bytes, then payload bytes |

Both dimensions are 1–4096. The palette path uses slash separators and begins with **Palettes/** under the chosen Assets root. Absolute paths, parent traversal and links beneath the selected root are unsupported. PFI stores no RGB palette entries. A change in palette identity, order or membership requires the original palette or explicit rebinding.

Pixels start at top left, left to right across each row, then top to bottom. Packed indices place their least significant bit first into successive low-to-high bits of each byte. There is no padding between rows. Unused high bits in the last byte are zero. Example indices 1, 2, 31 become hex bytes 41 7C. Runs are two-byte pairs: run length minus one (0–255), then index (0–31). Runs can span rows. Brotli is a single standard Brotli stream containing the packed data. Decoders must validate exact decompressed lengths, palette identity and both hashes. Version 1 stores one static image. A future animation container could store a table of frame payloads and durations against one palette reference.

## Fair comparisons

RGBA PNG, 8-bit indexed PNG, all three PFI methods and JPEG XL use the same converted raster. The lab measures full on-disk file sizes and checks decoded RGBA exactly. Collection totals count the 115-byte shared PFPAL once. PFI payload and header bytes are reported separately. Encoding and decoding timings include file I/O. JPEG XL timings also include external process startup.

Configure official **cjxl** and **djxl**, or put both on PATH. The lab checks tool versions, encodes with distance 0 and effort 7, decodes to PNG, and verifies the resulting pixels. Missing executables leave that benchmark unavailable. Technical lighting maps should use their native formats.

## Initial measured batch

Eight images used a single illustrative 31-color palette. The set included two Mine10 renders, a power-strip sprite, and five fixtures covering transparency, flat fill, repetition, indexed noise and a 768×512 image. All retained their original RGBA after conversion. The palette combines the repository's 24 opaque defaults with seven example colors. The user's 32-color preset was unavailable, so this is an illustrative palette and no production removal is recommended.

| Format | Bytes for all eight images |
|---|---:|
| Original and canonical RGBA PNG | 225,133 each |
| Indexed PNG | 49,015 |
| Packed PFI with palette | 329,450 |
| RLE PFI with palette | 235,315 |
| Brotli PFI with palette | 44,876 |
| Auto PFI with palette | 44,858 |
| JPEG XL | Unavailable on this machine |

All 40 measured output decodes matched the canonical RGBA pixels. Auto saved 4,157 bytes versus indexed PNG across the mixed batch (8.5%, about 520 bytes per image). Brotli won on six images, Runs won on the icon, and Packed won on noise. On the three rendered sprites alone, Auto used 3,744 bytes with the palette, versus 3,593 bytes for indexed PNG. Raw packing is a predictable baseline and Brotli is particularly good on repeated patterns. The small gain on this mixed set, and the loss on the rendered subset, do not justify adopting PFI for production on this evidence. Test the same converted assets with official JPEG XL tools before deciding.
