"""Close the rotunda's column joints in Blender without rebuilding any mesh.

Run with Blender --background --factory-startup --python this_file.py.
The generator also calls repair_columns() before saving/exporting.
Topology, UVs, material slots, names, and every non-column component are retained.
"""
import bpy
import hashlib
import json
import os
import shutil
import struct
from datetime import datetime

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))


def components(mesh):
    neighbors = [[] for _ in mesh.vertices]
    for edge in mesh.edges:
        a, b = edge.vertices
        neighbors[a].append(b)
        neighbors[b].append(a)
    seen = set()
    for vertex in mesh.vertices:
        if vertex.index in seen:
            continue
        found, pending = [], [vertex.index]
        seen.add(vertex.index)
        while pending:
            index = pending.pop()
            found.append(index)
            for other in neighbors[index]:
                if other not in seen:
                    seen.add(other)
                    pending.append(other)
        yield sorted(found)


def digest(mesh, indices=None):
    h = hashlib.sha256()
    for index in range(len(mesh.vertices)) if indices is None else indices:
        h.update(struct.pack('<3f', *mesh.vertices[index].co))
    if indices is None:
        for polygon in mesh.polygons:
            h.update(struct.pack('<%di' % len(polygon.vertices), *polygon.vertices))
            h.update(struct.pack('<i?', polygon.material_index, polygon.use_smooth))
        for layer in mesh.uv_layers:
            h.update(layer.name.encode())
            for item in layer.data:
                h.update(struct.pack('<2f', *item.uv))
    return h.hexdigest()


def repair_columns():
    before = {obj.name: digest(obj.data) for obj in bpy.context.scene.objects if obj.type == 'MESH'}
    edits = []
    untouched = 0
    for obj in bpy.context.scene.objects:
        is_bay = obj.name.startswith('Arcade_')
        is_stage = obj.name == 'Kino_Stage__NeroMarble'
        if obj.type != 'MESH' or not (is_bay or is_stage):
            continue
        groups = list(components(obj.data))
        allowed = set()
        original = [digest(obj.data, indices) for indices in groups]

        def reshape(component, expected_vertices, low, high, label):
            indices = groups[component]
            assert len(indices) == expected_vertices, (obj.name, component, len(indices))
            old_low = min(obj.data.vertices[i].co.z for i in indices)
            old_high = max(obj.data.vertices[i].co.z for i in indices)
            allowed.add(component)
            if abs(old_low-low) < 2e-6 and abs(old_high-high) < 2e-6:
                return
            for index in indices:
                vertex = obj.data.vertices[index]
                vertex.co.z = low + (vertex.co.z-old_low) * (high-low)/(old_high-old_low)
            edits.append({'object': obj.name, 'joint': label, 'before_z': [old_low, old_high], 'after_z': [low, high]})

        if is_stage:
            assert len(groups) == 9
            for component in (1, 3):
                reshape(component, 32, .245, 6.37, 'display pilaster to gold foot')
        elif obj.name.endswith('__NeroMarble'):
            assert len(groups) == 4
            reshape(0, 32, .375, 6.18, 'pilaster shaft into lower capital collar')
            reshape(1, 32, -.02, .32, 'pilaster plinth into existing floor')
            reshape(2, 32, .355, .57, 'base neck into gold collar')
            reshape(3, 32, 6.20, 6.58, 'capital through crown moulding into existing cornice')
        elif obj.name.endswith('__IvoryMarble'):
            assert len(groups) == 16
            for component in (2, 4):
                indices = groups[component]
                assert len(indices) == 120
                allowed.add(component)
                # Keep all six radial profile rings and all shaft/capital vertices.
                levels = ((.42, -.02), (.50, .16), (.58, .36))
                changed = 0
                for index in indices:
                    vertex = obj.data.vertices[index]
                    for previous, target in levels:
                        if abs(vertex.co.z-previous) < 2e-6:
                            vertex.co.z = target
                            changed += 1
                            break
                assert changed in (0, 60), (obj.name, changed)
                if changed:
                    edits.append({'object': obj.name, 'joint': 'round column moulded foot down to existing floor', 'profile_z': levels})
            for component in range(8, 15):
                reshape(component, 96, .25, 1.11, 'baluster seated inside unchanged sill and handrail')
        elif obj.name.endswith('__BrushedGold'):
            assert len(groups) == 29
            for component in (16, 22):
                reshape(component, 60, .13, .19, 'round column lower foot collar')
            for component in (17, 23):
                reshape(component, 60, .30, .36, 'round column upper foot collar')
            for component in range(11, 16):
                reshape(component, 48, .3675, .4775, 'base bead seated in gold moulding')

        for component, indices in enumerate(groups):
            if component not in allowed:
                assert digest(obj.data, indices) == original[component], (obj.name, component)
                untouched += 1
        obj.data.update()

    after = {obj.name: digest(obj.data) for obj in bpy.context.scene.objects if obj.type == 'MESH'}
    changed_objects = [name for name in before if before[name] != after[name]]
    protected = {name: value for name, value in before.items() if name.startswith(('Floor', 'Ceiling'))}
    assert all(after[name] == value for name, value in protected.items())
    assert all(name.startswith('Arcade_') or name == 'Kino_Stage__NeroMarble' for name in changed_objects)
    return {'edited_components': len(edits), 'changed_objects': changed_objects,
            'floor_and_ceiling_unchanged': True, 'protected_mesh_sha256': protected,
            'untouched_arcade_and_stage_components': untouched, 'edits': edits}


def export_fbx(path):
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
        axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True, mesh_smooth_type='FACE',
        use_tspace=True, add_leaf_bones=False, bake_anim=False, path_mode='AUTO')


if __name__ == '__main__':
    source = os.path.join(ROOT, 'SourceArt/KinoRotunda/KinoRotunda.blend')
    target = os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx')
    reports = os.path.join(ROOT, 'Artifacts/KinoRotunda/ColumnJoints')
    os.makedirs(reports, exist_ok=True)
    backup = os.path.join(reports, 'Before-' + datetime.now().strftime('%Y%m%d-%H%M%S'))
    os.makedirs(backup)
    for path in (source, target, target + '.meta'):
        shutil.copy2(path, backup)
    bpy.ops.wm.open_mainfile(filepath=source)
    result = repair_columns()
    result['backup'] = backup
    result['vertices'] = sum(len(o.data.vertices) for o in bpy.context.scene.objects if o.type == 'MESH')
    result['triangles'] = sum(len(p.vertices)-2 for o in bpy.context.scene.objects if o.type == 'MESH' for p in o.data.polygons)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=source)
    export_fbx(target)
    with open(os.path.join(reports, 'repair-report.json'), 'w') as stream:
        json.dump(result, stream, indent=2)
    print(json.dumps({k: v for k, v in result.items() if k not in ('edits', 'protected_mesh_sha256', 'changed_objects')}), flush=True)
