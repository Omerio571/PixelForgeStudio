# Dynamic lighting asset format v2

Enable **Dynamic Lighting**, generate, and use **Base color**, **Normal map**, **Emission mask** and **Emission preview** above the playback timeline. **Compare maps** opens the current frame side by side. The comparison slider and per-side map selectors also work with other saved renders, including a baked render. **Save lighting asset** writes a new folder with the matching PNGs, a lighting manifest, and the editable Aseprite file.

## Capture and conversion

The Blender worker evaluates each animated pose and makes disposable mesh copies. Dynamic mode uses explicit material Base Color through an emission pass mixed with a restrained, non-directional ambient occlusion term, then applies the existing ID selection, palette quantization, pixel cleanup and outlines. It keeps grouped inward outlines and uses constant outline shading. Directional highlights are omitted. Studio preview light uses the normals and stays in the viewport. Ground shadows default off. When explicitly enabled, they are baked and carry +Z normals and zero emission on shadow-only pixels. The application warns about overlap with runtime shadows.

The existing orthographic surface rays run at the same subpixel centers as the color and ID passes. For each pixel and ray-hit object the nearest facet is retained. The final color converter's winning object ID selects the normal. If the ray grid and render coverage disagree at an edge, the converter can borrow a nearby sample within two pixels from the same object. If none exists, generation reports an error. RGB is never averaged across components. An inward outline inherits its visible facet normal, and the normal alpha matches the final color alpha byte for byte.

## Coordinate system and sampling

World axes are **+X East, +Y North, +Z Up**. PNG origin is top-left, with image Y downward. Image Y and world Y are unrelated.

For evaluated object matrix **M** and temporary presentation yaw **R**, the worker transforms a polygon normal by inverse-transpose of the linear part of **R M**, and normalizes it. Dynamic maps keep the mesh normal orientation, including back faces of open geometry. It then removes the studio yaw with **Rᵀ** and normalizes again. The exported normal includes the evaluated object's physical transform and nonuniform scaling. A new camera direction changes what is visible without changing the physical axes of the asset. **cameraToWorld** is a row-major 3×3 mapping of Blender camera-local right, up and backward axes into that canonical world basis. **presentationYawDegrees** describes the removed studio rotation.

The PNG stores **RGB = round((N + 1) × 127.5)** and uses zero RGBA for transparent pixels. Alpha is binary. Read normals as linear bytes using nearest-neighbor sampling. Decode with **normalize(rgb × 2 − 1)**. There is no green-channel flip. Light direction must be expressed in the same world space and point toward the light. Use a real world-space rotation for physical yaw. Screen-space flips and rotations are unsupported. Export directional variants when required. Render textures may need a vertical texture-coordinate flip when displayed or read back. That is separate from the PNG normal convention.

## Material emission

Use an unlinked Principled BSDF Emission Color and Emission Strength, or a directly connected Emission shader. Blender strength maps through `E/(1+E)`, where E is nonnegative strength times the largest linear emission color channel. For a precise mask, add a numeric custom **material** property `pixelforge_emission` between 0 and 1. It takes priority and 1 exports as 255. Use the override for linked textures, mixed shaders and node groups. Bright Base Color alone never emits. The inactive sprite uses Base Color, and the preview/runtime chooses the emitted color.

The emission mask stores equal RGB channels and color-matched binary alpha. The winning object's nearest facet supplies the material value. An ambiguous edge that needs a borrowed normal gets no emission, and painted outline/highlight pixels stay zero. A mask describes the sprite's self-emission and does not create a light in the environment. A future optional region-ID texture could provide independently driven regions while preserving the v2 grayscale mask.

## Atlas and edits

The version 2 **lighting** block identifies all three PNGs, axes, encoding, sampling and view transform. The surrounding manifest includes canvas dimensions, fixed-grid frame rectangles, durations, tags, world scale and pivot. Static assets use a one-frame atlas. Animation timing updates the shared manifest and native durations while reusing the existing maps.

Reload saved Aseprite edits before exporting edited artwork. Pixel or frame changes invalidate the maps conservatively, since even color-only changes can represent reordered poses with the same silhouette. The original native edit is preserved. Timing-only edits keep the maps if the composites and frame order match.

## Standalone raylib validator

Build [main.cpp](../tests/lighting-validator/main.cpp) against raylib 6.2 using C++20. With MSVC, link raylib.lib and opengl32.lib, gdi32.lib, winmm.lib, user32.lib and shell32.lib. Match the installed raylib library's runtime configuration.

Run the program with a **.lighting.json** path to see the base, normal and lit animation at integer zoom. Left and Right cycle East, West, North, South and Above. Space toggles glow, C changes tint, and E switches the data view to the emission mask. v1 bundles still open. Pass an optional output directory to test every frame against a CPU reference for all five light directions and save PNGs from the first frame. The validator demonstrates directional diffuse lighting. It does not model light distance or surfaces hidden behind the sprite.

For future dynamic shadows, start with explicitly authored footprints and simplified height-bearing occluders. They can model tall walls or machinery without guessing hidden geometry from a sprite. Add per-pixel world positions only when elevated local lights need them. A height map alone cannot represent overlapping surfaces in a tilted view.
