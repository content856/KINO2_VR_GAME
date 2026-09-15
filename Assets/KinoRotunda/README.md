# KINO Rotunda

Reference-inspired 3D hall: a 28 m diameter arcade, 25 open arches, a stepped circular ceiling with an open oculus, concentric marble flooring, brass inlays, a KINO display and a small armillary ornament.

Open `Assets/KinoRotunda/Scenes/KinoRotunda.unity`. The scene is also first in the shared build scene list. `SampleScene` and the existing XR loaders remain available.

## Source and regeneration

- Blender source: `SourceArt/KinoRotunda/KinoRotunda.blend` (outside Assets to avoid Unity's automatic Blender importer).
- Unity model: `Assets/KinoRotunda/Models/KinoRotunda.fbx`.
- Geometry generator: `Tools/KinoRotunda/build_rotunda.py`, run with Blender 4.1 in background mode.
- Texture generator: `Tools/KinoRotunda/create_textures.py`, requires Python, NumPy and Pillow.
- Unity builder: `Tools > KINO Rotunda > 1 - Build environment`, followed by `2 - Bake lighting`.

The build command recreates the generated KINO scene and prefab. Keep manual variations in a duplicate scene/prefab before running it again. Other scenes are not rebuilt. Any unsaved non-KINO source scene is saved as `SourceSceneBackup.unity` before switching scenes.

## Model and materials

Blender stores geometry, UVs and six semantic surface slots only. It contains no lights, cameras, HDRI or texture-node materials. Dimensions are in metres, with the finished floor at Unity Y=0.

- UV0: consistent planar marble mapping, plus a dedicated 0–1 display UV.
- UV1: separately packed `LightmapUV` charts, retained by the Unity FBX importer.
- Column joints: the Blender repair pass seats the pilaster/round-column feet, capitals, balusters and display pilasters. Floor, ceiling, terrace slabs, topology, UV charts and Unity material mappings are preserved. The geometry generator includes this pass; `Artifacts/KinoRotunda/ColumnJoints` contains the original model backup, before/after close-ups and source/FBX contact validation.
- Unity materials: NeroMarble, IvoryMarble, BrushedGold, BronzeShadow, WarmLED and Screen.
- Arcade depth: the ivory arches and upper walls are recessed 0.80 m outward behind the black pilasters. Columns and capitals are 50% wider, centred beneath the arch spring footprint (including a 0.075 m tangential / 0.13 m outward alignment correction). The balustrade moves back 0.65 m, the exterior lip extends to radius 15.25 m, and a rear soffit closes the upper connection. `Tools/KinoRotunda/recess_arcade.py` applies these changes during regeneration. Backups and geometry previews are in `Artifacts/KinoRotunda/ArcadeDepth`.
- Marble maps are original deterministic seamless textures. The original KINO display graphic remains on the architectural screen. The scene's `KINO Gameplay` prefab overlays a live number grid and timer; see `Assets/KINOVR/README.md` for the numbered-ball prototype.

`Tools > KINO Rotunda > 9 - Blue marble polish` gives `NeroMarble` a deep blue tint and a subtle cool specular sheen using the standard URP Lit shader. It preserves the existing textures and smoothness, screen, lights, HDRI and reflection settings. The material backup and before/after previews are saved under `Artifacts/KinoRotunda/BlueMarble`; no lighting bake is triggered.

Select `NeroMarble.mat` (or use `Tools > KINO Rotunda > 10 - Edit marble colours`) to adjust **Marble Color** and **Sheen Color** at the top of its Inspector. Changes update the scene immediately, save to the material and support Undo. The HDR marble picker preserves the tint's existing brightness. These controls are only added to this material; the standard URP shader and Inspector remain available below them. Menu 9 reapplies the preset, so use these colour pickers to retain your own adjustments.

## Lighting and runtime use

All 292 rounded gold ornaments are individual mesh objects under `Animation_Balls`, grouped by arcade bay, stage, stage return and armillary. Each `Ball_*__BrushedGold` object has a centred pivot, its original geometry/material/UVs, and an independent transform. The FBX importer and environment builder keep these objects non-static with probe lighting so they can be animated later. The last generator pass, `Tools/KinoRotunda/separate_balls.py`, recreates this hierarchy.

In the current playable scene, the 14 armillary/lottery ball renderers and 63 tube ball renderers are hidden and replaced by the separate `KINO air balls` prefab instance. These use the gameplay oval mesh, lacquer and numbered text, with idle tube airflow and slow/fast lottery mixing. The other architectural gold ornaments remain visible. The Blender/FBX source validation still checks the original imported balls; use **Tools > KINO VR > Air balls** to set up and validate the visible replacements after regenerating the environment. See `Assets/KINOVR/README.md` for round-state integration and controls.

The scene preserves the exact inherited skybox material. Warm sconces, cove illumination, downlights and a broad ceiling fill are baked in Unity; reflection and light probes are saved with the scene. The progressive GPU lightmapper is configured for subsequent bakes.

KINO-specific URP pipeline copies enable HDR and four-sample MSAA. The atmosphere profile supplies restrained bloom and ACES tone mapping. The original pipeline assets remain available, while quality levels point to the KINO copies.

The architecture is static and has simplified floor, stage and perimeter collision. The saved environment camera remains a composition preview. The separate `KINO Gameplay` prefab supplies the VR rig, hand catchers and a desktop gameplay camera. Headset performance and player interactions require validation in the intended runtime. Scene lightmaps are scene-specific: the environment prefab can be reused elsewhere, but lighting should be rebaked in the destination scene. Rebuilding the environment recreates the scene, so run `Tools > KINO VR > 1 - Set up numbered timed round` afterwards to add gameplay again.

The column repair retains the existing baked lighting. Rebake lighting when updating the scene's illumination to account for the moved column surfaces.

### Interior illumination pass

`Tools > KINO Rotunda > 7 - Brighten interior (preserve reflection setup)` brightens the existing scene without running the environment or reference-appearance rebuild. It retains the current HDRI and every reflection probe setting, including disabled probes and cubemap assignments. The separate `CeilingLED` material strengthens only the circular ceiling strips. A named lighting group adds 52 tangent area lights around three ceiling rings, eight broad arcade fills, and a subtle cool ceiling bounce. All 61 added lights are baked; no additional realtime lights or geometry are introduced. Repeating the command updates this group without duplicating lights.

The command saves a scene backup and before/after probe reports in `Artifacts/KinoRotunda/InteriorLighting`. This pass does not regenerate the custom reflection cubemaps: their contents remain available for the user's own reflection work. The scene contains the added lighting; a new full environment rebuild or reuse of the older environment prefab requires running the interior pass again.

Unity previews now render through a floating-point HDR target before conversion to PNG, so bright LED emission reaches bloom before it is tone-mapped.

`Tools > KINO Rotunda > 8 - Brighten display wall and draw ornament` adds two baked wall softboxes and a baked ornament fill. The existing display spotlight becomes Mixed so the metallic ornament receives a direct specular highlight; this is one realtime direct spotlight alongside the baked lighting. Screen graphics, ceiling lighting, HDRI and reflection settings are preserved. The scene backup, room previews, close-ups and probe comparisons are saved under `Artifacts/KinoRotunda/StageLighting`.

## Verification output

### Hall dust

`Hall Dust - soft motes near the arches` is a native looping Particle System. Sparse, warm, translucent grains drift near the arcade at an emission radius of 9.5–11.25 m and heights of 1.5–5.25 m. The central player area and the approach to the display are clear of emitters. Prewarming populates the room on startup; each 30–38 second lifetime fades in and out, and low-frequency noise gently changes direction.

One invisible mesh supplies emission points throughout that volume, and one billboard renderer shares the `HallDust` material. Emission is 3.2 particles/second with a hard cap of 128 particles (256 triangles). The one-pass URP shader computes the soft grain from UVs without a texture and depth-tests against the room. It fades near the camera and the hall boundaries. There are no particle lights, shadows, collisions, trails, depth texture copies or additional runtime scripts. The material supplies a restrained warm tint rather than sampling the scene lights. Headset GPU cost still needs measurement on Quest.

Use the Particle System Inspector's **Emission**, **Start Size**, **Start Color** and **Noise** modules to tune the effect. `Tools > KINO Rotunda > Dust` sets up the preset, validates it and captures previews; setup reapplies the preset and saves a scene backup. After rebuilding the whole environment, run **Set up subtle hall dust** again. Scene comparisons, a motion clip when encoded, and validation reports are in `Artifacts/KinoRotunda/HallDust`.

### Exterior birds

The scene's `Exterior Birds` object animates an occasional flock of 16 distant birds from the user-supplied `Textures/Birds/Birds.png`. Its **8 columns x 2 rows** play in reading order, for 16 frames at a nominal 32 FPS. **Stabilize Animation** defaults on: each full cell is registered to a fixed body/tail attachment point, removing the large vertical jump between sheet rows and at the loop boundary. Registration moves the quad relative to the body and mirrors its offset on right-side passes. The original PNG, wing poses, cell extent and bird scale stay intact; there is no added bob. The small registration table is calibrated to this source sheet, so a replacement sheet needs new landmarks. Validation independently measures the source alpha contour and checks the rendered landmark across all 16 frames in both directions.

`KinoExteriorBirds` runs one compact flock at a time. It starts behind the player, randomly chooses the left or right side, and travels towards the display wall, where the architecture hides it before it despawns. Left passes travel left-to-right; right passes mirror that route. The main flock is broad and irregular at the front, tapering to **two staggered followers at the rear**. Positions are sampled once per pass with separation between neighbours, and each bird follows the shared curved route at its own distance along the arc. A 2–5 second initial wait and a random 16–30 second bird-free interval between passes prevent continuous circulation. The runtime uses a local random generator, independent of gameplay randomness. Members retain their formation and shared angular travel speed, with different flap phases/rates and slight size variations. The default radius is about 50 m and bird width is 0.8 m. The Inspector exposes these settings plus flock length and wait ranges. The centre-eye anchor supplies one common billboard orientation for both VR eyes.

All birds share one dynamic mesh (64 vertices / 32 triangles at the default count), one unlit material and one shader pass. The renderer switches off during the quiet intervals. The shader samples only the PNG's alpha, so its transparent white RGB cannot create pale edges. It depth-tests against the architecture. No bird lights, shadows, colliders, reflection-probe updates or per-bird GameObjects are added. Android imports use ASTC 6x6, a 1024 maximum dimension, mipmaps and no retained CPU texture copy. Headset GPU timing still needs measurement on the target Quest.

`Tools > KINO Rotunda > Birds` adds/repairs the setup, validates flight, body stability, rear followers and sheet sampling, and captures scene previews plus isolated source/registered animation frames. After regenerating the entire environment, run **Add exterior birds** again. Preview PNGs, motion clips when encoded, source-scene backups and validation reports live in `Artifacts/KinoRotunda/ExteriorBirds`.

`Artifacts/KinoRotunda` contains the Blender and Unity validation reports, skybox identity comparison, render-device information, build logs and the rendered Unity preview. Use `Tools > KINO Rotunda > 5 - Validate environment` to check model scale, UV channels and material assignments after changing the model.
