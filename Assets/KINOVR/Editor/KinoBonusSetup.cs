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
                gameplay.GetComponent<KinoRoundController>().roundDuration = 60;
                gameplay.GetComponent<KinoRoundController>().bonusNumberInterval = 1;
                var initialState = new KinoRoundState();
                initialState.Begin(60, 0);
                var board = gameplay.GetComponentInChildren<KinoNumberBoard>(true);
                board.ResetBoard();
                board.SetRoundProgress(initialState, false);
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
            var go = new GameObject("KINO Bonus board preview camera");
            var camera = go.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true;
            var screen = Screen().bounds;
            camera.orthographicSize = screen.size.y * .505f;
            camera.transform.SetPositionAndRotation(screen.center + Vector3.back * 2, Quaternion.identity);
            camera.nearClipPlane = .02f; camera.farClipPlane = 80;
            original.gameObject.SetActive(false);
            try
            {
                for (int step = 0; step < 2; step++)
                {
                    var board = Object.Instantiate(original, original.transform.parent);
                    try
                    {
                        board.gameObject.SetActive(true); board.ResetBoard();
                        var state = new KinoRoundState(); state.Begin(60, 0);
                        int[] caught = { 7, 26, 80 };
                        for (int i = 0; i < 20; i++)
                        {
                            state.TryRegisterNormalLaunch(i * 3);
                            if (i < caught.Length) { state.TryCatch(caught[i], i * 3); board.MarkCaught(caught[i]); }
                            else state.TryMiss(false);
                        }
                        state.Tick(60); state.TryBeginBonus(); state.TryRegisterBonusLaunch();
                        if (step == 1)
                        {
                            state.TryCatch(7, 61, true); board.MarkCaught(7, true); state.Stop();
                        }
                        board.SetRoundProgress(state, step == 1);
                        foreach (var label in board.GetComponentsInChildren<TMPro.TMP_Text>(true)) label.ForceMeshUpdate(true, true);
                        Capture(camera, BonusOutput + (step == 0 ? "/Board-before-bonus.png" : "/Board-after-bonus.png"), 1600, 904);
                    }
                    finally { Object.DestroyImmediate(board.gameObject); }
                }
            }
            finally
            {
                original.gameObject.SetActive(wasActive); Object.DestroyImmediate(go);
            }
            Status("KINO_BONUS_BOARD_PREVIEWS_READY");
        }

        internal static void CaptureFinalBonusTestView(KinoRoundController round, KinoPooledBall liveBall, string name)
        {
            Directory.CreateDirectory(BonusOutput);
            var go = new GameObject("Final bonus QA camera");
            var camera = go.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true;
            camera.nearClipPlane = .02f; camera.farClipPlane = 80;
            GameObject sample = null;
            try
            {
                if (liveBall)
                {
                    sample = Object.Instantiate(round.launcher.ballPrefab);
                    sample.GetComponent<Rigidbody>().isKinematic = true;
                    sample.GetComponent<Catchable>().enabled = false;
                    sample.transform.position = new Vector3(0, 30, 0);
                    camera.orthographicSize = .17f;
                    camera.transform.SetPositionAndRotation(sample.transform.position + Vector3.back, Quaternion.identity);
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.012f, .026f, .06f);
                    var visual = sample.GetComponent<KinoBallNumber>();
                    visual.SetBonus(true); visual.SetNumber(liveBall.GetComponent<Catchable>().Number, camera.transform);
                    visual.numberLabel.ForceMeshUpdate(true, true);
                }
                else
                {
                    var screen = Screen().bounds;
                    camera.orthographicSize = screen.size.y * .505f;
                    camera.transform.SetPositionAndRotation(screen.center + Vector3.back * 2, Quaternion.identity);
                    foreach (var label in round.board.GetComponentsInChildren<TMPro.TMP_Text>(true)) label.ForceMeshUpdate(true, true);
                }
                Capture(camera, BonusOutput + "/" + name + ".png", liveBall ? 1000 : 1600, liveBall ? 650 : 904);
            }
            finally
            {
                if (sample) Object.DestroyImmediate(sample);
                Object.DestroyImmediate(go);
            }
        }
    }
}
