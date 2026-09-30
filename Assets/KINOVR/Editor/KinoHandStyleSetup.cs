using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    public static class KinoHandStyleSetup
    {
        const string MaterialPath = "Assets/KINOVR/Materials/HandSatin.mat";
        const string Output = "Artifacts/KinoGameplay/Hands";
        const string ScenePath = "Assets/KinoRotunda/Scenes/KinoRotunda.unity";

        // Targets the two catching hands, not controller models or SDK package assets.
        static SkinnedMeshRenderer[] CatchingHands(GameObject root) => root
            .GetComponentsInChildren<HandCatcher>(true)
            .Select(c => c.GetComponentInParent<OVRHand>(true))
            .Where(h => h)
            .Select(h => h.GetComponent<SkinnedMeshRenderer>())
            .Where(r => r).Distinct().ToArray();

        public static void Configure(GameObject rig)
        {
            var shader = Shader.Find("KINO/Hand Satin");
            if (!shader) throw new InvalidOperationException("KINO/Hand Satin shader is missing.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(shader) { name = "HandSatin", enableInstancing = true };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            foreach (var hand in CatchingHands(rig))
                hand.sharedMaterial = material;
        }

        [MenuItem("Tools/KINO VR/Hands/1 - Apply blue satin and gold")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play before changing hand materials.");
            var root = PrefabUtility.LoadPrefabContents(KinoExperienceSetup.PrefabPath);
            try
            {
                var hands = CatchingHands(root);
                if (hands.Length != 2)
                    throw new InvalidOperationException("Expected exactly two tracked catching hands.");
                var previousMaterials = hands.Select(h => h.sharedMaterial).ToArray();
                Configure(root);
                if (hands.Where((h, i) => h.sharedMaterial != previousMaterials[i]).Any())
                    PrefabUtility.SaveAsPrefabAsset(root, KinoExperienceSetup.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Validate();
        }

        [MenuItem("Tools/KINO VR/Hands/2 - Validate hand materials")]
        public static void Validate()
        {
            Directory.CreateDirectory(Output);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material || material.shader.name != "KINO/Hand Satin" || ShaderUtil.ShaderHasError(material.shader))
                throw new InvalidOperationException("Hand material is missing or has shader errors.");
            var root = PrefabUtility.LoadPrefabContents(KinoExperienceSetup.PrefabPath);
            try
            {
                var hands = CatchingHands(root);
                if (hands.Length != 2 || hands.Any(h => h.sharedMaterial != material || !h.GetComponent<OVRMeshRenderer>()))
                    throw new InvalidOperationException("Both tracked catching hands must use HandSatin.");
                // Exercise the actual instruction-screen queue swap and restore.
                // The SDK still owns renderer visibility and system-gesture feedback.
                var orders = hands.Select(h => h.sortingOrder).ToArray();
                var helperType = typeof(KinoExperienceController).Assembly.GetType("KinoVR.KinoModeHands", true);
                var setVisible = helperType.GetMethod("SetVisible");
                using (var menu = (IDisposable)Activator.CreateInstance(helperType, new object[] { root.transform }))
                {
                    setVisible.Invoke(menu, new object[] { true });
                    if (hands.Any(h => h.sharedMaterial.shader != material.shader || h.sharedMaterial.renderQueue != 3000 || h.sortingOrder != 75))
                        throw new InvalidOperationException("Instruction-screen hand presentation failed.");
                    setVisible.Invoke(menu, new object[] { false });
                    if (hands.Any(h => h.sharedMaterial != material))
                        throw new InvalidOperationException("Gameplay hand material was not restored.");
                    setVisible.Invoke(menu, new object[] { true });
                }
                if (hands.Where((h, i) => h.sharedMaterial != material || h.sortingOrder != orders[i]).Any())
                    throw new InvalidOperationException("Hand helper did not restore material and sorting on disposal.");
                File.WriteAllText(Output + "/Validation.txt",
                    "PASS: two tracked catching hands share HandSatin.\nPASS: shader has no reported compiler errors.\n" +
                    "PASS: instruction queue, gameplay restore and disposal preserve materials and sorting.\n" +
                    "Preview uses Meta's OpenXR reference hand mesh. Live tracking, stereo comfort and Quest GPU timing require a headset.\n");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("[KinoHands] Validation passed.");
        }

        [MenuItem("Tools/KINO VR/Hands/3 - Capture hand previews")]
        public static void Preview()
        {
            Directory.CreateDirectory(Output);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material) throw new InvalidOperationException("Apply the hand material first.");
            var temporary = new List<GameObject>();
            try
            {
                var cameraGO = new GameObject("Hand material preview camera");
                temporary.Add(cameraGO);
                var camera = cameraGO.AddComponent<Camera>();
                camera.enabled = false;
                camera.nearClipPlane = .02f;
                camera.farClipPlane = 100;
                camera.allowHDR = true;
                camera.fieldOfView = 48;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.018f, .028f, .048f);
                camera.transform.position = new Vector3(0, 35, -.46f);
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                var left = CreateHand(false, material, temporary);
                var right = CreateHand(true, material, temporary);
                left.transform.SetPositionAndRotation(new Vector3(-.115f, 35, 0), Quaternion.Euler(0, -12, -12));
                right.transform.SetPositionAndRotation(new Vector3(.115f, 35, 0), Quaternion.Euler(0, 168, 12));
                Capture(camera, "Hands-studio", 1400, 1000);

                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == ScenePath)
                {
                    var view = Object.FindFirstObjectByType<KinoPlayerView>(FindObjectsInactive.Include);
                    if (view && view.desktopCamera)
                    {
                        camera.CopyFrom(view.desktopCamera);
                        camera.enabled = false;
                        camera.transform.SetPositionAndRotation(view.desktopCamera.transform.position, view.desktopCamera.transform.rotation);
                        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                        left.transform.SetPositionAndRotation(camera.transform.TransformPoint(new Vector3(-.15f, -.12f, .48f)), camera.transform.rotation * Quaternion.Euler(12, 20, -15));
                        right.transform.SetPositionAndRotation(camera.transform.TransformPoint(new Vector3(.15f, -.12f, .48f)), camera.transform.rotation * Quaternion.Euler(12, -20, 15));
                        Capture(camera, "Hands-in-Rotunda", 1600, 1000);
                    }
                }
            }
            finally { foreach (var go in temporary) if (go) Object.DestroyImmediate(go); }
        }

        static GameObject CreateHand(bool right, Material material, List<GameObject> temporary)
        {
            string path = "Packages/com.meta.xr.sdk.core/Meshes/HandTracking/OpenXR" + (right ? "Right" : "Left") + "Hand.fbx";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset) throw new InvalidOperationException("Missing Meta OpenXR reference hand: " + path);
            var pivot = new GameObject(right ? "Right hand preview" : "Left hand preview");
            temporary.Add(pivot);
            var model = Object.Instantiate(asset, pivot.transform);
            // Meta's reference pose extends along +Z. Present fingers upward.
            model.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var renderers = model.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers) renderer.sharedMaterial = material;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float scale = .23f / Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            model.transform.localScale *= scale;
            model.transform.position -= bounds.center * scale;
            return pivot;
        }

        static void Capture(Camera camera, string name, int width, int height)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var output = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            var previousTarget = camera.targetTexture;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(target, output);
                RenderTexture.active = output;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgb;
                RenderTexture.ReleaseTemporary(target);
                RenderTexture.ReleaseTemporary(output);
                Object.DestroyImmediate(pixels);
            }
        }

        public static void BatchApplyAndPreview()
        {
            Apply();
            EditorSceneManager.OpenScene(ScenePath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var sceneHands = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(CatchingHands).ToArray();
            if (sceneHands.Length != 2 || sceneHands.Any(h => h.sharedMaterial != material))
                throw new InvalidOperationException("Rotunda scene has unexpected hand material overrides.");
            Preview();
            Validate();
            File.AppendAllText(Output + "/Validation.txt", "PASS: saved Rotunda scene inherits both hand materials.\n");
        }
    }
}
