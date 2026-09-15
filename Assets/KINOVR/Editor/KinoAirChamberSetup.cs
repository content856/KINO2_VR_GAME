using System;
using System.IO;
using System.Linq;
using KinoVR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    [InitializeOnLoad]
    public static class KinoAirChamberSetup
    {
        const string ScenePath = "Assets/KinoRotunda/Scenes/KinoRotunda.unity";
        const string BallPath = "Assets/KINOVR/Prefabs/NumberedBall.prefab";
        const string VisualPath = "Assets/KINOVR/Prefabs/DecorativeNumberedBall.prefab";
        const string AirPath = "Assets/KINOVR/Prefabs/KinoAirBalls.prefab";
        const string RootName = "KINO air balls";
        const string Output = "Artifacts/KinoGameplay/AirBalls";
        const string TestKey = "KinoAirBalls.BatchTest";
        static readonly int[] Bays = { 2, 6, 10, 14, 18, 22, 26 };
        static int phase;
        static double nextCheck;
        static Vector3 tubeBefore, lotteryBefore;
        static bool passed;

        static KinoAirChamberSetup()
        {
            EditorApplication.update += PlayTick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(TestKey, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                { phase = 0; passed = false; nextCheck = EditorApplication.timeSinceStartup + 1; }
                if (state == PlayModeStateChange.EnteredEditMode)
                { SessionState.SetBool(TestKey, false); EditorApplication.Exit(passed ? 0 : 1); }
            };
        }

        public static void BatchRun()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                Setup();
                Validate();
                CapturePreview();
                // Reload the saved asset to exercise persistence, not the preview copy.
                EditorSceneManager.OpenScene(ScenePath);
                SessionState.SetBool(TestKey, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception e) { Fail(e); }
        }

        public static void BatchCapture()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                CapturePreview();
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Fail(e); }
        }

        [MenuItem("Tools/KINO VR/Air balls/1 - Set up tube and lottery balls")]
        public static void Setup()
        {
            RequireScene();
            Directory.CreateDirectory(Output);
            var scene = SceneManager.GetActiveScene();
            if (!File.Exists(Output + "/Before.unity"))
                EditorSceneManager.SaveScene(scene, Output + "/Before.unity", true);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(BallPath);
            Check(source, "Gameplay ball is missing.");
            // A prefab variant inherits the exact gameplay mesh, lacquer, and text.
            // Removing physics/catching prevents decorative numbers awarding points.
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                visual.name = "DecorativeNumberedBall";
                foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
                foreach (var catcher in visual.GetComponentsInChildren<Catchable>(true)) Object.DestroyImmediate(catcher);
                visual.tag = "Untagged";
                PrefabUtility.SaveAsPrefabAsset(visual, VisualPath);
            }
            finally { Object.DestroyImmediate(visual); }
            var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath);
            var old = GameObject.Find(RootName);
            if (old) Object.DestroyImmediate(old);
            var root = new GameObject(RootName);
            foreach (int bay in Bays)
            {
                var glass = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                    .Single(r => r.name == $"Tube_{bay:00}__TubeGlass");
                var chamber = new GameObject($"Tube {bay:00} - idle air").AddComponent<KinoAirChamber>();
                chamber.transform.SetParent(root.transform, false);
                chamber.transform.position = new Vector3(glass.bounds.center.x, 0, glass.bounds.center.z);
                chamber.seed = bay;
                chamber.balls = new KinoBallNumber[9]; chamber.numbers = new int[9];
                for (int i = 0; i < 9; i++)
                    AddBall(chamber, visualPrefab, i, new Vector3(0, .65f + i * .54f, 0), .3f,
                        Quaternion.Euler(0, bay * 360f / 28, 0));
            }
            var lottery = new GameObject("Lottery - slow idle, fast round").AddComponent<KinoAirChamber>();
            lottery.transform.SetParent(root.transform, false);
            lottery.transform.position = new Vector3(0, .67f, 10.67f);
            lottery.kind = KinoAirChamber.ChamberKind.Lottery;
            lottery.radius = .599f; lottery.floor = 0; lottery.ballRadius = .63f * .18f; lottery.seed = 83;
            lottery.balls = new KinoBallNumber[14]; lottery.numbers = new int[14];
            for (int i = 0; i < 14; i++)
            {
                float angle = i < 8 ? i * Mathf.PI * 2 / 8 : (i - 8) * Mathf.PI * 2 / 6 + .35f;
                float radius = i < 8 ? .414f : .31f;
                AddBall(lottery, visualPrefab, i, new Vector3(Mathf.Cos(angle) * radius,
                    i < 8 ? .137f : .32f, Mathf.Sin(angle) * radius), .18f, Quaternion.identity);
            }
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, AirPath, InteractionMode.AutomatedAction);
            var round = Object.FindFirstObjectByType<KinoRoundController>();
            Check(round, "Round controller is missing.");
            foreach (var chamber in root.GetComponentsInChildren<KinoAirChamber>())
            {
                chamber.round = round; chamber.playerView = round.playerView;
                PrefabUtility.RecordPrefabInstancePropertyModifications(chamber);
            }
            // Retain imported source objects for model regeneration, but replace
            // their visible geometry in this scene with the numbered prefab variant.
            foreach (var renderer in OriginalBalls())
            {
                renderer.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void AddBall(KinoAirChamber chamber, GameObject prefab, int index, Vector3 position, float scale, Quaternion rotation)
        {
            var ball = (GameObject)PrefabUtility.InstantiatePrefab(prefab, chamber.transform);
            int number = (chamber.seed * 13 + index * 17) % 80 + 1;
            ball.name = "Air ball " + number;
            ball.transform.localPosition = position;
            ball.transform.localRotation = rotation;
            ball.transform.localScale = Vector3.one * scale;
            var label = ball.GetComponent<KinoBallNumber>();
            label.SetNumber(number, null);
            PrefabUtility.RecordPrefabInstancePropertyModifications(ball.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(label.numberLabel);
            chamber.balls[index] = label; chamber.numbers[index] = number;
        }

        static MeshRenderer[] OriginalBalls() => Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(r => r.name.StartsWith("Ball_Tube_", StringComparison.Ordinal) || r.name.StartsWith("Ball_Armillary_", StringComparison.Ordinal)).ToArray();

        [MenuItem("Tools/KINO VR/Air balls/2 - Validate containment and states")]
        public static void Validate()
        {
            RequireScene();
            Directory.CreateDirectory(Output);
            KinoGameplaySetup.Validate();
            var chambers = GameObject.Find(RootName).GetComponentsInChildren<KinoAirChamber>();
            Check(chambers.Length == 8 && chambers.Sum(c => c.balls.Length) == 77, "Expected 7 tubes, one lottery and 77 balls.");
            Check(OriginalBalls().Length == 77 && OriginalBalls().All(r => !r.enabled), "Old decorative balls remain visible.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(BallPath);
            foreach (var chamber in chambers)
            foreach (var ball in chamber.balls)
            {
                Check(ball.GetComponent<MeshFilter>().sharedMesh == source.GetComponent<MeshFilter>().sharedMesh, "Mesh differs from gameplay.");
                Check(ball.GetComponent<MeshRenderer>().sharedMaterial == source.GetComponent<MeshRenderer>().sharedMaterial, "Lacquer differs from gameplay.");
                Check(ball.numberLabel.font == source.GetComponent<KinoBallNumber>().numberLabel.font && ball.numberLabel.color == Color.black, "Incorrect number styling.");
                var scale = ball.numberLabel.transform.lossyScale;
                Check(Mathf.Abs(scale.x - scale.y) < .00001f, "Number is stretched.");
                Check(!ball.GetComponent<Catchable>() && !ball.GetComponent<Collider>() && !ball.GetComponent<Rigidbody>(), "Decorative ball participates in gameplay physics.");
            }
            // Sample a full minute, including transitions, using disposable copies.
            var copy = Object.Instantiate(GameObject.Find(RootName));
            var randomBefore = UnityEngine.Random.state;
            float minGap = float.MaxValue, tubeTravel = 0;
            try
            {
                var samples = copy.GetComponentsInChildren<KinoAirChamber>();
                var starts = samples.Select(c => c.balls[4].transform.localPosition).ToArray();
                foreach (var c in samples) c.ResetMotion();
                for (int frame = 0; frame < 3600; frame++)
                {
                    bool running = frame >= 1200 && frame < 2400;
                    for (int n = 0; n < samples.Length; n++)
                    {
                        var c = samples[n];
                        var before = c.balls[4].transform.localPosition;
                        c.Advance(1f / 60, running);
                        if (c.kind == KinoAirChamber.ChamberKind.Tube)
                        {
                            if (running) Check(c.balls[4].transform.localPosition == before, "Tube idle wrote a gameplay position.");
                            else tubeTravel = Mathf.Max(tubeTravel, Mathf.Abs(c.balls[4].transform.localPosition.y - starts[n].y));
                        }
                        for (int i = 0; i < c.balls.Length; i++)
                        {
                            var p = c.balls[i].transform.localPosition;
                            Check(float.IsFinite(p.sqrMagnitude), "Motion contains NaN/Infinity.");
                            Check(p.y >= c.floor + c.ballRadius + .011f, "Ball passes through chamber floor.");
                            if (c.kind == KinoAirChamber.ChamberKind.Tube)
                            {
                                Check(new Vector2(p.x, p.z).magnitude + c.ballRadius < c.radius, "Ball leaves tube wall.");
                                Check(p.y + c.ballRadius < c.ceiling, "Ball leaves tube ceiling.");
                            }
                            else Check(p.magnitude + c.ballRadius < c.radius, "Ball leaves lottery dome.");
                            for (int j = i + 1; j < c.balls.Length; j++)
                            {
                                var delta = c.balls[j].transform.localPosition - p;
                                float gap = c.kind == KinoAirChamber.ChamberKind.Tube ? Mathf.Abs(delta.y) - .265f : delta.magnitude - c.ballRadius * 2;
                                minGap = Mathf.Min(minGap, gap);
                            }
                        }
                    }
                }
                Check(minGap > -.018f, "Ball overlap exceeds tolerance: " + minGap);
                Check(tubeTravel > .8f, "Tube motion does not visibly rise/fall.");
                Check(UnityEngine.Random.state.Equals(randomBefore), "Decorations consumed gameplay randomness.");
                File.WriteAllText(Output + "/validation.txt", $"PASS: 77 replacements; exact gameplay mesh/material/font; uniform text; no catching/physics; 60s wall/floor/dome containment; contacts; tubes freeze in gameplay and resume; independent randomness.\nMinimum contact gap: {minGap:F5} m. Tube excursion: {tubeTravel:F3} m.\n");
            }
            finally { Object.DestroyImmediate(copy); }
        }

        [MenuItem("Tools/KINO VR/Air balls/3 - Capture air motion previews")]
        public static void CapturePreview()
        {
            RequireScene();
            Directory.CreateDirectory(Output);
            var source = GameObject.Find(RootName);
            var copy = Object.Instantiate(source);
            source.SetActive(false);
            var go = new GameObject("Air balls preview camera");
            var camera = go.AddComponent<Camera>();
            camera.allowHDR = true; camera.nearClipPlane = .03f; camera.farClipPlane = 120;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            try
            {
                var chambers = copy.GetComponentsInChildren<KinoAirChamber>();
                foreach (var c in chambers) { c.ResetMotion(); c.FaceNumbers(camera.transform); }
                void Pose(Vector3 from, Vector3 to, float fov)
                {
                    camera.transform.position = from; camera.transform.LookAt(to); camera.fieldOfView = fov;
                    foreach (var c in chambers) c.FaceNumbers(camera.transform);
                }
                Pose(new Vector3(0, 2.7f, 1.4f), new Vector3(0, 2.9f, 12.4f), 69);
                Capture(camera, Output + "/Stage.png", 1600, 1000);
                for (int shot = 0; shot < 2; shot++)
                {
                    string folder = Output + (shot == 0 ? "/TubeFrames" : "/LotteryFrames");
                    Directory.CreateDirectory(folder);
                    foreach (var c in chambers) c.ResetMotion();
                    for (int frame = 0; frame < 120; frame++)
                    {
                        for (int sub = 0; sub < 3; sub++) foreach (var c in chambers) c.Advance(1f / 60, shot == 1 && frame >= 60);
                        if (shot == 0) Pose(new Vector3(4.85f, 3.0f, 8.65f), new Vector3(6.12f, 2.9f, 12.7f), 67);
                        else Pose(new Vector3(.25f, 1.25f, 8.85f), new Vector3(0, .88f, 10.67f), 35);
                        Capture(camera, folder + "/" + frame.ToString("000") + ".png", shot == 0 ? 640 : 960, 640);
                        if (frame == 40) Capture(camera, Output + (shot == 0 ? "/Tube.png" : "/Lottery-idle.png"), 1280, 1000);
                        if (shot == 1 && frame == 100) Capture(camera, Output + "/Lottery-gameplay.png", 1280, 1000);
                    }
                }
                // Capture the room again after the first render has loaded the
                // transparent shader variants and reflection resources.
                Pose(new Vector3(0, 2.7f, 1.4f), new Vector3(0, 2.9f, 12.4f), 69);
                Capture(camera, Output + "/Stage.png", 1600, 1000);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(go); source.SetActive(true); }
        }

        static void Capture(Camera camera, string path, int width, int height)
        {
            Canvas.ForceUpdateCanvases();
            var active = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var output = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            try
            {
                camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(target, output); RenderTexture.active = output;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = active; GL.sRGBWrite = srgb;
                RenderTexture.ReleaseTemporary(target); RenderTexture.ReleaseTemporary(output); Object.DestroyImmediate(texture);
            }
        }

        static void PlayTick()
        {
            if (!SessionState.GetBool(TestKey, false) || !EditorApplication.isPlaying || passed || EditorApplication.timeSinceStartup < nextCheck) return;
            try
            {
                var round = Object.FindFirstObjectByType<KinoRoundController>();
                var chambers = Object.FindObjectsByType<KinoAirChamber>(FindObjectsSortMode.None);
                var tube = chambers.First(c => c.kind == KinoAirChamber.ChamberKind.Tube);
                var lottery = chambers.Single(c => c.kind == KinoAirChamber.ChamberKind.Lottery);
                if (phase == 0)
                {
                    round.FinishRound();
                    tubeBefore = tube.balls[4].transform.position; lotteryBefore = lottery.balls[4].transform.position;
                }
                else if (phase == 1)
                {
                    Check(Vector3.Distance(tubeBefore, tube.balls[4].transform.position) > .03f, "Idle tube is static in Play mode.");
                    Check(Vector3.Distance(lotteryBefore, lottery.balls[4].transform.position) > .01f, "Idle lottery is static.");
                    round.BeginRound(20); round.launcher.StopLaunching(true);
                    tubeBefore = tube.balls[4].transform.position;
                    var ball = round.launcher.SpawnBall();
                    Check(ball && ball.GetComponent<Catchable>(), "Gameplay spawn failed.");
                    ball.GetComponent<Catchable>().Catch();
                    Check(round.State.CatchCount == 1, "Gameplay catch failed.");
                }
                else if (phase == 2)
                {
                    Check(tubeBefore == tube.balls[4].transform.position && !tube.IdleMotionActive, "Tube did not hand off when gameplay began.");
                    Check(Mathf.Abs(lottery.CurrentMixSpeed - lottery.gameplayMixSpeed) < .01f, "Lottery did not speed up.");
                    Check(Object.FindObjectsByType<Catchable>(FindObjectsSortMode.None).Length == 0, "Decorations are catchable.");
                    foreach (var c in chambers) for (int i = 0; i < c.balls.Length; i++)
                        Check(c.balls[i].numberLabel.text == c.numbers[i].ToString(), "Runtime number mismatch.");
                    round.BeginRound(.15f); round.launcher.StopLaunching(true);
                }
                else
                {
                    Check(!round.IsRunning && tube.IdleMotionActive, "Deadline did not restore idle.");
                    Check(tubeBefore != tube.balls[4].transform.position, "Tube did not resume after timeout.");
                    Check(Mathf.Abs(lottery.CurrentMixSpeed - lottery.idleMixSpeed) < .01f, "Lottery did not slow down.");
                    File.WriteAllText(Output + "/play-test.txt", "PASS: saved scene reloaded; idle air moves; BeginRound freezes tube motion; lottery speeds up; gameplay spawn and catch work; 77 decorative balls cannot be caught; matching runtime numbers; deadline restores idle motion and slow mixing.\n");
                    passed = true; EditorApplication.isPlaying = false; return;
                }
                phase++; nextCheck = EditorApplication.timeSinceStartup + 2;
            }
            catch (Exception e) { Fail(e); }
        }

        static void RequireScene()
        {
            Check(SceneManager.GetActiveScene().path == ScenePath && !EditorApplication.isPlayingOrWillChangePlaymode, "Open KinoRotunda in Edit mode first.");
            Check(!Lightmapping.isRunning, "Wait for the light bake.");
        }
        static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        static void Fail(Exception e)
        {
            Directory.CreateDirectory(Output); File.WriteAllText(Output + "/error.txt", e.ToString());
            Debug.LogException(e); SessionState.SetBool(TestKey, false); EditorApplication.Exit(1);
        }
    }
}
