# 360° skybox importer

Open **Tools > KINO Rotunda > 360 Skybox** in the KinoRotunda scene. Drop a local panorama onto the window, or use **Choose file and apply**. Default settings import the file, apply the sky, bake lighting and update active reflection probes.

- Accepts mono, equirectangular 360 × 180° JPG, PNG, HDR and EXR files with a 2:1 aspect ratio.
- JPG/PNG is decoded from sRGB and saved as a linear, half-float EXR. This changes the storage format, not the captured dynamic range; clipped highlights cannot be recovered.
- HDR/EXR preserves HDR radiance. Texture import caps the desktop texture at 8K and the Quest texture at 4K by default; these limits can be reduced in the window. Quest uses native RGB9E5 HDR storage (approximately 43 MiB at 4K including mipmaps, or 11 MiB at 2K), avoiding an RGBM-packed LDR texture.
- The current Directional Light keeps its rotation, colour and intensity. A sun detector analyses the upper hemisphere, including the horizontal seam, and adjusts only the panorama's yaw. The level horizon is preserved. A different source sun elevation is reported rather than tilting the city.
- Uniform skies, broad overcast lighting and ambiguous highlights do not get a fabricated sun direction. A preview uses the previous sky rotation, and baking waits for a sun selection or disabled alignment. Click the photographed sun in the preview and reapply to align manually. Optional extra yaw/exposure controls remain available.
- Each application writes its own source copy, EXR (for LDR), sky material, metadata and reflection maps below `Assets/KinoRotunda/Textures/Panoramas`. Original source files and previous sky materials/maps are retained.
- Scene backups and the latest report/status are under `Artifacts/KinoRotunda/Panorama`. Applying saves the active scene. The sky assignment supports Undo. A scene backup restores scene settings, but Unity can overwrite shared lightmap assets during a bake; rebake after restoring an earlier sky.
- Inactive probes are left alone. Active Custom probes receive new captured maps after all captures succeed; their other settings are preserved. Realtime probes keep their existing mode/update policy. No prefab rebuild is performed.

Keep only the KinoRotunda scene loaded, exit Play Mode, and let existing lighting bakes finish before applying. Turning off lighting/reflection options gives a quick background-only preview; the existing baked illumination then remains from the previous sky.

## Selected Athens panorama

[Sunset over Syntagma Square drone aerial view, Athens, Greece — FOTO360](https://www.360cities.net/image/sunset-over-syntagma-square-drone-aerial-view-athens-greece), advertised resolution 17,966 × 8,983.

The public page is a viewer. **Hosted Embed does not include a file download.** Obtain the full equirectangular JPG with a licence covering inclusion in the VR game/app. Once available, drop the downloaded image into the window and record the source/licence URL. The project does not include this photograph or an extracted viewer copy.

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
