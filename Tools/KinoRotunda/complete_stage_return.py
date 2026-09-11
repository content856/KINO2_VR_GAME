"""Restore the omitted left end pilaster using the existing right-hand detail."""
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


def complete_stage_return():
    selections = {'NeroMarble': range(4), 'BrushedGold': range(1, 16),
                  'BronzeShadow': range(1), 'WarmLED': range(3)}
    names = ['Stage_Return_Left__' + material for material in selections]
    existing = [name for name in names if name in bpy.data.objects]
    if existing:
        assert len(existing) == len(names), 'Incomplete existing stage return'
        return []
    original = {o.name: digest(o.data) for o in bpy.context.scene.objects if o.type == 'MESH'}
    created = []
    for material, chosen in selections.items():
        source = bpy.data.objects['Arcade_02__' + material]
        groups = list(components(source.data))
        keep = {index for component in chosen for index in groups[component]}
        mesh = source.data.copy()
        mesh.name = 'Stage_Return_Left_' + material
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in keep], context='VERTS')
        # Mirror the complete terminal detail, including its repaired joints.
        for vertex in bm.verts:
            vertex.co.x = -vertex.co.x
        if material == 'NeroMarble':
            # A concealed solid return connects the curved arcade to the flat
            # screen surround. It overlaps both, without moving either one.
            cube = bmesh.ops.create_cube(bm, size=1)['verts']
            for vertex in cube:
                vertex.co = Vector((-4.44 + vertex.co.x*.48,
                                    13.00 + vertex.co.y*.64,
                                    3.2425 + vertex.co.z*6.515))
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        obj = bpy.data.objects.new('Stage_Return_Left__' + material, mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj['surface'] = material
        obj['units'] = 'metres'
        obj['lightmap_uv'] = 'LightmapUV'
        obj['detail_source'] = 'Arcade_02 terminal pilaster, mirrored'
        # Only the newly created meshes are unwrapped. Existing lightmap
        # charts, including all floor and ceiling charts, remain untouched.
        uv0 = mesh.uv_layers[0]
        for polygon in mesh.polygons:
            axis = max(range(3), key=lambda k: abs(polygon.normal[k]))
            for loop_index in polygon.loop_indices:
                vertex = mesh.vertices[mesh.loops[loop_index].vertex_index].co
                uv0.data[loop_index].uv = ((vertex.x, vertex.y) if axis == 2 else
                                         (vertex.x, vertex.z) if axis == 1 else
                                         (vertex.y, vertex.z))
                uv0.data[loop_index].uv *= .20
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        mesh.uv_layers.active_index = 1
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.025,
                                 area_weight=.2, correct_aspect=True, scale_to_bounds=True)
        bpy.ops.object.mode_set(mode='OBJECT')
        mesh.uv_layers.active_index = 0
        created.append(obj)
    assert all(digest(bpy.data.objects[name].data) == value for name, value in original.items())
    bpy.ops.object.select_all(action='SELECT')
    return created


if __name__ == '__main__':
    source = os.path.join(ROOT, 'SourceArt/KinoRotunda/KinoRotunda.blend')
    target = os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx')
    output = os.path.join(ROOT, 'Artifacts/KinoRotunda/StageReturn')
    os.makedirs(output, exist_ok=True)
    backup = os.path.join(output, 'Before-' + datetime.now().strftime('%Y%m%d-%H%M%S'))
    os.makedirs(backup)
    for path in (source, target, target + '.meta'):
        shutil.copy2(path, backup)
    bpy.ops.wm.open_mainfile(filepath=source)
    created = complete_stage_return()
    report = {'backup': backup, 'added_objects': [o.name for o in created],
              'all_existing_meshes_including_floor_and_ceiling_unchanged': True,
              'added_vertices': sum(len(o.data.vertices) for o in created),
              'added_triangles': sum(len(p.vertices)-2 for o in created for p in o.data.polygons)}
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=source)
    export_fbx(target)
    with open(os.path.join(output, 'repair-report.json'), 'w') as stream:
        json.dump(report, stream, indent=2)
    print(json.dumps(report), flush=True)
