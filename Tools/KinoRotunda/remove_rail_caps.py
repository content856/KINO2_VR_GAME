"""Remove only the thin floating gold cap above each marble balustrade."""
import bpy
import bmesh
import json
import os
import shutil
import sys
from datetime import datetime

sys.path.insert(0, os.path.dirname(__file__))
from repair_column_joints import ROOT, components, digest, export_fbx


def retained_data(mesh, keep):
    vertices = tuple(tuple(v.co) for v in mesh.vertices if v.index in keep)
    faces = []
    for face in mesh.polygons:
        if face.vertices[0] in keep:
            faces.append((tuple(tuple(mesh.vertices[i].co) for i in face.vertices),
                          face.material_index, face.use_smooth,
                          tuple(tuple(tuple(layer.data[i].uv) for i in face.loop_indices)
                                for layer in mesh.uv_layers)))
    return vertices, tuple(faces)


def remove_rail_caps():
    removed = []
    for bay in range(2, 27):
        obj = bpy.data.objects[f'Arcade_{bay:02d}__BrushedGold']
        if obj.get('rail_top_cap_removed'):
            continue
        mesh = obj.data
        groups = list(components(mesh))
        assert len(groups) == 29, obj.name
        cap = set(groups[28])
        assert len(cap) == 32
        assert all(1.216 < mesh.vertices[i].co.z < 1.236 for i in cap)
        keep = set(range(len(mesh.vertices))) - cap
        before = retained_data(mesh, keep)
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in cap], context='VERTS')
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        assert len(list(components(mesh))) == 28
        assert retained_data(mesh, set(range(len(mesh.vertices)))) == before, obj.name
        obj['rail_top_cap_removed'] = True
        removed.append(obj.name)
    return removed


if __name__ == '__main__':
    source = os.path.join(ROOT, 'SourceArt/KinoRotunda/KinoRotunda.blend')
    target = os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx')
    output = os.path.join(ROOT, 'Artifacts/KinoRotunda/RailCaps')
    backup = os.path.join(output, 'Before-'+datetime.now().strftime('%Y%m%d-%H%M%S'))
    os.makedirs(backup)
    for path in (source, target, target+'.meta'):
        shutil.copy2(path, backup)
    bpy.ops.wm.open_mainfile(filepath=source)
    original = {o.name: digest(o.data) for o in bpy.context.scene.objects if o.type == 'MESH'}
    removed = remove_rail_caps()
    assert len(removed) == 25
    assert all(digest(bpy.data.objects[name].data) == previous
               for name, previous in original.items() if name not in removed)
    assert remove_rail_caps() == []
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.wm.save_as_mainfile(filepath=source)
    export_fbx(target)
    with open(target+'.meta', 'rb') as current, open(os.path.join(backup, 'KinoRotunda.fbx.meta'), 'rb') as old:
        assert current.read() == old.read()
    report = {'backup': backup, 'removed_caps': len(removed), 'changed_meshes': removed,
              'all_remaining_geometry_uvs_and_materials_preserved': True,
              'unity_metadata_unchanged': True, 'idempotent': True}
    with open(os.path.join(output, 'repair-report.json'), 'w') as stream:
        json.dump(report, stream, indent=2)
    print(json.dumps({k:v for k,v in report.items() if k != 'changed_meshes'}), flush=True)
