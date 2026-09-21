using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KinoRotunda.Editor
{
    public static class KinoPanoramaTests
    {
        [MenuItem("Tools/KINO Rotunda/Validate 360 importer")]
        public static void Run()
        {
            KinoPanoramaSkybox.RequireScene();
            Directory.CreateDirectory(KinoPanoramaSkybox.Output);
            string output = KinoPanoramaSkybox.Output;
            var oldSky = RenderSettings.skybox;
            var sun = RenderSettings.sun;
            string sunBefore = EditorJsonUtility.ToJson(sun);
            string skyBefore = EditorJsonUtility.ToJson(oldSky);
            Quaternion rotation = sun.transform.rotation;
            string probesBefore = ProbeState();
            var maps = LightmapSettings.lightmaps;
            byte[] sceneBefore = File.ReadAllBytes(KinoPanoramaSkybox.ScenePath);
            string[] initialFolders = Directory.Exists(KinoPanoramaSkybox.Root) ? Directory.GetDirectories(KinoPanoramaSkybox.Root) : Array.Empty<string>();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            var report = new System.Text.StringBuilder();
            try
            {
                KinoPanoramaAnalysis.RunMathChecks();
                report.AppendLine("PASS: panorama mapping, yaw sign, seam, sunny/overcast/ambiguous/city-light fixtures.");
                var options = new KinoPanoramaSkybox.Options { desktopSize = 4096, questSize = 2048, bakeLighting = false, updateReflections = false };
                string png = output + "/Test-LDR.png";
                var ldr = new Texture2D(512, 256, TextureFormat.RGB24, false, false);
                try
                {
                    ldr.SetPixels(Enumerable.Repeat(new Color(.5f, .5f, .5f), 512 * 256).ToArray());
                    ldr.Apply();
                    File.WriteAllBytes(png, ldr.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(ldr); }
                var converted = KinoPanoramaSkybox.Import(png, options);
                var hdr = AssetDatabase.LoadAssetAtPath<Texture2D>(converted.texturePath);
                var sample = KinoPanoramaSkybox.ReadLinear(hdr, 64, 32);
                try
                {
                    Assert(Mathf.Abs(sample.GetPixel(32, 16).r - Mathf.GammaToLinearSpace(.5f)) < .008f,
                        $"JPG/PNG to EXR must decode sRGB exactly once (actual={sample.GetPixel(32, 16).r}, format={hdr.format}, graphics={hdr.graphicsFormat}).");
                    Assert(!converted.aligned, "Uniform panorama must not invent a sun bearing.");
                }
                finally { Object.DestroyImmediate(sample); }
                var ti = (TextureImporter)AssetImporter.GetAtPath(converted.texturePath);
                Assert(!ti.sRGBTexture && !ti.isReadable && ti.mipmapEnabled, "Runtime HDR import flags.");
                Assert(ti.wrapModeU == TextureWrapMode.Repeat && ti.wrapModeV == TextureWrapMode.Clamp, "Panorama seam wrapping.");
                Assert(ti.GetPlatformTextureSettings("Android").maxTextureSize == 2048, "Quest texture size override.");
                report.AppendLine("PASS: native GPU LDR-to-linear EXR roundtrip, runtime flags, panorama wrapping, Quest size limit.");

                string exr = output + "/Test-HDR.exr";
                var synthetic = new Texture2D(512, 256, TextureFormat.RGBAHalf, false, true);
                Vector2 uv = new Vector2(.27f, .5f + 6.06f / 180);
                try
                {
                    var pixels = Enumerable.Repeat(new Color(.02f, .02f, .02f), 512 * 256).ToArray();
                    int cx = Mathf.RoundToInt(uv.x * 512), cy = Mathf.RoundToInt(uv.y * 256);
                    for (int y = cy - 3; y <= cy + 3; y++)
                        for (int x = cx - 3; x <= cx + 3; x++) pixels[y * 512 + x] = new Color(16, 12, 8, 1);
                    synthetic.SetPixels(pixels); synthetic.Apply();
                    File.WriteAllBytes(exr, synthetic.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
                }
                finally { Object.DestroyImmediate(synthetic); }
                var hdrResult = KinoPanoramaSkybox.Import(exr, options);
                sample = KinoPanoramaSkybox.ReadLinear(AssetDatabase.LoadAssetAtPath<Texture2D>(hdrResult.texturePath), 512, 256);
                try { Assert(sample.GetPixels().Max(c => c.r) > 10, "HDR highlights must survive import above 1.0."); }
                finally { Object.DestroyImmediate(sample); }
                Assert(hdrResult.aligned, "Clear synthetic sun should auto-align.");
                report.AppendLine("PASS: native HDR radiance above 1.0 and sun detection.");

                var applied = KinoPanoramaSkybox.ApplyFile(exr, options, false);
                Assert(RenderSettings.skybox && RenderSettings.skybox.GetTexture("_MainTex"), "Skybox material installed.");
                Assert(Quaternion.Angle(rotation, sun.transform.rotation) < .001f, "Sun orientation preserved.");
                Assert(EditorJsonUtility.ToJson(sun) == sunBefore, "Sun settings preserved.");
                Assert(EditorJsonUtility.ToJson(oldSky) == skyBefore, "Previous sky material preserved.");
                Assert(ProbeState() == probesBefore, "Existing reflection settings preserved.");
                Assert(LightmapSettings.lightmaps.Select(m => m.lightmapColor).SequenceEqual(maps.Select(m => m.lightmapColor)), "Existing lightmap assignments preserved.");
                VerifyRenderedDirection(sun, output);
                report.AppendLine("PASS: importer application, native sky shader orientation, sun/probe/lightmap/previous-sky preservation.");

                string wrong = output + "/Test-invalid-layout.png";
                var invalid = new Texture2D(512, 512, TextureFormat.RGB24, false);
                try { File.WriteAllBytes(wrong, invalid.EncodeToPNG()); }
                finally { Object.DestroyImmediate(invalid); }
                bool rejected = false;
                var beforeInvalid = RenderSettings.skybox;
                try { KinoPanoramaSkybox.ApplyFile(wrong, options, false); }
                catch (ArgumentException) { rejected = true; }
                Assert(rejected && RenderSettings.skybox == beforeInvalid, "Invalid layout must not change scene sky.");
                report.AppendLine("PASS: invalid aspect rejected without changing the sky.");
                Assert(File.ReadAllBytes(KinoPanoramaSkybox.ScenePath).SequenceEqual(sceneBefore), "Saved scene bytes unchanged by validation.");
                report.AppendLine("PASS: saved scene unchanged. Validation did not rebake lighting.");
            }
            finally
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(undoGroup);
                RenderSettings.skybox = oldSky;
                DynamicGI.UpdateEnvironment();
                // Only remove folders created by this test invocation; existing panorama imports are untouched.
                if (Directory.Exists(KinoPanoramaSkybox.Root))
                    foreach (string folder in Directory.GetDirectories(KinoPanoramaSkybox.Root).Except(initialFolders))
                        AssetDatabase.DeleteAsset(folder.Replace('\\', '/'));
            }
            File.WriteAllText(output + "/tests.txt", report.ToString());
            KinoPanoramaSkybox.WriteStatus("VALIDATED: 360 importer checks passed; original scene sky restored.");
        }

        static string ProbeState() => string.Join("\n", Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
            .Select(p => EditorJsonUtility.ToJson(p)));

        static void VerifyRenderedDirection(Light sun, string output)
        {
            var go = new GameObject("Panorama validation camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 60;
            camera.allowHDR = true;
            go.transform.rotation = Quaternion.LookRotation(-sun.transform.forward, Vector3.up);
            var target = RenderTexture.GetTemporary(128, 128, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            Texture2D rendered = null;
            try
            {
                camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                rendered = KinoPanoramaSkybox.ReadLinear(target, 128, 128);
                float centre = rendered.GetPixel(64, 64).r;
                float edge = rendered.GetPixel(8, 64).r;
                Assert(centre > edge * 8 && centre > 2, $"Rendered sun must face the unchanged directional light (centre={centre}, edge={edge}).");
                File.WriteAllBytes(output + "/test-alignment.exr", rendered.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
            }
            finally
            {
                if (rendered) Object.DestroyImmediate(rendered);
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(go);
            }
        }

        static void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException("Panorama validation failed: " + message); }
    }
}
