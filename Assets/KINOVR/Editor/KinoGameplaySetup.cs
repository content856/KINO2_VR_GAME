using System;
using System.IO;
using System.Linq;
using KinoVR;
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
    // Only explicit menu actions / request files alter assets or scenes.
    [InitializeOnLoad]
    public static class KinoGameplaySetup
    {
        const string ScenePath = "Assets/KinoRotunda/Scenes/KinoRotunda.unity";
        const string OriginalGame = "Assets/KINOVR/Scenes/KINO_VR_Game.unity";
        const string Output = "Artifacts/KinoGameplay";
        const string Request = "Temp/KinoGameplay.request";
        const string TestKey = "KinoGameplay.PlayTest";
        const string RootName = "KINO Gameplay";
        static double nextPoll;
        static int testPhase;
        static double testAt;
        static bool busy;
        static TMP_FontAsset font;
        static Material panel, gold, badge;

        static KinoGameplaySetup()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode) { testPhase = 0; testAt = EditorApplication.timeSinceStartup + 1; }
            };
        }
        static void Status(string value)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/status.txt", DateTime.Now.ToString("s") + " " + value);
            Debug.Log("[KinoGameplay] " + value);
        }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || busy) return;
            if (EditorApplication.isPlaying)
            {
                if (SessionState.GetBool(TestKey, false)) PlayTestTick();
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .5;
            if (!File.Exists(Request)) return;
            string command = File.ReadAllText(Request).Trim();
            File.Delete(Request);
            busy = true;
            try
            {
                switch (command)
                {
                    case "inspect": Inspect(); break;
                    case "setup": Setup(); break;
                    case "preview": Preview(); break;
                    case "pack": Pack(); break;
                    case "validate": Validate(); Status("VALIDATED"); break;
                    case "test": Validate(); SessionState.SetBool(TestKey, true); Status("PLAY_TEST_STARTING"); EditorApplication.isPlaying = true; break;
                    default: throw new ArgumentException("Unknown gameplay command: " + command);
                }
            }
            catch (Exception e) { Failure(e); }
            finally { busy = false; }
        }
        static void Failure(Exception e)
        {
            SessionState.SetBool(TestKey, false);
            File.WriteAllText(Output + "/error.txt", e.ToString());
            Status("ERROR " + e.Message);
            Debug.LogException(e);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
        static void RequireScene()
        {
            if (SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open KinoRotunda first.");
            if (Lightmapping.isRunning) throw new InvalidOperationException("Wait for the light bake.");
            Directory.CreateDirectory(Output);
        }
        static MeshRenderer Screen() => Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Single(r => r.name.StartsWith("Screen_Surface", StringComparison.Ordinal));
        static void Inspect()
        {
            RequireScene();
            var screen = Screen();
            File.WriteAllText(Output + "/inspection.txt", $"Scene: {SceneManager.GetActiveScene().path}\nDirty: {SceneManager.GetActiveScene().isDirty}\nScreen: {screen.name}\nBounds: {screen.bounds}\nCamera: {Camera.main?.transform.position}\n");
            Status("INSPECTED");
        }

        [MenuItem("Tools/KINO VR/1 - Set up numbered timed round")]
        public static void Setup()
        {
            RequireScene();
            if (GameObject.Find(RootName)) { Validate(); Status("ALREADY_CONFIGURED"); return; }
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, Output + "/Before.unity", true);
            File.Copy("ProjectSettings/EditorBuildSettings.asset", Output + "/EditorBuildSettings.before.asset", true);
            LoadMaterials();
            var prefab = MakeNumberedBall();
            var environmentCamera = Camera.main;
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Set up KINO gameplay");
            var round = root.AddComponent<KinoRoundController>();
            var score = root.AddComponent<ScoreManager>();
            var view = root.AddComponent<KinoPlayerView>();
            round.score = score;
            round.playerView = view;
            view.environmentCamera = environmentCamera;

            var sourceScene = EditorSceneManager.OpenScene(OriginalGame, OpenSceneMode.Additive);
            try
            {
                var originalRig = sourceScene.GetRootGameObjects().Single(g => g.name == "OVRCameraRig");
                var rig = Object.Instantiate(originalRig);
                rig.name = "OVRCameraRig";
                SceneManager.MoveGameObjectToScene(rig, scene);
                rig.transform.SetParent(root.transform, false);
                rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                view.vrRig = rig;
                view.head = rig.GetComponent<OVRCameraRig>().centerEyeAnchor;
                rig.SetActive(false); // Activated by KinoPlayerView before gameplay starts on headset.
            }
            finally { EditorSceneManager.CloseScene(sourceScene, true); }
            SceneManager.SetActiveScene(scene);
            var cameraGO = new GameObject("Desktop gameplay preview");
            cameraGO.transform.SetParent(root.transform, false);
            var desktop = cameraGO.AddComponent<Camera>();
            cameraGO.AddComponent<AudioListener>();
            cameraGO.tag = "MainCamera";
            desktop.transform.position = new Vector3(0, 1.35f, 0);
            desktop.transform.LookAt(new Vector3(0, 2.2f, 11.8f));
            desktop.fieldOfView = 65;
            desktop.nearClipPlane = .05f;
            desktop.farClipPlane = 100;
            desktop.allowHDR = true;
            desktop.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            view.desktopCamera = desktop;
            cameraGO.SetActive(false);

            var launcherGO = new GameObject("Numbered ball launcher");
            launcherGO.transform.SetParent(root.transform, false);
            var launcher = launcherGO.AddComponent<BallLauncher>();
            launcher.autoStart = false;
            launcher.round = round;
            launcher.player = view.head;
            launcher.ballPrefab = prefab;
            launcher.aimHeightOffset = -.3f;
            launcher.flightTime = 1.6f;
            launcher.missRadius = .4f;
            launcher.spawnPoints = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                var spawn = new GameObject("Launch point " + (i + 1)).transform;
                spawn.SetParent(launcherGO.transform, false);
                spawn.position = new Vector3((i - 1) * 2.2f, 1.2f, 7.5f);
                launcher.spawnPoints[i] = spawn;
            }
            round.launcher = launcher;
            round.board = MakeBoard(root.transform, Screen().bounds);
            round.board.SetProgress(0, round.roundDuration, round.roundDuration, false);
            var builds = EditorBuildSettings.scenes.Select(s => new EditorBuildSettingsScene(s.path, false)).ToList();
            builds.RemoveAll(s => s.path == ScenePath);
            builds.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = builds.ToArray();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Validate();
            Pack();
            Selection.activeGameObject = root;
            Status("SETUP_COMPLETE");
        }

        static void LoadMaterials()
        {
            if (!AssetDatabase.IsValidFolder("Assets/KINOVR/Materials")) AssetDatabase.CreateFolder("Assets/KINOVR", "Materials");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (!font) throw new InvalidOperationException("Missing TMP font.");
            panel = GraphicMaterial("BoardBlue", 0);
            gold = GraphicMaterial("BoardGold", 1);
            badge = GraphicMaterial("BallNumberBadge", 2);
        }

        static void Pack()
        {
            RequireScene();
            var root = GameObject.Find(RootName);
            if (!root) throw new InvalidOperationException("Gameplay root is missing.");
            var environmentCamera = root.GetComponent<KinoPlayerView>().environmentCamera;
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, "Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab", InteractionMode.AutomatedAction);
            // The room camera belongs to the scene, so retain it as a scene override.
            var view = root.GetComponent<KinoPlayerView>();
            view.environmentCamera = environmentCamera;
            PrefabUtility.RecordPrefabInstancePropertyModifications(view);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Validate();
            Status("GAMEPLAY_PREFAB_READY");
        }
        static Material GraphicMaterial(string name, float mode)
        {
            string path = "Assets/KINOVR/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                var shader = Shader.Find("KINO/Board Graphic");
                if (!shader) throw new InvalidOperationException("Missing board shader.");
                material = new Material(shader) { name = name };
                material.SetFloat("_Mode", mode);
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }
        static GameObject MakeNumberedBall()
        {
            const string path = "Assets/KINOVR/Prefabs/NumberedBall.prefab";
            var contents = PrefabUtility.LoadPrefabContents("Assets/KINOVR/Prefabs/NormalBallPrefab.prefab");
            try
            {
                contents.name = "NumberedBall";
                contents.transform.localScale = Vector3.one * .3f;
                var body = contents.GetComponent<Rigidbody>();
                body.mass = .1f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                const string materialPath = "Assets/KINOVR/Materials/NumberedBallGold.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (!material)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "NumberedBallGold" };
                    material.SetColor("_BaseColor", new Color(1, .72f, .06f));
                    material.SetFloat("_Metallic", .35f);
                    material.SetFloat("_Smoothness", .65f);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                contents.GetComponent<MeshRenderer>().sharedMaterial = material;
                var visual = contents.AddComponent<KinoBallNumber>();
                var face = new GameObject("Number facing player").transform;
                face.SetParent(contents.transform, false);
                visual.face = face;
                var canvas = CanvasAt("Number badge", face, new Vector2(128, 128));
                canvas.transform.localPosition = new Vector3(0, 0, -.505f);
                canvas.transform.localScale = Vector3.one * (.60f / 128);
                Graphic("White badge", canvas.transform, 0, 0, 128, 128, badge);
                visual.numberLabel = Text("Number", canvas.transform, 0, 0, 128, 128, "1", 76, new Color(.025f, .04f, .07f), true);
                return PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        static KinoNumberBoard MakeBoard(Transform parent, Bounds screen)
        {
            var canvas = CanvasAt("Live KINO Board", parent, new Vector2(1065, 602));
            canvas.transform.position = screen.center + Vector3.back * .015f;
            canvas.transform.rotation = Quaternion.identity;
            canvas.transform.localScale = new Vector3(screen.size.x / 1065, screen.size.y / 602, .01f);
            var background = new GameObject("Existing KINO artwork", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            background.transform.SetParent(canvas.transform, false);
            Place(background.rectTransform, 0, 0, 1065, 602);
            background.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/KinoRotunda/Textures/KinoDisplay.png");
            background.raycastTarget = false;
            // Keep the user's logo and outer frame; replace the baked-in example numbers and times.
            Graphic("Live number field", canvas.transform, 59, 121, 955, 443, panel);
            Graphic("Catch header", canvas.transform, 48, 16, 310, 76, panel);
            Graphic("Timer header", canvas.transform, 774, 16, 235, 76, panel);
            var board = canvas.gameObject.AddComponent<KinoNumberBoard>();
            board.statusText = Text("Round status", canvas.transform, 65, 17, 278, 24, "BALLS CAUGHT", 17, new Color(.45f, .82f, 1), true, TextAlignmentOptions.Left);
            board.catchCountText = Text("Catch count", canvas.transform, 65, 39, 278, 47, "000", 43, Color.white, true, TextAlignmentOptions.Left);
            Text("Time caption", canvas.transform, 789, 17, 201, 24, "TIME", 17, new Color(.45f, .82f, 1), true, TextAlignmentOptions.Right);
            board.timeText = Text("Time remaining", canvas.transform, 789, 39, 201, 47, "01:15", 43, Color.white, true, TextAlignmentOptions.Right);
            var bar = Graphic("Time track", canvas.transform, 62, 97, 942, 10, null);
            bar.color = new Color(.005f, .035f, .10f);
            var fill = Graphic("Time remaining fill", bar.transform, 0, 0, 942, 10, null);
            fill.color = new Color(.03f, .68f, 1);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            board.timeFill = fill.rectTransform;
            for (int number = 1; number <= 80; number++)
            {
                int col = (number - 1) % 10, row = (number - 1) / 10;
                float x = 108 + col * 95.4f, y = 155 + row * 54.2f;
                var marker = Graphic("Caught " + number, canvas.transform, x - 42, y - 25, 84, 50, gold);
                board.caughtMarkers[number - 1] = marker.gameObject;
                board.numberLabels[number - 1] = Text("Number " + number, canvas.transform, x - 44, y - 26, 88, 52, number.ToString(), 33, board.waitingColor, false);
            }
            board.ResetBoard();
            return board;
        }
        static Canvas CanvasAt(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.GetComponent<RectTransform>().sizeDelta = size;
            return canvas;
        }
        static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x + width / 2, -y - height / 2);
        }
        static Image Graphic(string name, Transform parent, float x, float y, float w, float h, Material material)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.material = material;
            image.raycastTarget = false;
            Place(image.rectTransform, x, y, w, h);
            return image;
        }
        static TextMeshProUGUI Text(string name, Transform parent, float x, float y, float w, float h, string value, float size, Color color, bool bold, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            Place(text.rectTransform, x, y, w, h);
            return text;
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        [MenuItem("Tools/KINO VR/2 - Validate numbered round")]
        public static void Validate()
        {
            RequireScene();
            var round = Object.FindFirstObjectByType<KinoRoundController>();
            Assert(round && round.launcher && round.score && round.board && round.playerView, "Round references incomplete.");
            Assert(round.board.numberLabels.Length == 80 && round.board.caughtMarkers.Length == 80, "Expected 80 board positions.");
            for (int i = 0; i < 80; i++)
                Assert(round.board.numberLabels[i] && round.board.numberLabels[i].text == (i + 1).ToString() && round.board.caughtMarkers[i], "Incorrect board position " + (i + 1));
            Assert(round.playerView.vrRig.GetComponentsInChildren<HandCatcher>(true).Length == 2, "Expected two hand catchers.");
            Assert(round.playerView.head && round.playerView.desktopCamera, "Missing player view.");
            var prefab = round.launcher.ballPrefab;
            Assert(prefab && prefab.GetComponent<Catchable>() && prefab.GetComponent<KinoBallNumber>() && prefab.CompareTag("Ball"), "Invalid numbered ball prefab.");
            var state = new KinoRoundState();
            Assert(!state.TryCatch(1, 0), "Catch accepted before start.");
            state.Begin(75, 100);
            Assert(!state.TryCatch(0, 101) && !state.TryCatch(81, 101), "Invalid number accepted.");
            for (int n = 1; n <= 80; n++) Assert(state.TryCatch(n, 101), "Catch unexpectedly limited at " + n);
            Assert(state.CatchCount == 80 && state.UniqueCount == 80 && state.IsRunning, "Unexpected catch cap.");
            Assert(state.TryCatch(1, 102) && state.CatchCount == 81 && state.UniqueCount == 80, "Repeat handling incorrect.");
            Assert(state.TryCatch(80, 174.99), "Catch before deadline rejected.");
            Assert(!state.TryCatch(1, 175) && state.RemainingSeconds == 0, "Catch at deadline accepted.");
            state.Begin(10, 200);
            Assert(state.CatchCount == 0 && state.UniqueCount == 0 && !state.HasCaught(80), "Round reset failed.");
            state.Stop();
            Assert(!state.TryCatch(1, 201), "Catch after stop accepted.");
            File.WriteAllText(Output + "/validation.txt", "PASS: 80 numbered positions; two hand catchers; numbered prefab; invalid numbers; no 20-ball cap; repeat catches; exact deadline; round reset; stop.\n");
        }

        [MenuItem("Tools/KINO VR/3 - Capture board and ball previews")]
        public static void Preview()
        {
            RequireScene();
            var round = Object.FindFirstObjectByType<KinoRoundController>();
            var b = round.board;
            var screen = Screen().bounds;
            var go = new GameObject("Gameplay QA camera");
            var camera = go.AddComponent<Camera>();
            camera.nearClipPlane = .02f;
            camera.farClipPlane = 80;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            try
            {
                camera.orthographic = true;
                camera.orthographicSize = screen.size.y * .505f;
                camera.transform.position = screen.center + Vector3.back * 2;
                camera.transform.rotation = Quaternion.identity;
                b.ResetBoard();
                b.SetProgress(0, 75, 75, false);
                Capture(camera, Output + "/Board-empty.png", 1600, 904);
                foreach (int n in new[] { 1, 7, 16, 26, 37, 49, 59, 80 }) b.MarkCaught(n);
                b.SetProgress(8, 43, 75, false);
                Capture(camera, Output + "/Board-caught.png", 1600, 904);
                camera.orthographic = false;
                camera.fieldOfView = 65;
                camera.transform.SetPositionAndRotation(round.playerView.desktopCamera.transform.position, round.playerView.desktopCamera.transform.rotation);
                var ball = Object.Instantiate(round.launcher.ballPrefab);
                try
                {
                    ball.transform.position = new Vector3(.35f, 1.42f, 1.1f);
                    ball.GetComponent<KinoBallNumber>().SetNumber(80, camera.transform);
                    Capture(camera, Output + "/Player-view.png", 1600, 1000);
                }
                finally { Object.DestroyImmediate(ball); }
            }
            finally
            {
                b.ResetBoard();
                b.SetProgress(0, round.roundDuration, round.roundDuration, false);
                Object.DestroyImmediate(go);
            }
            Status("PREVIEWS_READY");
        }
        static void Capture(Camera camera, string path, int width, int height)
        {
            Canvas.ForceUpdateCanvases();
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
                RenderTexture.active = rt;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(pixels);
            }
        }
        static void PlayTestTick()
        {
            if (EditorApplication.timeSinceStartup < testAt) return;
            try
            {
                var round = Object.FindFirstObjectByType<KinoRoundController>();
                Assert(round, "Missing round in play mode.");
                if (testPhase == 0)
                {
                    round.BeginRound(10);
                    round.launcher.StopLaunching(false);
                    for (int i = 0; i < 25; i++)
                    {
                        var ball = round.launcher.SpawnBall();
                        Assert(ball, "Manual spawn failed.");
                        var caught = ball.GetComponent<Catchable>();
                        int number = caught.Number;
                        Assert(number >= 1 && number <= 80, "Spawned invalid number.");
                        Assert(ball.GetComponent<KinoBallNumber>().numberLabel.text == number.ToString(), "Ball number does not match label.");
                        caught.Catch();
                        caught.Catch();
                        Assert(round.board.caughtMarkers[number - 1].activeSelf, "Catch did not light board.");
                    }
                    Assert(round.State.CatchCount == 25 && round.score.CurrentScore == 25 && round.IsRunning, "Catch counted twice or stopped at 20.");
                    round.BeginRound(.25f);
                    Assert(round.State.CatchCount == 0 && round.score.CurrentScore == 0, "Restart failed.");
                    Assert(round.board.caughtMarkers.All(m => !m.activeSelf), "Restart did not clear board.");
                    round.launcher.SpawnBall();
                    testPhase = 1;
                    testAt = EditorApplication.timeSinceStartup + 1;
                }
                else
                {
                    Assert(!round.IsRunning && !round.launcher.IsLaunching, "Timer did not stop round.");
                    Assert(!round.TryCatch(1) && round.State.CatchCount == 0 && round.score.CurrentScore == 0, "Late catch changed score.");
                    Assert(!round.launcher.SpawnBall(), "Spawn accepted after timeout.");
                    Assert(Object.FindObjectsByType<Catchable>(FindObjectsSortMode.None).Length == 0, "Live balls remained after timeout.");
                    Assert(round.board.timeText.text == "00:00" && round.board.statusText.text == "ROUND COMPLETE", "Incorrect final board.");
                    File.WriteAllText(Output + "/play-validation.txt", "PASS: numbered prefab labels; 25 catches; double-catch guard; board correspondence; +1 counter; restart clears board; timer stops launch and catches; live balls cleared; final UI.\n");
                    SessionState.SetBool(TestKey, false);
                    Status("PLAY_TEST_PASSED");
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception e) { Failure(e); }
        }
    }
}
