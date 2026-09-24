using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    public static class KinoMainSpecialSetup
    {
        public const string Output = "Artifacts/KinoGameplay/MainSpecials";
        public const string BallPath = "Assets/KINOVR/Prefabs/NumberedBall.prefab";
        [MenuItem("Tools/KINO VR/Main specials/1 - Apply glow, Mystery and contact feedback")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            var ball = PrefabUtility.LoadPrefabContents(BallPath);
            TMP_FontAsset font;
            try
            {
                var visual = ball.GetComponent<KinoBallNumber>();
                visual.glowMaterial = Material("NumberedBallGlow", "KINO/Ball Lacquer");
                visual.glowMaterial.SetFloat("_Glow", 1);
                visual.glowMaterial.SetFloat("_EnvironmentAmount", .05f);
                EditorUtility.SetDirty(visual.glowMaterial);
                visual.mysteryMaterial = Material("NumberedBallMystery", "KINO/Ball Lacquer");
                visual.mysteryMaterial.SetFloat("_MysteryDark", 1);
                EditorUtility.SetDirty(visual.mysteryMaterial);
                var halo = ball.transform.Find("Glow halo");
                if (!halo)
                {
                    halo = new GameObject("Glow halo", typeof(MeshFilter), typeof(MeshRenderer)).transform;
                    halo.SetParent(ball.transform, false);
                }
                halo.localScale = Vector3.one * 1.32f;
                halo.GetComponent<MeshFilter>().sharedMesh = ball.GetComponent<MeshFilter>().sharedMesh;
                var renderer = halo.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = Material("NumberedBallHalo", "KINO/Glow Halo");
                renderer.sharedMaterial.SetColor("_Color", new Color(3, 2.3f, .65f, 1));
                EditorUtility.SetDirty(renderer.sharedMaterial);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                visual.glowHalo = halo.gameObject;
                halo.gameObject.SetActive(false);
                font = visual.numberLabel.font;
                PrefabUtility.SaveAsPrefabAsset(ball, BallPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(ball); }

            const string path = "Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab";
            var gameplay = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var round = gameplay.GetComponent<KinoRoundController>();
                round.enableMainSpecialBalls = true;
                var feedback = gameplay.GetComponent<KinoCatchFeedback>();
                if (!feedback) feedback = gameplay.AddComponent<KinoCatchFeedback>();
                feedback.font = font;
                const string textPath = "Assets/KINOVR/Materials/CatchPopupText.mat";
                var text = AssetDatabase.LoadAssetAtPath<Material>(textPath);
                if (!text)
                {
                    text = new Material(font.material) { name = "CatchPopupText" };
                    AssetDatabase.CreateAsset(text, textPath);
                }
                text.EnableKeyword("OUTLINE_ON");
                text.SetFloat("_OutlineWidth", .22f);
                text.SetColor("_OutlineColor", new Color(.02f, .015f, .025f, 1));
                EditorUtility.SetDirty(text);
                feedback.textMaterial = text;
                round.catchFeedback = feedback;
                PrefabUtility.SaveAsPrefabAsset(gameplay, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(gameplay); }
            AssetDatabase.SaveAssets();
            KinoMainSpecialTests.ValidateRules();
            ValidateAssets();
        }
        static Material Material(string name, string shader)
        {
            string path = "Assets/KINOVR/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            material = new Material(Shader.Find(shader)) { name = name, enableInstancing = true };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        public static void ValidateAssets()
        {
            var ball = AssetDatabase.LoadAssetAtPath<GameObject>(BallPath).GetComponent<KinoBallNumber>();
            if (!ball.glowMaterial || !ball.mysteryMaterial || !ball.glowHalo || ball.glowHalo.activeSelf)
                throw new InvalidOperationException("Missing glow/Mystery appearance or halo active on a normal ball.");
            if (!ball.numberLabel.font.HasCharacter('?') || !ball.numberLabel.font.HasCharacter('×'))
                throw new InvalidOperationException("Feedback font lacks ? or ×.");
            var round = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab").GetComponent<KinoRoundController>();
            if (!round.enableMainSpecialBalls || !round.catchFeedback || !round.catchFeedback.textMaterial)
                throw new InvalidOperationException("Main-special gameplay or feedback not configured.");
        }
        [MenuItem("Tools/KINO VR/Main specials/2 - Capture appearance")]
        public static void Preview()
        {
            Directory.CreateDirectory(Output);
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Special ball appearance preview");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var camera = root.AddComponent<Camera>();
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.04f, .045f, .065f);
            camera.fieldOfView = 45;
            camera.nearClipPlane = .05f;
            var target = new RenderTexture(1500, 750, 24);
            var previous = RenderTexture.active;
            try
            {
                var font = AssetDatabase.LoadAssetAtPath<GameObject>(BallPath).GetComponent<KinoBallNumber>().numberLabel.font;
                for (int i = 0; i < 3; i++)
                {
                    var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BallPath), root.transform);
                    go.transform.position = new Vector3((i - 1) * .9f, .12f, 2.4f);
                    go.transform.localScale = Vector3.one * .6f;
                    var visual = go.GetComponent<KinoBallNumber>();
                    visual.SetBonus(false, false, i == 1, i == 2);
                    visual.SetNumber(i == 2 ? 0 : 28, camera.transform);
                    visual.numberLabel.ForceMeshUpdate();
                    var labelGO = new GameObject("Value", typeof(TextMeshPro));
                    labelGO.transform.SetParent(root.transform, false);
                    labelGO.transform.position = new Vector3((i - 1) * .9f, -.32f, 2.4f);
                    labelGO.transform.localScale = Vector3.one * .16f;
                    var label = labelGO.GetComponent<TextMeshPro>();
                    label.font = font; label.fontSize = 6; label.alignment = TextAlignmentOptions.Center;
                    label.text = i == 0 ? "+1" : i == 1 ? "+2" : "×2 / ×3 / ×4";
                    label.color = i == 1 ? new Color(1, .87f, .24f) : Color.white;
                    label.ForceMeshUpdate();
                }
                camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                File.WriteAllBytes(Output + "/appearance.png", image.EncodeToPNG());
                foreach (Transform child in root.transform) child.gameObject.SetActive(false);
                var feedback = root.AddComponent<KinoCatchFeedback>();
                feedback.font = font;
                feedback.textMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/KINOVR/Materials/CatchPopupText.mat");
                feedback.ResetFeedback(camera.transform);
                for (int i = 0; i < 5; i++)
                    feedback.Show(new Vector3((i - 2) * .28f, .02f, 1.2f),
                        i == 0 ? Catchable.BallType.Normal : i == 1 ? Catchable.BallType.MoreWins : Catchable.BallType.Mystery, i);
                var state = new KinoRoundState(); state.Begin(60, 0, 10);
                double when = state.NextMysteryLaunchAt;
                state.TryRegisterMysteryLaunch(when); state.TryCatchMystery(4, when);
                feedback.Present(state);
                foreach (var label in root.GetComponentsInChildren<TMP_Text>()) label.ForceMeshUpdate();
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                File.WriteAllBytes(Output + "/contact-feedback.png", image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
