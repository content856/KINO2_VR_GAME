"""Read-only structural and UV validation of the saved Blender source."""
import bpy,json,math,os
root=os.path.abspath(os.path.join(os.path.dirname(__file__),'..','..'))
bpy.ops.wm.open_mainfile(filepath=os.path.join(root,'SourceArt/KinoRotunda/KinoRotunda.blend'))
errors=[];normals={};meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
for obj in meshes:
    if len(obj.data.uv_layers)!=2:errors.append(obj.name+': expected two UV channels')
    for layer in obj.data.uv_layers:
        for uv in layer.data:
            if not all(math.isfinite(v) for v in uv.uv):errors.append(obj.name+': nonfinite UV');break
            if layer.name=='LightmapUV' and not all(-.0001<=v<=1.0001 for v in uv.uv):errors.append(obj.name+': UV2 out of range');break
    if obj.name.startswith('Floor_Compass') or obj.name.startswith('Screen_Surface'):
        normals[obj.name]=[list(p.normal) for p in obj.data.polygons[:32]]
report={'errors':errors,'mesh_count':len(meshes),'all_uvs_finite':not errors,'light_objects':sum(o.type=='LIGHT' for o in bpy.context.scene.objects),'camera_objects':sum(o.type=='CAMERA' for o in bpy.context.scene.objects),'world':str(bpy.context.scene.world),'orientation_samples':normals}
with open(os.path.join(root,'Artifacts/KinoRotunda/blender-validation.json'),'w') as f:json.dump(report,f,indent=2)
print(json.dumps(report))
