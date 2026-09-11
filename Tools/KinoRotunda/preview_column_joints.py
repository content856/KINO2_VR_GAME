"""Temporary Blender workbench cameras; does not save or alter the source file."""
import bpy
import os
import sys
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
args = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
stage_return = '--stage' in args
if stage_return:
    args.remove('--stage')
pilaster_sides = '--sides' in args
if pilaster_sides:
    args.remove('--sides')
alignment = '--alignment' in args
if alignment:
    args.remove('--alignment')
    pilaster_sides = True
depth = '--depth' in args
if depth:
    args.remove('--depth')
seams = '--seams' in args
if seams:
    args.remove('--seams')
source, label = args if args else (os.path.join(ROOT, 'SourceArt/KinoRotunda/KinoRotunda.blend'), 'After')
bpy.ops.wm.open_mainfile(filepath=source)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.render.resolution_x = 1400
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'MATERIAL'
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.display.shading.cavity_type = 'BOTH'
scene.display.shading.curvature_ridge_factor = 1.1
scene.display.shading.curvature_valley_factor = 1.1
scene.display.shading.background_type = 'WORLD'
scene.world = bpy.data.worlds.new('QA background only')
scene.world.color = (.16, .22, .30)
camera_data = bpy.data.cameras.new('QA Camera')
camera = bpy.data.objects.new('QA Camera', camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera_data.type = 'ORTHO'
camera_data.clip_start = .01
camera_data.clip_end = 100
output = os.path.join(ROOT, 'Artifacts/KinoRotunda/ColumnJoints')
if stage_return:
    output = os.path.join(ROOT, 'Artifacts/KinoRotunda/StageReturn')
if pilaster_sides:
    output = os.path.join(ROOT, 'Artifacts/KinoRotunda/PilasterSides')
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1400
if alignment:
    output = os.path.join(ROOT, 'Artifacts/KinoRotunda/PilasterAlignment')
if depth:
    output = os.path.join(ROOT, 'Artifacts/KinoRotunda/ArcadeDepth')
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1400
if seams:
    output = os.path.join(ROOT, 'Artifacts/KinoRotunda/MarbleSeams')
    scene.display.shading.color_type = 'TEXTURE'
    for name in ('IvoryMarble', 'NeroMarble'):
        material = bpy.data.materials[name]
        material.use_nodes = True
        texture = material.node_tree.nodes.new('ShaderNodeTexImage')
        texture.image = bpy.data.images.load(os.path.join(ROOT, 'Assets/KinoRotunda/Textures', name+'_BaseColor.png'))
        material.node_tree.nodes.active = texture
        material.node_tree.links.new(texture.outputs['Color'], material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
os.makedirs(output, exist_ok=True)
views = [
    ('Bay', (0.5, -7.7, 3.8), (0.5, -14, 3.35), 11.0),
    ('Bases', (1.1, -9.8, 1.15), (.7, -13.9, .38), 3.5),
    ('Capitals', (1.2, -10.4, 5.8), (1.1, -13.9, 6.25), 2.8),
]
if stage_return:
    views = [('Left-Return', (-3.0, 4.0, 3.5), (-5.6, 13.0, 3.3), 10.5),
             ('Left-Joint', (-2.0, 7.0, 4.5), (-4.6, 13.0, 4.8), 3.5)]
if pilaster_sides:
    views = [('Pilaster', (1.60, -8.0, 3.3), (1.60, -13.8, 3.3), 7.3),
             ('Seam-Front', (1.9, -9.0, 3.0), (1.9, -13.7, 3.0), 1.0),
             ('Seam-Oblique', (4.0, -10.0, 3.0), (1.9, -13.7, 3.0), 1.4)]
if depth:
    views = [('Bay', (0, -7.7, 3.5), (0, -14, 3.35), 7.4),
             ('Oblique', (3.5, -8, 3.5), (.4, -14, 3.35), 7.4),
             ('Detail', (4, -10, 3), (1.9, -13.7, 3), 1.6),
             ('Capital', (0, -12.2, 5.15), (1.095, -14.64, 4.90), 1.4),
             ('Foot', (0, -12.2, .9), (1.095, -14.64, .23), 1.6)]
if seams:
    views = [('Column', (.2, -12, 2.8), (1.095, -14.64, 2.8), 4.4),
             ('Column-Rear', (1.6, -16.7, 2.8), (1.095, -14.64, 2.8), 4.4),
             ('Arch', (0, -12.7, 4.55), (0, -14.65, 5.37), 3.2)]
for name, position, target, scale in views:
    if seams:
        scene.render.resolution_x, scene.render.resolution_y = (1400, 900) if name == 'Arch' else (700, 1400)
    camera.location = position
    camera.rotation_euler = (Vector(target)-camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera_data.ortho_scale = scale
    scene.render.filepath = os.path.join(output, label + '-' + name + '.png')
    bpy.ops.render.render(write_still=True)
