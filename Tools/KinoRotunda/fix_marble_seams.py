"""Continuous UV0 for round columns/arches and clearance behind the arch soffit.

Run after recess_arcade. Retains topology, UV1, object names, material slots,
and every approved column/arch position. Only the concealed wall opening moves.
"""
import bpy
import math
import json
import os
import shutil
import sys
from datetime import datetime
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
from repair_column_joints import ROOT, components, digest, export_fbx
from align_pilasters import frame

SCALE = .20
WALL_OPENING_RADIUS = 1.145


def fix_marble_seams():
    changed = []
    for bay in range(2, 27):
        obj = bpy.data.objects[f'Arcade_{bay:02d}__IvoryMarble']
        if obj.get('marble_seams_version') == 1:
            continue
        assert obj.get('arcade_depth_version') == 2, 'Run recess_arcade first'
        mesh = obj.data
        groups = list(components(mesh))
        assert len(groups) == 16
        matrix = frame(bay*2*math.pi/28)
        inverse = matrix.inverted()
        local = [inverse @ vertex.co for vertex in mesh.vertices]
        uv = mesh.uv_layers['UVMap']
        membership = {index: component for component, group in enumerate(groups) for index in group}
        face_kind = {}

        # The wall used the same nominal inner radius as the independent arch,
        # sampled at different angles. Its reveal cut through the arch soffit.
        # Place that reveal inside the solid 14 cm arch band instead.
        for index in groups[0]:
            point = local[index]
            if point.z < 6.61:
                point.z = 4.86 + math.sqrt(max(0.0, WALL_OPENING_RADIUS**2-point.x**2))
                mesh.vertices[index].co = matrix @ point

        for poly in mesh.polygons:
            component = membership[poly.vertices[0]]
            points = [local[i] for i in poly.vertices]
            if component in (2, 4):
                side = -1 if component == 2 else 1
                centre = Vector((side*1.095, 14.64, 0))
                if len(poly.vertices) > 4:
                    # The end caps are concealed in the floor/capital.
                    poly.use_smooth = False
                    for loop in poly.loop_indices:
                        p = local[mesh.loops[loop].vertex_index] - centre
                        uv.data[loop].uv = (p.x, p.y)
                    face_kind[poly.index] = 'column_end'
                    continue
                # One complete tile per turn makes the rear wrap periodic.
                # Axial density matches the middle shaft circumference.
                circumference = 2*math.pi*((.125+.106)*.5*1.50)
                turns = [.5+math.atan2(p.x-centre.x, -(p.y-centre.y))/(2*math.pi) for p in points]
                if max(turns)-min(turns) > .5:
                    turns = [value+1 if value < .5 else value for value in turns]
                for loop, turn, point in zip(poly.loop_indices, turns, points):
                    uv.data[loop].uv = (turn, point.z/circumference)
                poly.use_smooth = True
                face_kind[poly.index] = 'column_side'
            elif component == 1:
                # Unroll the four-sided arch section as one continuous ribbon.
                # The single section seam sits on its buried outer rear edge.
                radii = [math.hypot(p.x, p.z-4.86) for p in points]
                inner = all(abs(r-1.025) < .00001 for r in radii)
                outer = all(abs(r-1.165) < .00001 for r in radii)
                front = all(abs(p.y-14.46) < .00001 for p in points)
                back = all(abs(p.y-14.82) < .00001 for p in points)
                if inner or outer or front or back:
                    for loop, p, radius in zip(poly.loop_indices, points, radii):
                        is_inner = abs(radius-1.025) < .00001
                        is_front = abs(p.y-14.46) < .00001
                        cross = (.50 if is_front else .86) if is_inner else (.36 if is_front else (1.0 if back else 0.0))
                        angle = math.atan2(max(0.0, p.z-4.86), p.x)
                        uv.data[loop].uv = (1.095*angle*SCALE, cross*SCALE)
                    poly.use_smooth = inner or outer
                    face_kind[poly.index] = 'arch_inner' if inner else 'arch_outer' if outer else 'arch_front' if front else 'arch_back'
                else:
                    for loop, p in zip(poly.loop_indices, points):
                        uv.data[loop].uv = (p.x*SCALE, p.y*SCALE)
                    poly.use_smooth = False
                    face_kind[poly.index] = 'arch_end'
        # Keep the arch's moulding edges crisp while smoothing its curved faces.
        edge_faces = {}
        for poly in mesh.polygons:
            if poly.index in face_kind:
                for key in poly.edge_keys:
                    edge_faces.setdefault(tuple(sorted(key)), []).append(face_kind[poly.index])
        for edge in mesh.edges:
            kinds = edge_faces.get(tuple(sorted(edge.vertices)), [])
            if kinds:
                edge.use_edge_sharp = len(set(kinds)) > 1
        mesh.update()
        obj['marble_seams_version'] = 1
        obj['marble_column_uv'] = 'cylindrical, one periodic wrap, rear seam'
        obj['marble_arch_uv'] = 'continuous arch-length / section-perimeter strip'
        obj['wall_opening_radius_m'] = WALL_OPENING_RADIUS
        changed.append(obj.name)
    return changed


if __name__ == '__main__':
    source = os.path.join(ROOT, 'SourceArt/KinoRotunda/KinoRotunda.blend')
    target = os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx')
    output = os.path.join(ROOT, 'Artifacts/KinoRotunda/MarbleSeams')
    os.makedirs(output, exist_ok=True)
    backup = os.path.join(output, 'Before-'+datetime.now().strftime('%Y%m%d-%H%M%S'))
    os.makedirs(backup)
    for path in (source, target, target+'.meta'):
        shutil.copy2(path, backup)
    bpy.ops.wm.open_mainfile(filepath=source)
    original = {o.name: digest(o.data) for o in bpy.context.scene.objects if o.type == 'MESH'}
    changed = fix_marble_seams()
    assert len(changed) == 25
    assert all(digest(bpy.data.objects[name].data) == old for name, old in original.items() if name not in changed)
    assert fix_marble_seams() == []
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.wm.save_as_mainfile(filepath=source)
    export_fbx(target)
    report = {'backup': backup, 'changed_meshes': changed, 'columns': 50, 'arches': 25,
              'wall_opening_radius_m': WALL_OPENING_RADIUS, 'idempotent': True,
              'changes': ['continuous cylindrical column UV0', 'continuous arch strip UV0',
                          'smooth curved arch normals with crisp rims', 'wall/arch intrados overlap removed']}
    with open(os.path.join(output, 'repair-report.json'), 'w') as stream:
        json.dump(report, stream, indent=2)
    print(json.dumps(report), flush=True)
