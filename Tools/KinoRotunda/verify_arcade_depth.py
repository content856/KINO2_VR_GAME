"""Read-only source/FBX preservation and contact checks for the recessed arcade."""
import bpy
import hashlib
import json
import math
import os
import struct
import sys
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(__file__))
from repair_column_joints import ROOT, components, digest
from align_pilasters import frame

OUTPUT = os.path.join(ROOT, 'Artifacts/KinoRotunda/ArcadeDepth')
with open(os.path.join(OUTPUT, 'repair-report.json')) as stream:
    repair = json.load(stream)


def snapshot():
    result = {}
    for obj in bpy.context.scene.objects:
        item = {'type': obj.type, 'matrix': tuple(tuple(row) for row in obj.matrix_world)}
        if obj.type == 'MESH':
            mesh = obj.data
            topology, uvs = hashlib.sha256(), hashlib.sha256()
            for polygon in mesh.polygons:
                topology.update(struct.pack('<%di' % len(polygon.vertices), *polygon.vertices))
                topology.update(struct.pack('<i?', polygon.material_index, polygon.use_smooth))
            for layer in mesh.uv_layers:
                uvs.update(layer.name.encode())
                for item_uv in layer.data:
                    uvs.update(struct.pack('<2f', *item_uv.uv))
            item.update(digest=digest(mesh), topology=topology.hexdigest(), uv=uvs.hexdigest(),
                        materials=[m.name for m in mesh.materials],
                        vertices=[tuple(v.co) for v in mesh.vertices])
        result[obj.name] = item
    return result


bpy.ops.wm.open_mainfile(filepath=os.path.join(repair['backup'], 'KinoRotunda.blend'))
before = snapshot()
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT, 'SourceArt/KinoRotunda/KinoRotunda.blend'))
after = snapshot()
assert set(after)-set(before) == set(repair['added_objects'])
assert set(before) <= set(after)
changed = []
for name, old in before.items():
    new = after[name]
    for field in ('type', 'matrix', 'topology', 'uv', 'materials'):
        assert old.get(field) == new.get(field), (name, field)
    if old.get('digest') == new.get('digest'):
        continue
    changed.append(name)
    assert name in repair['changed_meshes'], name
    assert len(old['vertices']) == len(new['vertices'])
    angle = int(name.split('_')[1].split('__')[0]) * 2 * math.pi / 28
    expected = Vector((math.sin(angle), math.cos(angle), 0)) * .50
    for original, current in zip(old['vertices'], new['vertices']):
        assert original[2] == current[2], (name, 'height changed')
    mesh = bpy.data.objects[name].data
    groups = list(components(mesh))
    arch_index = 1 if name.endswith('__IvoryMarble') else 0
    for index in groups[arch_index]:
        assert (Vector(new['vertices'][index])-Vector(old['vertices'][index])-expected).length < 3e-6
assert set(changed) == set(repair['changed_meshes'])
assert len(changed) == 50
assert not any(o.type in {'LIGHT', 'CAMERA'} for o in bpy.context.scene.objects)

trees = {}


def tree(obj, component):
    key = (obj.name, component)
    if key not in trees:
        group = list(components(obj.data))[component]
        remap = {old: new for new, old in enumerate(group)}
        vertices = [obj.matrix_world @ obj.data.vertices[i].co for i in group]
        faces = [tuple(remap[i] for i in p.vertices) for p in obj.data.polygons if p.vertices[0] in remap]
        trees[key] = BVHTree.FromPolygons(vertices, faces)
    return trees[key]


def interval(bvh, point):
    x, y = point
    low = bvh.ray_cast(Vector((x, y, -1)), Vector((0, 0, 1)), 12)[0]
    high = bvh.ray_cast(Vector((x, y, 10)), Vector((0, 0, -1)), 12)[0]
    return (low.z, high.z) if low is not None and high is not None else None


contacts = []


def contact(label, point, parts, low, high):
    intervals = sorted(value for obj, component in parts if (value := interval(tree(obj, component), point)))
    reach = low
    for start, end in intervals:
        if end < reach:
            continue
        assert start <= reach + .00002, (label, 'gap', reach, start)
        reach = max(reach, end)
        if reach >= high - .00002:
            break
    assert reach >= high - .00002, (label, reach, high)
    contacts.append(label)


for bay in range(2, 27):
    angle = bay * 2 * math.pi / 28
    def point(x, y):
        return (math.cos(angle)*x + math.sin(angle)*y, -math.sin(angle)*x + math.cos(angle)*y)
    ivory = bpy.data.objects[f'Arcade_{bay:02d}__IvoryMarble']
    gold = bpy.data.objects[f'Arcade_{bay:02d}__BrushedGold']
    groups = list(components(ivory.data))
    inverse = frame(angle).inverted()
    def local_bounds(component):
        points = [inverse @ ivory.data.vertices[i].co for i in groups[component]]
        return [min(v[j] for v in points) for j in range(3)], [max(v[j] for v in points) for j in range(3)]
    for side, shaft, capital, collar in ((-1, 2, 3, 19), (1, 4, 5, 25)):
        low, high = local_bounds(capital)
        centre = ((low[0]+high[0])/2, (low[1]+high[1])/2)
        assert abs(centre[0]-side*1.095) < 5e-6 and abs(centre[1]-14.64) < 5e-6
        assert abs(high[0]-low[0]-.60) < 5e-6 and abs(high[1]-low[1]-.60) < 5e-6
        shaft_low, shaft_high = local_bounds(shaft)
        assert abs(shaft_high[0]-shaft_low[0]-.54) < 5e-6
        xy = point(side*1.095, 14.64)
        for dx, dy in ((0,0), (-.25,0), (.25,0), (0,-.25), (0,.25)):
            support = point(side*1.095+dx, 14.64+dy)
            floor_hit = tree(ivory, 15).ray_cast(Vector((*support, .1)), Vector((0, 0, -1)), .2)[0]
            assert floor_hit is not None and abs(floor_hit.z-.01) < 1e-5, (bay, side, 'foot support')
        contact(f'{bay}: column {side} remains seated on floor', xy, [(ivory, shaft)], 0, 4.77)
        # Sample the full width/depth of the arch's spring, including its edges.
        for tangent in (1.035, 1.095, 1.155):
            for depth in (14.47, 14.64, 14.81):
                contact(f'{bay}: column {side} full arch bearing {tangent}/{depth}', point(side*tangent, depth),
                        [(ivory, capital), (ivory, 1)], 4.80, 4.90)
        contact(f'{bay}: column {side} neck to capital', xy,
                [(ivory, shaft), (gold, collar), (ivory, capital)], 4.70, 4.88)
    # Upper wall remains seated into the existing circular cornice.
    ceiling = bpy.data.objects['Ceiling__NeroMarble']
    cornice = [(ceiling, i) for i in range(len(list(components(ceiling.data))))]
    soffit = bpy.data.objects['Arcade_Recess_Soffit__NeroMarble']
    contact(f'{bay}: upper wall connects to soffit', point(0, 14.85), [(ivory, 0), (soffit, 0)], 6.50, 6.64)
    contact(f'{bay}: soffit overlaps existing cornice', point(0, 14.22), [(soffit, 0)] + cornice, 6.535, 6.64)

exports = []
for path in (os.path.join(repair['backup'], 'KinoRotunda.fbx'),
             os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx')):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    exports.append(snapshot())
assert exports[1].keys() == after.keys()
assert set(exports[1])-set(exports[0]) == set(repair['added_objects'])
for name, old in exports[0].items():
    new = exports[1][name]
    for field in ('type', 'matrix', 'topology', 'uv', 'materials'):
        assert old.get(field) == new.get(field), ('FBX', name, field)
    if name not in changed:
        assert old == new, ('FBX unchanged object', name)
for name, current in exports[1].items():
    if 'vertices' in current:
        assert len(current['vertices']) == len(after[name]['vertices'])
        for exported, source in zip(current['vertices'], after[name]['vertices']):
            assert (Vector(exported) - Vector(source)).length < 5e-5, ('FBX/source mismatch', name)
meta = os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx.meta')
with open(meta, 'rb') as current, open(os.path.join(repair['backup'], 'KinoRotunda.fbx.meta'), 'rb') as original:
    assert current.read() == original.read()
report = {'errors': [], 'changed_meshes': len(changed), 'contact_checks_passed': len(contacts),
          'source_and_fbx_match': True, 'topology_uv_materials_transforms_preserved': True,
          'black_pilasters_main_floor_original_ceiling_and_other_meshes_unchanged': True,
          'full_arch_spring_supported_by_centred_capitals': True, 'column_width_scale': 1.5,
          'arch_additional_recess_m': .5, 'arch_total_recess_m': .8,
          'unity_metadata_unchanged': True, 'contacts': contacts}
with open(os.path.join(OUTPUT, 'validation-report.json'), 'w') as stream:
    json.dump(report, stream, indent=2)
print(json.dumps({k: v for k, v in report.items() if k != 'contacts'}), flush=True)
