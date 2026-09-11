"""Recess the ivory arcade 80 cm and seat wider columns under its arches.

Move each arch, its upper wall, both columns and their gold details together.
The move is outward along the bay normal, retaining every mesh/UV index.
"""
import bpy
import bmesh
import json
import math
import os
import shutil
import sys
from datetime import datetime
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
from repair_column_joints import ROOT, components, digest, export_fbx
from align_pilasters import frame

RECESS_M = .80
COLUMN_SCALE = 1.50
RAIL_OFFSET = .65
SELECTIONS = {'IvoryMarble': tuple(range(6)),
              'BrushedGold': (0,) + tuple(range(16, 28))}


def recess_arcade():
    moved = []
    for bay in range(2, 27):
        angle = bay * 2 * math.pi / 28
        outward = Vector((math.sin(angle), math.cos(angle), 0))
        for material, chosen in SELECTIONS.items():
            obj = bpy.data.objects[f'Arcade_{bay:02d}__{material}']
            previous = float(obj.get('arcade_recess_m', 0.0))
            if abs(previous - RECESS_M) < 1e-6 and obj.get('arcade_depth_version') == 2:
                continue
            groups = list(components(obj.data))
            assert len(groups) == (16 if material == 'IvoryMarble' else 29), obj.name
            offset = outward * (RECESS_M - previous)
            for component in chosen:
                for index in groups[component]:
                    obj.data.vertices[index].co += offset
            matrix = frame(angle)
            inverse = matrix.inverted()
            scale = COLUMN_SCALE / float(obj.get('column_width_scale', 1.0))
            old_x = float(obj.get('column_centre_x_offset_m', 0.0))
            old_y = float(obj.get('column_centre_y_offset_m', 0.0))
            # The arch spring occupies x=1.025..1.165 and y=13.66..14.02
            # before recessing. Centre the whole capital over that footprint.
            columns = ((-1, (2, 3)), (1, (4, 5))) if material == 'IvoryMarble' else (
                (-1, range(16, 22)), (1, range(22, 28)))
            for side, column_groups in columns:
                for component in column_groups:
                    for index in groups[component]:
                        local = inverse @ obj.data.vertices[index].co
                        local.x = (local.x-side*(1.02+old_x))*scale + side*1.095
                        local.y = (local.y-(13.71+RECESS_M+old_y))*scale + 13.84+RECESS_M
                        obj.data.vertices[index].co = matrix @ local
            rail_delta = RAIL_OFFSET - float(obj.get('arcade_rail_offset_m', 0.0))
            for component in (range(6, 15) if material == 'IvoryMarble' else (28,)):
                for index in groups[component]:
                    obj.data.vertices[index].co += outward * rail_delta
            if material == 'IvoryMarble':
                # The existing exterior lip grows out to support the new feet.
                old_outer = float(obj.get('arcade_lip_outer_radius_m', 14.65))
                for index in groups[15]:
                    vertex = obj.data.vertices[index]
                    radius = math.hypot(vertex.co.x, vertex.co.y)
                    target = 14.35 + (radius-14.35)*(.90/(old_outer-14.35))
                    vertex.co.x *= target/radius
                    vertex.co.y *= target/radius
                # Extend only the outer wall edges so adjacent rear walls meet.
                old_half = float(obj.get('arcade_wall_half_width_m', 1.54))
                for index in groups[0]:
                    local = inverse @ obj.data.vertices[index].co
                    if abs(local.x) > 1.165:
                        local.x = math.copysign(1.165+(abs(local.x)-1.165)*(.585/(old_half-1.165)), local.x)
                        obj.data.vertices[index].co = matrix @ local
                obj['arcade_lip_outer_radius_m'] = 15.25
                obj['arcade_wall_half_width_m'] = 1.75
            obj.data.update()
            obj['arcade_recess_m'] = RECESS_M
            obj['column_width_scale'] = COLUMN_SCALE
            obj['column_centre_x_offset_m'] = .075
            obj['column_centre_y_offset_m'] = .13
            obj['arcade_rail_offset_m'] = RAIL_OFFSET
            obj['arcade_depth_version'] = 2
            moved.append(obj.name)
    add_recess_soffit()
    return moved


def add_recess_soffit():
    """Close the added depth above the rear wall without changing the ceiling."""
    name = 'Arcade_Recess_Soffit__NeroMarble'
    if name in bpy.data.objects:
        return
    vertices, faces = [], []
    segments = 200
    for i in range(segments+1):
        angle = (1.5+25*i/segments)*2*math.pi/28
        for radius, height in ((14.20, 6.64), (15.20, 6.64), (14.20, 6.535), (15.20, 6.535)):
            vertices.append((radius*math.sin(angle), radius*math.cos(angle), height))
    for i in range(segments):
        j, k = 4*i, 4*(i+1)
        faces.extend(((j,k,k+1,j+1), (j+2,j+3,k+3,k+2), (j,j+2,k+2,k), (j+1,k+1,k+3,j+3)))
    faces.extend(((0,1,3,2), (4*segments+2,4*segments+3,4*segments+1,4*segments)))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(bpy.data.materials['NeroMarble'])
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    uv = mesh.uv_layers.new(name='UVMap')
    for polygon in mesh.polygons:
        for index in polygon.loop_indices:
            vertex = mesh.vertices[mesh.loops[index].vertex_index].co
            uv.data[index].uv = (vertex.x*.20, vertex.y*.20)
    mesh.uv_layers.new(name='LightmapUV')
    mesh.uv_layers.active_index = 1
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.025, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    mesh.uv_layers.active_index = 0
    obj['surface'] = 'NeroMarble'
    obj['units'] = 'metres'
    obj['lightmap_uv'] = 'LightmapUV'


if __name__ == '__main__':
    source = os.path.join(ROOT, 'SourceArt/KinoRotunda/KinoRotunda.blend')
    target = os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx')
    output = os.path.join(ROOT, 'Artifacts/KinoRotunda/ArcadeDepth')
    os.makedirs(output, exist_ok=True)
    backup = os.path.join(output, 'Before-' + datetime.now().strftime('%Y%m%d-%H%M%S'))
    os.makedirs(backup)
    for path in (source, target, target + '.meta'):
        shutil.copy2(path, backup)
    for filename in ('repair-report.json', 'validation-report.json'):
        path = os.path.join(output, filename)
        if os.path.exists(path):
            shutil.copy2(path, backup)
    bpy.ops.wm.open_mainfile(filepath=source)
    original = {o.name: digest(o.data) for o in bpy.context.scene.objects if o.type == 'MESH'}
    moved = recess_arcade()
    assert len(moved) == 50, 'Expected 25 bays to update'
    assert all(digest(bpy.data.objects[name].data) == value
               for name, value in original.items() if name not in moved)
    assert recess_arcade() == [], 'Re-running must not accumulate displacement'
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.wm.save_as_mainfile(filepath=source)
    export_fbx(target)
    report = {'backup': backup, 'outward_translation_m': RECESS_M,
              'additional_arch_recess_m': .50, 'column_width_scale': COLUMN_SCALE,
              'column_arch_alignment_correction_m': {'tangent': .075, 'outward': .13},
              'rail_offset_m': RAIL_OFFSET, 'exterior_lip_radius_m': 15.25,
              'added_objects': ['Arcade_Recess_Soffit__NeroMarble'],
              'bays': 25, 'columns': 50, 'arches': 25, 'changed_meshes': moved,
              'assembly': 'ivory columns, capitals, arches, upper walls and attached gold details',
              'other_meshes_identical': True, 'idempotent': True}
    with open(os.path.join(output, 'repair-report.json'), 'w') as stream:
        json.dump(report, stream, indent=2)
    print(json.dumps(report), flush=True)
