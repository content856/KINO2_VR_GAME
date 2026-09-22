using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    public static partial class KinoGameplaySetup
    {
        internal const string SecondChanceOutput = "Artifacts/KinoGameplay/SecondChance";
        const string SecondChanceLogo = "Assets/KINOVR/Textures/SecondChanceLogo.png";

        [MenuItem("Tools/KINO VR/Second Chance/1 - Set up sequence and client showcase")]
        public static void ApplySecondChance()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            LoadMaterials();
            Directory.CreateDirectory(SecondChanceOutput);
            var importer = (TextureImporter)AssetImporter.GetAtPath(SecondChanceLogo);
            if (!importer) throw new InvalidOperationException("Render SecondChanceLogo.png from the supplied PDF first.");
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true; android.maxTextureSize = 4096; android.format = TextureImporterFormat.ASTC_4x4;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();

            const string ballPath = "Assets/KINOVR/Prefabs/NumberedBall.prefab";
            var ball = PrefabUtility.LoadPrefabContents(ballPath);
            try
            {
                ConfigureSecondChanceBall(ball);
                PrefabUtility.SaveAsPrefabAsset(ball, ballPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(ball); }
            const string gameplayPath = "Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab";
            var gameplay = PrefabUtility.LoadPrefabContents(gameplayPath);
            try
            {
                ConfigureSecondChance(gameplay.GetComponent<KinoRoundController>());
                PrefabUtility.SaveAsPrefabAsset(gameplay, gameplayPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(gameplay); }
            AssetDatabase.SaveAssets();
            KinoSecondChanceTests.ValidateRulesAndAssets();
            Status("SECOND_CHANCE_READY");
        }

        static void ConfigureSecondChanceBall(GameObject ball)
        {
            const string path = "Assets/KINOVR/Materials/NumberedBallSecondChance.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("KINO/Ball Lacquer")) { name = "NumberedBallSecondChance" };
                material.SetFloat("_SecondChanceGreen", 1);
                material.SetFloat("_EnvironmentAmount", .16f);
                material.enableInstancing = true;
                AssetDatabase.CreateAsset(material, path);
            }
            ball.GetComponent<KinoBallNumber>().secondChanceMaterial = material;
            const string effectPath = "Assets/KINOVR/Prefabs/SecondChanceCatch.prefab";
            var effect = AssetDatabase.LoadAssetAtPath<GameObject>(effectPath);
            if (!effect)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KINOVR/Prefabs/Explosion.prefab");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                try
                {
                    instance.name = "SecondChanceCatch";
                    foreach (var particles in instance.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        var main = particles.main;
                        main.startColor = new Color(.05f, 1, .18f, 1);
                    }
                    effect = PrefabUtility.SaveAsPrefabAsset(instance, effectPath);
                }
                finally { Object.DestroyImmediate(instance); }
            }
            ball.GetComponent<Catchable>().secondChanceCatchVFX = effect;
        }

        static void ConfigureSecondChance(KinoRoundController round)
        {
            var board = round.board;
            board.secondChanceMarkerMaterial = GraphicMaterial("BoardSecondChance", 1);
            board.secondChanceMarkerMaterial.SetFloat("_SecondChanceGreen", 1);
            EditorUtility.SetDirty(board.secondChanceMarkerMaterial);
            const string popupPath = "Assets/KINOVR/Materials/BoardMultiplierText.mat";
            var popupMaterial = AssetDatabase.LoadAssetAtPath<Material>(popupPath);
            if (!popupMaterial)
            {
                popupMaterial = new Material(font.material) { name = "BoardMultiplierText" };
                popupMaterial.SetFloat("_OutlineWidth", .18f);
                popupMaterial.SetColor("_OutlineColor", new Color(.08f, .1f, .015f, 1));
                AssetDatabase.CreateAsset(popupMaterial, popupPath);
            }
            if (round.secondChancePresentation)
            {
                foreach (var label in board.multiplierLabels) if (label) label.fontSharedMaterial = popupMaterial;
                KinoExperienceSetup.ConfigureSecondChanceScreen(round.secondChancePresentation);
                return;
            }
            var presentation = round.gameObject.AddComponent<KinoSecondChancePresentation>();
            round.secondChancePresentation = presentation;
            presentation.playerView = round.playerView;
            presentation.normalBrand = round.boostPresentation ? round.boostPresentation.normalBrand : null;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SecondChanceLogo);
            var root = FullBoardGroup("Second Chance display", board.transform);
            var announce = FullBoardGroup("Second Chance announcement", root);
            Graphic("Dark green backdrop", announce, 59, 121, 955, 443, null).color = new Color(.004f, .032f, .018f, .98f);
            Graphic("Green upper line", announce, 90, 151, 885, 3, null).color = new Color(.04f, .8f, .16f);
            Graphic("Green lower line", announce, 90, 532, 885, 3, null).color = new Color(.04f, .8f, .16f);
            LogoImage("2nd Chance supplied logo", announce, texture, 123, 203, 819);
            Text("Extra balls", announce, 125, 423, 815, 43, "3 EXTRA BALLS", 34, Color.white, true);
            Text("Triple points", announce, 125, 474, 815, 38, "x3 POINTS", 29, new Color(.22f, 1, .4f), true);
            presentation.announcement = announce.gameObject;
            var header = FullBoardGroup("Second Chance active header", root);
            LogoImage("Second Chance header logo", header, texture, 370, 9, 326);
            presentation.activeHeader = header.gameObject;
            // Separate overlay labels restart on every repeat catch without replacing the number.
            var effects = FullBoardGroup("Boost per-number multipliers", board.transform);
            board.multiplierLabels = new TMP_Text[80];
            for (int i = 0; i < 80; i++)
            {
                float x = 108 + i % 10 * 95.4f, y = 155 + i / 10 * 54.2f;
                var label = Text("Multiplier " + (i + 1), effects, x + 10, y - 31, 35, 22, "x3", 19, new Color(1, .9f, .3f), true);
                label.fontSharedMaterial = popupMaterial;
                label.gameObject.SetActive(false);
                board.multiplierLabels[i] = label;
            }
            KinoExperienceSetup.ConfigureSecondChanceScreen(presentation);
            presentation.ResetPresentation();
            EditorUtility.SetDirty(round);
        }
        static void LogoImage(string name, Transform parent, Texture2D texture, float x, float y, float width)
        {
            var logo = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            logo.transform.SetParent(parent, false);
            logo.texture = texture; logo.raycastTarget = false;
            Place(logo.rectTransform, x, y, width, width * texture.height / texture.width);
        }
    }
}
