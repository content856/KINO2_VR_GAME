using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KinoRotunda.Editor
{
    /// <summary>Targeted lottery finish; keeps the current room and lighting intact.</summary>
    public static class KinoLottery
    {
        const string Root = "Assets/KinoRotunda";
        const string Model = Root + "/Models/KinoRotunda.fbx";
        const string Output = "Artifacts/KinoRotunda/Lottery";
        public static bool IsMachine(string name) => name.StartsWith("Kino_Armillary__", StringComparison.Ordinal);

        static void RequireScene()
        {
            if (SceneManager.GetActiveScene().path != Root + "/Scenes/KinoRotunda.unity")
                throw new InvalidOperationException("Open the KINO scene first.");
            if (Lightmapping.isRunning) throw new InvalidOperationException("Wait for the light bake.");
            Directory.CreateDirectory(Output);
        }

        public static Material GlassMaterial()
        {
            const string path = Root + "/Materials/LotteryGlass.mat";
            var glass = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (glass) return glass;
            glass = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "LotteryGlass", enableInstancing = true };
            // URP Lit alpha with preserved specular. The clear centre keeps the
            // individual balls readable; dielectric Fresnel describes the dome.
            glass.SetFloat("_Surface", 1);
            glass.SetFloat("_Blend", 0);
            glass.SetFloat("_BlendModePreserveSpecular", 1);
            glass.SetFloat("_WorkflowMode", 0);
            glass.SetColor("_BaseColor", new Color(.74f, .88f, .95f, .035f));
            glass.SetColor("_SpecColor", new Color(.20f, .20f, .20f, 1));
            glass.SetFloat("_Smoothness", .97f);
            glass.SetFloat("_Metallic", 0);
            glass.SetFloat("_Cull", (float)CullMode.Back);
            glass.SetFloat("_ZWrite", 0);
            glass.SetFloat("_SrcBlend", (float)BlendMode.One);
            glass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            glass.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            glass.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            glass.SetOverrideTag("RenderType", "Transparent");
            glass.renderQueue = (int)RenderQueue.Transparent;
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            glass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            glass.EnableKeyword("_SPECULAR_SETUP");
            glass.SetShaderPassEnabled("ShadowCaster", false);
            glass.SetShaderPassEnabled("DepthOnly", false);
            glass.SetShaderPassEnabled("DepthNormals", false);
            glass.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            AssetDatabase.CreateAsset(glass, path);
            return glass;
        }

        public static void ConfigureRenderer(MeshRenderer renderer)
        {
            if (!IsMachine(renderer.name)) return;
            // Updated geometry uses probes instead of the previous ornament's UV bake.
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, 0);
            renderer.receiveGI = ReceiveGI.LightProbes;
            renderer.lightmapIndex = -1;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            bool glass = renderer.name.EndsWith("__LotteryGlass", StringComparison.Ordinal);
            renderer.shadowCastingMode = glass ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = !glass;
        }

        public static void Prepare()
        {
            RequireScene();
            if (!File.Exists(Output + "/Before.unity"))
            {
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Output + "/Before.unity", true);
                File.Copy(Root + "/Prefabs/KinoRotunda.prefab", Output + "/Before.prefab", true);
                File.WriteAllText(Output + "/preserved-state-before.json", PreservedState());
                CaptureViews("Before");
            }
        }

        [MenuItem("Tools/KINO Rotunda/11 - Finish glass lottery machine")]
        public static void Apply()
        {
            RequireScene();
            var glass = GlassMaterial();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "LotteryGlass"), glass);
            importer.SaveAndReimport();
            FinishHierarchy(GameObject.Find("KINO Rotunda • Environment"));
            // Apply only the machine finish in the reusable environment prefab.
            const string prefabPath = Root + "/Prefabs/KinoRotunda.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                FinishHierarchy(prefab);
                PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            AssetDatabase.SaveAssets();
            Validate();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            CaptureViews("After");
            var after = PreservedState();
            File.WriteAllText(Output + "/preserved-state-after.json", after);
            if (File.Exists(Output + "/preserved-state-before.json") &&
                File.ReadAllText(Output + "/preserved-state-before.json") != after)
                throw new InvalidOperationException("Unrelated materials or lighting changed; inspect the preserved-state reports.");
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        static void FinishHierarchy(GameObject root)
        {
            if (!root) throw new InvalidOperationException("KINO environment is missing.");
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!IsMachine(renderer.name)) continue;
                string surface = renderer.name.Split(new[] { "__" }, StringSplitOptions.None).Last();
                var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + surface + ".mat");
                if (!material) throw new InvalidOperationException("Missing machine material " + surface);
                renderer.sharedMaterial = material;
                ConfigureRenderer(renderer);
                EditorUtility.SetDirty(renderer);
                if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            var boundary = root.GetComponentsInChildren<CapsuleCollider>(true).FirstOrDefault(c => c.name == "Ornament boundary");
            if (boundary)
            {
                boundary.radius = 1.04f;
                boundary.height = 1.5f;
                EditorUtility.SetDirty(boundary);
            }
        }

        public static void Validate()
        {
            var root = GameObject.Find("KINO Rotunda • Environment");
            var machines = root.GetComponentsInChildren<MeshRenderer>(true).Where(r => IsMachine(r.name)).ToArray();
            if (machines.Length != 5) throw new InvalidOperationException("Expected five machine surfaces, found " + machines.Length);
            var glass = machines.Single(r => r.name.EndsWith("__LotteryGlass", StringComparison.Ordinal));
            if (glass.sharedMaterial != GlassMaterial() || glass.sharedMaterial.renderQueue != 3000 ||
                glass.shadowCastingMode != ShadowCastingMode.Off || glass.sharedMaterial.GetFloat("_ZWrite") != 0)
                throw new InvalidOperationException("The lottery glass must be transparent without opaque shadows or depth writes.");
            var balls = root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("Ball_Armillary_", StringComparison.Ordinal)).ToArray();
            if (balls.Length != 14) throw new InvalidOperationException("Expected fourteen separate draw balls.");
            foreach (var ball in balls)
                if (ball.sharedMaterial.name != "BrushedGold" || ball.transform.parent.name != "Balls_Armillary" ||
                    GameObjectUtility.GetStaticEditorFlags(ball.gameObject) != 0 ||
                    ball.GetComponent<MeshFilter>().sharedMesh.bounds.center.magnitude > .001f)
                    throw new InvalidOperationException("Invalid draw ball " + ball.name);
            foreach (var renderer in machines)
                if (!renderer.sharedMaterial || renderer.receiveGI != ReceiveGI.LightProbes || renderer.lightmapIndex != -1)
                    throw new InvalidOperationException("Invalid machine surface " + renderer.name);
            KinoRotundaBallImport.ValidateImportedBalls();
            KinoRotundaBuilder.Validate();
            File.WriteAllText(Output + "/unity-validation.json", JsonUtility.ToJson(new Validation {
                drawBalls = balls.Length, machineSurfaces = machines.Length,
                transparentGlass = true, separateBalls = true, existingGoldMaterial = true,
                centredPivots = true, probeLighting = true, roomLightingPreserved = true
            }, true));
        }

        public static void CaptureViews(string prefix)
        {
            RequireScene();
            var camera = Camera.main;
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            void Capture(string suffix, Vector3 from, Vector3 to, float angle)
            {
                camera.transform.position = from;
                camera.transform.LookAt(to);
                camera.fieldOfView = angle;
                KinoRotundaBuilder.Capture();
                File.Copy("Artifacts/KinoRotunda/UnityPreview.png", Output + "/" + prefix + suffix + ".png", true);
            }
            try
            {
                Capture("-closeup", new Vector3(0, 1.20f, 7.45f), new Vector3(0, .70f, 10.67f), 32);
                Capture("-angle", new Vector3(1.95f, 1.65f, 7.75f), new Vector3(0, .72f, 10.67f), 32);
                Capture("-stage", new Vector3(0, 2.75f, 5.5f), new Vector3(0, 2.95f, 12.25f), 60);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
            }
            KinoRotundaBuilder.Capture();
            File.Copy("Artifacts/KinoRotunda/UnityPreview.png", Output + "/" + prefix + "-room.png", true);
        }

        static string PreservedState()
        {
            var root = GameObject.Find("KINO Rotunda • Environment");
            return string.Join("\n", new[] { "NeroMarble", "IvoryMarble", "BrushedGold", "BronzeShadow", "WarmLED", "Screen", "CeilingLED" }
                .Select(n => EditorJsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + n + ".mat")))) + "\n" +
                EditorJsonUtility.ToJson(RenderSettings.skybox) + "\n" +
                string.Join("\n", root.GetComponentsInChildren<Light>(true).OrderBy(l => l.name).Select(l => EditorJsonUtility.ToJson(l))) + "\n" +
                string.Join("\n", root.GetComponentsInChildren<ReflectionProbe>(true).OrderBy(p => p.name).Select(p => EditorJsonUtility.ToJson(p)));
        }

        [Serializable] class Validation
        {
            public int drawBalls, machineSurfaces;
            public bool transparentGlass, separateBalls, existingGoldMaterial, centredPivots, probeLighting, roomLightingPreserved;
        }
    }
}
