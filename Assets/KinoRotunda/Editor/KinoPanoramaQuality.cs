using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace KinoRotunda.Editor
{
    public static class KinoPanoramaQuality
    {
        [MenuItem("Tools/KINO Rotunda/Use full-resolution LDR sky (8K)")]
        public static void ImproveCurrent()
        {
            KinoPanoramaSkybox.RequireScene();
            var sky = RenderSettings.skybox;
            if (!sky || sky.shader.name != "KINO/Cropped Panorama Skybox")
                throw new InvalidOperationException("Select an imported cropped panorama sky first.");
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(sky)).Replace('\\', '/');
            string provenance = folder + "/Import.json";
            if (!File.Exists(provenance)) throw new InvalidOperationException("The original panorama import report is missing.");
            var imported = JsonUtility.FromJson<KinoPanoramaSkybox.Result>(File.ReadAllText(provenance));
            string source = imported.sourcePath;
            string extension = Path.GetExtension(source).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
                throw new InvalidOperationException("This option is for original LDR photographs only; keep HDR/EXR skies in HDR.");
            var importer = AssetImporter.GetAtPath(source) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Original photograph is unavailable.");
            var before = sky.GetTexture("_MainTex") as Texture2D;
            float rotation = sky.GetFloat("_Rotation"), exposure = sky.GetFloat("_Exposure");
            var sun = RenderSettings.sun;
            string sunState = sun ? EditorJsonUtility.ToJson(sun) + EditorJsonUtility.ToJson(sun.transform) : "";
            string output = KinoPanoramaSkybox.Output + "/Quality-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            Directory.CreateDirectory(output);
            File.Copy(AssetDatabase.GetAssetPath(sky), output + "/Skybox-before.mat");
            File.Copy(source + ".meta", output + "/Source-before.meta");
            string beforeDescription = Describe(before);
            Capture(output + "/Before.png", rotation);

            // Sampling an sRGB texture decodes it to linear in this Linear project. The
            // PNG has no extra HDR range to preserve. ASTC 4x4 at 8K uses roughly the
            // same device storage as the previous RGB9E5 texture at half resolution.
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 8192;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.isReadable = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.compressionQuality = 100;
            foreach (string platform in new[] { "Android", "Standalone" })
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                settings.name = platform;
                settings.overridden = true;
                settings.maxTextureSize = 8192;
                settings.format = platform == "Android" ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.BC7;
                settings.textureCompression = TextureImporterCompression.CompressedHQ;
                settings.compressionQuality = 100;
                settings.crunchedCompression = false;
                importer.SetPlatformTextureSettings(settings);
            }
            KinoPanoramaSkybox.WriteStatus("Importing full-resolution 8K LDR sky...");
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(source);
            importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
            if (!texture || texture.width != Math.Min(8192, sourceWidth))
                throw new InvalidOperationException("Imported sky did not retain the requested resolution.");
            Undo.RecordObject(sky, "Use full-resolution panorama");
            sky.SetTexture("_MainTex", texture);
            EditorUtility.SetDirty(sky);
            AssetDatabase.SaveAssets();
            DynamicGI.UpdateEnvironment();
            Capture(output + "/After.png", rotation);
            if (sky.GetFloat("_Rotation") != rotation || sky.GetFloat("_Exposure") != exposure ||
                (sun && sunState != EditorJsonUtility.ToJson(sun) + EditorJsonUtility.ToJson(sun.transform)))
                throw new InvalidOperationException("Sky rotation, exposure or sun changed during quality upgrade.");
            File.WriteAllText(output + "/Report.txt", "Before: " + beforeDescription + "\nAfter: " + Describe(texture) +
                $"\nSource: {sourceWidth} x {sourceHeight}\nRotation preserved: {rotation}\nExposure preserved: {exposure}" +
                "\nPASS: source resolution, sky rotation, exposure and scene sun preserved.\nAndroid: ASTC 4x4; Standalone: BC7. Mipmaps retained for VR stability.\n");
            File.WriteAllText(KinoPanoramaSkybox.Output + "/last-quality.txt", output);
            KinoPanoramaSkybox.WriteStatus("Complete: full-resolution 8K LDR sky saved. Rotation, exposure and sun preserved. Comparison: " + output);
        }

        static string Describe(Texture2D texture) => texture ? $"{texture.width} x {texture.height}, {texture.format}, {texture.mipmapCount} mip levels" : "none";

        static void Capture(string path, float rotation)
        {
            var go = new GameObject("Panorama detail comparison") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 42;
            camera.aspect = 16f / 9f;
            camera.allowHDR = true;
            Vector3 direction = KinoPanoramaAnalysis.UvToDirection(new Vector2(.46f, .5f - 8f / 180));
            direction = Quaternion.Euler(0, -rotation, 0) * direction;
            camera.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var display = RenderTexture.GetTemporary(1920, 1080, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var png = new Texture2D(1920,1080,TextureFormat.RGB24,false,false);
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                camera.targetTexture = rt;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
                GL.sRGBWrite = true;
                Graphics.Blit(rt, display);
                RenderTexture.active = display;
                png.ReadPixels(new Rect(0,0,1920,1080),0,0);
                png.Apply();
                File.WriteAllBytes(path,png.EncodeToPNG());
            }
            finally
            {
                GL.sRGBWrite = srgb;
                RenderTexture.active = previous;
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                RenderTexture.ReleaseTemporary(display);
                Object.DestroyImmediate(png);
                Object.DestroyImmediate(go);
            }
        }
    }
}
