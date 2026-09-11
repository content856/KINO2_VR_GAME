"""Blender 4.1: geometry-only KINO rotunda, metres, UV0 + packed UV1.
Run: blender --background --factory-startup --python Tools/KinoRotunda/build_rotunda.py
No lights, cameras, texture nodes or HDRI are stored in the blend file.
Material slots are semantic export labels; Unity supplies all surface appearance.
"""
import bpy, bmesh, math, json, os
from mathutils import Vector, Matrix
from collections import defaultdict
from math import sin, cos, pi

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', 'KinoRotunda', 'Models')
SOURCE = os.path.join(ROOT, 'SourceArt', 'KinoRotunda')
REPORT = os.path.join(ROOT, 'Artifacts', 'KinoRotunda')
for p in (OUT, SOURCE, REPORT): os.makedirs(p, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for data in list(bpy.data.materials): bpy.data.materials.remove(data)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
scene.world = None
R, H, N = 14.0, 7.1, 28
STEP = 2*pi/N
BUCKETS = {}
MATS = {}
labels = {'NeroMarble':(.08,.09,.12,1), 'IvoryMarble':(.7,.67,.59,1),
          'BrushedGold':(.6,.4,.13,1), 'BronzeShadow':(.15,.10,.04,1),
          'WarmLED':(1,.7,.25,1), 'Screen':(.04,.06,.08,1)}
for name, color in labels.items():
    m = bpy.data.materials.new(name)
    m.use_nodes = False
    m.diffuse_color = color
    MATS[name] = m

def bucket(group, mat):
    key=(group,mat)
    if key not in BUCKETS: BUCKETS[key]={'v':[], 'f':[], 's':[], 'uv':[]}
    return BUCKETS[key]

def emit(group, mat, verts, faces, smooth=False, matrix=None, uvs=None):
    b=bucket(group,mat); offset=len(b['v'])
    b['v'].extend([tuple(matrix @ Vector(v)) if matrix else tuple(v) for v in verts])
    b['f'].extend([tuple(i+offset for i in f) for f in faces])
    b['s'].extend([smooth]*len(faces))
    b['uv'].extend(uvs if uvs else [None]*len(faces))

def frame(theta):
    # Local x tangent, local y outward, local z up. Right-handed basis.
    return Matrix(((cos(theta),sin(theta),0,0),(-sin(theta),cos(theta),0,0),(0,0,1,0),(0,0,0,1)))

box_cache={}
def box(group,mat,loc,size,bevel=0,matrix=None):
    key=tuple(size)+(bevel,)
    if key not in box_cache:
        bm=bmesh.new(); bmesh.ops.create_cube(bm,size=1)
        for v in bm.verts:
            v.co.x*=size[0]; v.co.y*=size[1]; v.co.z*=size[2]
        if bevel>0:
            bmesh.ops.bevel(bm,geom=list(bm.edges),offset=min(bevel,min(size)*.4),segments=2,affect='EDGES')
        bm.verts.ensure_lookup_table(); bm.verts.index_update()
        box_cache[key]=([tuple(v.co) for v in bm.verts],[tuple(v.index for v in f.verts) for f in bm.faces])
        bm.free()
    v,f=box_cache[key]
    transform=Matrix.Translation(Vector(loc))
    if matrix: transform=matrix@transform
    emit(group,mat,v,f,matrix=transform)

def lathe(group,mat,profile,loc=(0,0,0),segments=24,matrix=None):
    verts=[]; faces=[]
    for r,z in profile:
        verts.extend([(loc[0]+r*cos(i*2*pi/segments),loc[1]+r*sin(i*2*pi/segments),loc[2]+z) for i in range(segments)])
    for j in range(len(profile)-1):
        for i in range(segments):
            k=(i+1)%segments
            faces.append((j*segments+i,j*segments+k,(j+1)*segments+k,(j+1)*segments+i))
    faces.extend([tuple(reversed(range(segments))),tuple((len(profile)-1)*segments+i for i in range(segments))])
    emit(group,mat,verts,faces,True,matrix)

def band(group,mat,ri,ro,z,depth=.05,start=0,end=2*pi,segments=168):
    verts=[]; faces=[]
    for i in range(segments+1):
        a=start+(end-start)*i/segments
        for r,h in ((ri,z),(ro,z),(ri,z-depth),(ro,z-depth)):
            verts.append((r*sin(a),r*cos(a),h))
    for i in range(segments):
        j=4*i;k=j+4
        faces.extend([(j,k,k+1,j+1),(j+2,j+3,k+3,k+2),(j,j+2,k+2,k),(j+1,k+1,k+3,j+3)])
    if end-start<2*pi-.001: faces.extend([(0,1,3,2),(4*segments+2,4*segments+3,4*segments+1,4*segments)])
    emit(group,mat,verts,faces)

def tube(group,mat,points,r=.02,sides=8,closed=False,matrix=None):
    verts=[];faces=[]; count=len(points)
    for i,p in enumerate(points):
        prev=Vector(points[(i-1)%count] if closed or i>0 else points[i])
        nex=Vector(points[(i+1)%count] if closed or i<count-1 else points[i])
        tangent=(nex-prev).normalized()
        ref=Vector((0,0,1)) if abs(tangent.z)<.9 else Vector((1,0,0))
        u=tangent.cross(ref).normalized(); v=tangent.cross(u).normalized()
        verts.extend([tuple(Vector(p)+r*(u*cos(j*2*pi/sides)+v*sin(j*2*pi/sides))) for j in range(sides)])
    for i in range(count if closed else count-1):
        k=(i+1)%count
        for j in range(sides):
            nj=(j+1)%sides; faces.append((i*sides+j,i*sides+nj,k*sides+nj,k*sides+j))
    emit(group,mat,verts,faces,True,matrix)

def arch(group,mat,half,thickness,spring,y,depth,matrix):
    verts=[];faces=[]; seg=24
    for i in range(seg+1):
        a=i*pi/seg
        for r,d in ((half,y-depth/2),(half+thickness,y-depth/2),(half,y+depth/2),(half+thickness,y+depth/2)):
            verts.append((r*cos(a),d,spring+r*sin(a)))
    for i in range(seg):
        j=i*4;k=j+4
        faces.extend([(j,j+1,k+1,k),(j+2,k+2,k+3,j+3),(j,k,k+2,j+2),(j+1,j+3,k+3,k+1)])
    faces.extend([(0,2,3,1),(seg*4,seg*4+1,seg*4+3,seg*4+2)])
    emit(group,mat,verts,faces,matrix=matrix)

def spandrel(group,matrix,half=1.025,spring=4.86,top=6.62):
    # Solid upper wall above the arch, with a genuinely open arched aperture.
    verts=[];faces=[];seg=32;outer=1.54
    for i in range(seg+1):
        x=-outer+2*outer*i/seg
        bottom=spring+math.sqrt(max(0,half*half-x*x)) if abs(x)<half else spring
        for y,z in ((R-.12,bottom),(R-.12,top),(R+.24,bottom),(R+.24,top)):
            verts.append((x,y,z))
    for i in range(seg):
        j=4*i;k=j+4
        faces.extend([(j,k,k+1,j+1),(j+2,j+3,k+3,k+2),(j,j+2,k+2,k),(j+1,k+1,k+3,j+3)])
    emit(group,'IvoryMarble',verts,faces,matrix=matrix)

# Weld the floor sectors into continuous surfaces for seamless lightmap charts.
for i in range(N):
    g='Floor'; a=(i-.5)*STEP;b=(i+.5)*STEP
    for ri,ro,mat in ((.001,2.32,'NeroMarble'),(2.32,6.50,'IvoryMarble'),(6.50,10.95,'NeroMarble'),(10.95,14.65,'IvoryMarble')):
        band(g,mat,ri,ro,0,.22,a,b,8)
    for r,w in ((2.32,.035),(4.05,.016),(6.49,.038),(8.25,.035),(10.94,.04),(14.10,.035)):
        band(g,'BrushedGold',r-w/2,r+w/2,.005,.016,a,b,8)
    for r in (6.54,10.90): band(g,'WarmLED',r-.009,r+.009,.008,.008,a,b,8)

# Compass rose, formed from contrasting metal triangles, without a decal.
for i in range(16):
    a=i*2*pi/16; length=1.42 if i%2==0 else .90
    tip=(sin(a)*length,cos(a)*length,.013)
    left=(sin(a-.22)*.32,cos(a-.22)*.32,.013)
    right=(sin(a+.22)*.32,cos(a+.22)*.32,.013)
    emit('Floor_Compass','BrushedGold',[(0,0,.013),left,tip],[(0,2,1)])
    emit('Floor_Compass','BronzeShadow',[(0,0,.013),tip,right],[(0,2,1)])
band('Floor_Compass','BrushedGold',.12,.16,.018,.006,segments=48)

# Continuous stepped ceiling with an open 3.6 m oculus.
for i in range(N):
    g='Ceiling';a=(i-.5)*STEP;b=(i+.5)*STEP
    for ri,ro,z in ((1.84,2.28,7.70),(2.28,3.25,7.40),(3.25,5.25,6.88),(5.25,8.80,7.03),(8.80,11.35,7.14),(11.35,14.48,7.21)):
        band(g,'NeroMarble',ri,ro,z+.20,.20,a,b,8)
    # Vertical risers close the stepped annuli; only the central oculus is open.
    for r,z0,z1 in ((2.28,7.4,7.9),(3.25,6.88,7.60),(5.25,6.88,7.23),(8.8,7.03,7.34),(11.35,7.14,7.41),(14.28,6.56,7.41)):
        band(g,'NeroMarble',r-.028,r+.028,z1,z1-z0,a,b,8)
    for r,z,w in ((1.85,7.70,.06),(2.24,7.40,.10),(2.36,7.41,.05),(3.22,6.88,.06),(5.24,6.88,.05),(8.78,7.03,.05),(11.32,7.14,.045),(14.10,6.89,.10)):
        band(g,'BrushedGold',r-w/2,r+w/2,z+.07,.09,a,b,8)
    for r,z in ((1.89,7.685),(2.30,7.385),(3.30,6.865),(5.24,6.865),(8.79,7.015),(14.02,6.705)):
        band(g,'WarmLED',r-.017,r+.017,z+.01,.018,a,b,8)
    # Cornice and fascia below the outer ceiling lip.
    for ri,ro,z,d,mat in ((13.82,14.45,6.88,.16,'NeroMarble'),(13.77,14.48,6.70,.045,'BrushedGold'),(13.83,14.43,6.64,.07,'NeroMarble'),(13.81,14.45,6.56,.025,'BrushedGold')):
        band(g,mat,ri,ro,z,d,a,b,8)

# 25 repeated bays; three bays behind the KINO display are enclosed by the stage.
lights=[]
for i in range(N):
    signed=i if i<=N//2 else i-N
    if abs(signed)<=1: continue
    theta=i*STEP; m=frame(theta);g=f'Arcade_{i:02d}'
    spandrel(g,m)
    arch(g,'IvoryMarble',1.025,.14,4.86,R-.16,.36,m)
    arch(g,'BrushedGold',1.040,.028,4.86,R-.36,.025,m)
    # Narrow pilasters and rich layered feet/capitals.
    for x in (-1.48,):
        box(g,'NeroMarble',(x,R-.08,3.26),(.80,.52,5.77),.025,m)
        box(g,'NeroMarble',(x,R-.11,.19),(1.02,.78,.26),.045,m)
        box(g,'BrushedGold',(x,R-.14,.345),(.98,.72,.065),.016,m)
        box(g,'NeroMarble',(x,R-.11,.48),(.88,.65,.18),.022,m)
        box(g,'BrushedGold',(x,R-.12,6.19),(.99,.74,.07),.014,m)
        box(g,'NeroMarble',(x,R-.10,6.30),(1.08,.78,.12),.016,m)
        box(g,'BrushedGold',(x,R-.13,6.40),(1.12,.80,.045),.010,m)
        # Thin rectangular panel inlay, oriented inward.
        for dx in (-.305,.305): box(g,'BrushedGold',(x+dx,R-.35,3.30),(.013,.012,5.34),0,m)
        for z in (.64,5.97): box(g,'BrushedGold',(x,R-.35,z),(.622,.012,.014),0,m)
        # Tall three-light brass wall sconces.
        box(g,'BronzeShadow',(x,R-.395,3.00),(.21,.10,.84),.025,m)
        for dx,z,h in ((-.12,3.03,.55),(0,3.07,.95),(.12,3.03,.55)):
            box(g,'BrushedGold',(x+dx,R-.48,z),(.040,.075,h+.10),.015,m)
            box(g,'WarmLED',(x+dx,R-.525,z),(.020,.022,h),.006,m)
        p=m@Vector((x,R-.90,3.05)); lights.append({'name':f'Sconce_{i:02d}','position':list(p),'kind':'sconce'})
        # Decorative gold beads above the base.
        for dx in (-.30,-.15,0,.15,.30):
            lathe(g,'BrushedGold',[(.025,0),(.05,.035),(.045,.08),(.015,.11)],(x+dx,R-.43,.38),12,m)
    # Slim round columns at each arch jamb, ivory shafts with gold collars.
    for x in (-1.02,1.02):
        lathe(g,'IvoryMarble',[(.18,.42),(.18,.50),(.125,.58),(.106,4.60),(.145,4.69),(.17,4.78)],(x,R-.29,0),20,m)
        for z in (.44,.56,4.64,4.77):
            lathe(g,'BrushedGold',[(.17,z),(.18,z+.025),(.17,z+.06)],(x,R-.29,0),20,m)
        box(g,'IvoryMarble',(x,R-.29,4.835),(.40,.40,.11),.022,m)
        # Simplified capital leaves.
        for dx in (-.105,.105):
            lathe(g,'BrushedGold',[(.045,0),(.070,.075),(.02,.15)],(x+dx,R-.40,4.67),10,m)
    # Outside terrace and balustrade, fully modelled openings between spindles.
    box(g,'IvoryMarble',(0,R+.03,.18),(2.10,.73,.18),.02,m)
    box(g,'IvoryMarble',(0,R+.20,1.15),(2.08,.31,.13),.025,m)
    box(g,'BrushedGold',(0,R+.20,1.226),(2.10,.33,.018),.005,m)
    for j in range(7):
        x=-.86+j*.2867
        lathe(g,'IvoryMarble',[(.07,.28),(.09,.35),(.055,.43),(.055,.51),(.085,.68),(.075,.83),(.037,.98),(.072,1.08)],(x,R+.20,0),12,m)
    # Exterior lip supports the rail visually and provides a closed floor edge.
    band(g,'IvoryMarble',14.35,14.65,.01,.23,(i-.5)*STEP,(i+.5)*STEP,8)

# Display architecture. All pieces face south, toward the centre of the hall.
g='Kino_Stage'
box(g,'NeroMarble',(0,12.52,3.35),(8.80,.62,6.25),.07)
for x in (-3.66,3.66):
    box(g,'NeroMarble',(x,12.12,3.32),(1.17,.75,6.10),.055)
    for dx in (-.49,.49): box(g,'BrushedGold',(x+dx,11.73,3.35),(.019,.018,5.80),.004)
    for z in (.45,6.26): box(g,'BrushedGold',(x,11.73,z),(1.01,.020,.025),.004)
    box(g,'BrushedGold',(x,12.06,.22),(1.43,1.0,.09),.02)
    box(g,'NeroMarble',(x,12.09,.12),(1.53,1.1,.17),.03)
box(g,'BronzeShadow',(0,12.06,3.44),(5.98,.29,3.88),.035)
for x in (-3.04,3.04): box(g,'BrushedGold',(x,11.88,3.44),(.055,.055,4.0),.01)
for z in (1.46,5.42): box(g,'BrushedGold',(0,11.88,z),(6.12,.055,.055),.01)
box(g,'NeroMarble',(0,12.08,5.91),(6.05,.45,.90),.04)
for z in (5.51,6.34):box(g,'BrushedGold',(0,11.84,z),(6.15,.035,.028),.008)
for width,y,z,d in ((9.45,12.05,.06,1.45),(9.10,12.18,.17,1.20),(8.90,12.28,.27,1.0)):
    box(g,'NeroMarble',(0,y,z),(width,d,.13),.025)
    box(g,'BrushedGold',(0,y-d/2-.015,z+.03),(width,.03,.028),.006)
for j in range(48):
    x=-4.28+j*8.56/47
    lathe(g,'BrushedGold',[(.030,0),(.065,.06),(.040,.115)],(x,11.73,.27),12)
# Dedicated screen quad, conventional 0..1 UVs; Unity creates the display material.
emit('Screen_Surface','Screen',[(-2.92,11.875,1.57),(2.92,11.875,1.57),(2.92,11.875,5.32),(-2.92,11.875,5.32)],[(0,1,2,3)],uvs=[[(0,0),(1,0),(1,1),(0,1)]])

# Small lottery / armillary ornament on a three-tier base.
g='Kino_Armillary';cy=10.67
for radius,z,h in ((.86,.025,.12),(.75,.15,.13),(.64,.29,.16)):
    lathe(g,'NeroMarble',[(radius,0),(radius,h)],(0,cy,z),64)
    lathe(g,'BrushedGold',[(radius+.008,h-.015),(radius+.008,h+.012)],(0,cy,z),64)
lathe(g,'BrushedGold',[(.48,.44),(.30,.54),(.20,.64)],(0,cy,0),48)
for ang in (0,pi/3,-pi/3):
    pts=[]
    for j in range(64):
        t=j*2*pi/64; x=.48*cos(t);z=.92+.48*sin(t)
        pts.append((x*cos(ang),cy+x*sin(ang),z))
    tube(g,'BrushedGold',pts,.026,8,True)
for h,r in ((.72,.435),(.95,.479),(1.16,.412)):
    tube(g,'BrushedGold',[(r*cos(j*2*pi/64),cy+r*sin(j*2*pi/64),h) for j in range(64)],.017,8,True)
for j in range(14):
    a=j*2.399;r=.29*math.sqrt(j/14)
    lathe(g,'BrushedGold',[(.014,0),(.075,.05),(.075,.11),(.014,.16)],(r*cos(a),cy+r*sin(a),.68+(j%3)*.11),12)

# Two small round medallions beside the stage.
for side in (-1,1):
    theta=side*STEP*2.5; m=frame(theta);g=f'Wall_Medallion_{side}'
    pts=[(.25*cos(j*2*pi/64),R-.42,3.18+.25*sin(j*2*pi/64)) for j in range(64)]
    tube(g,'BrushedGold',pts,.018,8,True,m)

# Downlight geometry and positions. Flush fixtures, no Blender Light objects.
for i in range(14):
    a=(i+.5)*2*pi/14; x=11.9*sin(a);y=11.9*cos(a)
    lathe('Ceiling_Downlights','BrushedGold',[(.080,0),(.080,.020)],(x,y,7.19),16)
    lathe('Ceiling_Downlights','WarmLED',[(.053,0),(.053,.010)],(x,y,7.177),16)
    lights.append({'name':f'Downlight_{i:02d}','position':[x,y,6.95],'kind':'downlight'})

objects=[]
for (group,mat),data in BUCKETS.items():
    mesh=bpy.data.meshes.new(f'{group}_{mat}')
    mesh.from_pydata(data['v'],[],data['f']);mesh.update()
    # Recalculate winding on closed components and preserve outward-facing screen.
    bm=bmesh.new();bm.from_mesh(mesh)
    if group in ('Floor','Ceiling'):
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
    bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
    # Isolated inlay triangles have no closed volume to define their outside.
    if group=='Floor_Compass':
        for f in bm.faces:
            if len(f.verts)==3 and f.normal.z<0:f.normal_flip()
    bm.to_mesh(mesh);bm.free()
    mesh.materials.append(MATS[mat])
    obj=bpy.data.objects.new(f'{group}__{mat}',mesh);scene.collection.objects.link(obj)
    uv=mesh.uv_layers.new(name='UVMap')
    # Planar metre-based box projection, so marble density agrees on all pieces.
    for poly in mesh.polygons:
        normal=poly.normal;axis=max(range(3),key=lambda k:abs(normal[k]))
        for li in poly.loop_indices:
            v=mesh.vertices[mesh.loops[li].vertex_index].co
            if group=='Screen_Surface':
                u,w=(v.x+2.92)/5.84,(v.z-1.57)/3.75
            elif axis==2: u,w=v.x*.20,v.y*.20
            elif axis==1:u,w=v.x*.20,v.z*.20
            else:u,w=v.y*.20,v.z*.20
            uv.data[li].uv=(u,w)
        poly.use_smooth=False
    # Mark smooth surfaces using original face groups; winding does not reorder faces.
    for poly,smooth in zip(mesh.polygons,data['s']): poly.use_smooth=smooth
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    mesh.uv_layers.new(name='LightmapUV');mesh.uv_layers.active_index=1
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.025,area_weight=.2,correct_aspect=True,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT');mesh.uv_layers.active_index=0
    obj['surface']=mat;obj['units']='metres';obj['lightmap_uv']='LightmapUV'
    objects.append(obj)
    print(f'Built {obj.name}: {len(mesh.vertices)} vertices',flush=True)

# Add gold floor lettering as editable mesh geometry.
bpy.ops.object.text_add(location=(0,-1.72,.025))
txt=bpy.context.object;txt.name='Floor_Lettering__BrushedGold'
txt.data.body='K I N O';txt.data.align_x='CENTER';txt.data.size=.25;txt.data.extrude=.001
txt.data.materials.append(MATS['BrushedGold']);bpy.ops.object.convert(target='MESH')
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.03);bpy.ops.object.mode_set(mode='OBJECT')
txt.data.uv_layers.active.name='UVMap';txt.data.uv_layers.new(name='LightmapUV',do_init=True);objects.append(txt)

# Empty anchors survive export and remove coordinate-convention guessing in Unity.
for name,p in [('Anchor_View',(0,-12.0,2.45)),('Anchor_Centre',(0,0,0)),('Anchor_Screen',(0,11.7,3.45))]+[(f"Anchor_{l['name']}",l['position']) for l in lights]:
    obj=bpy.data.objects.new(name,None);scene.collection.objects.link(obj);obj.location=p;obj.empty_display_size=.15
    if name.startswith('Anchor_Downlight'):obj['kind']='downlight'
    elif name.startswith('Anchor_Sconce'):obj['kind']='sconce'

# Geometry inspection view in the saved Blender file.
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.clip_end=1000
        area.spaces.active.region_3d.view_location=(0,0,3)
        area.spaces.active.region_3d.view_distance=28
        area.spaces.active.shading.type='SOLID'
        area.spaces.active.shading.color_type='MATERIAL'
bpy.ops.object.select_all(action='SELECT')
# Apply the same targeted column-joint repair on regeneration. Floor, ceiling,
# terrace slabs, UV charts and mesh topology are preserved by this pass.
import sys
sys.path.insert(0, os.path.dirname(__file__))
from repair_column_joints import repair_columns
repair_columns()
from complete_stage_return import complete_stage_return
objects.extend(complete_stage_return())
from align_pilasters import align_pilasters
align_pilasters()
from recess_arcade import recess_arcade
recess_arcade()
from fix_marble_seams import fix_marble_seams
fix_marble_seams()
from remove_rail_caps import remove_rail_caps
remove_rail_caps()
from separate_balls import separate_balls
separate_balls()
objects = [o for o in scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='SELECT')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SOURCE,'KinoRotunda.blend'))
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'KinoRotunda.fbx'),use_selection=True,object_types={'MESH','EMPTY'},global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',use_tspace=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects)
report={'radius_m':R,'height_m':H,'arcade_bays':25,'meshes':len(objects),'triangles':triangles,'vertices':sum(len(o.data.vertices) for o in objects),'uv_channels':2,'lights_in_blend':sum(o.type=='LIGHT' for o in scene.objects),'cameras_in_blend':sum(o.type=='CAMERA' for o in scene.objects),'materials':'semantic slots only, no nodes/textures','blend':os.path.join(SOURCE,'KinoRotunda.blend'),'fbx':os.path.join(OUT,'KinoRotunda.fbx')}
with open(os.path.join(REPORT,'blender-report.json'),'w') as f:json.dump(report,f,indent=2)
print(json.dumps(report,indent=2),flush=True)
