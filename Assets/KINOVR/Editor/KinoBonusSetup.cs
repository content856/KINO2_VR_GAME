using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    public static partial class KinoGameplaySetup
    {
        internal const string BonusMaterialPath = "Assets/KINOVR/Materials/NumberedBallBonus.mat";
        internal const string BonusEffectPath = "Assets/KINOVR/Prefabs/KinoBonusCatch.prefab";
        internal const string BonusOutput = "Artifacts/KinoGameplay/KinoBonus";

        static void ConfigureKinoBonusBoard(KinoNumberBoard board)
        {
            var material = GraphicMaterial("BoardBonus", 1);
            material.SetFloat("_BonusRed", 1);
            EditorUtility.SetDirty(material);
            board.kinoBonusMarkerMaterial = material;
        }

        static void ConfigureKinoBonusBall(GameObject ball)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(BonusMaterialPath);
            if (!material)
            {
                material = new Material(Shader.Find("KINO/Ball Lacquer")) { name = "NumberedBallBonus" };
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_BonusRed", 1);
                material.SetFloat("_EnvironmentAmount", .16f);
                material.enableInstancing = true;
                AssetDatabase.CreateAsset(material, BonusMaterialPath);
            }
            var effect = AssetDatabase.LoadAssetAtPath<GameObject>(BonusEffectPath);
            if (!effect)
            {
                // A prefab variant keeps the ordinary catch's timing, shape and material.
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KINOVR/Prefabs/Explosion.prefab");
                if (!source) throw new InvalidOperationException("Missing ordinary catch effect.");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                try
                {
                    instance.name = "KinoBonusCatch";
                    foreach (var particles in instance.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        var main = particles.main;
                        main.startColor = new Color(1, .035f, .065f, 1);
                    }
                    effect = PrefabUtility.SaveAsPrefabAsset(instance, BonusEffectPath);
                }
                finally { Object.DestroyImmediate(instance); }
            }
            ball.GetComponent<KinoBallNumber>().kinoBonusMaterial = material;
            ball.GetComponent<Catchable>().kinoBonusCatchVFX = effect;
        }

        [MenuItem("Tools/KINO VR/KINO Bonus/1 - Set up red bonus balls")]
        public static void ApplyKinoBonus()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            const string path = "Assets/KINOVR/Prefabs/NumberedBall.prefab";
            var ball = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ConfigureKinoBonusBall(ball);
                PrefabUtility.SaveAsPrefabAsset(ball, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(ball); }
            // Serialize the new default while retaining every existing difficulty setting.
            const string gameplayPath = "Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab";
            var gameplay = PrefabUtility.LoadPrefabContents(gameplayPath);
            try
            {
                ConfigureKinoBonusBoard(gameplay.GetComponentInChildren<KinoNumberBoard>(true));
                PrefabUtility.SaveAsPrefabAsset(gameplay, gameplayPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(gameplay); }
            AssetDatabase.SaveAssets();
            KinoBonusTests.ValidateRulesAndAssets();
            Status("KINO_BONUS_READY");
        }

        [MenuItem("Tools/KINO VR/KINO Bonus/3 - Preview bonus ball and red catch")]
        public static void CaptureKinoBonus()
        {
            Directory.CreateDirectory(BonusOutput);
            var go = new GameObject("KINO Bonus preview camera");
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = .17f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.012f, .026f, .06f);
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 5;
            var ball = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KINOVR/Prefabs/NumberedBall.prefab"));
            GameObject effect = null;
            try
            {
                ball.GetComponent<Rigidbody>().isKinematic = true;
                ball.transform.position = new Vector3(0, 30, 0);
                camera.transform.SetPositionAndRotation(ball.transform.position + Vector3.back, Quaternion.identity);
                var visual = ball.GetComponent<KinoBallNumber>();
                visual.SetBonus(true);
                foreach (int number in new[] { 7, 80 })
                {
                    visual.SetNumber(number, camera.transform);
                    Capture(camera, BonusOutput + "/Bonus-" + number + ".png", 1000, 650);
                }
                camera.transform.position = ball.transform.position + new Vector3(.55f, .18f, -1);
                camera.transform.LookAt(ball.transform);
                visual.SetNumber(80, camera.transform);
                Capture(camera, BonusOutput + "/Bonus-oblique.png", 1000, 650);
                camera.transform.SetPositionAndRotation(ball.transform.position + Vector3.back * 3, Quaternion.identity);
                camera.orthographicSize = .85f;
                effect = Object.Instantiate(visual.GetComponent<Catchable>().kinoBonusCatchVFX, ball.transform.position, Quaternion.identity);
                ball.SetActive(false);
                effect.GetComponent<ParticleSystem>().Simulate(.15f, true, true);
                Capture(camera, BonusOutput + "/Bonus-catch.png", 1000, 650);
            }
            finally
            {
                if (effect) Object.DestroyImmediate(effect);
                Object.DestroyImmediate(ball);
                Object.DestroyImmediate(go);
            }
            Status("KINO_BONUS_PREVIEWS_READY");
        }

        [MenuItem("Tools/KINO VR/KINO Bonus/4 - Preview red board upgrade")]
        public static void CaptureKinoBonusBoard()
        {
            RequireScene();
            Directory.CreateDirectory(BonusOutput);
            var original = Object.FindFirstObjectByType<KinoRoundController>().board;
            bool wasActive = original.gameObject.activeSelf;
            var board = Object.Instantiate(original, original.transform.parent);
            original.gameObject.SetActive(false);
            var go = new GameObject("KINO Bonus board preview camera");
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            var screen = Screen().bounds;
            camera.orthographicSize = screen.size.y * .505f;
            camera.transform.SetPositionAndRotation(screen.center + Vector3.back * 2, Quaternion.identity);
            camera.nearClipPlane = .02f; camera.farClipPlane = 80;
            try
            {
                board.ResetBoard();
                board.MarkCaught(7); board.MarkCaught(26); board.MarkCaught(80, true);
                var state = new KinoRoundState();
                state.Begin(75, 0); state.TryCatch(7, 1); state.TryCatch(26, 2); state.TryCatch(80, 3, true);
                board.SetRoundProgress(state, false);
                Capture(camera, BonusOutput + "/Board-before-bonus.png", 1600, 904);
                board.MarkCaught(7, true); state.TryCatch(7, 4, true);
                // A following ordinary repeat must leave the upgraded marker red.
                board.MarkCaught(7); state.TryCatch(7, 5);
                board.SetRoundProgress(state, false);
                Capture(camera, BonusOutput + "/Board-after-bonus.png", 1600, 904);
            }
            finally
            {
                Object.DestroyImmediate(board.gameObject);
                original.gameObject.SetActive(wasActive);
                Object.DestroyImmediate(go);
            }
            Status("KINO_BONUS_BOARD_PREVIEWS_READY");
        }
    }
}
