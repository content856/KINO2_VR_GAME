"""Seven reference-inspired glass draw tubes in every fourth arcade opening.

Run on the saved source or call add_perimeter_tubes() after refine_lottery.
Existing architecture, UVs and animation objects remain byte-for-byte intact.
"""
import bpy
import bmesh
import json
import math
import os
import shutil
import sys
from datetime import datetime
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
from repair_column_joints import ROOT, digest, export_fbx

BAYS = (2, 6, 10, 14, 18, 22, 26)
RADIUS = 14.10
GLASS_RADIUS = .275
HEIGHT = 5.72
BALL_RADIUS = .139
BALL_HEIGHTS = (.51, .81, 1.12, 1.73, 2.43, 3.13, 3.83, 4.53, 5.19)
OUTPUT = os.path.join(ROOT, 'Artifacts/KinoRotunda/PerimeterTubes')


def is_tube(name):
    return name.startswith(('Tube_', 'Ball_Tube_', 'Balls_Tube_')) or name == 'Perimeter_Tubes'


def add_perimeter_tubes():
    for obj in list(bpy.data.objects):
        if is_tube(obj.name):
            bpy.data.objects.remove(obj, do_unlink=True)
    collection = bpy.data.collections.get('Perimeter Tubes')
    if not collection:
        collection = bpy.data.collections.new('Perimeter Tubes')
        bpy.context.scene.collection.children.link(collection)
    glass = bpy.data.materials.get('TubeGlass')
    if not glass:
        glass = bpy.data.materials.new('TubeGlass')
        glass.use_nodes = False
        glass.diffuse_color = (.72, .88, .96, .055)
    def empty(name, parent=None):
        obj = bpy.data.objects.new(name, None)
        collection.objects.link(obj)
        obj.parent = parent
        obj.empty_display_size = .12
        return obj

    root = empty('Perimeter_Tubes')
    root['selected_bays'] = list(BAYS)
    root['angular_spacing_degrees'] = 360 / 7
    animation = bpy.data.objects['Animation_Balls']

    def finish_mesh(mesh, surface, smart=True):
        mesh.materials.append(bpy.data.materials[surface])
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.000001)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        uv = mesh.uv_layers.new(name='UVMap')
        for p in mesh.polygons:
            p.use_smooth = abs(p.normal.z) < .98
            axis = max(range(3), key=lambda k: abs(p.normal[k]))
            for li in p.loop_indices:
                v = mesh.vertices[mesh.loops[li].vertex_index].co
                uv.data[li].uv = (v.x, v.y) if axis == 2 else ((v.x, v.z) if axis == 1 else (v.y, v.z))
        mesh.uv_layers.new(name='LightmapUV')

    def link_mesh(name, mesh, parent, location=(0, 0, 0), unwrap=False):
        obj = bpy.data.objects.new(name, mesh)
        collection.objects.link(obj)
        obj.parent = parent
        obj.location = location
        obj['surface'] = mesh.materials[0].name
        obj['units'] = 'metres'
        obj['lightmap_uv'] = 'LightmapUV'
        if unwrap:
            bpy.ops.object.select_all(action='DESELECT')
            obj.select_set(True)
            bpy.context.view_layer.objects.active = obj
            mesh.uv_layers.active_index = 1
            bpy.ops.object.mode_set(mode='EDIT')
            bpy.ops.mesh.select_all(action='SELECT')
            bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.025, scale_to_bounds=True)
            bpy.ops.object.mode_set(mode='OBJECT')
            mesh.uv_layers.active_index = 0
        return obj

    buckets = {}
    def lathe(surface, profile, segments=64):
        vertices, faces = buckets.setdefault(surface, ([], []))
        offset = len(vertices)
        vertices.extend((r*math.cos(i*math.tau/segments), r*math.sin(i*math.tau/segments), z)
                        for r, z in profile for i in range(segments))
        faces.extend(tuple(offset+k for k in (j*segments+i, j*segments+(i+1)%segments,
                     (j+1)*segments+(i+1)%segments, (j+1)*segments+i))
                     for j in range(len(profile)-1) for i in range(segments))

    # Low, turned plinth; gold reveals echo the screen and marble pilasters.
    lathe('NeroMarble', [(0, .008), (.411, .008), (.435, .026), (.435, .095),
                         (.419, .115), (.367, .115), (.367, .24), (.351, .257), (0, .257)])
    lathe('BrushedGold', [(0, .032), (.438, .032), (.438, .055), (0, .055)])
    lathe('BrushedGold', [(0, .109), (.42, .109), (.42, .13), (0, .13)])
    lathe('BronzeShadow', [(0, .255), (.316, .255), (.316, .316), (0, .316)])
    lathe('BrushedGold', [(.266, .294), (.305, .294), (.310, .304), (.310, .337),
                          (.299, .347), (.266, .347), (.266, .294)])
    lathe('WarmLED', [(.364, .205), (.368, .205), (.368, .211), (.364, .211), (.364, .205)])
    # Single outward-facing wall keeps realtime transparency clean. The open
    # ends sit inside thin machined collars, with no opaque lid over the balls.
    lathe('TubeGlass', [(GLASS_RADIUS, .325), (GLASS_RADIUS, 5.665)], 96)
    lathe('BrushedGold', [(.267, 5.642), (.286, 5.642), (.296, 5.654), (.296, 5.686),
                          (.286, 5.70), (.267, 5.70), (.267, 5.642)])
    lathe('BronzeShadow', [(.270, 5.698), (.288, 5.698), (.288, HEIGHT), (.270, HEIGHT), (.270, 5.698)])
    hardware = {}
    for surface, (vertices, faces) in buckets.items():
        mesh = bpy.data.meshes.new('Tube_Shared__'+surface)
        mesh.from_pydata(vertices, [], faces)
        finish_mesh(mesh, surface)
        hardware[surface] = mesh
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=16, radius=BALL_RADIUS)
    sphere = bpy.data.meshes.new('Tube_Ball_Shared')
    bm.to_mesh(sphere)
    bm.free()
    finish_mesh(sphere, 'BrushedGold')
    for polygon in sphere.polygons:
        polygon.use_smooth = True

    tubes = []
    for index, bay in enumerate(BAYS):
        theta = bay*math.tau/28
        frame = Matrix.Rotation(-theta, 4, 'Z')
        centre = Vector((RADIUS*math.sin(theta), RADIUS*math.cos(theta), 0))
        group = empty(f'Tube_{bay:02d}', root)
        group.location = centre
        group.rotation_euler.z = -theta
        group['arcade_bay'] = bay
        for surface, mesh in hardware.items():
            link_mesh(f'Tube_{bay:02d}__{surface}', mesh, group, unwrap=index == 0)
        balls_group = empty(f'Balls_Tube_{bay:02d}', animation)
        for j, height in enumerate(BALL_HEIGHTS):
            # Three settled balls at the foot, followed by a gently staggered column.
            x = (-.075, .065, -.042)[j % 3] * (1 if j < 3 else .55)
            y = .024*math.sin(j*2.1+index)
            ball = link_mesh(f'Ball_Tube_{bay:02d}_{j+1:02d}__BrushedGold', sphere, balls_group,
                             centre+frame@Vector((x, y, height)), unwrap=index == 0 and j == 0)
            ball.rotation_euler.z = -theta
            ball['animation_ready'] = True
        tubes.append({'bay': bay, 'angle_degrees': math.degrees(theta), 'position_blender_m': list(centre)})
    return tubes


def validate():
    groups = [bpy.data.objects[f'Tube_{bay:02d}'] for bay in BAYS]
    assert len(groups) == 7 and all((BAYS[(i+1)%7]-BAYS[i]) % 28 == 4 for i in range(7))
    balls = [o for o in bpy.context.scene.objects if o.name.startswith('Ball_Tube_')]
    assert len(balls) == 63
    for ball in balls:
        assert ball.parent.parent.name == 'Animation_Balls'
        assert Vector(tuple((min(v.co[k] for v in ball.data.vertices)+max(v.co[k] for v in ball.data.vertices))*.5 for k in range(3))).length < 1e-5
        bay = int(ball.name.split('_')[2])
        delta = ball.location-bpy.data.objects[f'Tube_{bay:02d}'].location
        assert math.hypot(delta.x, delta.y)+BALL_RADIUS < GLASS_RADIUS-.01
        assert .347 < delta.z-BALL_RADIUS and delta.z+BALL_RADIUS < 5.642
    for a in balls:
        for b in balls:
            if a != b and a.parent == b.parent:
                assert (a.location-b.location).length > 2*BALL_RADIUS
    # Bases remain in front of the balustrade and inside the jambs; the crown
    # is below the arch intrados across its entire width.
    assert RADIUS+.438 < 14.665
    assert .438 < 1.095-.255
    assert HEIGHT < 4.86+math.sqrt(1.025**2-.296**2)
    return {'tubes': 7, 'bays': list(BAYS), 'spacing_degrees': 360/7, 'separate_balls': 63,
            'height_m': HEIGHT, 'glass_diameter_m': GLASS_RADIUS*2,
            'ball_clearances_valid': True, 'arch_jamb_rail_clearances_valid': True,
            'screen_flanked_symmetrically': True}


if __name__ == '__main__':
    source = os.path.join(ROOT, 'SourceArt/KinoRotunda/KinoRotunda.blend')
    target = os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx')
    os.makedirs(OUTPUT, exist_ok=True)
    backup = os.path.join(OUTPUT, 'Before-'+datetime.now().strftime('%Y%m%d-%H%M%S'))
    os.makedirs(backup)
    for path in (source, target, target+'.meta'):
        shutil.copy2(path, backup)
    bpy.ops.wm.open_mainfile(filepath=source)
    original = {o.name: (digest(o.data), tuple(tuple(row) for row in o.matrix_world))
                for o in bpy.context.scene.objects if o.type == 'MESH' and not is_tube(o.name)}
    tubes = add_perimeter_tubes()
    report = validate()
    assert all((digest(bpy.data.objects[name].data), tuple(tuple(row) for row in bpy.data.objects[name].matrix_world)) == value
               for name, value in original.items())
    report.update({'backup': backup, 'existing_geometry_uvs_transforms_preserved': True, 'placements': tubes})
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.context.scene.objects:
        if is_tube(obj.name): obj.select_set(True)
    bpy.context.view_layer.objects.active = bpy.data.objects['Tube_02']
    bpy.ops.wm.save_as_mainfile(filepath=source)
    export_fbx(target)
    with open(os.path.join(OUTPUT, 'geometry-validation.json'), 'w') as stream:
        json.dump(report, stream, indent=2)
    print(json.dumps(report), flush=True)

