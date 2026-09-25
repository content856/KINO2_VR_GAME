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

        [MenuItem("Tools/KINO VR/Experience/1 - Apply eight-stage flow")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            Directory.CreateDirectory(Output);
            AssetDatabase.Refresh();
            var font = PrepareFont();
            var allwyn = PrepareTexture("AllwynOnBlack");
            var kino = PrepareTexture("KinoLogo");
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var round = root.GetComponent<KinoRoundController>();
                var flow = root.GetComponent<KinoExperienceController>();
                if (!flow) flow = root.AddComponent<KinoExperienceController>();
                flow.round = round; round.experience = flow;
                round.startAutomatically = false; round.showcaseBoostAfterSecondChance = false;
                if (round.launcher) round.launcher.autoStart = false;
                if (!flow.contentCanvas) BuildPresentation(flow, font, allwyn, kino);
                if (flow.allwynLogo.texture != allwyn) flow.brandingSeconds = 3;
                flow.allwynLogo.texture = allwyn;
                if (!flow.modeCanvas) BuildModeSelection(flow, font);
                flow.enclosure = ConfigureEnclosure(root);
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
            Debug.Log("KINO eight-stage experience configured.");
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
            presentation.enclosure = ConfigureEnclosure(presentation.gameObject);
            RemoveChild(presentation.transform, "View blackout");
            if (presentation.announcementCanvas) return;
            if (!font) font = PrepareFont();
            if (presentation.announcement) UnityEngine.Object.DestroyImmediate(presentation.announcement);
            var canvas = NewCanvas("Second Chance reading screen", presentation.transform, new Vector2(1100, 700));
            canvas.transform.localScale = Vector3.one * .002f;
            canvas.sortingOrder = 100;
            presentation.announcementCanvas = canvas;
            presentation.announcement = canvas.gameObject;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/KINOVR/Textures/SecondChanceLogo.png");
            Logo("Second Chance supplied logo", canvas.transform, texture, new Vector2(0, 90), 700);
            Label("Extra balls", canvas.transform, font, new Vector2(0, -150), new Vector2(1000, 70), 40).text = "3 ΕΠΙΠΛΕΟΝ ΜΠΑΛΕΣ";
            Label("Triple points", canvas.transform, font, new Vector2(0, -230), new Vector2(1000, 65), 34).text = "x3 ΠΟΝΤΟΙ";
            canvas.gameObject.SetActive(false);
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
            Require(flow.contentCanvas.renderMode == RenderMode.WorldSpace && flow.modeCanvas && flow.normalModeButton && flow.boostModeButton, "Missing VR mode selection.");
            Require(flow.body.font.HasCharacters(flow.safetyText, out uint[] missing, true, false), "Missing Greek safety glyphs.");
            KinoExperienceTests.ValidateState();
            KinoSecondChanceTests.ValidateRules();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/validation.txt", "PASS: session references, supplied logos, Greek text, startup guards, Normal/Boost selection, 360-degree background/fades, five-second closing, flow and round rules.\n");
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        public static void ApplyBatch() { Apply(); }
        public static void ApplyAndTestBatch() { Apply(); KinoExperienceTests.RunBatch(); }
    }
}
