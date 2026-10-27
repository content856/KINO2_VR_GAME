// One-shot Unity editor utility. Temporarily compiled under Assets/Editor for this import.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace KinoRotunda.Editor
{
    public static class ApplyMatchedAthens
    {
        const string Folder = "Assets/KinoRotunda/Textures/Panoramas/AthensColorMatched";
        const string Output = "Artifacts/KinoRotunda/Panorama/AthensColorMatched-20261027";

        public static void CaptureBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(KinoPanoramaSkybox.ScenePath);
                if (AssetDatabase.GetAssetPath(RenderSettings.skybox) != Folder + "/Skybox.mat")
                    throw new InvalidOperationException("The saved scene has not retained the matched skybox.");
                Capture("Sky-toward-sun.png", -RenderSettings.sun.transform.forward, false);
                Capture("Player-view.png", new Vector3(0, .85f, 11.8f), true);
                Capture("Right-arch-view.png", new Vector3(12, .8f, 15), true);
                File.AppendAllText(Output + "/Report.txt", "PASS: reopened saved scene and recaptured with synchronous shader compilation.\n");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void RunBatch()
        {
            try
            {
                Directory.CreateDirectory(Output);
                var scene = EditorSceneManager.OpenScene(KinoPanoramaSkybox.ScenePath);
                KinoPanoramaSkybox.RequireScene();
                var sun = RenderSettings.sun;
                if (!sun) throw new InvalidOperationException("Scene sun is missing.");
                var sunState = EditorJsonUtility.ToJson(sun) + EditorJsonUtility.ToJson(sun.transform);
                var previous = AssetDatabase.GetAssetPath(RenderSettings.skybox);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/AthensColorMatched.png");
                var importer = AssetImporter.GetAtPath(Folder + "/AthensColorMatched.png") as TextureImporter;
                if (!texture || !importer || texture.width != 8192 || texture.height != 4096)
                    throw new InvalidOperationException("Expected an imported 8192 x 4096 texture.");
                if (!importer.sRGBTexture || !importer.mipmapEnabled || importer.isReadable ||
                    importer.wrapModeU != TextureWrapMode.Repeat || importer.wrapModeV != TextureWrapMode.Clamp)
                    throw new InvalidOperationException("Panorama texture sampling settings are incorrect.");
                foreach (string platform in new[] { "Android", "Standalone" })
                {
                    var settings = importer.GetPlatformTextureSettings(platform);
                    var format = platform == "Android" ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.BC7;
                    if (!settings.overridden || settings.maxTextureSize != 8192 || settings.format != format)
                        throw new InvalidOperationException("Unexpected panorama format on " + platform);
                }
                EditorSceneManager.SaveScene(scene, Output + "/Before.unity", true);
                var shader = Shader.Find("Skybox/Panoramic");
                if (!shader || !shader.isSupported) throw new InvalidOperationException("Panoramic shader unavailable.");
                var uv = new Vector2(.914f, .535f);
                float rotation = KinoPanoramaAnalysis.RotationToMatchSun(uv, -sun.transform.forward);
                var material = new Material(shader) { name = "Athens Color Matched Skybox" };
                material.SetTexture("_MainTex", texture);
                material.SetFloat("_Mapping", 1);
                material.SetFloat("_ImageType", 0);
                material.SetFloat("_Layout", 0);
                material.SetFloat("_Rotation", rotation);
                material.SetFloat("_Exposure", 1);
                material.SetColor("_Tint", Color.gray);
                material.SetOverrideTag("KinoPanoramaBaseYaw", rotation.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                var materialPath = Folder + "/Skybox.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(materialPath))
                    throw new InvalidOperationException("Material already exists; refusing to overwrite the previous application.");
                AssetDatabase.CreateAsset(material, materialPath);
                RenderSettings.skybox = material;
                DynamicGI.UpdateEnvironment();
                if (sunState != EditorJsonUtility.ToJson(sun) + EditorJsonUtility.ToJson(sun.transform))
                    throw new InvalidOperationException("Sun settings changed.");
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save scene.");
                AssetDatabase.SaveAssets();
                Capture("Sky-toward-sun.png", -sun.transform.forward, false);
                Capture("Player-view.png", new Vector3(0, .85f, 11.8f), true);
                Capture("Right-arch-view.png", new Vector3(12, .8f, 15), true);
                float elevation = Mathf.Asin(-sun.transform.forward.y) * Mathf.Rad2Deg;
                File.WriteAllText(Output + "/Report.txt",
                    "PASS: matched Athens skybox assigned and saved in KinoRotunda.\n" +
                    "Previous material: " + previous + "\nNew material: " + materialPath +
                    "\nTexture: " + texture.width + " x " + texture.height + ", " + texture.format +
                    "\nAndroid: ASTC 4x4 at 8192. Standalone: BC7 at 8192. sRGB, mipmaps, no CPU read copy.\n" +
                    "Exposure: 1. Rotation: " + rotation + " degrees.\n" +
                    "Photographed sun elevation: 6.3 degrees; scene sun elevation: " + elevation + ".\n" +
                    "Sun transform, intensity and color preserved. Existing baked lighting/reflection maps retained.\n" +
                    "Native URP captures: Sky-toward-sun.png, Player-view.png, Right-arch-view.png.\n");
                Debug.Log("[AthensSky] PASS: scene saved and three native renders captured.");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/Error.txt", e.ToString());
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static void Capture(string filename, Vector3 direction, bool sceneGeometry)
        {
            var go = new GameObject("Athens sky verification camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(0, 1.35f, 0);
            camera.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            camera.fieldOfView = sceneGeometry ? 65 : 80;
            camera.aspect = 16f / 9f;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 200;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.Skybox;
            if (!sceneGeometry) camera.cullingMask = 0;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = sceneGeometry;
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var rt = RenderTexture.GetTemporary(1920,1080,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            var display = RenderTexture.GetTemporary(1920,1080,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var png = new Texture2D(1920,1080,TextureFormat.RGB24,false,false);
            try
            {
                camera.targetTexture = rt;
                // Warm the render pipeline and newly loaded material variants before capture.
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
                GL.sRGBWrite = true;
                Graphics.Blit(rt,display);
                RenderTexture.active = display;
                png.ReadPixels(new Rect(0,0,1920,1080),0,0);
                png.Apply();
                File.WriteAllBytes(Output + "/" + filename,png.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                ShaderUtil.allowAsyncCompilation = asyncShaders;
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                RenderTexture.ReleaseTemporary(rt);
                RenderTexture.ReleaseTemporary(display);
                Object.DestroyImmediate(png);
                Object.DestroyImmediate(go);
            }
        }
    }
}
