"""Compare saved source/export to backup and ray-test actual column contacts."""
import bpy
import json
import math
import os
import sys
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(__file__))
from repair_column_joints import ROOT, components, digest

REPORTS = os.path.join(ROOT, 'Artifacts/KinoRotunda/ColumnJoints')
with open(os.path.join(REPORTS, 'repair-report.json')) as stream:
    repair = json.load(stream)
backup = repair['backup']


def snapshot():
    result = {}
    for obj in bpy.context.scene.objects:
        item = {'transform': tuple(tuple(row) for row in obj.matrix_world), 'type': obj.type}
        if obj.type == 'MESH':
            mesh = obj.data
            item['digest'] = digest(mesh)
            item['topology'] = [tuple(p.vertices) for p in mesh.polygons]
            item['uv'] = [(layer.name, [tuple(p.uv) for p in layer.data]) for layer in mesh.uv_layers]
            item['materials'] = [mat.name for mat in mesh.materials]
            item['vertices'] = len(mesh.vertices)
        result[obj.name] = item
    return result


bpy.ops.wm.open_mainfile(filepath=os.path.join(backup, 'KinoRotunda.blend'))
before = snapshot()
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT, 'SourceArt/KinoRotunda/KinoRotunda.blend'))
after = snapshot()
assert before.keys() == after.keys(), 'Object names/hierarchy changed'
changed = []
for name, original in before.items():
    current = after[name]
    assert original['transform'] == current['transform'], name
    if 'digest' not in original:
        continue
    for field in ('topology', 'uv', 'materials', 'vertices'):
        assert original[field] == current[field], (name, field)
    if original['digest'] != current['digest']:
        changed.append(name)
        assert name in repair['changed_objects'], name
    if name.startswith(('Floor', 'Ceiling')):
        assert original == current, name

# Each component gets a BVH so overlapping ornamental shells do not obscure
# the entry/exit intersections of the structural pieces behind them.
cache = {}
def component_tree(obj, component):
    key = (obj.name, component)
    if key not in cache:
        indices = list(components(obj.data))[component]
        index_set = set(indices)
        remap = {old: new for new, old in enumerate(indices)}
        points = [obj.matrix_world @ obj.data.vertices[i].co for i in indices]
        faces = [tuple(remap[i] for i in p.vertices) for p in obj.data.polygons if p.vertices[0] in index_set]
        cache[key] = BVHTree.FromPolygons(points, faces)
    return cache[key]


def vertical_interval(tree, x, y):
    bottom = tree.ray_cast(Vector((x, y, -1)), Vector((0, 0, 1)), 12)[0]
    top = tree.ray_cast(Vector((x, y, 10)), Vector((0, 0, -1)), 12)[0]
    return (bottom.z, top.z) if bottom is not None and top is not None else None


checks = []
def contact(name, point, parts, z0, z1):
    intervals = sorted(v for obj, component in parts if (v := vertical_interval(component_tree(obj, component), *point)) is not None)
    reach = z0
    for low, high in intervals:
        if high < reach:
            continue
        assert low <= reach + .00002, (name, 'open gap', reach, low)
        reach = max(reach, high)
        if reach >= z1-.00002:
            break
    assert reach >= z1-.00002, (name, 'unconnected end', reach, z1)
    checks.append(name)


for bay in range(2, 27):
    angle = bay*2*math.pi/28
    def point(x, y):
        return (math.cos(angle)*x + math.sin(angle)*y, -math.sin(angle)*x + math.cos(angle)*y)
    black = bpy.data.objects[f'Arcade_{bay:02d}__NeroMarble']
    white = bpy.data.objects[f'Arcade_{bay:02d}__IvoryMarble']
    gold = bpy.data.objects[f'Arcade_{bay:02d}__BrushedGold']
    contact(f'{bay}: pilaster foot through capital', point(-1.48, 13.92),
            [(black, i) for i in range(4)] + [(gold, i) for i in (1, 2, 3)], 0, 6.57)
    cornice = bpy.data.objects['Ceiling__BrushedGold']
    # Cornice has multiple annular components; locate its actual lower contact.
    cornice_intervals = [vertical_interval(component_tree(cornice, c), *point(-1.48, 13.92)) for c in range(len(list(components(cornice.data))))]
    assert any(v and 6.50 < v[0] < 6.58 for v in cornice_intervals), bay
    checks.append(f'{bay}: capital overlaps unchanged cornice')
    for side, shaft, abacus, collar in ((-1, 2, 3, 19), (1, 4, 5, 25)):
        contact(f'{bay}: round column {side} floor contact', point(side*1.02, 13.71), [(white, shaft)], 0, 4.77)
        contact(f'{bay}: round column {side} arch joint', point(side*1.11, 13.71),
                [(white, shaft), (white, abacus), (gold, collar), (white, 1)], 4.70, 4.92)
    for j in range(7):
        contact(f'{bay}: baluster {j} sill to handrail', point(-.86+j*.2867, 14.20),
                [(white, 6), (white, 8+j), (white, 7)], .24, 1.13)

stage = bpy.data.objects['Kino_Stage__NeroMarble']
stage_gold = bpy.data.objects['Kino_Stage__BrushedGold']
for side, stem, foot, collar in ((-1, 1, 2, 4), (1, 3, 4, 9)):
    contact(f'display pilaster {side}', (side*3.66, 12.10), [(stage, foot), (stage_gold, collar), (stage, stem)], .17, .32)

exports = []
for path in (os.path.join(backup, 'KinoRotunda.fbx'), os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx')):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    exports.append(snapshot())
assert exports[0].keys() == exports[1].keys(), 'FBX object names changed'
protected_fbx = []
for name in exports[0]:
    if name.startswith(('Floor', 'Ceiling')):
        assert exports[0][name] == exports[1][name], ('FBX protected data', name)
        protected_fbx.append(name)
    if 'uv' in exports[1][name]:
        assert exports[0][name]['uv'] == exports[1][name]['uv'], ('FBX UV', name)
        assert exports[0][name]['vertices'] == exports[1][name]['vertices'], ('FBX topology', name)

meta = os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx.meta')
with open(meta, 'rb') as stream:
    metadata = stream.read()
with open(os.path.join(backup, 'KinoRotunda.fbx.meta'), 'rb') as stream:
    assert metadata == stream.read(), 'Unity GUID/import settings changed'
result = {'errors': [], 'contact_checks_passed': len(checks), 'changed_meshes': len(changed),
          'source_topology_uv_materials_transforms_unchanged': True, 'fbx_uvs_and_vertex_counts_unchanged': True,
          'floor_ceiling_source_and_fbx_identical': protected_fbx, 'unity_metadata_unchanged': True,
          'contact_checks': checks}
with open(os.path.join(REPORTS, 'validation-report.json'), 'w') as stream:
    json.dump(result, stream, indent=2)
print(json.dumps({k: v for k, v in result.items() if k != 'contact_checks'}), flush=True)
