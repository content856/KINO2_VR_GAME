using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KinoVR.Editor
{
    public static class KinoExperienceSetup
    {
        public const string PrefabPath = "Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab";
        public const string Output = "Artifacts/ExperienceFlow";
        const string FontPath = "Assets/KINOVR/Fonts/ExperienceGreek.asset";

        [MenuItem("Tools/KINO VR/Experience/1 - Apply visitor flow")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            Directory.CreateDirectory(Output);
            AssetDatabase.Refresh();
            var font = PrepareFont();
            var allwyn = PrepareTexture("AllwynOnBlack");
            var kino = PrepareTexture("KinoLogo");
            ConfigureApplicationSplash(kino);
            ConfigureAppIdentity();
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var round = root.GetComponent<KinoRoundController>();
                var flow = root.GetComponent<KinoExperienceController>();
                if (!flow) flow = root.AddComponent<KinoExperienceController>();
                flow.round = round; round.experience = flow;
                ConfigureBoardOverlays(round.board);
                if (round.boostPresentation) ConfigureBoostHeader(round.boostPresentation, font);
                round.startAutomatically = false; round.showcaseBoostAfterSecondChance = false;
                if (round.launcher) round.launcher.autoStart = false;
                if (!flow.contentCanvas) BuildPresentation(flow, font, allwyn, kino);
                flow.goldScoreMaterial = PrepareGoldText(font);
                if (flow.allwynLogo.texture != allwyn) flow.brandingSeconds = 3;
                flow.allwynLogo.texture = allwyn;
                flow.kinoLogo.texture = kino;
                if (!flow.modeCanvas) BuildModeSelection(flow, font);
                if (!flow.startButton)
                {
                    flow.startButton = ModeButton(flow, font, false, Vector2.zero);
                    flow.startButton.name = "Start selected experience";
                    flow.startButton.startSelectedMode = true;
                    flow.startButton.transform.Find("Mode label").GetComponent<TMP_Text>().text = "ΞΕΚΙΝΑ";
                    flow.startButton.transform.Find("Mode hint").GetComponent<TMP_Text>().text = "Άγγιξε για να παίξεις";
                }
                flow.enclosure = ConfigureEnclosure(root);
                KinoWaveEnvironmentSetup.Configure(root, flow, font);
                flow.closingSeconds = 5;
                RemoveChild(flow.contentCanvas.transform, "Quiet background");
                RemoveChild(root.transform, "Session blackout");
                RemoveChild(root.transform, "View blackout");
                var manager = root.GetComponentInChildren<OVRManager>(true);
                if (manager)
                {
                    var settings = new SerializedObject(manager);
                    settings.FindProperty("_trackingOriginType").intValue = (int)OVRManager.TrackingOrigin.FloorLevel;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                }
                if (round.secondChancePresentation)
                {
                    round.secondChancePresentation.roomTreatment = round.boostPresentation;
                    ConfigureSecondChanceScreen(round.secondChancePresentation, font);
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("KINO visitor flow with blue wave environment, direct ΞΕΚΙΝΑ start and no splash screen configured.");
        }
        static TMP_FontAsset PrepareFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (existing) return existing;
            Directory.CreateDirectory("Assets/KINOVR/Fonts");
            AssetDatabase.Refresh();
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
            if (!source) throw new InvalidOperationException("LiberationSans.ttf is missing.");
            var font = TMP_FontAsset.CreateFontAsset(source, 64, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048);
            font.name = "Experience Greek";
            string characters = new string(Enumerable.Range(32, 95).Concat(Enumerable.Range(0x384, 80)).Select(c => (char)c).ToArray()) + "•–’";
            font.TryAddCharacters(characters, out string unusedMissing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, FontPath);
            foreach (var atlas in font.atlasTextures) { atlas.name = "Experience Greek Atlas"; AssetDatabase.AddObjectToAsset(atlas, font); }
            font.material.name = "Experience Greek Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
            return font;
        }
        static Texture2D PrepareTexture(string name)
        {
            string path = "Assets/KINOVR/Textures/Experience/" + name + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!importer) throw new InvalidOperationException(path + " is missing.");
            if (name == "KinoLogo")
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
            }
            importer.alphaIsTransparency = true; importer.mipmapEnabled = true;
            importer.npotScale = TextureImporterNPOTScale.None; importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 8;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true; android.maxTextureSize = 2048; android.format = TextureImporterFormat.ASTC_4x4;
            importer.SetPlatformTextureSettings(android); importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        // Name and package shown on the headset (replaces the URP template's Unity identifiers).
        public const string AppName = "KINO VR";
        public const string CompanyName = "Digital Tribes";
        public const string PackageId = "com.digitaltribes.kinovr";
        static void ConfigureAppIdentity()
        {
            PlayerSettings.productName = AppName;
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, PackageId);
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, PackageId);
        }
        // No splash screen: the app opens straight into the KINO VR experience (the KINO logo step).
        static void ConfigureApplicationSplash(Texture2D kino)
        {
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.logos = new PlayerSettings.SplashScreenLogo[0];
            PlayerSettings.SplashScreen.background = null;
            PlayerSettings.SplashScreen.backgroundPortrait = null;
            PlayerSettings.SplashScreen.backgroundColor = Color.black;
            PlayerSettings.virtualRealitySplashScreen = null;
            var config = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Oculus/OculusProjectConfig.asset");
            if (config)
            {
                var settings = new SerializedObject(config);
                var splash = settings.FindProperty("systemSplashScreen");
                if (splash != null) splash.objectReferenceValue = null;
                // Hands only: the experience never uses controllers, and with controllers allowed the
                // headset keeps controller mode until they are put down, delaying the hands.
                var hands = settings.FindProperty("handTrackingSupport");
                if (hands != null) hands.intValue = 2;
                if (settings.ApplyModifiedPropertiesWithoutUndo()) EditorUtility.SetDirty(config);
            }
        }
        static Material PrepareGoldText(TMP_FontAsset font)
        {
            const string path = "Assets/KINOVR/Materials/ExperienceGoldText.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(font.material) { name = "ExperienceGoldText" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetFloat("_FaceDilate", .12f);
            material.SetFloat("_OutlineWidth", .055f);
            material.SetColor("_OutlineColor", new Color(.24f, .105f, .018f, 1));
            material.EnableKeyword("UNDERLAY_ON");
            material.SetColor("_UnderlayColor", new Color(.16f, .064f, .009f, .85f));
            material.SetFloat("_UnderlayOffsetX", .7f);
            material.SetFloat("_UnderlayOffsetY", -.7f);
            material.SetFloat("_UnderlayDilate", .1f);
            material.SetFloat("_UnderlaySoftness", .08f);
            EditorUtility.SetDirty(material);
            return material;
        }
        static void BuildPresentation(KinoExperienceController flow, TMP_FontAsset font, Texture2D allwyn, Texture2D kino)
        {
            var canvas = NewCanvas("Experience screens", flow.transform, new Vector2(1100, 700));
            canvas.transform.localScale = Vector3.one * .002f;
            canvas.sortingOrder = 100;
            flow.contentCanvas = canvas;
            flow.content = canvas.gameObject.AddComponent<CanvasGroup>();
            flow.content.alpha = 0;
            flow.content.interactable = false; flow.content.blocksRaycasts = false;
            flow.title = Label("Title", canvas.transform, font, new Vector2(0, 225), new Vector2(1100, 100), 48);
            flow.body = Label("Message", canvas.transform, font, new Vector2(0, -5), new Vector2(1040, 330), 34);
            flow.footer = Label("Footer", canvas.transform, font, new Vector2(0, -245), new Vector2(1080, 65), 25);
            flow.footer.color = new Color(.68f, .83f, .93f);
            flow.allwynLogo = Logo("Allwyn original logo", canvas.transform, allwyn, new Vector2(0, 150), 480);
            flow.kinoLogo = Logo("KINO original logo", canvas.transform, kino, new Vector2(0, -110), 470);
        }
        static void RemoveChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child) UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
        internal static KinoBlackEnclosure ConfigureEnclosure(GameObject root)
        {
            var enclosure = root.GetComponent<KinoBlackEnclosure>();
            if (!enclosure) enclosure = root.AddComponent<KinoBlackEnclosure>();
            var shader = Shader.Find("KINO/Black Enclosure");
            if (!shader) throw new InvalidOperationException("Missing 360-degree enclosure shader.");
            Material Prepare(string name, int queue)
            {
                string path = "Assets/KINOVR/Materials/" + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material) { material = new Material(shader) { name = name }; AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                material.renderQueue = queue;
                material.SetColor("_Color", Color.black);
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                return material;
            }
            enclosure.backgroundMaterial = Prepare("EnclosureBackground", 3000);
            enclosure.fadeMaterial = Prepare("EnclosureFade", 4100);
            return enclosure;
        }
        internal static void ConfigureSecondChanceScreen(KinoSecondChancePresentation presentation, TMP_FontAsset font = null)
        {
            presentation.board = presentation.GetComponent<KinoRoundController>().board;
            ConfigureBoardOverlays(presentation.board);
            presentation.enclosure = ConfigureEnclosure(presentation.gameObject);
            RemoveChild(presentation.transform, "View blackout");
            if (!font) font = PrepareFont();
            var goldText = PrepareGoldText(font);
            var canvas = presentation.announcementCanvas;
            if (!canvas)
            {
                if (presentation.announcement) UnityEngine.Object.DestroyImmediate(presentation.announcement);
                canvas = NewCanvas("Second Chance reading screen", presentation.transform, new Vector2(1100, 700));
            }
            // Upgrade existing prefabs too; repeat setup must not accumulate decoration.
            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(canvas.transform.GetChild(i).gameObject);
            KinoBoardOverlay.Place(canvas, presentation.board, KinoBoardOverlay.ArtworkSize);
            presentation.announcementCanvas = canvas;
            presentation.announcement = canvas.gameObject;
            var panel = new GameObject("Emerald and gold frame", typeof(RectTransform), typeof(KinoSecondChancePanel));
            panel.transform.SetParent(canvas.transform, false);
            Place((RectTransform)panel.transform, Vector2.zero, KinoBoardOverlay.ArtworkSize);
            panel.GetComponent<KinoSecondChancePanel>().raycastTarget = false;
            var number = Label("Second chance number", canvas.transform, font, new Vector2(-338, 57), new Vector2(155, 180), 146);
            number.text = "2";
            KinoScreenTypography.Gold(number, goldText);
            var ordinal = Label("Second chance ordinal", canvas.transform, font, new Vector2(-264, 97), new Vector2(58, 72), 44);
            ordinal.text = "η";
            KinoScreenTypography.Gold(ordinal, goldText);
            var title = Label("Second chance title", canvas.transform, font, new Vector2(111, 57), new Vector2(644, 150), 90);
            title.text = "ΕΥΚΑΙΡΙΑ";
            title.textWrappingMode = TextWrappingModes.NoWrap;
            KinoScreenTypography.Gold(title, goldText);
            var extra = Label("Extra balls", canvas.transform, font, new Vector2(0, -124), new Vector2(700, 60), 40);
            extra.text = "3 ΕΠΙΠΛΕΟΝ ΜΠΑΛΕΣ";
            extra.color = KinoScreenTypography.Ivory;
            var points = Label("Triple points", canvas.transform, font, new Vector2(0, -181), new Vector2(650, 60), 41);
            points.text = "x3 ΠΟΝΤΟΙ";
            points.fontStyle = FontStyles.Bold;
            points.color = new Color(.3f, 1, .43f);
            canvas.gameObject.SetActive(false);
        }
        internal static void ConfigureBoostHeader(KinoBoostPresentation presentation, TMP_FontAsset font = null)
        {
            if (!font) font = PrepareFont();
            var goldText = PrepareGoldText(font);
            void Header(GameObject group)
            {
                if (!group) throw new InvalidOperationException("Missing BOOST header group.");
                for (int i = group.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(group.transform.GetChild(i).gameObject);
                // Central header bay, above the number field and between score/time.
                var rect = (RectTransform)group.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
                Place(rect, new Vector2(4, 253), new Vector2(335, 82));
                var title = Label("BOOST title", rect, font, new Vector2(0, 15), new Vector2(330, 54), 43);
                title.text = "BOOST";
                KinoScreenTypography.Gold(title, goldText);
                var detail = Label("Boost points", rect, font, new Vector2(0, -23), new Vector2(330, 30), 23);
                detail.text = "x3 ΠΟΝΤΟΙ";
                detail.color = KinoScreenTypography.Ivory;
                group.SetActive(false);
            }
            Header(presentation.activeBadge);
            var intro = presentation.announcement;
            for (int i = intro.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(intro.transform.GetChild(i).gameObject);
            var canvas = intro.GetComponent<Canvas>();
            if (!canvas) canvas = intro.gameObject.AddComponent<Canvas>();
            KinoBoardOverlay.Place(canvas, presentation.board, KinoBoardOverlay.ArtworkSize);
            var frame = new GameObject("Boost gold frame", typeof(RectTransform), typeof(KinoBoostPanel)).GetComponent<KinoBoostPanel>();
            frame.transform.SetParent(intro.transform, false);
            Place(frame.rectTransform, Vector2.zero, KinoBoardOverlay.ArtworkSize);
            frame.raycastTarget = false;
            var title = Label("BOOST introduction", intro.transform, font, new Vector2(0, 70), new Vector2(760, 165), 145);
            title.text = "BOOST";
            KinoScreenTypography.Gold(title, goldText);
            var points = Label("Boost triple points", intro.transform, font, new Vector2(0, -55), new Vector2(430, 85), 61);
            points.text = "x3 ΠΟΝΤΟΙ";
            points.color = KinoScreenTypography.Ivory;
            var explanation = Label("Boost faster balls", intro.transform, font, new Vector2(0, -154), new Vector2(860, 65), 37);
            explanation.text = "ΠΙΟ ΓΡΗΓΟΡΕΣ ΜΠΑΛΕΣ";
            explanation.color = KinoScreenTypography.Ivory;
            intro.gameObject.SetActive(false);
            presentation.announcement.alpha = 0;
        }
        static void ConfigureBoardOverlays(KinoNumberBoard board)
        {
            board.numberField = board.transform.Find("Live number field") as RectTransform;
            if (!board.numberGrid)
            {
                var group = new GameObject("Number grid content", typeof(RectTransform), typeof(CanvasGroup));
                group.transform.SetParent(board.transform, false);
                var rect = (RectTransform)group.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                board.numberGrid = group.GetComponent<CanvasGroup>();
                board.numberGrid.blocksRaycasts = false;
                board.numberGrid.interactable = false;
            }
            void Move(Transform child)
            {
                if (child && child.parent != board.numberGrid.transform)
                    child.SetParent(board.numberGrid.transform, true);
            }
            // Keep each marker below its number and preserve all existing references/positions.
            for (int i = 0; i < board.numberLabels.Length; i++)
            {
                if (i < board.caughtMarkers.Length && board.caughtMarkers[i]) Move(board.caughtMarkers[i].transform);
                if (board.numberLabels[i]) Move(board.numberLabels[i].transform);
            }
            foreach (var label in board.multiplierLabels) if (label) Move(label.transform);
            board.SetSecondChanceCover(false);
            board.SetFinaleCover(false);
            board.SetBoostCover(false);
        }
        static Canvas NewCanvas(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas)); go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = size; return canvas;
        }
        static void BuildModeSelection(KinoExperienceController flow, TMP_FontAsset font)
        {
            var canvas = NewCanvas("Normal or Boost - hand selection", flow.transform, new Vector2(520, 170));
            canvas.transform.localScale = Vector3.one * .001f;
            canvas.sortingOrder = 200;
            flow.modeCanvas = canvas;
            flow.normalModeButton = ModeButton(flow, font, false, new Vector2(-130, 0));
            flow.boostModeButton = ModeButton(flow, font, true, new Vector2(130, 0));
            canvas.gameObject.SetActive(false);
        }
        static KinoExperienceModeButton ModeButton(KinoExperienceController flow, TMP_FontAsset font, bool boost, Vector2 position)
        {
            var face = NewImage(boost ? "Boost selection" : "Normal selection", flow.modeCanvas.transform, position, new Vector2(220, 110));
            face.color = boost ? new Color(.45f, .25f, .012f) : new Color(.012f, .16f, .22f);
            var button = face.gameObject.AddComponent<Button>();
            button.targetGraphic = face; button.navigation = new Navigation { mode = Navigation.Mode.None };
            var label = Label("Mode label", face.transform, font, new Vector2(0, 18), new Vector2(210, 45), 32);
            label.text = boost ? "ΜΕ BOOST" : "NORMAL";
            var hint = Label("Mode hint", face.transform, font, new Vector2(0, -28), new Vector2(210, 40), 18);
            hint.text = boost ? "Second Chance + Boost" : "Έως τη Second Chance";
            var choice = face.gameObject.AddComponent<KinoExperienceModeButton>();
            choice.experience = flow; choice.includeBoost = boost; choice.button = button;
            choice.pressArea = face.gameObject.AddComponent<BoxCollider>();
            choice.pressArea.size = new Vector3(220, 110, 45); choice.pressArea.isTrigger = true;
            var body = face.gameObject.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
            choice.Hide(); return choice;
        }
        static void Place(RectTransform rect, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.anchoredPosition = position; rect.sizeDelta = size; }
        static Image NewImage(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>(); image.raycastTarget = false; Place(image.rectTransform, position, size); return image;
        }
        static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, Vector2 position, Vector2 size, float points)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
            var label = go.GetComponent<TextMeshProUGUI>(); label.font = font; label.fontSize = points;
            label.alignment = TextAlignmentOptions.Center; label.color = Color.white; label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal; Place(label.rectTransform, position, size); return label;
        }
        static RawImage Logo(string name, Transform parent, Texture2D texture, Vector2 position, float width)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage)); go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>(); image.texture = texture; image.raycastTarget = false;
            Place(image.rectTransform, position, new Vector2(width, width * texture.height / texture.width)); return image;
        }
        [MenuItem("Tools/KINO VR/Experience/2 - Validate flow")]
        public static void Validate()
        {
            var round = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<KinoRoundController>();
            var flow = round.GetComponent<KinoExperienceController>();
            Require(flow && flow.round == round && round.experience == flow, "Session references missing.");
            Require(!round.startAutomatically && !round.showcaseBoostAfterSecondChance && !round.launcher.autoStart, "Launch bypass enabled.");
            Require(flow.allwynLogo.texture && flow.kinoLogo.texture && flow.enclosure && flow.enclosure.backgroundMaterial && flow.enclosure.fadeMaterial, "Missing logo/enclosure.");
            Require(!ShaderUtil.ShaderHasError(flow.enclosure.fadeMaterial.shader), "Enclosure shader error.");
            Require(flow.closingSeconds == 5 && round.secondChancePresentation.enclosure == flow.enclosure && round.secondChancePresentation.announcementCanvas,
                "Missing automatic reset timing or spherical Second Chance presentation.");
            Require(flow.contentCanvas.renderMode == RenderMode.WorldSpace && flow.modeCanvas && flow.normalModeButton && flow.boostModeButton &&
                flow.startButton && flow.startButton.startSelectedMode, "Missing VR mode selection or standby start button.");
            Require(flow.kinoSplashSeconds >= 1, "Every visitor must see the KINO splash before gameplay.");
            Require(PlayerSettings.productName == AppName && PlayerSettings.companyName == CompanyName &&
                PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android) == PackageId,
                "App name, company or Android package is not the KINO VR identity.");
            Require(!PlayerSettings.SplashScreen.show && !PlayerSettings.virtualRealitySplashScreen,
                "The app must start without a splash screen, directly in the KINO VR experience.");
            Require(flow.body.font.HasCharacters(flow.safetyText, out uint[] missing, true, false), "Missing Greek safety glyphs.");
            KinoWaveEnvironmentSetup.Validate(flow);
            KinoExperienceTests.ValidateState();
            KinoSecondChanceTests.ValidateRules();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/validation.txt", "PASS: session references, no splash screen (straight into the experience), Greek text, startup guards, blue wave environment and brand fonts, KINO Boost inactive at launch with a single activation for every visitor and no menu (operator mode selection retained behind offerBoostSelection), editable pre/post-game sequence, outro on the environment, standby start button, per-visitor KINO splash, retained mode, 360-degree background/fades, five-second closing, flow and round rules.\n");
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        public static void ApplyBatch() { Apply(); }
        public static void ApplyAndTestBatch() { Apply(); KinoExperienceTests.RunBatch(); }
    }
}
