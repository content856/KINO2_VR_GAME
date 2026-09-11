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
    // This pass deliberately has its own bake completion: the user's probe layout,
    // disabled probes, skybox, materials and camera framing must survive unchanged.
    [InitializeOnLoad]
    public static class KinoInteriorLighting
    {
        const string Root = "Assets/KinoRotunda";
        const string Output = "Artifacts/KinoRotunda/InteriorLighting";
        const string Job = "KinoRotunda.InteriorBake";
        const string RigName = "Interior light • ceiling rings and arcade fill";
        static KinoInteriorLighting() { Lightmapping.bakeCompleted += Complete; }

        static GameObject Environment => GameObject.Find("KINO Rotunda • Environment");

        static void RequireScene()
        {
            if (!Environment || SceneManager.GetActiveScene().path != Root + "/Scenes/KinoRotunda.unity")
                throw new InvalidOperationException("Open the KINO scene first.");
        }

        public static void Inspect()
        {
            RequireScene();
            Directory.CreateDirectory(Output);
            var data = new Inspection
            {
                isBaking = Lightmapping.isRunning,
                sceneDirty = SceneManager.GetActiveScene().isDirty,
                probes = Probes(),
                lights = Environment.GetComponentsInChildren<Light>(true).Select(l => new Lamp
                {
                    name = l.name, enabled = l.enabled && l.gameObject.activeInHierarchy,
                    type = l.type.ToString(), mode = l.lightmapBakeType.ToString(), intensity = l.intensity,
                    position = l.transform.position, color = l.color, area = l.areaSize
                }).ToArray(),
                ledSurfaces = Environment.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => r.sharedMaterial && (r.sharedMaterial.name.Contains("LED") || r.name.Contains("Ceiling")))
                    .Select(r => $"{r.name}: {r.sharedMaterial.name}, bounds={r.bounds}, GI={r.receiveGI}, enabled={r.enabled}").ToArray()
            };
            File.WriteAllText(Output + "/inspection.json", JsonUtility.ToJson(data, true));
        }

        [MenuItem("Tools/KINO Rotunda/7 - Brighten interior (preserve reflection setup)")]
        public static void Apply()
        {
            RequireScene();
            if (Lightmapping.isRunning) throw new InvalidOperationException("Wait for the current bake.");
            Directory.CreateDirectory(Output);
            if (!File.Exists(Output + "/Before.unity"))
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Output + "/Before.unity", true);
            Inspect();
            File.WriteAllText(Output + "/probes-before.json", JsonUtility.ToJson(new ProbeList { probes = Probes() }, true));
            File.WriteAllText(Output + "/sky-before.json", EditorJsonUtility.ToJson(RenderSettings.skybox));
            File.WriteAllText("Temp/KinoRotunda.manual-control", "Preserve the user's reflections during the interior lighting pass.");
            SessionState.SetBool("KinoRotunda.Baking", false);

            var root = Environment.transform;
            var rig = root.Find(RigName);
            if (!rig) { rig = new GameObject(RigName).transform; rig.SetParent(root, false); }

            // Keep the floor strips and wall sconces on their existing material.
            var source = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/WarmLED.mat");
            const string ledPath = Root + "/Materials/CeilingLED.mat";
            var led = AssetDatabase.LoadAssetAtPath<Material>(ledPath);
            if (!led) { led = new Material(source) { name = "CeilingLED" }; AssetDatabase.CreateAsset(led, ledPath); }
            led.SetColor("_EmissionColor", new Color(1, .48f, .17f) * 14);
            led.EnableKeyword("_EMISSION");
            led.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(led);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name == "Ceiling__WarmLED"))
            {
                r.sharedMaterial = led;
                EditorUtility.SetDirty(r);
            }

            // Tangent area emitters form continuous, overlapping pools of light beneath
            // the actual ceiling rings. Baked-only lights add no per-frame light cost.
            Ring(rig, "Inner", 3.30f, 6.72f, 12, 6);
            Ring(rig, "Middle", 5.24f, 6.72f, 16, 6);
            Ring(rig, "Outer", 8.79f, 6.86f, 24, 6);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4;
                var radial = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                Area(rig, "Arcade soft fill " + i.ToString("00"), radial * 7.8f + Vector3.up * 4.4f,
                    radial * 14 + Vector3.up * 3.3f, new Vector2(4.5f, 3), 3.5f, new Color(1, .83f, .64f));
            }
            Area(rig, "Cool bounce to ceiling", new Vector3(0, .35f, 0), new Vector3(0, 7, 0),
                new Vector2(10, 10), .85f, new Color(.66f, .78f, 1));
            foreach (var l in root.GetComponentsInChildren<Light>().Where(l => !l.transform.IsChildOf(rig)))
            {
                // Absolute minimums make rerunning the command idempotent.
                if (l.name.StartsWith("Sconce")) l.intensity = Mathf.Max(l.intensity, 5.5f);
                else if (l.name.StartsWith("Downlight")) l.intensity = Mathf.Max(l.intensity, 13);
                else if (l.name.StartsWith("Cove bounce")) l.intensity = Mathf.Max(l.intensity, 2.8f);
                else if (l.name == "Display wash") l.intensity = Mathf.Max(l.intensity, 18);
                else if (l.name == "Soft warm ceiling bounce") l.intensity = Mathf.Max(l.intensity, 2.7f);
                EditorUtility.SetDirty(l);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            SessionState.SetString(Job + ".Output", Output);
            SessionState.SetBool(Job, true);
            WriteStatus("BAKING_INTERIOR");
            if (!Lightmapping.BakeAsync()) { SessionState.SetBool(Job, false); throw new InvalidOperationException("Could not start interior bake."); }
        }

        [MenuItem("Tools/KINO Rotunda/8 - Brighten display wall and draw ornament")]
        public static void ApplyStage()
        {
            RequireScene();
            if (Lightmapping.isRunning) throw new InvalidOperationException("Wait for the current bake.");
            const string output = "Artifacts/KinoRotunda/StageLighting";
            Directory.CreateDirectory(output);
            if (!File.Exists(output + "/Before.unity"))
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), output + "/Before.unity", true);
            File.WriteAllText(output + "/probes-before.json", JsonUtility.ToJson(new ProbeList { probes = Probes() }, true));
            File.WriteAllText(output + "/sky-before.json", EditorJsonUtility.ToJson(RenderSettings.skybox));
            CaptureStage(output + "/Before-closeup.png");
            KinoRotundaBuilder.Capture();
            File.Copy("Artifacts/KinoRotunda/UnityPreview.png", output + "/Before.png", true);
            File.WriteAllText("Temp/KinoRotunda.manual-control", "Preserve reflection settings during the stage lighting pass.");
            SessionState.SetBool("KinoRotunda.Baking", false);

            var root = Environment.transform;
            var key = root.GetComponentsInChildren<Light>().First(l => l.name == "Display wash");
            // Mixed direct light supplies the specular response that baked diffuse
            // lighting alone cannot provide on the metallic draw ornament.
            key.lightmapBakeType = LightmapBakeType.Mixed;
            key.intensity = 36;
            key.color = new Color(1, .84f, .65f);
            key.useColorTemperature = false;
            key.range = Mathf.Max(key.range, 10.5f);
            key.shadows = LightShadows.Soft;
            EditorUtility.SetDirty(key);

            const string name = "Display light • wall wash and ornament fill";
            var rig = root.Find(name);
            if (!rig) { rig = new GameObject(name).transform; rig.SetParent(root, false); }
            foreach (float side in new[] { -1f, 1f })
                Area(rig, side < 0 ? "Wall softbox left" : "Wall softbox right",
                    new Vector3(side * 3.1f, 4.2f, 9.1f), new Vector3(side * 3.2f, 3.5f, 12.25f),
                    new Vector2(2.5f, 3.5f), 6, new Color(1, .85f, .66f));
            Area(rig, "Ornament soft fill", new Vector3(0, 2.7f, 8.4f), new Vector3(0, .95f, 10.67f),
                new Vector2(2, 1.25f), 4, new Color(1, .79f, .51f));

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            SessionState.SetString(Job + ".Output", output);
            SessionState.SetBool(Job, true);
            WriteStatus("BAKING_STAGE");
            if (!Lightmapping.BakeAsync()) { SessionState.SetBool(Job, false); throw new InvalidOperationException("Could not start stage bake."); }
        }

        // Also callable after an editor reload if Unity's completion callback was lost.
        public static void FinishStage()
        {
            RequireScene();
            if (Lightmapping.isRunning) throw new InvalidOperationException("Wait for the stage bake.");
            if (LightmapSettings.lightmaps.Length == 0) throw new InvalidOperationException("Bake stage lighting first.");
            SessionState.SetString(Job + ".Output", "Artifacts/KinoRotunda/StageLighting");
            SessionState.SetBool(Job, false);
            EditorApplication.delayCall -= FinishLighting;
            FinishLighting();
        }

        static void CaptureStage(string path)
        {
            var camera = Camera.main;
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                camera.transform.position = new Vector3(0, 2.75f, 5.5f);
                camera.transform.LookAt(new Vector3(0, 2.95f, 12.25f));
                camera.fieldOfView = 60;
                KinoRotundaBuilder.Capture();
                File.Copy("Artifacts/KinoRotunda/UnityPreview.png", path, true);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
            }
        }

        static void Ring(Transform rig, string label, float radius, float height, int count, float intensity)
        {
            for (int i = 0; i < count; i++)
            {
                float a = i * 2 * Mathf.PI / count;
                var position = new Vector3(Mathf.Sin(a) * radius, height, Mathf.Cos(a) * radius);
                var l = Area(rig, label + " ring " + i.ToString("00"), position, position + Vector3.down,
                    new Vector2(2 * Mathf.PI * radius / count * 1.03f, .5f), intensity, new Color(1, .79f, .54f));
                l.transform.rotation = Quaternion.Euler(90, a * Mathf.Rad2Deg, 0);
            }
        }

        static Light Area(Transform parent, string name, Vector3 position, Vector3 target, Vector2 size, float intensity, Color color)
        {
            var t = parent.Find(name);
            if (!t) { t = new GameObject(name).transform; t.SetParent(parent, false); }
            t.position = position;
            t.LookAt(target);
            var l = t.GetComponent<Light>();
            if (!l) l = t.gameObject.AddComponent<Light>();
            l.type = LightType.Rectangle;
            l.areaSize = size;
            l.color = color;
            l.intensity = intensity;
            l.range = 20;
            l.bounceIntensity = 1;
            l.lightmapBakeType = LightmapBakeType.Baked;
            l.shadows = LightShadows.Soft;
            EditorUtility.SetDirty(l);
            return l;
        }

        static void Complete()
        {
            if (!SessionState.GetBool(Job, false)) return;
            SessionState.SetBool(Job, false);
            EditorApplication.delayCall += FinishLighting;
        }

        static void FinishLighting()
        {
                try
                {
                    RequireScene();
                    string output = SessionState.GetString(Job + ".Output", Output);
                    // Keep the user's texture assignments and all probe settings.
                    // Baking diffuse illumination must not reset or re-enable a probe.
                    EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                    if (output.EndsWith("/StageLighting", StringComparison.Ordinal))
                        CaptureStage(output + "/After-closeup.png");
                    KinoRotundaBuilder.Capture();
                    File.Copy("Artifacts/KinoRotunda/UnityPreview.png", output + "/After.png", true);
                    KinoRotundaBuilder.Validate();
                    var after = JsonUtility.ToJson(new ProbeList { probes = Probes() }, true);
                    File.WriteAllText(output + "/probes-after.json", after);
                    if (after != File.ReadAllText(output + "/probes-before.json"))
                        throw new InvalidOperationException("Reflection settings changed during the bake; inspect the before/after report.");
                    if (EditorJsonUtility.ToJson(RenderSettings.skybox) != File.ReadAllText(output + "/sky-before.json"))
                        throw new InvalidOperationException("Sky settings changed during the bake.");
                    WriteStatus("LIGHTING_COMPLETE — lighting saved, reflection settings preserved");
                }
                catch (Exception e) { WriteStatus("INTERIOR_ERROR " + e); Debug.LogException(e); }
        }

        static ProbeState[] Probes() => Environment.GetComponentsInChildren<ReflectionProbe>(true)
            .OrderBy(p => p.GetInstanceID()).Select(p => new ProbeState
            {
                name = p.name, enabled = p.enabled, active = p.gameObject.activeSelf,
                position = p.transform.position, rotation = p.transform.rotation, scale = p.transform.localScale,
                settings = EditorJsonUtility.ToJson(p)
            }).ToArray();

        static void WriteStatus(string text)
        {
            File.WriteAllText("Artifacts/KinoRotunda/unity-status.txt", DateTime.Now.ToString("s") + " " + text);
            Debug.Log("[KinoRotunda] " + text);
        }

        [Serializable] class Inspection { public bool isBaking, sceneDirty; public ProbeState[] probes; public Lamp[] lights; public string[] ledSurfaces; }
        [Serializable] class ProbeList { public ProbeState[] probes; }
        [Serializable] class ProbeState { public string name, settings; public bool enabled, active; public Vector3 position, scale; public Quaternion rotation; }
        [Serializable] class Lamp { public string name, type, mode; public bool enabled; public float intensity; public Vector3 position; public Color color; public Vector2 area; }
    }
}
