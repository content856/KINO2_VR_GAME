using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    public static partial class KinoGameplaySetup
    {
        const string BoostOutput = "Artifacts/KinoGameplay/Boost";
        const string GameplayPrefab = "Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab";

        public static void BatchBoost()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                ApplyBoost();
                Validate();
                KinoBoostTests.ValidateRules();
                CaptureBoost();
                KinoBoostTests.Run(true);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void BatchBoostPreview()
        {
            EditorSceneManager.OpenScene(ScenePath);
            CaptureBoost();
        }

        [MenuItem("Tools/KINO VR/BOOST/1 - Set up bonus round and sharp logo")]
        public static void ApplyBoost()
        {
            RequireScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            Directory.CreateDirectory(BoostOutput);
            ConfigureLogoImport();
            LoadMaterials();
            var contents = PrefabUtility.LoadPrefabContents(GameplayPrefab);
            try
            {
                ConfigureBoost(contents.GetComponent<KinoRoundController>());
                PrefabUtility.SaveAsPrefabAsset(contents, GameplayPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            var artwork = AssetDatabase.LoadAssetAtPath<Material>("Assets/KINOVR/Materials/BoardAnimatedArtwork.mat");
            artwork.SetFloat("_HideBakedLogo", 1);
            EditorUtility.SetDirty(artwork);
            AssetDatabase.SaveAssetIfDirty(artwork);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            File.WriteAllText(BoostOutput + "/setup.txt", "75 s main / 6 s calm / 25 s BOOST defaults; cumulative score; catches x3 during BOOST.\nIndependent source KINO logo, trilinear Kaiser mipmaps, -0.35 bias, anisotropy 8, Android ASTC 4x4.\nNo depth-of-field volume in this scene. Logo was baked into a 1065x602 screen image. Headset head-angle comparison is still required.\n");
        }

        static void ConfigureLogoImport()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/KINOVR/KINO-logo-RGB.png");
            importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipMapBias = -.35f;
            importer.anisoLevel = 8;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 2048;
            android.format = TextureImporterFormat.ASTC_4x4;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        static RectTransform FullBoardGroup(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static void ConfigureBoost(KinoRoundController round)
        {
            var board = round.board;
            var artwork = AssetDatabase.LoadAssetAtPath<Material>("Assets/KINOVR/Materials/BoardAnimatedArtwork.mat");
            artwork.SetFloat("_HideBakedLogo", 1);
            EditorUtility.SetDirty(artwork);
            AssetDatabase.SaveAssetIfDirty(artwork);
            // The command is idempotent: retain the authored timing/pace after first setup.
            var presentation = round.GetComponent<KinoBoostPresentation>();
            if (presentation)
            {
                var oldBacking = board.transform.Find("BOOST display/Sharp KINO brand/Cover low resolution logo");
                if (oldBacking) Object.DestroyImmediate(oldBacking.gameObject);
                presentation.panels = presentation.panels.Where(p => p).ToArray();
                foreach (var light in presentation.goldAccents.GetComponentsInChildren<Light>(true)) light.intensity = 1.2f;
                return;
            }
            presentation = round.gameObject.AddComponent<KinoBoostPresentation>();
            round.boostPresentation = presentation;
            round.enableBoostRound = true;
            presentation.board = board;
            var root = FullBoardGroup("BOOST display", board.transform);
            var brand = FullBoardGroup("Sharp KINO brand", root);
            var logo = new GameObject("Source resolution KINO logo", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            logo.transform.SetParent(brand, false);
            logo.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/KINOVR/KINO-logo-RGB.png");
            // Crop the transparent margins with UVs; the original artwork stays untouched.
            logo.uvRect = new Rect(64f / 3160, 620f / 3160, 3032f / 3160, 1920f / 3160);
            logo.raycastTarget = false;
            Place(logo.rectTransform, 470, 5, 130, 82.32f);
            presentation.normalBrand = brand.gameObject;

            var badge = FullBoardGroup("BOOST active header", root);
            Text("Boost wordmark", badge, 365, 8, 335, 46, "KINO BOOST", 37, new Color(1, .75f, .23f), true);
            Text("Multiplier", badge, 365, 52, 335, 32, "x3  •  BOOST ACTIVE", 23, new Color(1, .82f, .4f), true);
            presentation.activeBadge = badge.gameObject;
            badge.gameObject.SetActive(false);

            // Give score and physical catches separate, readable positions in the header.
            Place((RectTransform)board.catchCountText.transform, 65, 35, 150, 51);
            board.caughtTotalText = Text("Physical catches", root, 206, 55, 143, 27, "CAUGHT 000", 18, new Color(.65f, .8f, .87f), true, TextAlignmentOptions.Left);
            board.statusText.text = "SCORE";

            var announce = FullBoardGroup("KINO BOOST announcement", root);
            presentation.announcement = announce.gameObject.AddComponent<CanvasGroup>();
            presentation.announcement.blocksRaycasts = false;
            var burst = GraphicMaterial("BoardBoostBurst", 4);
            Graphic("Gold burst", announce, 59, 121, 955, 443, burst);
            Graphic("Upper gold line", announce, 86, 142, 900, 2, null).color = new Color(1, .62f, .12f);
            Graphic("Lower gold line", announce, 86, 543, 900, 2, null).color = new Color(1, .62f, .12f);
            var kino = Text("KINO", announce, 170, 165, 725, 99, "KINO", 89, Color.white, true);
            var boost = Text("BOOST", announce, 105, 242, 855, 158, "BOOST", 147, Color.white, true);
            foreach (var title in new[] { kino, boost })
            {
                title.enableVertexGradient = true;
                title.colorGradient = new VertexGradient(new Color(1, .96f, .63f), new Color(1, .96f, .63f), new Color(1, .50f, .035f), new Color(1, .50f, .035f));
                var shadow = title.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(.12f, .035f, 0, .9f);
                shadow.effectDistance = new Vector2(3, -4);
            }
            Text("Triple score", announce, 180, 403, 705, 65, "x3", 63, new Color(1, .79f, .28f), true);
            Text("Boost explanation", announce, 125, 485, 815, 37, "TRIPLE POINTS  •  FASTER BALLS", 27, new Color(1, .82f, .43f), true);
            announce.gameObject.SetActive(false);

            presentation.panels = board.GetComponentsInChildren<Graphic>(true)
                .Where(g => (g.material && g.material.shader.name == "KINO/Board Graphic" && g.material.GetFloat("_Mode") < .5f) ||
                    g.name == "Existing KINO artwork").ToArray();
            var audio = new GameObject("KINO BOOST announcement audio").AddComponent<AudioSource>();
            audio.transform.SetParent(round.transform, false);
            audio.playOnAwake = false;
            audio.spatialBlend = 0;
            audio.volume = .8f;
            audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/KINOVR/Audio/KinoBoost.wav");
            presentation.announcementAudio = audio;

            var accents = new GameObject("BOOST golden rings and light");
            accents.transform.SetParent(round.transform, false);
            var lightMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/KINOVR/Materials/BoostLight.mat");
            if (!lightMat)
            {
                lightMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "BoostLight" };
                lightMat.SetColor("_BaseColor", new Color(3, 1.25f, .075f));
                AssetDatabase.CreateAsset(lightMat, "Assets/KINOVR/Materials/BoostLight.mat");
            }
            foreach (float radius in new[] { 3.2f, 3.65f, 4.7f }) MakeRing(accents.transform, lightMat, radius, .035f, 0);
            MakeRing(accents.transform, lightMat, 1.4f, .16f, 10.1f);
            for (int i = 0; i < 2; i++)
            {
                var light = new GameObject("BOOST amber fill " + i).AddComponent<Light>();
                light.transform.SetParent(accents.transform, false);
                light.transform.localPosition = new Vector3(i == 0 ? -3 : 3, 3.5f, 7);
                light.type = LightType.Point;
                light.color = new Color(1, .55f, .08f);
                light.intensity = 1.2f;
                light.range = 14;
                light.shadows = LightShadows.None;
                light.lightmapBakeType = LightmapBakeType.Realtime;
            }
            presentation.goldAccents = accents;
            accents.SetActive(false);
            EditorUtility.SetDirty(round);
        }

        static void MakeRing(Transform parent, Material material, float radius, float height, float z)
        {
            var ring = new GameObject("Gold floor ring " + radius).AddComponent<LineRenderer>();
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0, height, z);
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = 192;
            ring.widthMultiplier = .018f;
            ring.sharedMaterial = material;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.alignment = LineAlignment.TransformZ;
            ring.transform.localRotation = Quaternion.Euler(90, 0, 0);
            for (int i = 0; i < ring.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2 / ring.positionCount;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius);
            }
        }

        [MenuItem("Tools/KINO VR/BOOST/2 - Capture before and BOOST previews")]
        public static void CaptureBoost()
        {
            RequireScene();
            Directory.CreateDirectory(BoostOutput);
            var round = Object.FindFirstObjectByType<KinoRoundController>();
            var preview = round.boostPresentation;
            Assert(preview && preview.announcementAudio.clip, "BOOST presentation/audio missing.");
            Assert(AssetDatabase.LoadAssetAtPath<Material>("Assets/KINOVR/Materials/BoardAnimatedArtwork.mat").GetFloat("_HideBakedLogo") == 1,
                "The original low-resolution logo must be masked behind the source logo.");
            Assert(!ShaderUtil.ShaderHasError(Shader.Find("KINO/Board Graphic")), "Board shader failed to compile.");
            var go = new GameObject("BOOST QA camera");
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(round.playerView.desktopCamera);
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(round.playerView.desktopCamera.transform.position, round.playerView.desktopCamera.transform.rotation);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            try
            {
                Canvas.ForceUpdateCanvases();
                CaptureBoostCamera(camera, "Room-normal");
                CaptureQuestSettings(camera, round, "Quest-settings-normal");
                var previewState = new KinoRoundState();
                previewState.Begin(75, 0, 6, 25);
                foreach (int n in new[] { 4, 11, 16, 24, 35, 41, 56, 62, 67, 72, 75, 80 })
                {
                    previewState.TryCatch(n, 10);
                    round.board.MarkCaught(n);
                }
                previewState.Tick(81);
                round.board.SetRoundProgress(previewState, false);
                preview.PreviewBoost();
                Canvas.ForceUpdateCanvases();
                CaptureBoostCamera(camera, "Room-boost-announcement");
                CaptureQuestSettings(camera, round, "Quest-settings-boost-announcement");
                preview.announcement.gameObject.SetActive(false);
                CaptureBoostCamera(camera, "Room-boost-play");
                CaptureQuestSettings(camera, round, "Quest-settings-boost-play");
                var bounds = Screen().bounds;
                camera.orthographic = true;
                camera.orthographicSize = bounds.size.y * .52f;
                camera.transform.SetPositionAndRotation(bounds.center + Vector3.back * 2, Quaternion.identity);
                preview.announcement.gameObject.SetActive(true);
                CaptureBoostCamera(camera, "Board-boost-announcement");
                preview.SetBoost(false);
                round.board.ResetBoard();
                round.board.SetProgress(0, 75, 75, false);
                round.board.statusText.text = "SCORE";
                round.board.caughtTotalText.text = "CAUGHT 000";
                CaptureBoostCamera(camera, "Board-sharp-logo");
            }
            finally
            {
                Object.DestroyImmediate(go);
                // Reload without saving temporary material copies or preview changes.
                EditorSceneManager.OpenScene(ScenePath);
            }
        }
        static void CaptureQuestSettings(Camera camera, KinoRoundController round, string name)
        {
            var vrCamera = round.playerView.head.GetComponent<Camera>();
            var data = camera.GetUniversalAdditionalCameraData();
            bool hdr = camera.allowHDR, post = data.renderPostProcessing;
            float fov = camera.fieldOfView;
            try
            {
                camera.allowHDR = vrCamera.allowHDR;
                camera.fieldOfView = vrCamera.fieldOfView;
                data.renderPostProcessing = vrCamera.GetUniversalAdditionalCameraData().renderPostProcessing;
                CaptureBoostCamera(camera, name);
            }
            finally { camera.allowHDR = hdr; camera.fieldOfView = fov; data.renderPostProcessing = post; }
        }
        static void CaptureBoostCamera(Camera camera, string name)
        {
            var target = RenderTexture.GetTemporary(1600, 1000, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
            var previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
                image.Apply();
                File.WriteAllBytes(BoostOutput + "/" + name + ".png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; if (image) Object.DestroyImmediate(image); RenderTexture.ReleaseTemporary(target); }
        }
    }
}
