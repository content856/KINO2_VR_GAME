using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KinoVR.Editor
{
    // Blue wave environment and the pre-game screens from the UX storyboard:
    // KINO + VR EXPERIENCE on the ready screen, a glass safety panel with a reading-time
    // bar, the welcome screen with the KINO logo, and the CF Asty brand fonts.
    // Called by "1 - Apply visitor flow"; the menu item below runs the same full setup.
    public static class KinoWaveEnvironmentSetup
    {
        const string FontFolder = "Assets/KINOVR/Fonts/";
        const string TextureFolder = "Assets/KINOVR/Textures/Experience/";
        const string MaterialFolder = "Assets/KINOVR/Materials/";

        [MenuItem("Tools/KINO VR/Experience/5 - Apply blue wave environment")]
        public static void ApplyMenu() => KinoExperienceSetup.Apply();

        internal static void Configure(GameObject root, KinoExperienceController flow, TMP_FontAsset fallback)
        {
            var waves = root.GetComponent<KinoWaveEnvironment>();
            if (!waves) waves = root.AddComponent<KinoWaveEnvironment>();
            waves.enclosure = flow.enclosure;
            waves.lineMaterial = PrepareMaterial("KinoWaveLines", "KINO/Wave Lines");
            waves.particleMaterial = PrepareMaterial("KinoWaveParticles", "KINO/Wave Particles");
            waves.skyMaterial = PrepareMaterial("KinoWaveSky", "KINO/Wave Sky");
            flow.waves = waves;
            // No mode buttons. KINO Boost stays inactive at launch; activating it later (includeBoost)
            // runs the Boost wave for every visitor. Setup never overrides that activation.
            flow.offerBoostSelection = false;
            // Approved safety copy replaces the prototype placeholder (and its 'temporary text' footer).
            if (flow.placeholderSafety || flow.safetyVersion == "placeholder-v1")
            {
                flow.safetyText = KinoExperienceController.DefaultSafetyText;
                flow.safetyVersion = "v1";
                flow.placeholderSafety = false;
            }
            flow.outroOnEnvironment = true;
            if (flow.preGame == null || flow.preGame.Count == 0) flow.preGame = KinoSequence.DefaultPreGame();
            if (flow.postGame == null || flow.postGame.Count == 0) flow.postGame = KinoSequence.DefaultPostGame();
            KinoSequence.Sanitize(flow.preGame, KinoSequence.PreGameSteps);
            KinoSequence.Sanitize(flow.postGame, KinoSequence.PostGameSteps);
            // The default sequence leaves the Allwyn logo off; the sequence editor owns the order after this.

            flow.headingFont = PrepareBrandFont("CFAstyStd-Bold", fallback);
            flow.textFont = PrepareBrandFont("CFAstyStd-Book", fallback);

            // Pooled GPU catch bursts for every ball in the game.
            var bursts = root.GetComponent<KinoCatchBurst>();
            if (!bursts) bursts = root.AddComponent<KinoCatchBurst>();
            bursts.material = PrepareMaterial("KinoCatchBurst", "KINO/Catch Burst");
            bursts.sparksPerBurst = 64;
            if (flow.startButton) ConfigureStartButton(flow.startButton, flow.headingFont, flow.textFont);
            PadBoardLogo(root);

            var canvas = flow.contentCanvas.transform;
            var kino = (Texture2D)flow.kinoLogo.texture;
            var vr = PrepareTexture("VrExperienceLogo");
            flow.brandLogo = Logo(canvas, "KINO brand logo", kino, flow.brandLogo);
            flow.vrExperienceLogo = Logo(canvas, "VR EXPERIENCE logo", vr, flow.vrExperienceLogo);

            if (!flow.safetyPanel)
            {
                var existing = canvas.Find("Safety glass panel");
                flow.safetyPanel = existing ? existing.GetComponent<KinoGlassPanel>() : null;
            }
            if (!flow.safetyPanel)
            {
                var go = new GameObject("Safety glass panel", typeof(RectTransform), typeof(KinoGlassPanel));
                go.transform.SetParent(canvas, false);
                flow.safetyPanel = go.GetComponent<KinoGlassPanel>();
            }
            flow.safetyPanel.raycastTarget = false;
            Place(flow.safetyPanel.rectTransform, new Vector2(0, -12), new Vector2(1040, 650));
            // Behind all text; the finale panel is never shown at the same time.
            flow.safetyPanel.transform.SetSiblingIndex(0);
            flow.safetyPanel.gameObject.SetActive(false);

            var track = canvas.Find("Safety reading time") as RectTransform;
            if (!track)
            {
                track = (RectTransform)new GameObject("Safety reading time", typeof(RectTransform), typeof(Image)).transform;
                track.SetParent(canvas, false);
            }
            Place(track, new Vector2(0, -268), new Vector2(560, 6));
            var trackImage = track.GetComponent<Image>();
            trackImage.color = new Color(.16f, .27f, .5f, 1); trackImage.raycastTarget = false;
            var fill = track.Find("Fill") as RectTransform;
            if (!fill)
            {
                fill = (RectTransform)new GameObject("Fill", typeof(RectTransform), typeof(Image)).transform;
                fill.SetParent(track, false);
            }
            fill.anchorMin = new Vector2(0, 0); fill.anchorMax = new Vector2(0, 1); fill.pivot = new Vector2(0, .5f);
            fill.anchoredPosition = Vector2.zero; fill.sizeDelta = new Vector2(0, 0);
            var fillImage = fill.GetComponent<Image>();
            fillImage.color = new Color(.36f, .8f, 1f, 1); fillImage.raycastTarget = false;
            flow.safetyProgress = fill;
            track.SetSiblingIndex(1);
            track.gameObject.SetActive(false);
        }

        // The board header shows KINO-logo-RGB through a RawImage whose UV rect crops the texture right
        // at the artwork's edges. Shrunk on the board, lower mipmaps blend the artwork up to those cut
        // edges and draw a thin bright outline. Widen the crop into the transparent padding and grow the
        // RawImage by the same ratio around its centre: the logo keeps its exact size and position.
        const float LogoPaddingPx = 60;
        static void PadBoardLogo(GameObject root)
        {
            foreach (var image in root.GetComponentsInChildren<RawImage>(true))
            {
                var texture = image.texture;
                if (!texture || texture.name != "KINO-logo-RGB") continue;
                var uv = image.uvRect;
                float pu = LogoPaddingPx / texture.width, pv = 2 * LogoPaddingPx / texture.height;
                // Already padded, or no room left inside the texture: leave it.
                if (uv.xMin - pu < -1e-4f || uv.yMin - pv < -1e-4f || uv.xMax + pu > 1 + 1e-4f || uv.yMax + pv > 1 + 1e-4f) continue;
                if (uv.width > .99f) continue;
                var padded = Rect.MinMaxRect(uv.xMin - pu, uv.yMin - pv, uv.xMax + pu, uv.yMax + pv);
                var rect = image.rectTransform;
                Vector2 oldSize = rect.rect.size;
                Vector2 newSize = new Vector2(oldSize.x * padded.width / uv.width, oldSize.y * padded.height / uv.height);
                Vector2 grow = newSize - oldSize;
                // Same visual centre whatever the pivot.
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newSize.x);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, newSize.y);
                rect.anchoredPosition -= Vector2.Scale(new Vector2(.5f, .5f) - rect.pivot, grow);
                image.uvRect = padded;
                Debug.Log("[KINO] Padded board logo crop to stop its edge outline: " + image.name, image);
            }
        }

        static void ConfigureStartButton(KinoExperienceModeButton start, TMP_FontAsset heading, TMP_FontAsset text)
        {
            var faceImage = start.GetComponent<Image>();
            // The Image stays as the Button's target graphic; the glass pill draws the button.
            if (faceImage) faceImage.color = new Color(1, 1, 1, 0);
            var rect = (RectTransform)start.transform;
            // 9 x 4.5 cm pill (about 11 x 5.6 degrees at 46 cm, above Meta's 2.5 to 3 degree minimum).
            // The touch area is a little larger than the pill so the touch stays forgiving.
            rect.sizeDelta = new Vector2(90, 45);
            if (start.pressArea) start.pressArea.size = new Vector3(110, 60, 45);
            KinoGlassPanel Panel(string name, float grow)
            {
                var found = start.transform.Find(name);
                var panel = found ? found.GetComponent<KinoGlassPanel>() : null;
                if (!panel)
                {
                    var go = new GameObject(name, typeof(RectTransform), typeof(KinoGlassPanel));
                    go.transform.SetParent(start.transform, false);
                    panel = go.GetComponent<KinoGlassPanel>();
                }
                panel.raycastTarget = false;
                Place(panel.rectTransform, Vector2.zero, rect.sizeDelta + Vector2.one * grow);
                panel.cornerRadius = (rect.sizeDelta.y + grow) * .5f;
                return panel;
            }
            var halo = Panel("Start halo", 9);
            halo.fill = new Color(0, 0, 0, 0); halo.edgeWidth = 0; halo.innerGlow = 5; halo.outerGlow = 15; halo.sheen = 0;
            halo.edge = new Color(.3f, .7f, 1f, 1f);
            halo.transform.SetSiblingIndex(0);
            var face = Panel("Start glass", 0);
            face.edgeWidth = 1.5f; face.innerGlow = 7; face.outerGlow = 8; face.sheen = .08f;
            face.transform.SetSiblingIndex(1);
            var visual = start.GetComponent<KinoStartButtonVisual>();
            if (!visual) visual = start.gameObject.AddComponent<KinoStartButtonVisual>();
            visual.button = start; visual.face = face; visual.halo = halo;
            var label = start.transform.Find("Mode label");
            var hint = start.transform.Find("Mode hint");
            visual.label = label ? label.GetComponent<TMP_Text>() : null;
            visual.hint = hint ? hint.GetComponent<TMP_Text>() : null;
            if (visual.label)
            {
                visual.label.font = heading; visual.label.fontSharedMaterial = heading.material;
                visual.label.characterSpacing = 3; visual.label.enableAutoSizing = true;
                visual.label.fontSizeMin = 12; visual.label.fontSizeMax = 20;
                visual.label.rectTransform.sizeDelta = new Vector2(80, 30);
                visual.label.rectTransform.anchoredPosition = Vector2.zero;
            }
            // At this size the small hint would be too small to read; the ready screen already says what to do.
            if (visual.hint) visual.hint.gameObject.SetActive(false);
        }

        internal static void Validate(KinoExperienceController flow)
        {
            var waves = flow.waves;
            Require(waves && waves.lineMaterial && waves.particleMaterial && waves.skyMaterial && waves.enclosure == flow.enclosure,
                "Blue wave environment is missing a material or its enclosure.");
            foreach (var material in new[] { waves.lineMaterial, waves.particleMaterial, waves.skyMaterial })
                Require(material.shader && !ShaderUtil.ShaderHasError(material.shader), "Wave shader error: " + material.name);
            Require(!flow.offerBoostSelection, "The NORMAL / ΜΕ BOOST menu must stay hidden: KINO Boost is a single on/off activation for every visitor.");
            Require(flow.preGame != null && flow.postGame != null &&
                KinoSequence.PreGameSteps.All(step => flow.preGame.Count(e => e != null && e.step == step) == 1) &&
                KinoSequence.PostGameSteps.All(step => flow.postGame.Count(e => e != null && e.step == step) == 1),
                "Experience sequence lists are incomplete or duplicated. Open Tools > KINO VR > Experience Sequence to repair them.");
            Require(flow.brandLogo && flow.vrExperienceLogo && flow.safetyPanel && flow.safetyProgress,
                "Missing ready/safety/welcome storyboard elements.");
            Require(flow.headingFont && flow.textFont, "Missing CF Asty brand fonts.");
            var bursts = flow.GetComponent<KinoCatchBurst>();
            Require(bursts && bursts.material && !ShaderUtil.ShaderHasError(bursts.material.shader), "Catch burst effect or its shader is missing.");
            Require(flow.startButton.GetComponent<KinoStartButtonVisual>(), "Start button visual is missing.");
            // Meta comfort and hands guidance: reading panels at least 0.5 m away and outside the 0.5 to 0.8 m
            // touch/ray hand-off zone; direct-touch controls 42 to 46 cm from the user.
            Require(flow.contentDistance >= .8f && flow.contentDistance <= 3, "Reading panels must sit 0.8 to 3 m away.");
            Require(flow.touchDistance >= .42f - 1e-4f && flow.touchDistance <= .46f + 1e-4f, "ΞΕΚΙΝΑ must sit 42 to 46 cm from the user.");
            Require(!flow.placeholderSafety && flow.safetyVersion != "placeholder-v1", "Safety screen still uses the placeholder copy.");
            foreach (var text in new[] { flow.safetyText, "ΕΤΟΙΜΟΣ ΓΙΑ ΠΑΙΧΝΙΔΙ; Κάθισε άνετα. Άγγιξε το ΞΕΚΙΝΑ όταν είσαι έτοιμος. Σήκωσε τα χέρια σου μπροστά σου.",
                "ΚΑΛΩΣ ΗΡΘΕΣ ΣΤΟΝ ΚΟΣΜΟ ΤΟΥ ΚΙΝΟ Πιάσε τις μπάλες με τα χέρια σου. Κάθε πιάσιμο μετράει!",
                "Μείνε καθιστός και κράτα τα χέρια σου ελεύθερα. Η εμπειρία ξεκινά σε λίγο." })
            {
                Require(flow.textFont.HasCharacters(text, out uint[] missingText, true, false) &&
                    flow.headingFont.HasCharacters(text, out uint[] missingHeading, true, false), "Brand font is missing glyphs for: " + text);
            }
        }

        static Material PrepareMaterial(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (!shader) throw new InvalidOperationException("Missing shader " + shaderName + ".");
            string path = MaterialFolder + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader) { name = name }; AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.renderQueue = 3000;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static TMP_FontAsset PrepareBrandFont(string fileName, TMP_FontAsset fallback)
        {
            string assetPath = FontFolder + fileName + " SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing) { AddFallback(existing, fallback); return existing; }
            var source = AssetDatabase.LoadAssetAtPath<Font>(FontFolder + fileName + ".otf");
            if (!source) throw new InvalidOperationException(FontFolder + fileName + ".otf is missing.");
            var font = TMP_FontAsset.CreateFontAsset(source, 72, 7, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048);
            font.name = fileName + " SDF";
            // Latin, Greek with tonos, and the punctuation used on the screens.
            string characters = new string(Enumerable.Range(32, 95).Concat(Enumerable.Range(0x384, 80)).Select(c => (char)c).ToArray()) + "•–’«»…";
            font.TryAddCharacters(characters, out string unusedMissing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, assetPath);
            foreach (var atlas in font.atlasTextures) { atlas.name = fileName + " Atlas"; AssetDatabase.AddObjectToAsset(atlas, font); }
            font.material.name = fileName + " Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
            AddFallback(font, fallback);
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            return font;
        }

        static void AddFallback(TMP_FontAsset font, TMP_FontAsset fallback)
        {
            if (!fallback || font == fallback) return;
            if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!font.fallbackFontAssetTable.Contains(fallback)) { font.fallbackFontAssetTable.Add(fallback); EditorUtility.SetDirty(font); }
        }

        static Texture2D PrepareTexture(string name)
        {
            string path = TextureFolder + name + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!importer) throw new InvalidOperationException(path + " is missing.");
            importer.textureType = TextureImporterType.Default;
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

        static RawImage Logo(Transform canvas, string name, Texture2D texture, RawImage existing)
        {
            if (!existing)
            {
                var found = canvas.Find(name);
                existing = found ? found.GetComponent<RawImage>() : null;
            }
            if (!existing)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(canvas, false);
                existing = go.GetComponent<RawImage>();
            }
            existing.texture = texture;
            existing.raycastTarget = false;
            Place(existing.rectTransform, Vector2.zero, new Vector2(400, 400f * texture.height / texture.width));
            existing.gameObject.SetActive(false);
            return existing;
        }

        static void Place(RectTransform rect, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.anchoredPosition = position; rect.sizeDelta = size; }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
