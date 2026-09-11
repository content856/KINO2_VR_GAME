"""Rigidly reposition complete pilaster assemblies to face the rotunda centre.

No widening, scaling, or reshaping. Existing shafts, plinths, capitals,
inlays and sconces move together, preserving their dimensions and UVs.
"""
import bpy
import json
import math
import os
import shutil
import sys
from datetime import datetime
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
from repair_column_joints import ROOT, components, export_fbx


def frame(angle):
    c, s = math.cos(angle), math.sin(angle)
    return Matrix(((c, s, 0, 0), (-s, c, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1)))


def alignment_transform(bay):
    angle = bay*2*math.pi/28
    radius = math.hypot(1.48, 13.92)
    return (frame(angle-math.pi/28) @ Matrix.Translation((1.48, radius-13.92, 0))
            @ frame(angle).inverted())


def align_pilasters():
    selections = {'NeroMarble': range(4), 'BrushedGold': range(1, 16),
                  'BronzeShadow': range(1), 'WarmLED': range(3)}
    moved = []
    for bay in range(2, 27):
        black = bpy.data.objects[f'Arcade_{bay:02d}__NeroMarble']
        if black.get('radial_alignment_version') == 1:
            continue
        transform = alignment_transform(bay)
        for material, selected in selections.items():
            obj = bpy.data.objects[f'Arcade_{bay:02d}__{material}']
            groups = list(components(obj.data))
            for component in selected:
                for index in groups[component]:
                    obj.data.vertices[index].co = transform @ obj.data.vertices[index].co
            obj.data.update()
        anchor = bpy.data.objects.get(f'Anchor_Sconce_{bay:02d}')
        if anchor:
            anchor.location = transform @ anchor.location
        black['radial_alignment_version'] = 1
        moved.append(black.name)
    # The approved left display terminal mirrors bay 02. Its concealed
    # connecting block stays in place while the ornamental column rotates.
    black = bpy.data.objects.get('Stage_Return_Left__NeroMarble')
    if black and black.get('radial_alignment_version') != 1:
        mirror = Matrix.Diagonal((-1, 1, 1, 1))
        transform = mirror @ alignment_transform(2) @ mirror
        for material in selections:
            obj = bpy.data.objects['Stage_Return_Left__'+material]
            groups = list(components(obj.data))
            chosen = range(4) if material == 'NeroMarble' else range(len(groups))
            for component in chosen:
                for index in groups[component]:
                    obj.data.vertices[index].co = transform @ obj.data.vertices[index].co
            obj.data.update()
        black['radial_alignment_version'] = 1
        moved.append(black.name)
    return moved


if __name__ == '__main__':
    source = os.path.join(ROOT, 'SourceArt/KinoRotunda/KinoRotunda.blend')
    fbx = os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx')
    output = os.path.join(ROOT, 'Artifacts/KinoRotunda/PilasterAlignment')
    os.makedirs(output, exist_ok=True)
    backup = os.path.join(output, 'Before-'+datetime.now().strftime('%Y%m%d-%H%M%S'))
    os.makedirs(backup)
    for path in (source, fbx, fbx+'.meta'):
        shutil.copy2(path, backup)
    bpy.ops.wm.open_mainfile(filepath=source)
    moved = align_pilasters()
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.wm.save_as_mainfile(filepath=source)
    export_fbx(fbx)
    report = {'backup': backup, 'moved_pilasters': moved,
              'rotation_degrees': 180/28, 'local_left_translation_m': .087353,
              'method': 'rigid translation and rotation only; original dimensions restored'}
    with open(os.path.join(output, 'repair-report.json'), 'w') as stream:
        json.dump(report, stream, indent=2)
    print(json.dumps(report), flush=True)
