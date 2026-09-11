using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KinoRotunda.Editor
{
    // Explicit, file-triggered editor jobs make the same builder usable in an open editor
    // and in batch mode. Merely importing this script never rebuilds or changes a scene.
    [InitializeOnLoad]
    public static class KinoRotundaBuilder
    {
        const string Root = "Assets/KinoRotunda";
        const string ModelPath = Root + "/Models/KinoRotunda.fbx";
        const string ScenePath = Root + "/Scenes/KinoRotunda.unity";
        const string Request = "Temp/KinoRotunda.request";
        const string Artifacts = "Artifacts/KinoRotunda";
        static double nextPoll;
        static bool busy;
        static bool completingBake;
        static readonly Color Warm = new Color(1f, .67f, .34f);

        static KinoRotundaBuilder()
        {
            EditorApplication.update += Poll;
            Lightmapping.bakeCompleted += BakeCompleted;
        }

        static void Status(string text)
        {
            Directory.CreateDirectory(Artifacts);
            File.WriteAllText(Artifacts + "/unity-status.txt", DateTime.Now.ToString("s") + " " + text);
            Debug.Log("[KinoRotunda] " + text);
        }

        static void Poll()
        {
            // A manual handoff lets an in-flight light bake finish without replacing
            // reflection settings that the user is now editing.
            if (File.Exists("Temp/KinoRotunda.manual-control"))
                SessionState.SetBool("KinoRotunda.Baking", false);
            if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(Request)) return;
            string command = File.ReadAllText(Request).Trim();
            File.Delete(Request);
            busy = true;
            try
            {
                switch (command)
                {
                    case "build": Build(); break;
                    case "bake": Bake(); break;
                    case "capture": Capture(); Validate(); Status("CAPTURED"); break;
                    case "validate": Validate(); Status("VALIDATED"); break;
                    case "validate-balls": KinoRotundaBallImport.ValidateImportedBalls(); Status("BALLS_VALIDATED"); break;
                    case "reflections": BakeReflections(); Capture(); Validate(); Status("REFLECTIONS_READY"); break;
                    case "finish": FinishAppearance(); break;
                    case "reference-lighting": KinoReferenceLighting.Apply(); break;
                    case "lighting-audit": KinoReferenceLighting.Audit(); Status("LIGHTING_AUDITED"); break;
                    case "interior-inspect": KinoInteriorLighting.Inspect(); Status("INTERIOR_INSPECTED"); break;
                    case "interior-lighting": KinoInteriorLighting.Apply(); break;
                    case "stage-lighting": KinoInteriorLighting.ApplyStage(); break;
                    case "stage-preview": KinoInteriorLighting.FinishStage(); break;
                    default: throw new ArgumentException("Unknown KINO editor command: " + command);
                }
            }
            catch (Exception e)
            {
                Status("ERROR " + e);
                File.WriteAllText(Artifacts + "/unity-error.txt", e.ToString());
                Debug.LogException(e);
            }
            finally { busy = false; }
        }

        [MenuItem("Tools/KINO Rotunda/1 - Build environment")]
        public static void Build()
        {
            if (Lightmapping.isRunning) throw new InvalidOperationException("Wait for the current light bake to finish.");
            Status("IMPORTING");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ImportTextures();
            var materials = MakeMaterials();
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (!importer) throw new FileNotFoundException("Export the Blender model first", ModelPath);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.generateSecondaryUV = false; // Blender's packed LightmapUV channel is retained.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var entry in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.Key), entry.Value);
            importer.SaveAndReimport();

            Material inheritedSky = RenderSettings.skybox;
            string skyID = SkyIdentity(inheritedSky);
            var old = SceneManager.GetActiveScene();
            // Preserve any unsaved user scene as a separate copy before switching context.
            if (old.isDirty && old.path != ScenePath)
                EditorSceneManager.SaveScene(old, Root + "/Scenes/SourceSceneBackup.unity", true);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "KinoRotunda";
            RenderSettings.skybox = inheritedSky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.22f, .27f, .36f);
            RenderSettings.ambientEquatorColor = new Color(.14f, .115f, .09f);
            RenderSettings.ambientGroundColor = new Color(.075f, .063f, .047f);
            RenderSettings.ambientIntensity = 1;
            RenderSettings.reflectionIntensity = .85f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 256;
            RenderSettings.fog = false;

            var root = new GameObject("KINO Rotunda • Environment");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), scene);
            model.name = "Architecture • Blender FBX";
            model.transform.SetParent(root.transform, false);
            var all = model.GetComponentsInChildren<Transform>(true);
            Transform Anchor(string name) => all.First(t => t.name == name);
            // Normalize the FBX's forward convention using exported semantic anchors.
            var screenAnchor = Anchor("Anchor_Screen");
            if (screenAnchor.position.z < 0) model.transform.rotation = Quaternion.Euler(0, 180, 0);
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                string surface = renderer.name.Split(new[] { "__" }, StringSplitOptions.None).Last();
                if (materials.TryGetValue(surface, out var mat)) renderer.sharedMaterial = mat;
                bool animatedBall = renderer.name.StartsWith("Ball_", StringComparison.Ordinal);
                renderer.receiveGI = animatedBall ? ReceiveGI.LightProbes : ReceiveGI.Lightmaps;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, animatedBall ? 0 : StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                var so = new SerializedObject(renderer);
                so.FindProperty("m_ScaleInLightmap").floatValue = surface == "BrushedGold" || surface == "BronzeShadow" ? .24f : surface == "WarmLED" ? .30f : .7f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            MakeColliders(root.transform);
            var lightRoot = new GameObject("Lighting • baked warm interior").transform;
            lightRoot.SetParent(root.transform, false);
            foreach (var a in all)
            {
                if (a.name.StartsWith("Anchor_Sconce"))
                    Light(a.name.Replace("Anchor_", ""), LightType.Point, a.position, Warm, 5.5f, 5.3f, lightRoot);
                else if (a.name.StartsWith("Anchor_Downlight"))
                {
                    var l = Light(a.name.Replace("Anchor_", ""), LightType.Spot, a.position, new Color(1,.78f,.50f), 9, 9, lightRoot);
                    l.transform.rotation = Quaternion.Euler(90, 0, 0);
                    l.spotAngle = 95; l.innerSpotAngle = 52;
                }
            }
            // Low-intensity cove bounce follows the actual emissive ceiling rings.
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6;
                Light("Cove bounce " + i.ToString("00"), LightType.Point, new Vector3(Mathf.Sin(a)*5.4f,6.55f,Mathf.Cos(a)*5.4f), Warm, 2.2f, 7.5f, lightRoot);
            }
            var stage = Light("Display wash", LightType.Spot, new Vector3(0,6.15f,8.4f), Warm, 14, 9, lightRoot);
            stage.transform.LookAt(new Vector3(0,1.4f,11.9f));stage.spotAngle=85;stage.innerSpotAngle=45;
            Light("Oculus daylight", LightType.Point, new Vector3(0,6.95f,0), new Color(.59f,.74f,1), 4, 11, lightRoot);
            var floorBounce=Light("Soft warm ceiling bounce",LightType.Rectangle,new Vector3(0,6.55f,0),new Color(1,.77f,.52f),1.6f,16,lightRoot);
            floorBounce.transform.rotation=Quaternion.Euler(90,0,0);floorBounce.areaSize=new Vector2(10,10);
            var sun = Light("Daylight through the arches", LightType.Directional, new Vector3(0,10,0), new Color(1,.84f,.64f), .65f, 100, lightRoot);
            sun.transform.rotation = Quaternion.Euler(32,-58,0);
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            // The skybox material itself is never edited or substituted.
            File.WriteAllText(Artifacts + "/skybox-preservation.txt", "Before: " + skyID + "\nAfter: " + SkyIdentity(RenderSettings.skybox));

            MakeProbes(root.transform);
            ConfigurePipelines();
            MakeVolume(root.transform);
            var cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            var camera = cameraGO.AddComponent<Camera>();
            cameraGO.AddComponent<AudioListener>();
            camera.transform.position = Anchor("Anchor_View").position;
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(0,3.45f,11.7f)-camera.transform.position);
            camera.fieldOfView = 64;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 120;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.None;
            cameraData.dithering = true;
            var lighting = new LightingSettings { name = "KINO Baked Lighting", bakedGI = true, realtimeGI = false,
                lightmapper = LightingSettings.Lightmapper.ProgressiveGPU, lightmapResolution = 14,
                lightmapMaxSize = 2048, lightmapPadding = 4, directSampleCount = 32, indirectSampleCount = 128,
                environmentSampleCount = 128, maxBounces = 3, ao = true, aoMaxDistance = .65f,
                aoExponentIndirect = 1, aoExponentDirect = 0, indirectScale = 1.2f,
                lightmapCompression = LightmapCompression.HighQuality };
            SaveAsset(lighting, Root + "/Settings/KinoLighting.asset");
            Lightmapping.lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(Root + "/Settings/KinoLighting.asset");
            LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
            KinoReferenceLighting.Configure();
            PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/KinoRotunda.prefab");
            EditorSceneManager.SaveScene(scene, ScenePath);
            var builds = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            builds.Insert(0, new EditorBuildSettingsScene(ScenePath,true));
            EditorBuildSettings.scenes = builds.ToArray();
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.AlignViewToObject(camera.transform);
            EditorApplication.QueuePlayerLoopUpdate();
            Validate();
            Status("BUILT — ready for lighting bake");
            EditorApplication.delayCall += () => { try { Capture(); } catch (Exception e) { Debug.LogException(e); } };
        }

        static string SkyIdentity(Material sky)
        {
            if (!sky) return "null";
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sky, out string guid, out long local);
            return sky.name + " | " + guid + " | " + local;
        }

        static void ImportTextures()
        {
            foreach (string file in Directory.GetFiles(Root + "/Textures", "*.png"))
            {
                var path=file.Replace('\\','/');
                var ti=(TextureImporter)AssetImporter.GetAtPath(path);
                bool normal = path.Contains("_Normal");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal;
                ti.wrapMode = path.Contains("Display") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 8;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
        }

        static Dictionary<string, Material> MakeMaterials()
        {
            var materials=new Dictionary<string,Material>();
            Material Make(string name,Color color,float metallic,float smoothness)
            {
                string path=Root+"/Materials/"+name+".mat";
                var m=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
                m.name=name;m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smoothness);
                m.enableInstancing=true;
                materials.Add(name,m);return m;
            }
            foreach(string name in new[]{"NeroMarble","IvoryMarble"})
            {
                var m=Make(name,Color.white,name=="NeroMarble"?.14f:.04f,name=="NeroMarble"?.89f:.87f);
                m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/"+name+"_BaseColor.png"));
                m.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/"+name+"_Normal.png"));
                m.SetFloat("_BumpScale",.35f);m.EnableKeyword("_NORMALMAP");
            }
            Make("BrushedGold",new Color(1f,.64f,.20f),.68f,.78f);
            Make("BronzeShadow",new Color(.18f,.10f,.037f),.78f,.66f);
            var led=Make("WarmLED",new Color(1,.62f,.23f),0,.55f);
            led.EnableKeyword("_EMISSION");led.SetColor("_EmissionColor",new Color(1,.38f,.10f)*4f);
            led.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
            var screen=Make("Screen",Color.white,0,.38f);
            var graphic=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/KinoDisplay.png");
            screen.SetTexture("_BaseMap",graphic);screen.SetTexture("_EmissionMap",graphic);
            screen.EnableKeyword("_EMISSION");screen.SetColor("_EmissionColor",Color.white*1.35f);
            screen.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
            foreach(var m in materials.Values)EditorUtility.SetDirty(m);
            KinoReferenceLighting.ConfigureMaterials();
            AssetDatabase.SaveAssets();return materials;
        }

        static Light Light(string name,LightType type,Vector3 position,Color color,float intensity,float range,Transform parent)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.position=position;
            var l=go.AddComponent<Light>();l.type=type;l.color=color;l.intensity=intensity;l.range=range;
            l.lightmapBakeType=LightmapBakeType.Baked;l.shadows=LightShadows.Soft;l.bounceIntensity=1;
            l.shadowRadius=.16f;return l;
        }

        static void MakeColliders(Transform root)
        {
            var parent=new GameObject("Collision • simplified solid boundaries").transform;parent.SetParent(root,false);
            var floor=new GameObject("Walkable floor");floor.transform.SetParent(parent,false);floor.transform.position=new Vector3(0,-.12f,0);
            var fc=floor.AddComponent<BoxCollider>();fc.size=new Vector3(29.3f,.24f,29.3f);
            for(int i=0;i<56;i++)
            {
                float a=i*Mathf.PI*2/56;var wall=new GameObject("Perimeter "+i.ToString("00"));wall.transform.SetParent(parent,false);
                wall.transform.position=new Vector3(Mathf.Sin(a)*14.35f,3.35f,Mathf.Cos(a)*14.35f);
                wall.transform.rotation=Quaternion.Euler(0,a*Mathf.Rad2Deg,0);var c=wall.AddComponent<BoxCollider>();c.size=new Vector3(1.7f,6.7f,.18f);
            }
            var stage=new GameObject("Display boundary");stage.transform.SetParent(parent,false);stage.transform.position=new Vector3(0,3.2f,12.25f);
            stage.AddComponent<BoxCollider>().size=new Vector3(8.9f,6.4f,1.1f);
            var orb=new GameObject("Ornament boundary");orb.transform.SetParent(parent,false);orb.transform.position=new Vector3(0,.75f,10.67f);
            var cc=orb.AddComponent<CapsuleCollider>();cc.radius=.85f;cc.height=1.5f;
        }

        static void MakeProbes(Transform root)
        {
            var group=new GameObject("Light probes • visitor height");group.transform.SetParent(root,false);
            var positions=new List<Vector3>();
            foreach(float y in new[]{.45f,1.75f,3.4f})
            {
                positions.Add(new Vector3(0,y,0));
                foreach(float r in new[]{4f,8f,12f})for(int i=0;i<12;i++)
                    positions.Add(new Vector3(Mathf.Sin(i*Mathf.PI/6)*r,y,Mathf.Cos(i*Mathf.PI/6)*r));
            }
            group.AddComponent<LightProbeGroup>().probePositions=positions.ToArray();
            foreach(var item in new[]{("Centre",new Vector3(0,2.6f,0)),("Display",new Vector3(0,2.3f,9))})
            {
                var go=new GameObject("Reflection • "+item.Item1);go.transform.SetParent(root,false);go.transform.position=item.Item2;
                var p=go.AddComponent<ReflectionProbe>();p.mode=ReflectionProbeMode.Baked;p.resolution=256;
                p.hdr=true;p.boxProjection=true;p.size=item.Item1=="Centre"?new Vector3(29,8,29):new Vector3(13,7,12);
                p.center=new Vector3(0,1,0);p.blendDistance=3;p.nearClipPlane=.12f;p.farClipPlane=90;
                p.clearFlags=ReflectionProbeClearFlags.Skybox;p.intensity=.95f;
            }
        }

        static void ConfigurePipelines()
        {
            UniversalRenderPipelineAsset Clone(string source,string name)
            {
                string path=Root+"/Settings/"+name+".asset";
                var rp=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if(!rp){AssetDatabase.CopyAsset(source,path);rp=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);}
                rp.supportsHDR=true;rp.renderScale=1;rp.msaaSampleCount=4;
                rp.supportsCameraDepthTexture=false;rp.supportsCameraOpaqueTexture=false;
                EditorUtility.SetDirty(rp);return rp;
            }
            var mobile=Clone("Assets/Settings/Mobile_RPAsset.asset","KinoQuestPipeline");
            var pc=Clone("Assets/Settings/PC_RPAsset.asset","KinoDesktopPipeline");
            // Existing renderers remain shared; only the KINO pipeline assets and quality
            // references change. XR loaders, platform settings and skybox remain intact.
            var quality=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels=quality.FindProperty("m_QualitySettings");
            for(int i=0;i<levels.arraySize;i++)
            {
                var q=levels.GetArrayElementAtIndex(i);string name=q.FindPropertyRelative("name").stringValue;
                q.FindPropertyRelative("customRenderPipeline").objectReferenceValue=name=="PC"?pc:mobile;
            }
            quality.ApplyModifiedPropertiesWithoutUndo();
        }

        static void MakeVolume(Transform root)
        {
            string path=Root+"/Settings/KinoAtmosphere.asset";
            var p=ScriptableObject.CreateInstance<VolumeProfile>();p.name="KinoAtmosphere";
            var bloom=p.Add<Bloom>(true);bloom.threshold.Override(.9f);bloom.intensity.Override(.55f);bloom.scatter.Override(.70f);
            bloom.highQualityFiltering.Override(false);
            var tone=p.Add<Tonemapping>(true);tone.mode.Override(TonemappingMode.ACES);
            var color=p.Add<ColorAdjustments>(true);color.postExposure.Override(.45f);color.contrast.Override(10);color.saturation.Override(-6);
            var vignette=p.Add<Vignette>(true);vignette.intensity.Override(.12f);vignette.smoothness.Override(.4f);
            if(AssetDatabase.LoadAssetAtPath<VolumeProfile>(path))AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(p,path);
            foreach(var component in p.components)AssetDatabase.AddObjectToAsset(component,p);
            var go=new GameObject("Atmosphere • restrained warm bloom");go.transform.SetParent(root,false);
            var volume=go.AddComponent<Volume>();volume.isGlobal=true;volume.priority=10;volume.sharedProfile=p;
        }

        static void SaveAsset(Object value,string path)
        {
            var existing=AssetDatabase.LoadMainAssetAtPath(path);
            if(existing){EditorUtility.CopySerialized(value,existing);Object.DestroyImmediate(value);EditorUtility.SetDirty(existing);}
            else AssetDatabase.CreateAsset(value,path);
        }

        static void FinishAppearance()
        {
            MakeMaterials();
            KinoReferenceLighting.Configure();
            var p=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root+"/Settings/KinoAtmosphere.asset");
            if(p.TryGet<Bloom>(out var bloom))
            {
                bloom.threshold.Override(1);bloom.intensity.Override(.32f);bloom.scatter.Override(.68f);EditorUtility.SetDirty(bloom);
            }
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();
            if(Camera.main)SceneView.lastActiveSceneView?.AlignViewToObject(Camera.main.transform);
            Bake();
        }

        [MenuItem("Tools/KINO Rotunda/2 - Bake lighting")]
        public static void Bake()
        {
            if(SceneManager.GetActiveScene().path!=ScenePath)throw new InvalidOperationException("Open the KINO scene first.");
            if(Lightmapping.isRunning)return;
            if(File.Exists("Temp/KinoRotunda.manual-control"))File.Delete("Temp/KinoRotunda.manual-control");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            SessionState.SetBool("KinoRotunda.Baking",true);
            Status("BAKING");
            if(!Lightmapping.BakeAsync()){SessionState.SetBool("KinoRotunda.Baking",false);throw new InvalidOperationException("Unity could not start the bake.");}
        }

        static void BakeCompleted()
        {
            if(File.Exists("Temp/KinoRotunda.manual-control")){SessionState.SetBool("KinoRotunda.Baking",false);return;}
            if(!SessionState.GetBool("KinoRotunda.Baking",false)||completingBake)return;
            SessionState.SetBool("KinoRotunda.Baking",false);completingBake=true;
            EditorApplication.delayCall+=()=>
            {
                try
                {
                    if(File.Exists("Temp/KinoRotunda.manual-control"))return;
                    EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                    BakeReflections();Capture();Validate();KinoReferenceLighting.Audit();Status("COMPLETE — lighting, reflections and preview saved");
                }
                catch(Exception e){Status("ERROR after bake: "+e);Debug.LogException(e);}
                finally{completingBake=false;}
            };
        }

        [MenuItem("Tools/KINO Rotunda/3 - Bake reflection probes")]
        public static void BakeReflections()
        {
            foreach(var probe in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
            {
                string label=probe.name.Contains("Centre")?"Centre":probe.name.Contains("Floor")?"Floor":"Display";
                string path=Root+"/Settings/Reflection_"+label+".exr";
                probe.mode=ReflectionProbeMode.Baked;
                if(!Lightmapping.BakeReflectionProbe(probe,path))throw new InvalidOperationException("Reflection probe bake failed: "+label);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                // A serialized custom cubemap keeps the captured room available in the
                // prefab as well as in this scene's lighting-data asset.
                probe.customBakedTexture=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                probe.mode=ReflectionProbeMode.Custom;EditorUtility.SetDirty(probe);
            }
            var environment=GameObject.Find("KINO Rotunda • Environment");
            if(environment)PrefabUtility.SaveAsPrefabAsset(environment,Root+"/Prefabs/KinoRotunda.prefab");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/KINO Rotunda/4 - Save preview")]
        public static void Capture()
        {
            var camera=Camera.main;if(!camera)throw new InvalidOperationException("No main camera.");
            var lights=Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            bool unbaked=LightmapSettings.lightmaps.Length==0;
            var modes=lights.Select(l=>l.lightmapBakeType).ToArray();
            var shadows=lights.Select(l=>l.shadows).ToArray();
            var previous=RenderTexture.active;var target=camera.targetTexture;
            bool previousSrgbWrite=GL.sRGBWrite;
            // URP 17 inherits its intermediate color format from the target texture.
            // An LDR target clips LED emission before bloom, even with camera HDR on.
            var rt=RenderTexture.GetTemporary(1920,1080,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            var output=RenderTexture.GetTemporary(1920,1080,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false,false);
            try
            {
                if(unbaked)foreach(var l in lights){l.lightmapBakeType=LightmapBakeType.Realtime;if(l.type!=LightType.Directional)l.shadows=LightShadows.None;}
                camera.targetTexture=rt;
                // StandardRequest follows the same volume/post-processing update as
                // Game view, so bloom and tone mapping are included in the preview.
                RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest { destination=rt });
                GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;
                Graphics.Blit(rt,output);
                RenderTexture.active=output;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();
                Directory.CreateDirectory(Artifacts);File.WriteAllBytes(Artifacts+"/UnityPreview.png",tex.EncodeToPNG());
            }
            finally
            {
                for(int i=0;i<lights.Length;i++){lights[i].lightmapBakeType=modes[i];lights[i].shadows=shadows[i];}
                GL.sRGBWrite=previousSrgbWrite;
                camera.targetTexture=target;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);RenderTexture.ReleaseTemporary(output);Object.DestroyImmediate(tex);
            }
        }

        [MenuItem("Tools/KINO Rotunda/5 - Validate environment")]
        public static void Validate()
        {
            var root=GameObject.Find("KINO Rotunda • Environment");if(!root)throw new InvalidOperationException("KINO environment is missing.");
            var meshes=root.GetComponentsInChildren<MeshFilter>();var renderers=root.GetComponentsInChildren<MeshRenderer>();
            var errors=new List<string>();long tris=0;int verts=0;
            foreach(var mf in meshes)
            {
                var m=mf.sharedMesh;if(!m){errors.Add(mf.name+": missing mesh");continue;}
                verts+=m.vertexCount;
                for(int s=0;s<m.subMeshCount;s++)tris+=(long)m.GetIndexCount(s)/3;
                if(!m.HasVertexAttribute(VertexAttribute.TexCoord0)||!m.HasVertexAttribute(VertexAttribute.TexCoord1))errors.Add(mf.name+": missing UV channel");
            }
            foreach(var r in renderers)
                if(r.sharedMaterials.Any(m=>!m||!m.shader||m.shader.name=="Hidden/InternalErrorShader"))errors.Add(r.name+": invalid material");
            var bounds=new Bounds(Vector3.zero,Vector3.zero);foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            if(bounds.size.x<27||bounds.size.x>31||bounds.size.y<7||bounds.size.y>9)errors.Add("Unexpected model scale: "+bounds.size);
            var lights=root.GetComponentsInChildren<Light>();
            var report=new Report { scene=ScenePath,meshCount=meshes.Length,vertices=verts,triangles=tris,
                boundsMetres=bounds.size,materials=renderers.SelectMany(r=>r.sharedMaterials).Distinct().Count(),
                lights=lights.Length,realtimeLights=lights.Count(l=>l.lightmapBakeType!=LightmapBakeType.Baked),
                lightmaps=LightmapSettings.lightmaps.Length,bakedRenderers=renderers.Count(r=>r.lightmapIndex>=0&&r.lightmapIndex<65534),
                colliders=root.GetComponentsInChildren<Collider>().Length,
                reflectionProbes=root.GetComponentsInChildren<ReflectionProbe>().Count(p=>p.texture),
                skybox=SkyIdentity(RenderSettings.skybox),errors=errors.ToArray() };
            File.WriteAllText(Artifacts+"/unity-validation.json",JsonUtility.ToJson(report,true));
            File.WriteAllText(Artifacts+"/render-device.txt","Renderer: "+SystemInfo.graphicsDeviceName+"\nLightmapper: "+Lightmapping.lightingSettings.lightmapper+"\nPipeline: "+GraphicsSettings.currentRenderPipeline.name);
            if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
        }

        [Serializable] class Report
        {
            public string scene;public int meshCount,vertices,materials,lights,realtimeLights,lightmaps,bakedRenderers,colliders,reflectionProbes;
            public long triangles;public Vector3 boundsMetres;public string skybox;public string[] errors;
        }
    }
}
