using System;
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
    /// <summary>Applies the sunset reference look to the existing room without rebuilding geometry.</summary>
    public static class KinoReferenceLighting
    {
        const string Root = "Assets/KinoRotunda";
        const string Output = "Artifacts/KinoRotunda/ReferenceLighting";
        const string HdrPath = Root + "/Textures/HDRI/qwantani_sunset_4k.hdr";

        [MenuItem("Tools/KINO Rotunda/6 - Apply reference sunset lighting")]
        public static void Apply()
        {
            if (SceneManager.GetActiveScene().path != Root + "/Scenes/KinoRotunda.unity")
                throw new InvalidOperationException("Open the KINO scene first.");
            if (Lightmapping.isRunning) throw new InvalidOperationException("Wait for the lighting bake to finish.");
            Directory.CreateDirectory(Output);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Output + "/Before-appearance.unity", true);
            Configure();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            KinoRotundaBuilder.Bake();
        }

        public static void Configure()
        {
            var root = GameObject.Find("KINO Rotunda • Environment");
            if (!root) throw new InvalidOperationException("Missing KINO environment.");
            ConfigureSky();
            ConfigureMaterials();
            ConfigureLights(root.transform);
            ConfigureProbes(root.transform);
            ConfigureVolume();
            var settings = Lightmapping.lightingSettings;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.lightmapResolution = 18;
            settings.lightmapMaxSize = 2048;
            settings.lightmapPadding = 4;
            settings.directSampleCount = 64;
            settings.indirectSampleCount = 256;
            settings.environmentSampleCount = 256;
            settings.maxBounces = 4;
            settings.indirectScale = 1.05f;
            settings.ao = true;
            settings.aoMaxDistance = .32f;
            settings.aoExponentIndirect = .7f;
            settings.aoExponentDirect = 0;
            LightmapSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
            EditorUtility.SetDirty(settings);
            // Preserve transforms, UVs, animation pivots and all existing collision.
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                bool ball = r.name.StartsWith("Ball_", StringComparison.Ordinal);
                r.receiveGI = ball ? ReceiveGI.LightProbes : ReceiveGI.Lightmaps;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, ball ? 0 :
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                KinoLottery.ConfigureRenderer(r);
            }
            if (Camera.main)
            {
                var data = Camera.main.GetUniversalAdditionalCameraData();
                Camera.main.allowHDR = true;
                data.renderPostProcessing = true;
                data.dithering = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
            DynamicGI.UpdateEnvironment();
        }

        static void ConfigureSky()
        {
            AssetDatabase.ImportAsset(HdrPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(HdrPath);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = false;
            importer.maxTextureSize = 4096;
            importer.mipmapEnabled = true;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            const string path = Root + "/Materials/KinoSunsetSky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!sky)
            {
                sky = new Material(Shader.Find("Skybox/Panoramic")) { name = "KinoSunsetSky" };
                AssetDatabase.CreateAsset(sky, path);
            }
            sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(HdrPath));
            sky.SetFloat("_Mapping", 1);
            sky.SetFloat("_ImageType", 0);
            sky.SetFloat("_Exposure", .55f);
            // The HDR sun is at U=.59985, elevation 6.06 degrees. This rotation
            // places it 28 degrees to the left of the main camera's forward view.
            sky.SetFloat("_Rotation", 154);
            sky.SetColor("_Tint", new Color(.50f, .50f, .50f, 1));
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = .8f;
            RenderSettings.reflectionIntensity = 1;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 512;
            RenderSettings.fog = false;
            EditorUtility.SetDirty(sky);
        }

        public static void ConfigureMaterials()
        {
            Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + name + ".mat");
            foreach (string name in new[] { "NeroMarble", "IvoryMarble" })
            {
                var m = Mat(name);
                m.SetColor("_BaseColor", name == "NeroMarble" ? new Color(1f, 1.15f, 1.5f) : new Color(.92f, .91f, .90f));
                m.SetFloat("_Metallic", name == "NeroMarble" ? .06f : .015f);
                m.SetFloat("_Smoothness", name == "NeroMarble" ? .94f : .92f);
                m.SetFloat("_BumpScale", .12f);
                m.SetFloat("_EnvironmentReflections", 1);
                m.SetFloat("_SpecularHighlights", 1);
                m.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
                EditorUtility.SetDirty(m);
            }
            var gold = Mat("BrushedGold");
            gold.SetColor("_BaseColor", new Color(.83f, .57f, .25f));
            gold.SetFloat("_Metallic", .9f);
            gold.SetFloat("_Smoothness", .86f);
            EditorUtility.SetDirty(gold);
            var bronze = Mat("BronzeShadow");
            bronze.SetColor("_BaseColor", new Color(.24f, .15f, .065f));
            bronze.SetFloat("_Smoothness", .78f);
            EditorUtility.SetDirty(bronze);
            var led = Mat("WarmLED");
            led.SetColor("_BaseColor", new Color(1, .72f, .4f));
            led.EnableKeyword("_EMISSION");
            led.SetColor("_EmissionColor", new Color(1, .37f, .10f) * 4);
            led.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(led);
        }

        static void ConfigureLights(Transform root)
        {
            foreach (var l in root.GetComponentsInChildren<Light>())
            {
                l.lightmapBakeType = LightmapBakeType.Baked;
                l.shadows = LightShadows.Soft;
                l.bounceIntensity = 1;
                l.shadowRadius = .22f;
                l.useColorTemperature = false;
                l.color = new Color(1, .72f, .43f);
                l.colorTemperature = 2900;
                if (l.name.StartsWith("Sconce")) { l.intensity = 4.2f; l.range = 4.8f; }
                else if (l.name.StartsWith("Downlight")) { l.intensity = 8.5f; l.range = 9; l.color = new Color(1, .82f, .6f); }
                else if (l.name.StartsWith("Cove bounce")) { l.intensity = 1.8f; l.range = 7; }
                else if (l.name == "Display wash") { l.intensity = 12; }
                else if (l.name == "Oculus daylight") { l.intensity = 3.2f; l.color = new Color(.42f, .58f, 1); }
                else if (l.name == "Soft warm ceiling bounce") { l.intensity = 1.7f; l.color = new Color(1, .74f, .53f); }
                else if (l.type == LightType.Directional)
                {
                    l.intensity = 1.15f;
                    l.color = new Color(1, .73f, .43f);
                    l.transform.rotation = Quaternion.Euler(6.06f, 152, 0);
                    RenderSettings.sun = l;
                }
                EditorUtility.SetDirty(l);
            }
        }

        static void ConfigureProbes(Transform root)
        {
            ReflectionProbe Probe(string label, Vector3 position, Vector3 size, int importance)
            {
                string name = "Reflection • " + label;
                var t = root.Find(name);
                if (!t) { t = new GameObject(name).transform; t.SetParent(root, false); }
                t.position = position;
                var p = t.GetComponent<ReflectionProbe>();
                if (!p) p = t.gameObject.AddComponent<ReflectionProbe>();
                p.resolution = 512;
                p.mode = ReflectionProbeMode.Baked;
                p.hdr = true;
                p.boxProjection = true;
                p.size = size;
                p.center = new Vector3(0, 3.7f - position.y, 0);
                p.blendDistance = 2;
                p.nearClipPlane = .06f;
                p.farClipPlane = 100;
                p.clearFlags = ReflectionProbeClearFlags.Skybox;
                p.cullingMask = -1;
                p.intensity = 1;
                p.importance = importance;
                EditorUtility.SetDirty(p);
                return p;
            }
            Probe("Centre", new Vector3(0, 2.7f, 0), new Vector3(30, 8, 30), 1);
            Probe("Display", new Vector3(0, 2.6f, 9), new Vector3(13, 8, 11), 4);
            // Box projection must describe the room's walls and ceiling, not a thin
            // floor influence slab, otherwise every reflection collapses into streaks.
            var floor = Probe("Floor", new Vector3(0, .18f, 0), new Vector3(30, 7.6f, 30), 3);
            floor.center = new Vector3(0, 3.6f, 0);
            // Floor meshes span the room; anchor their probe selection at the polished surface.
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.StartsWith("Floor")))
                renderer.probeAnchor = floor.transform;
        }

        static void ConfigureVolume()
        {
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "/Settings/KinoAtmosphere.asset");
            if (p.TryGet<Bloom>(out var bloom))
            {
                bloom.threshold.Override(1);
                bloom.intensity.Override(.45f);
                bloom.scatter.Override(.68f);
                bloom.highQualityFiltering.Override(true);
                EditorUtility.SetDirty(bloom);
            }
            if (p.TryGet<ColorAdjustments>(out var color))
            {
                color.postExposure.Override(.10f);
                color.contrast.Override(14);
                color.saturation.Override(-8);
                EditorUtility.SetDirty(color);
            }
            if (p.TryGet<Vignette>(out var vignette))
            {
                vignette.intensity.Override(.13f);
                vignette.smoothness.Override(.5f);
                EditorUtility.SetDirty(vignette);
            }
            EditorUtility.SetDirty(p);
        }

        public static void Audit()
        {
            Directory.CreateDirectory(Output);
            var root = GameObject.Find("KINO Rotunda • Environment");
            var info = new System.Text.StringBuilder();
            info.AppendLine("Sky: " + RenderSettings.skybox.name);
            info.AppendLine("Baked lightmaps: " + LightmapSettings.lightmaps.Length);
            foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                info.AppendLine($"Volume: {volume.name}, enabled={volume.enabled}, layer={volume.gameObject.layer}, weight={volume.weight}, profile={volume.sharedProfile}, instanced={volume.HasInstantiatedProfile()}");
            var activeBloom = VolumeManager.instance.stack.GetComponent<Bloom>();
            info.AppendLine($"Active bloom: {activeBloom.intensity.value}, threshold={activeBloom.threshold.value}");
            info.AppendLine("Active tonemapping: " + VolumeManager.instance.stack.GetComponent<Tonemapping>().mode.value);
            foreach (var probe in root.GetComponentsInChildren<ReflectionProbe>())
                info.AppendLine($"{probe.name}: {probe.mode}, {probe.resolution}px, texture={probe.texture}, bounds={probe.bounds}");
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.StartsWith("Floor")))
            {
                var probes = new System.Collections.Generic.List<ReflectionProbeBlendInfo>();
                r.GetClosestReflectionProbes(probes);
                info.AppendLine($"{r.name}: {r.reflectionProbeUsage}, baked={r.lightmapIndex}, probes=" +
                    string.Join(", ", probes.Select(p => p.probe.name + "@" + p.weight)));
            }
            File.WriteAllText(Output + "/lighting-audit.txt", info.ToString());
        }
    }
}
