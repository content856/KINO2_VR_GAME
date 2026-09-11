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
- Marble maps are original deterministic seamless textures. The KINO display graphic is a static decorative reference; it does not implement a live game or number draw.

## Lighting and runtime use

The scene preserves the exact inherited skybox material. Warm sconces, cove illumination, downlights and a broad ceiling fill are baked in Unity; reflection and light probes are saved with the scene. The progressive GPU lightmapper is configured for subsequent bakes.

KINO-specific URP pipeline copies enable HDR and four-sample MSAA. The atmosphere profile supplies restrained bloom and ACES tone mapping. The original pipeline assets remain available, while quality levels point to the KINO copies.

The architecture is static and has simplified floor, stage and perimeter collision. The saved camera is a composition preview; it is not an XR rig or a locomotion system. Headset performance and player interactions require validation in the intended runtime. Scene lightmaps are scene-specific: the environment prefab can be reused elsewhere, but lighting should be rebaked in the destination scene.

The column repair retains the existing baked lighting. Rebake lighting when updating the scene's illumination to account for the moved column surfaces.

## Verification output

`Artifacts/KinoRotunda` contains the Blender and Unity validation reports, skybox identity comparison, render-device information, build logs and the rendered Unity preview. Use `Tools > KINO Rotunda > 5 - Validate environment` to check model scale, UV channels and material assignments after changing the model.
