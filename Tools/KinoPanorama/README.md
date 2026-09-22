# 360° skybox importer

Open **Tools > KINO Rotunda > 360 Skybox** in the KinoRotunda scene. Drop a local panorama onto the window, or use **Choose file and apply**. Default settings import the file, apply the sky, bake lighting and update active reflection probes.

- Accepts mono, equirectangular 360 × 180° JPG, PNG, HDR and EXR files with a 2:1 aspect ratio.
- Cropped 360° strips can use `cropped360`, a picked `sunUV` in the source image and `cropBottomElevation`. The original width/height determines the vertical angular coverage without stretching. A separate stereo-compatible sky shader fills uncaptured poles with a colour gradient. Set the bottom elevation to `sceneSunElevation - sourceSunUV.y * 360 * height / width` to calibrate the photographed sun's elevation while keeping horizontal image rows level.
- JPG/PNG is decoded from sRGB and saved as a linear, half-float EXR. This changes the storage format, not the captured dynamic range; clipped highlights cannot be recovered.
- HDR/EXR preserves HDR radiance. Texture import caps the desktop texture at 8K and the Quest texture at 4K by default; these limits can be reduced in the window. Quest uses native RGB9E5 HDR storage (approximately 43 MiB at 4K including mipmaps, or 11 MiB at 2K), avoiding an RGBM-packed LDR texture.
- The current Directional Light keeps its rotation, colour and intensity. A sun detector analyses the upper hemisphere, including the horizontal seam, and adjusts only the panorama's yaw. The level horizon is preserved. A different source sun elevation is reported rather than tilting the city.
- Uniform skies, broad overcast lighting and ambiguous highlights do not get a fabricated sun direction. A preview uses the previous sky rotation, and baking waits for a sun selection or disabled alignment. Click the photographed sun in the preview and reapply to align manually. Optional extra yaw/exposure controls remain available.
- Each application writes its own source copy, EXR (for LDR), sky material, metadata and reflection maps below `Assets/KinoRotunda/Textures/Panoramas`. Original source files and previous sky materials/maps are retained.
- Scene backups and the latest report/status are under `Artifacts/KinoRotunda/Panorama`. Applying saves the active scene. The sky assignment supports Undo. A scene backup restores scene settings, but Unity can overwrite shared lightmap assets during a bake; rebake after restoring an earlier sky.
- Inactive probes are left alone. Active Custom probes receive new captured maps after all captures succeed; their other settings are preserved. Realtime probes keep their existing mode/update policy. No prefab rebuild is performed.

Keep only the KinoRotunda scene loaded, exit Play Mode, and let existing lighting bakes finish before applying. Turning off lighting/reflection options gives a quick background-only preview; the existing baked illumination then remains from the previous sky.

For the Athens cropped LDR sky, **Tools > KINO Rotunda > Use full-resolution LDR sky (8K)** restores the source photograph's full detail, including in Android editor mode. It samples the original sRGB PNG/JPG directly, using 8K ASTC 4x4 on Android and BC7 on desktop. This avoids enlarging a downsampled 4K EXR, keeps mipmaps, and preserves the current material rotation/exposure and scene sun. At the Athens aspect ratio, 8K ASTC 4x4 costs approximately 22 MiB including mipmaps on-device, similar to the former 4K RGB9E5 texture. Editor fallback memory can differ. Comparisons and original material/import settings are saved under `Artifacts/KinoRotunda/Panorama/Quality-*`. Reapplying through the standard importer creates a new EXR sky; run this detail option again for the new LDR sky. The automation command is `{"command":"quality"}` in `Temp/KinoPanorama.request`.

## Selected Athens panorama

[Sunset over Syntagma Square drone aerial view, Athens, Greece — FOTO360](https://www.360cities.net/image/sunset-over-syntagma-square-drone-aerial-view-athens-greece), advertised resolution 17,966 × 8,983.

The current scene uses the user-supplied `Artifacts/athens_panorama_20260921/athens_panorama.png` strip (8192 × 2131). Its photographed sun is picked at approximately pixel (4555, 1300), measured from the top left. The strip covers 93.64746° vertically; its bottom is calibrated to -30.45856° for the existing 6.06° scene sun. The scene light itself remains unchanged. Uncaptured poles are a colour gradient, not additional photographed coverage. `athens-apply.json` records the reusable import settings.

## Validation and automation

**Tools > KINO Rotunda > Validate 360 importer** checks sun mapping/detection, LDR gamma conversion, HDR range, texture settings, invalid aspect handling and a native URP render pointing toward the unchanged scene sun. Validation restores the original sky and removes only its newly created test imports. It does not rebake lighting or save changes to the scene.

The open Unity editor also accepts an explicit JSON request in `Temp/KinoPanorama.request`:

```json
{"command":"open"}
```

```json
{"command":"test"}
```

```json
{
  "command": "apply",
  "sourcePath": "C:/path/to/licensed-panorama.jpg",
  "options": {
    "alignSun": true,
    "manualSun": false,
    "rotationOffset": 0,
    "exposure": 1,
    "desktopSize": 8192,
    "questSize": 4096,
    "bakeLighting": true,
    "updateReflections": true,
    "sourceUrl": "https://www.360cities.net/image/sunset-over-syntagma-square-drone-aerial-view-athens-greece"
  }
}
```

Importing these scripts alone never changes the sky or starts a bake. There is no network downloader or recurring watcher that imports arbitrary files automatically.
