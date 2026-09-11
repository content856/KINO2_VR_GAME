"""Read-only verification of the added left terminal and unchanged existing art."""
import bpy
import json
import math
import os
import sys
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(__file__))
from repair_column_joints import ROOT, components, digest

output = os.path.join(ROOT, 'Artifacts/KinoRotunda/StageReturn')
with open(os.path.join(output, 'repair-report.json')) as stream:
    repair = json.load(stream)
source = os.path.join(ROOT, 'SourceArt/KinoRotunda/KinoRotunda.blend')
fbx = os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx')


def snapshot():
    return {obj.name: (digest(obj.data), tuple(tuple(row) for row in obj.matrix_world),
                       tuple(mat.name for mat in obj.data.materials))
            for obj in bpy.context.scene.objects if obj.type == 'MESH'}


bpy.ops.wm.open_mainfile(filepath=os.path.join(repair['backup'], 'KinoRotunda.blend'))
before = snapshot()
bpy.ops.wm.open_mainfile(filepath=source)
after = snapshot()
assert all(after[name] == value for name, value in before.items())
assert set(after)-set(before) == set(repair['added_objects'])
for name in repair['added_objects']:
    mesh = bpy.data.objects[name].data
    assert [layer.name for layer in mesh.uv_layers] == ['UVMap', 'LightmapUV']
    for layer in mesh.uv_layers:
        assert all(math.isfinite(v) for item in layer.data for v in item.uv)
    assert all(-.0001 <= v <= 1.0001 for item in mesh.uv_layers[1].data for v in item.uv)


def tree(obj, indices=None):
    if indices is None:
        indices = list(range(len(obj.data.vertices)))
    index_set = set(indices)
    mapping = {index: i for i, index in enumerate(indices)}
    faces = [tuple(mapping[i] for i in p.vertices) for p in obj.data.polygons if p.vertices[0] in index_set]
    return BVHTree.FromPolygons([obj.matrix_world @ obj.data.vertices[i].co for i in indices], faces)


black = bpy.data.objects['Stage_Return_Left__NeroMarble']
groups = list(components(black.data))
assert len(groups) == 5
bridge = tree(black, groups[4])
assert bridge.overlap(tree(black, groups[0])), 'Return does not connect to pilaster'
assert bridge.overlap(tree(bpy.data.objects['Kino_Stage__NeroMarble'])), 'Return does not connect to display'
assert min(v.co.z for v in black.data.vertices) < 0
assert max(v.co.z for v in black.data.vertices) >= 6.57

exports = []
for path in (os.path.join(repair['backup'], 'KinoRotunda.fbx'), fbx):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    exports.append(snapshot())
assert all(exports[1][name] == value for name, value in exports[0].items())
assert set(exports[1])-set(exports[0]) == set(repair['added_objects'])
with open(fbx+'.meta', 'rb') as current, open(os.path.join(repair['backup'], 'KinoRotunda.fbx.meta'), 'rb') as old:
    assert current.read() == old.read()
result = {'errors': [], 'existing_source_and_fbx_meshes_identical': len(before),
          'added_meshes': repair['added_objects'], 'floor_ceiling_untouched': True,
          'return_intersects_both_pilaster_and_display': True, 'uv_channels_valid': True,
          'unity_guid_and_material_remaps_unchanged': True}
with open(os.path.join(output, 'validation-report.json'), 'w') as stream:
    json.dump(result, stream, indent=2)
print(json.dumps(result), flush=True)
