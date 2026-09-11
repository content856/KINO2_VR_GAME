"""Extract every rounded gold ornament into an independently animatable object.

Run last in the geometry pipeline. Original shapes, UV0/UV1 and materials are
retained. Each ball has a centred pivot and a stable, descriptive object name.
"""
import bpy
import bmesh
import json
import os
import shutil
import sys
from datetime import datetime
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
from repair_column_joints import ROOT, components, digest, export_fbx
from remove_rail_caps import retained_data


def specifications():
    for bay in range(2, 27):
        prefix = f'Arcade_{bay:02d}'
        parts = [(i, f'{prefix}_Base_{i-10:02d}', 48) for i in range(11, 16)]
        parts += [(20, f'{prefix}_Capital_Left_01', 30), (21, f'{prefix}_Capital_Left_02', 30),
                  (26, f'{prefix}_Capital_Right_01', 30), (27, f'{prefix}_Capital_Right_02', 30)]
        yield prefix+'__BrushedGold', prefix, 28, parts
    yield 'Stage_Return_Left__BrushedGold', 'Stage_Return_Left', 15, [
        (i, f'Stage_Return_Left_Base_{i-9:02d}', 48) for i in range(10, 15)]
    yield 'Kino_Stage__BrushedGold', 'Stage', 67, [
        (i, f'Stage_{i-18:02d}', 36) for i in range(19, 67)]
    yield 'Kino_Armillary__BrushedGold', 'Armillary', 24, [
        (i, f'Armillary_{i-9:02d}', 48) for i in range(10, 24)]


def empty(name, parent=None):
    obj = bpy.data.objects.get(name)
    if obj:
        assert obj.type == 'EMPTY' and obj.parent == parent
        return obj
    obj = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = parent
    obj.empty_display_size = .08
    return obj


def keep_vertices(mesh, indices):
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in indices], context='VERTS')
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


def separate_balls():
    root = empty('Animation_Balls')
    created = []
    for source_name, group_name, expected_count, parts in specifications():
        source = bpy.data.objects[source_name]
        if source.get('balls_separated_version') == 1:
            assert all('Ball_'+label+'__BrushedGold' in bpy.data.objects for _, label, _ in parts)
            continue
        groups = list(components(source.data))
        assert len(groups) == expected_count, (source_name, len(groups))
        group = empty('Balls_'+group_name, root)
        extracted = set()
        for component, label, expected_vertices in parts:
            indices = set(groups[component])
            assert len(indices) == expected_vertices, (source_name, component)
            assert not extracted.intersection(indices)
            extracted.update(indices)
            name = 'Ball_'+label+'__BrushedGold'
            assert name not in bpy.data.objects, name
            mesh = source.data.copy()
            mesh.name = name
            keep_vertices(mesh, indices)
            # Exact shape, face winding, UVs and material assignments survive extraction.
            assert retained_data(mesh, set(range(len(mesh.vertices)))) == retained_data(source.data, indices)
            original_points = [source.matrix_world @ v.co for v in mesh.vertices]
            centre = Vector(tuple((min(v.co[axis] for v in mesh.vertices)+max(v.co[axis] for v in mesh.vertices))*.5
                                  for axis in range(3)))
            mesh.transform(Matrix.Translation(-centre))
            ball = bpy.data.objects.new(name, mesh)
            bpy.context.scene.collection.objects.link(ball)
            ball.parent = group
            ball.matrix_world = source.matrix_world @ Matrix.Translation(centre)
            ball['surface'] = 'BrushedGold'
            ball['units'] = 'metres'
            ball['lightmap_uv'] = 'LightmapUV'
            ball['animation_ready'] = True
            ball['source_mesh'] = source_name
            ball['source_component'] = component
            assert all((ball.matrix_world @ v.co-old).length < 3e-6 for v, old in zip(mesh.vertices, original_points))
            created.append({'object': name, 'group': group.name, 'source': source_name,
                            'component': component, 'pivot_blender_m': list(ball.matrix_world.translation),
                            'vertices': len(mesh.vertices)})
        remaining = set(range(len(source.data.vertices)))-extracted
        original_remaining = retained_data(source.data, remaining)
        keep_vertices(source.data, remaining)
        assert retained_data(source.data, set(range(len(source.data.vertices)))) == original_remaining
        source['balls_separated_version'] = 1
    bpy.ops.object.select_all(action='SELECT')
    return created


def counts():
    meshes = [o.data for o in bpy.context.scene.objects if o.type == 'MESH']
    return {'vertices': sum(len(m.vertices) for m in meshes),
            'faces': sum(len(m.polygons) for m in meshes),
            'triangles': sum(len(p.vertices)-2 for m in meshes for p in m.polygons)}


if __name__ == '__main__':
    source = os.path.join(ROOT, 'SourceArt/KinoRotunda/KinoRotunda.blend')
    target = os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx')
    output = os.path.join(ROOT, 'Artifacts/KinoRotunda/SeparateBalls')
    backup = os.path.join(output, 'Before-'+datetime.now().strftime('%Y%m%d-%H%M%S'))
    os.makedirs(backup)
    for path in (source, target, target+'.meta'):
        shutil.copy2(path, backup)
    bpy.ops.wm.open_mainfile(filepath=source)
    original = {o.name: digest(o.data) for o in bpy.context.scene.objects if o.type == 'MESH'}
    original_counts = counts()
    created = separate_balls()
    assert len(created) == 292
    assert counts() == original_counts, 'Geometry must be moved, never duplicated or lost'
    changed = {item['source'] for item in created}
    assert all(digest(bpy.data.objects[name].data) == old for name, old in original.items() if name not in changed)
    assert separate_balls() == []
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=source)
    export_fbx(target)
    with open(target+'.meta', 'rb') as current, open(os.path.join(backup, 'KinoRotunda.fbx.meta'), 'rb') as old:
        assert current.read() == old.read()
    report = {'backup': backup, 'balls': len(created), 'groups': 28, 'root': 'Animation_Balls',
              'counts': original_counts, 'geometry_uvs_materials_preserved': True,
              'no_duplicated_or_missing_geometry': True, 'centred_pivots': True,
              'unity_metadata_unchanged': True, 'idempotent': True, 'objects': created}
    with open(os.path.join(output, 'repair-report.json'), 'w') as stream:
        json.dump(report, stream, indent=2)
    print(json.dumps({k:v for k,v in report.items() if k != 'objects'}), flush=True)
