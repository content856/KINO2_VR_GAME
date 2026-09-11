"""Reference-shaped lottery: stepped plinth, brass cage and a separate glass hemisphere.

Run after separate_balls. Retain all 14 draw balls' mesh data, UVs, materials,
names and centred pivots; only arrange their independent transforms in the dome.
The Blender source contains semantic materials only. Unity supplies the glass.
"""
import bpy
import bmesh
import math
import os
import sys
import json
import shutil
from datetime import datetime
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(__file__))
from repair_column_joints import ROOT, digest, export_fbx

CY, EQUATOR, RADIUS = 10.67, .67, .625
PREFIX = 'Kino_Armillary__'


def refine_lottery():
    buckets = {}

    def emit(material, vertices, faces, smooth=True):
        verts, polys, smoothing = buckets.setdefault(material, ([], [], []))
        offset = len(verts)
        verts.extend(vertices)
        polys.extend(tuple(offset+i for i in face) for face in faces)
        smoothing.extend([smooth]*len(faces))

    def lathe(material, profile, xy=(0, CY), segments=96):
        vertices = [(xy[0]+r*math.cos(i*math.tau/segments),
                     xy[1]+r*math.sin(i*math.tau/segments), z)
                    for r, z in profile for i in range(segments)]
        faces = [(j*segments+i, j*segments+(i+1)%segments,
                  (j+1)*segments+(i+1)%segments, (j+1)*segments+i)
                 for j in range(len(profile)-1) for i in range(segments)]
        emit(material, vertices, faces)

    def tube(material, points, radius, closed=False, sides=8):
        vertices, faces = [], []
        points = [Vector(p) for p in points]
        for i, point in enumerate(points):
            prev = points[(i-1)%len(points)] if closed or i else point
            nex = points[(i+1)%len(points)] if closed or i < len(points)-1 else point
            tangent = (nex-prev).normalized()
            ref = Vector((0, 0, 1)) if abs(tangent.z) < .9 else Vector((1, 0, 0))
            u = tangent.cross(ref).normalized()
            v = tangent.cross(u).normalized()
            vertices.extend(tuple(point+radius*(u*math.cos(j*math.tau/sides)+v*math.sin(j*math.tau/sides))) for j in range(sides))
        for i in range(len(points) if closed else len(points)-1):
            k = (i+1)%len(points)
            faces.extend((i*sides+j, i*sides+(j+1)%sides, k*sides+(j+1)%sides, k*sides+j) for j in range(sides))
        if not closed:
            faces += [tuple(reversed(range(sides))), tuple((len(points)-1)*sides+j for j in range(sides))]
        emit(material, vertices, faces)

    def ring(material, radius, height, thickness):
        tube(material, [(radius*math.cos(i*math.tau/96), CY+radius*math.sin(i*math.tau/96), height) for i in range(96)], thickness, True)

    # Three broad, bevelled navy-marble steps with fine polished gold reveals.
    for radius, bottom, top in ((1.02, .015, .16), (.90, .16, .32), (.79, .32, .48)):
        lathe('NeroMarble', [(0, bottom), (radius-.022, bottom), (radius, bottom+.02),
              (radius, top-.018), (radius-.018, top), (0, top)])
        ring('BrushedGold', radius-.002, top-.025, .012)
        ring('BrushedGold', radius-.012, bottom+.015, .007)
    # Warm, very narrow inlays integrated in the plinth rather than extra lights.
    for radius, height in ((1.017, .12), (.897, .285)):
        ring('WarmLED', radius, height, .0035)
    lathe('BronzeShadow', [(0, .478), (.66, .478), (.66, .52), (.60, .545), (0, .545)])
    lathe('NeroMarble', [(0, .54), (.575, .54), (.575, .63), (.56, .65), (0, .65)])
    ring('BrushedGold', .573, .635, .012)

    # Raised socket and evenly spaced short balusters around the glass seating.
    lathe('BronzeShadow', [(.604, .636), (.644, .636), (.644, .67), (.604, .67), (.604, .636)])
    ring('BrushedGold', .636, .66, .010)
    ring('BrushedGold', .698, .508, .012)
    ring('BrushedGold', .698, .685, .010)
    for i in range(20):
        a = i*math.tau/20
        xy = (.698*math.cos(a), CY+.698*math.sin(a))
        lathe('BrushedGold', [(0, .51), (.020, .51), (.022, .535), (.010, .552),
              (.010, .646), (.019, .66), (.019, .683), (0, .683)], xy, 12)

    # A fine six-rib brass skeleton just inside the clear outer hemisphere.
    for i in range(6):
        a = i*math.pi/3
        points = [(.607*math.cos(t)*math.cos(a), CY+.607*math.cos(t)*math.sin(a), EQUATOR+.607*math.sin(t))
                  for t in (j*math.pi/2/40 for j in range(41))]
        tube('BrushedGold', points, .009)
    ring('BrushedGold', .607, EQUATOR+.012, .008)
    # Subtle rear cross-latitude and small crown fitting echo the reference.
    tube('BrushedGold', [(.607*math.cos(.50)*math.cos(a), CY+.607*math.cos(.50)*math.sin(a), EQUATOR+.607*math.sin(.50))
                         for a in (j*math.pi/64 for j in range(65))], .006)
    lathe('BrushedGold', [(0, 1.267), (.038, 1.267), (.038, 1.29), (.019, 1.31), (.026, 1.328), (.008, 1.355), (0, 1.36)], segments=32)

    # Central spindle and three curved mixing fingers; separate balls surround it.
    lathe('BrushedGold', [(0, .65), (.082, .65), (.082, .67), (.031, .70), (.018, .99), (.030, 1.008), (0, 1.027)], segments=32)
    for i in range(3):
        a = i*math.tau/3+math.pi/6
        points = []
        for j in range(25):
            t = j/24
            r = .025+.12*math.sin(math.pi*t)
            points.append((r*math.cos(a), CY+r*math.sin(a), .77+.20*t))
        tube('BrushedGold', points, .012)

    # One open-bottom, outward-facing hemisphere: single surface avoids
    # double transparency / sorting artefacts in the realtime URP renderer.
    segments, rows = 96, 32
    vertices = [(RADIUS*math.cos(j*math.pi/2/rows)*math.cos(i*math.tau/segments),
                 CY+RADIUS*math.cos(j*math.pi/2/rows)*math.sin(i*math.tau/segments),
                 EQUATOR+RADIUS*math.sin(j*math.pi/2/rows))
                for j in range(rows) for i in range(segments)]
    faces = [(j*segments+i, j*segments+(i+1)%segments,
              (j+1)*segments+(i+1)%segments, (j+1)*segments+i)
             for j in range(rows-1) for i in range(segments)]
    pole = len(vertices)
    vertices.append((0, CY, EQUATOR+RADIUS))
    faces.extend(((rows-1)*segments+i, (rows-1)*segments+(i+1)%segments, pole) for i in range(segments))
    emit('LotteryGlass', vertices, faces)

    for material, (vertices, faces, smoothing) in buckets.items():
        name = PREFIX+material
        obj = bpy.data.objects.get(name)
        mesh = bpy.data.meshes.new(name+'_Mesh')
        mesh.from_pydata(vertices, [], faces)
        mesh.update()
        if obj:
            old = obj.data
            obj.data = mesh
            if old.users == 0:
                bpy.data.meshes.remove(old)
        else:
            obj = bpy.data.objects.new(name, mesh)
            bpy.context.scene.collection.objects.link(obj)
        mat = bpy.data.materials.get(material)
        if not mat:
            mat = bpy.data.materials.new(material)
            mat.use_nodes = False
            mat.diffuse_color = (.75, .90, 1.0, .05)
        mesh.materials.append(mat)
        uv = mesh.uv_layers.new(name='UVMap')
        for poly, smooth in zip(mesh.polygons, smoothing):
            poly.use_smooth = smooth
            axis = max(range(3), key=lambda k: abs(poly.normal[k]))
            for li in poly.loop_indices:
                v = mesh.vertices[mesh.loops[li].vertex_index].co
                uv.data[li].uv = (v.x*.2, v.y*.2) if axis == 2 else ((v.x*.2, v.z*.2) if axis == 1 else (v.y*.2, v.z*.2))
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        mesh.uv_layers.new(name='LightmapUV')
        mesh.uv_layers.active_index = 1
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.025, scale_to_bounds=True)
        bpy.ops.object.mode_set(mode='OBJECT')
        mesh.uv_layers.active_index = 0
        obj['surface'] = material
        obj['units'] = 'metres'
        obj['lightmap_uv'] = 'LightmapUV'
        obj['lottery_version'] = 1
        if material == 'BrushedGold':
            obj['balls_separated_version'] = 1

    balls = sorted((o for o in bpy.data.objects if o.name.startswith('Ball_Armillary_')), key=lambda o: o.name)
    assert len(balls) == 14
    for i, ball in enumerate(balls):
        if i < 10:
            a, r, z = i*math.tau/10+.12, .405, .752+(i%3)*.014
        else:
            a, r, z = (i-10)*math.pi/2+.25, .235, .91+(i%2)*.025
        ball.matrix_world = Matrix.Translation((r*math.cos(a), CY+r*math.sin(a), z))
    bpy.context.view_layer.update()
    for ball in balls:
        for vertex in ball.data.vertices:
            p = ball.matrix_world @ vertex.co - Vector((0, CY, EQUATOR))
            assert p.z > 0 and p.length < RADIUS-.009, (ball.name, tuple(p))
    return {'draw_balls': len(balls), 'glass_radius_m': RADIUS, 'glass_equator_m': EQUATOR,
            'base_diameter_m': 2.04, 'overall_height_m': 1.36,
            'machine_meshes': len(buckets), 'triangles': sum(len(p)-2 for _, faces, _ in buckets.values() for p in faces),
            'independent_balls': True, 'all_balls_inside_glass': True}


if __name__ == '__main__':
    source = os.path.join(ROOT, 'SourceArt/KinoRotunda/KinoRotunda.blend')
    target = os.path.join(ROOT, 'Assets/KinoRotunda/Models/KinoRotunda.fbx')
    output = os.path.join(ROOT, 'Artifacts/KinoRotunda/Lottery')
    backup = os.path.join(output, 'Before-'+datetime.now().strftime('%Y%m%d-%H%M%S'))
    os.makedirs(backup)
    for path in (source, target, target+'.meta'):
        shutil.copy2(path, backup)
    bpy.ops.wm.open_mainfile(filepath=source)
    meshes = {o.name: digest(o.data) for o in bpy.context.scene.objects if o.type == 'MESH' and not o.name.startswith(PREFIX)}
    result = refine_lottery()
    assert all(digest(bpy.data.objects[name].data) == value for name, value in meshes.items())
    first = {o.name: digest(o.data) for o in bpy.context.scene.objects if o.type == 'MESH'}
    refine_lottery()
    assert all(digest(bpy.data.objects[name].data) == value for name, value in first.items())
    result.update(backup=backup, all_other_meshes_and_ball_geometry_unchanged=True, idempotent=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=source)
    export_fbx(target)
    with open(os.path.join(output, 'geometry-validation.json'), 'w') as stream:
        json.dump(result, stream, indent=2)
    print(json.dumps(result), flush=True)
