using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KinoRotunda.Editor
{
    /// <summary>Explicit import jobs only. Importing scripts never replaces the scene sky.</summary>
    [InitializeOnLoad]
    public static class KinoPanoramaSkybox
    {
        public const string ScenePath = "Assets/KinoRotunda/Scenes/KinoRotunda.unity";
        public const string Root = "Assets/KinoRotunda/Textures/Panoramas";
        public const string Output = "Artifacts/KinoRotunda/Panorama";
        const string RequestPath = "Temp/KinoPanorama.request";
        const string JobKey = "KinoPanorama.Bake";
        static double nextPoll;
        static bool busy;
        public static string Status { get; private set; } = "Drop a 2:1 panorama to begin.";

        [Serializable]
        public class Options
        {
            public bool alignSun = true;
            public bool manualSun;
            public Vector2 sunUV = new Vector2(.5f, .55f);
            public float rotationOffset;
            public float exposure = 1;
            public int desktopSize = 8192;
            public int questSize = 4096;
            public bool bakeLighting = true;
            public bool updateReflections = true;
            public string sourceUrl = "";
            public bool cropped360;
            public float cropBottomElevation = -30;
        }

        [Serializable]
        public class Result
        {
            public string sourcePath, texturePath, materialPath, sourceUrl, note;
            public bool convertedLdr, aligned;
            public Vector2 sunUV;
            public float confidence, rotation, sunElevationDifference;
            public Quaternion originalSunRotation;
            public bool cropped360;
            public float cropBottomElevation, cropVerticalDegrees;
        }

        [Serializable]
        class BakeJob
        {
            public string scenePath, materialPath, folder;
            public bool updateReflections;
            public Quaternion sunRotation;
            public string sunGlobalId;
        }

        [Serializable]
        class Request { public string command, sourcePath; public Options options = new Options(); }

        static KinoPanoramaSkybox()
        {
            EditorApplication.update += Poll;
            Lightmapping.bakeCompleted += OnBakeCompleted;
            Lightmapping.bakeCancelled += OnBakeCancelled;
            EditorApplication.delayCall += () =>
            {
                if (!Lightmapping.isRunning && !string.IsNullOrEmpty(SessionState.GetString(JobKey, "")))
                {
                    SessionState.EraseString(JobKey);
                    WriteStatus("Previous bake was interrupted by a reload. Reapply the panorama to finish lighting/reflections.");
                }
            };
        }

        public static bool IsBusy => busy || Lightmapping.isRunning ||
            !string.IsNullOrEmpty(SessionState.GetString(JobKey, ""));

        public static void RequireScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Exit Play Mode and wait for compilation first.");
            if (SceneManager.GetActiveScene().path != ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Open only the KinoRotunda scene before applying a panorama.");
            if (Lightmapping.isRunning || !string.IsNullOrEmpty(SessionState.GetString(JobKey, "")))
                throw new InvalidOperationException("Wait for the current lighting job to finish.");
        }

        public static bool Supported(string path) => new[] { ".jpg", ".jpeg", ".png", ".hdr", ".exr" }
            .Contains(Path.GetExtension(path).ToLowerInvariant());

        public static Result Import(string sourcePath, Options options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!File.Exists(sourcePath) || !Supported(sourcePath))
                throw new ArgumentException("Choose a local JPG, PNG, HDR or EXR panorama.");
            if (options.desktopSize != 4096 && options.desktopSize != 8192)
                throw new ArgumentException("Desktop resolution must be 4096 or 8192.");
            if (options.questSize != 2048 && options.questSize != 4096)
                throw new ArgumentException("Quest resolution must be 2048 or 4096.");
            if (QualitySettings.activeColorSpace != ColorSpace.Linear)
                throw new InvalidOperationException("This importer requires the project's Linear colour space.");

            string hash;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(sourcePath))
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").Substring(0, 12).ToLowerInvariant();
            string stem = new string(Path.GetFileNameWithoutExtension(sourcePath)
                .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').Take(64).ToArray());
            string folder = Root + "/" + stem + "-" + hash + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            string original = folder + "/Source" + ext;
            Directory.CreateDirectory(folder);
            if (!File.Exists(original)) File.Copy(sourcePath, original);
            AssetDatabase.ImportAsset(original, ImportAssetOptions.ForceSynchronousImport);
            bool ldr = ext == ".png" || ext == ".jpg" || ext == ".jpeg";
            var importer = (TextureImporter)AssetImporter.GetAtPath(original);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            if (width < 512 || height < 256 || (!options.cropped360 && Math.Abs(width - height * 2) > 2))
                throw new ArgumentException($"Expected a full mono 360 x 180 degree panorama in 2:1 layout; got {width} x {height}. No scene changes made.");
            float verticalDegrees = 360f * height / width;
            if (options.cropped360 && (!options.manualSun || verticalDegrees >= 180 ||
                options.cropBottomElevation < -90 || options.cropBottomElevation + verticalDegrees > 90))
                throw new ArgumentException("A cropped 360 strip needs a picked sun and valid bottom elevation; its angular scale is derived from the original aspect ratio.");
            ConfigureImporter(importer, ldr, options, ldr);
            string texturePath = original;
            if (ldr)
            {
                texturePath = folder + "/Environment.exr";
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(original);
                Texture2D linear = null;
                try
                {
                    linear = ReadLinear(texture, texture.width, texture.height);
                    File.WriteAllBytes(texturePath, linear.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
                }
                finally { if (linear) Object.DestroyImmediate(linear); }
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureImporter((TextureImporter)AssetImporter.GetAtPath(texturePath), false, options, false);
            }

            var result = new Result
            {
                sourcePath = original, texturePath = texturePath, convertedLdr = ldr,
                sourceUrl = options.sourceUrl,
                cropped360 = options.cropped360, cropBottomElevation = options.cropBottomElevation,
                cropVerticalDegrees = verticalDegrees,
                note = ldr ? "Converted sRGB to linear half-float EXR. Original LDR dynamic range is unchanged." : "Original HDR values preserved."
            };
            var panorama = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            Texture2D sample = null;
            try
            {
                sample = ReadLinear(panorama, 1024, 512);
                var estimate = KinoPanoramaAnalysis.EstimateSun(sample);
                result.sunUV = options.manualSun ? options.sunUV : estimate.uv;
                if (options.cropped360)
                    result.sunUV.y = .5f + (options.cropBottomElevation + options.sunUV.y * verticalDegrees) / 180f;
                result.confidence = options.manualSun ? 1 : estimate.confidence;
                result.aligned = options.alignSun && (options.manualSun || estimate.reliable);
            }
            finally { if (sample) Object.DestroyImmediate(sample); }
            return result;
        }

        static void ConfigureImporter(TextureImporter importer, bool srgb, Options options, bool sourceForConversion)
        {
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = srgb;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = options.desktopSize;
            importer.isReadable = false;
            importer.mipmapEnabled = !sourceForConversion;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 0;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.textureCompression = sourceForConversion ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            var android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            // Do not reduce the intermediate LDR texture before EXR conversion on Android.
            android.overridden = !sourceForConversion;
            android.maxTextureSize = options.questSize;
            // Automatic Android HDR can become RGBM packed into LDR ASTC. Native shared-exponent
            // RGB9E5 keeps HDR radiance directly sampleable for analysis and supported on Quest.
            android.format = srgb ? TextureImporterFormat.Automatic : TextureImporterFormat.RGB9E5;
            android.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        public static Texture2D ReadLinear(Texture source, int width, int height)
        {
            var previous = RenderTexture.active;
            bool srgbWrite = GL.sRGBWrite;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            Texture2D copy = null;
            try
            {
                GL.sRGBWrite = false;
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply(false, false);
                return copy;
            }
            catch { if (copy) Object.DestroyImmediate(copy); throw; }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgbWrite;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        public static Result ApplyFile(string sourcePath, Options options, bool saveScene = true)
        {
            RequireScene();
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!saveScene && (options.bakeLighting || options.updateReflections))
                throw new ArgumentException("An unsaved preview cannot bake lighting or reflections.");
            var scene = SceneManager.GetActiveScene();
            var sun = RenderSettings.sun;
            if (options.alignSun && (!sun || sun.type != LightType.Directional))
                throw new InvalidOperationException("Assign the scene's Directional Light in Lighting > Environment > Sun Source first.");
            Quaternion sunRotation = sun ? sun.transform.rotation : Quaternion.identity;
            var previousSky = RenderSettings.skybox;
            float previousRotation = previousSky && previousSky.HasProperty("_Rotation") ? previousSky.GetFloat("_Rotation") : 0;
            if (previousSky && float.TryParse(previousSky.GetTag("KinoPanoramaBaseYaw", false, ""), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float storedBase)) previousRotation = storedBase;
            WriteStatus("Importing panorama...");
            Result result = Import(sourcePath, options);
            result.originalSunRotation = sunRotation;
            result.rotation = previousRotation;
            if (result.aligned && sun)
            {
                result.rotation = KinoPanoramaAnalysis.RotationToMatchSun(result.sunUV, -sun.transform.forward);
                float photoElevation = (result.sunUV.y - .5f) * 180;
                float lightElevation = Mathf.Asin(Mathf.Clamp(-sun.transform.forward.y, -1, 1)) * Mathf.Rad2Deg;
                result.sunElevationDifference = photoElevation - lightElevation;
            }
            else if (options.alignSun)
                result.note += " Sun detection uncertain: kept previous yaw. Click the sun in the preview to align it.";
            result.rotation = Mathf.Repeat(result.rotation + options.rotationOffset, 360);
            Directory.CreateDirectory(Output);
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            EditorSceneManager.SaveScene(scene, Output + "/Before-" + id + ".unity", true);
            string folder = Path.GetDirectoryName(result.texturePath).Replace('\\', '/');
            // Each application has its own material, so the previous saved sky and Undo remain valid.
            result.materialPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/Skybox.mat");
            var shader = Shader.Find(options.cropped360 ? "KINO/Cropped Panorama Skybox" : "Skybox/Panoramic");
            if (!shader) throw new InvalidOperationException("Unity's Skybox/Panoramic shader is unavailable.");
            var material = new Material(shader) { name = Path.GetFileName(folder) + " Sky" };
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(result.texturePath));
            if (options.cropped360)
            {
                material.SetFloat("_BottomElevation", result.cropBottomElevation);
                material.SetFloat("_VerticalDegrees", result.cropVerticalDegrees);
            }
            else
            {
                material.SetFloat("_Mapping", 1);
                material.SetFloat("_ImageType", 0);
                material.SetFloat("_Layout", 0);
            }
            material.SetFloat("_Rotation", result.rotation);
            material.SetFloat("_Exposure", Mathf.Clamp(options.exposure, 0, 8));
            material.SetColor("_Tint", Color.gray);
            material.SetOverrideTag("KinoPanoramaBaseYaw", Mathf.Repeat(result.rotation - options.rotationOffset, 360).ToString("R", CultureInfo.InvariantCulture));
            AssetDatabase.CreateAsset(material, result.materialPath);
            Undo.RecordObject(GetRenderSettingsObject(), "Apply 360 panorama skybox");
            RenderSettings.skybox = material;
            DynamicGI.UpdateEnvironment();
            if (sun && Quaternion.Angle(sunRotation, sun.transform.rotation) > .0001f)
                throw new InvalidOperationException("Sun orientation changed unexpectedly.");
            if (saveScene)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(folder + "/Import.json", JsonUtility.ToJson(result, true));
            File.WriteAllText(Output + "/last-import.json", JsonUtility.ToJson(result, true));
            AssetDatabase.ImportAsset(folder + "/Import.json");
            if (options.alignSun && !result.aligned)
            {
                WriteStatus("Preview applied. Sun detection is uncertain: click the sun in the image or disable alignment, then Apply to finish lighting/reflections.");
                return result;
            }
            if (options.bakeLighting)
            {
                // Legacy builder completion also replaces the user's reflection setup. This job owns completion.
                SessionState.SetBool("KinoRotunda.Baking", false);
                SessionState.SetString(JobKey, JsonUtility.ToJson(new BakeJob
                {
                    scenePath = scene.path, materialPath = result.materialPath, folder = folder,
                    updateReflections = options.updateReflections, sunRotation = sunRotation,
                    sunGlobalId = sun ? GlobalObjectId.GetGlobalObjectIdSlow(sun).ToString() : ""
                }));
                try
                {
                    if (!Lightmapping.BakeAsync())
                        throw new InvalidOperationException("Skybox applied, but Unity could not start the lighting bake. Reapply when ready.");
                }
                catch
                {
                    SessionState.EraseString(JobKey);
                    throw;
                }
                WriteStatus("Skybox applied. Baking lighting; sun orientation preserved.");
            }
            else
            {
                if (options.updateReflections) UpdateReflections(folder);
                WriteStatus("Skybox applied; sun orientation preserved. " + result.note);
            }
            return result;
        }

        static Object GetRenderSettingsObject()
        {
            // RenderSettings is a scene-owned Unity object; recording it makes the material assignment undoable.
            return Resources.FindObjectsOfTypeAll<RenderSettings>().First();
        }

        static void OnBakeCancelled()
        {
            if (string.IsNullOrEmpty(SessionState.GetString(JobKey, ""))) return;
            SessionState.EraseString(JobKey);
            WriteStatus("Bake cancelled. Skybox is saved; baked lighting/reflections still need updating.");
        }

        static void OnBakeCompleted()
        {
            string json = SessionState.GetString(JobKey, "");
            if (string.IsNullOrEmpty(json)) return;
            EditorApplication.delayCall += () =>
            {
                try
                {
                    var job = JsonUtility.FromJson<BakeJob>(json);
                    if (SceneManager.GetActiveScene().path != job.scenePath ||
                        AssetDatabase.GetAssetPath(RenderSettings.skybox) != job.materialPath)
                        throw new InvalidOperationException("Scene or sky changed during baking; reflection update skipped.");
                    Light sun = null;
                    if (GlobalObjectId.TryParse(job.sunGlobalId, out var sunId))
                        sun = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(sunId) as Light;
                    if (!string.IsNullOrEmpty(job.sunGlobalId) && !sun)
                        throw new InvalidOperationException("Sun was removed during the bake; check the scene lighting.");
                    if (sun && Quaternion.Angle(sun.transform.rotation, job.sunRotation) > .001f)
                        throw new InvalidOperationException("Sun was edited during the bake; rebake for the new direction.");
                    if (job.updateReflections) UpdateReflections(job.folder);
                    EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                    AssetDatabase.SaveAssets();
                    WriteStatus("Complete: skybox, lighting and requested reflections saved. Sun orientation preserved.");
                }
                catch (Exception e) { WriteStatus("ERROR: " + e.Message); Debug.LogException(e); }
                finally { SessionState.EraseString(JobKey); }
            };
        }

        static void UpdateReflections(string folder)
        {
            var captures = new List<KeyValuePair<ReflectionProbe, Cubemap>>();
            foreach (var probe in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
            {
                if (!probe.enabled || !probe.gameObject.activeInHierarchy || probe.mode == ReflectionProbeMode.Realtime) continue;
                string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/Reflection-" + probe.GetInstanceID() + ".exr");
                var previousMode = probe.mode;
                Undo.RecordObject(probe, "Update panorama reflection");
                try
                {
                    probe.mode = ReflectionProbeMode.Baked;
                    if (!Lightmapping.BakeReflectionProbe(probe, path))
                        throw new InvalidOperationException("Reflection bake failed: " + probe.name);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    var cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                    if (!cubemap) throw new InvalidOperationException("Reflection cubemap could not be loaded: " + probe.name);
                    if (previousMode == ReflectionProbeMode.Custom) captures.Add(new KeyValuePair<ReflectionProbe, Cubemap>(probe, cubemap));
                }
                finally { probe.mode = previousMode; EditorUtility.SetDirty(probe); }
            }
            // Commit custom maps only when every capture succeeded. Keep previous maps available for Undo.
            foreach (var capture in captures)
            {
                Undo.RecordObject(capture.Key, "Update panorama reflection");
                capture.Key.customBakedTexture = capture.Value;
                EditorUtility.SetDirty(capture.Key);
            }
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }

        public static void WriteStatus(string message)
        {
            Status = message;
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/status.txt", DateTime.Now.ToString("s") + " " + message);
            Debug.Log("[KinoPanorama] " + message);
        }

        static void Poll()
        {
            if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(RequestPath)) return;
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
            File.Delete(RequestPath);
            busy = true;
            try
            {
                switch (request.command)
                {
                    case "open": KinoPanoramaWindow.Open(); WriteStatus("360 Skybox window ready."); break;
                    case "apply": ApplyFile(request.sourcePath, request.options); break;
                    case "test": KinoPanoramaTests.Run(); break;
                    case "quality": KinoPanoramaQuality.ImproveCurrent(); break;
                    default: throw new ArgumentException("Unknown panorama command: " + request.command);
                }
            }
            catch (Exception e) { WriteStatus("ERROR: " + e); Debug.LogException(e); }
            finally { busy = false; }
        }
    }
}
