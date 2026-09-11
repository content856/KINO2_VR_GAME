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
    public static class KinoPerimeterTubes
    {
        const string Root = "Assets/KinoRotunda";
        const string Model = Root + "/Models/KinoRotunda.fbx";
        const string Output = "Artifacts/KinoRotunda/PerimeterTubes";
        public static readonly int[] Bays = { 2, 6, 10, 14, 18, 22, 26 };
        public static bool IsTube(string name) => name.StartsWith("Tube_", StringComparison.Ordinal) || name.StartsWith("Ball_Tube_", StringComparison.Ordinal);

        public static Material GlassMaterial()
        {
            const string path = Root + "/Materials/TubeGlass.mat";
            var glass = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (glass) return glass;
            glass = new Material(KinoLottery.GlassMaterial()) { name = "TubeGlass", enableInstancing = true };
            glass.SetColor("_BaseColor", new Color(.78f, .91f, .98f, .028f));
            glass.SetColor("_SpecColor", new Color(.12f, .12f, .12f, 1));
            glass.SetFloat("_Smoothness", .96f);
            AssetDatabase.CreateAsset(glass, path);
            return glass;
        }

        public static void ConfigureRenderer(MeshRenderer renderer)
        {
            if (!IsTube(renderer.name)) return;
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, 0);
            renderer.receiveGI = ReceiveGI.LightProbes;
            renderer.lightmapIndex = -1;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            bool glass = renderer.name.EndsWith("__TubeGlass", StringComparison.Ordinal);
            renderer.shadowCastingMode = glass ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = !glass;
        }

        static GameObject RequireScene()
        {
            if (SceneManager.GetActiveScene().path != Root + "/Scenes/KinoRotunda.unity")
                throw new InvalidOperationException("Open the KINO scene first.");
            if (Lightmapping.isRunning) throw new InvalidOperationException("Wait for the light bake.");
            var root = GameObject.Find("KINO Rotunda • Environment");
            if (!root) throw new InvalidOperationException("KINO environment is missing.");
            Directory.CreateDirectory(Output);
            return root;
        }

        static string PreservedState(GameObject root)
        {
            return string.Join("\n", new[] { "NeroMarble", "IvoryMarble", "BrushedGold", "BronzeShadow", "WarmLED", "Screen", "CeilingLED", "LotteryGlass" }
                .Select(n => EditorJsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + n + ".mat")))) + "\n" +
                EditorJsonUtility.ToJson(RenderSettings.skybox) + "\n" +
                string.Join("\n", root.GetComponentsInChildren<Light>(true).OrderBy(l => l.name).Select(l => EditorJsonUtility.ToJson(l))) + "\n" +
                string.Join("\n", root.GetComponentsInChildren<ReflectionProbe>(true).OrderBy(p => p.name).Select(p => EditorJsonUtility.ToJson(p)));
        }

        [MenuItem("Tools/KINO Rotunda/12 - Finish perimeter glass tubes")]
        public static void Apply()
        {
            var root = RequireScene();
            string before = PreservedState(root);
            File.WriteAllText(Output + "/preserved-state-before.json", before);
            string backup = Output + "/SceneBefore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), backup + ".unity", true);
            File.Copy(Root + "/Prefabs/KinoRotunda.prefab", backup + ".prefab");
            var glass = GlassMaterial();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "TubeGlass"), glass);
            importer.SaveAndReimport();
            FinishHierarchy(root);
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
            string after = PreservedState(root);
            File.WriteAllText(Output + "/preserved-state-after.json", after);
            if (before != after) throw new InvalidOperationException("Unrelated materials or lighting changed.");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            CaptureViews();
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        static void FinishHierarchy(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true).Where(r => IsTube(r.name)))
            {
                string surface = renderer.name.Split(new[] { "__" }, StringSplitOptions.None).Last();
                var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + surface + ".mat");
                if (!material) throw new InvalidOperationException("Missing tube material " + surface);
                renderer.sharedMaterial = material;
                ConfigureRenderer(renderer);
                EditorUtility.SetDirty(renderer);
                if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }

        public static void Validate()
        {
            var root = RequireScene();
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            var tubes = renderers.Where(r => r.name.EndsWith("__TubeGlass", StringComparison.Ordinal)).ToArray();
            var balls = renderers.Where(r => r.name.StartsWith("Ball_Tube_", StringComparison.Ordinal)).ToArray();
            if (tubes.Length != 7 || balls.Length != 63) throw new InvalidOperationException("Expected seven tubes and 63 plain gold balls.");
            foreach (int bay in Bays)
            {
                var tube = tubes.Single(r => r.name == $"Tube_{bay:00}__TubeGlass");
                float angle = bay * Mathf.PI * 2 / 28;
                Vector3 expected = new Vector3(Mathf.Sin(angle) * 14.10f, 2.995f, Mathf.Cos(angle) * 14.10f);
                if (Vector3.Distance(tube.bounds.center, expected) > .005f ||
                    tube.sharedMaterial != GlassMaterial() || tube.sharedMaterial.renderQueue != 3000 ||
                    tube.shadowCastingMode != ShadowCastingMode.Off || tube.sharedMaterial.GetFloat("_ZWrite") != 0)
                    throw new InvalidOperationException("Incorrect glass or bay placement: " + tube.name + " " + tube.bounds.center);
            }
            if (renderers.Any(r => r.name.StartsWith("Tube_Number_", StringComparison.Ordinal)))
                throw new InvalidOperationException("Tube balls must have no numbers.");
            foreach (var renderer in renderers.Where(r => IsTube(r.name)))
                if (!renderer.sharedMaterial || renderer.receiveGI != ReceiveGI.LightProbes || renderer.lightmapIndex != -1)
                    throw new InvalidOperationException("Invalid tube renderer " + renderer.name);
            KinoRotundaBallImport.ValidateImportedBalls();
            KinoRotundaBuilder.Validate();
            File.WriteAllText(Output + "/unity-validation.json", JsonUtility.ToJson(new Validation {
                tubes = tubes.Length, separateBalls = balls.Length, bays = Bays,
                equalAngularSpacing = true, screenFlanked = true, transparentGlass = true,
                noNumbers = true, probeLighting = true
            }, true));
        }

        public static void CaptureViews()
        {
            RequireScene();
            var camera = Camera.main;
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            void Capture(string name, Vector3 from, Vector3 to, float angle)
            {
                camera.transform.position = from;
                camera.transform.LookAt(to);
                camera.fieldOfView = angle;
                KinoRotundaBuilder.Capture();
                File.Copy("Artifacts/KinoRotunda/UnityPreview.png", Output + "/" + name + ".png", true);
            }
            try
            {
                Capture("After-stage", new Vector3(0, 3.15f, 1.4f), new Vector3(0, 2.95f, 12.4f), 69);
                Capture("After-tube", new Vector3(4.25f, 3.05f, 6.7f), new Vector3(6.12f, 2.90f, 12.70f), 53);
                Capture("After-rear", new Vector3(0, 3.0f, 3.0f), new Vector3(0, 2.9f, -12.8f), 100);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
            }
            KinoRotundaBuilder.Capture();
            File.Copy("Artifacts/KinoRotunda/UnityPreview.png", Output + "/After-room.png", true);
        }

        [Serializable] class Validation
        {
            public int tubes, separateBalls;
            public int[] bays;
            public bool equalAngularSpacing, screenFlanked, transparentGlass, noNumbers, probeLighting;
        }
    }
}
