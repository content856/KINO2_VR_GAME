using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    [InitializeOnLoad]
    public static class KinoExperienceTests
    {
        const string Key = "KINO.ExperienceTest";
        static double deadline, inputDeadline, closingClock, closingRealtime, menuReturnedAt;
        static bool started, inputSent, handMoved, remountPending, completionPending, completionVerified;
        static float completedBlackout, completedAudio;
        static int run, results, closed, completedLiveScore;
        static string currentSession, completedSession, completedRecord;
        static GameObject hand;
        static float handCreatedAt;
        static readonly List<KinoExperienceStage> visited = new List<KinoExperienceStage>();
        static readonly HashSet<KinoRoundPhase> phases = new HashSet<KinoRoundPhase>();
        static readonly HashSet<string> captured = new HashSet<string>();
        static KinoExperienceStage previous;

        static KinoExperienceTests()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                { started = false; deadline = EditorApplication.timeSinceStartup + 240; }
                if (change == PlayModeStateChange.EnteredEditMode)
                {
                    RestoreEditorXR();
                    if (!SessionState.GetBool(Key + "Exit", false)) return;
                    SessionState.SetBool(Key + "Exit", false);
                    EditorApplication.Exit(SessionState.GetBool(Key + "Passed", false) ? 0 : 1);
                }
            };
        }

        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        public static void ValidateState()
        {
            var state = new KinoExperienceState();
            bool Advance(double now, bool external = false) => state.Advance(now, 1, 12, 5, 6, 10, 6, external);
            Check(!state.ConfirmSafety() && !state.BeginBoost(0), "Input accepted before a session.");
            state.SelectMode(0);
            Check(state.Stage == KinoExperienceStage.ModeSelection && !Advance(1000), "Mode selection started automatically.");
            state.Begin(0);
            Check(!state.IncludeBoost && !Advance(.9), "Normal mode or startup duration failed.");
            Check(Advance(1) && state.Stage == KinoExperienceStage.Safety, "Safety missing.");
            Check(!Advance(12.99), "Safety cut short.");
            Check(!Advance(100, true), "External confirmation bypassed.");
            Check(state.ConfirmSafety() && !state.ConfirmSafety(), "Confirmation guard failed.");
            Check(Advance(100, true) && state.Stage == KinoExperienceStage.Branding, "Branding order.");
            Check(!Advance(104.99), "Branding cut short.");
            Check(Advance(105) && state.Stage == KinoExperienceStage.Introduction, "Introduction order.");
            Check(Advance(111) && state.Stage == KinoExperienceStage.Gameplay, "Gameplay order.");
            Check(!state.Finish(111) && !state.BeginBoost(111), "Finale or Boost skipped Second Chance.");
            Check(!Advance(1000), "Session timer ended live gameplay.");
            Check(state.BeginSecondChance(112) && !state.BeginSecondChance(113), "Second Chance repeats.");
            Check(!state.BeginBoost(114), "Normal mode entered Boost.");
            Check(state.Finish(120) && !state.Finish(121), "Finale repeats.");
            Check(Advance(130) && state.Stage == KinoExperienceStage.Closing, "Closing missing.");
            Check(!Advance(135.9), "Closing cut short.");
            Check(Advance(136) && state.Stage == KinoExperienceStage.Complete && !Advance(999), "Complete not terminal.");

            state.Reset(200); state.SelectMode(200); state.Begin(201, true); Advance(202);
            Check(state.IncludeBoost && !state.ExternalConfirmation && !Advance(213.99), "Mode, confirmation or time leaked.");
            Check(Advance(214) && !state.ExternalConfirmation, "Timed display falsely recorded consent.");
            Advance(219); Advance(225);
            Check(state.BeginSecondChance(226) && state.BeginBoost(227) && !state.BeginBoost(228), "Selected Boost missing or repeated.");
            Check(state.Stage == KinoExperienceStage.Boost && !Advance(1000), "Timed screens ended live Boost.");
            Check(state.Finish(1001) && state.Stage == KinoExperienceStage.Finale, "Boost did not lead to Finale.");
            state.Reset(1010);
            Check(!state.IncludeBoost && !state.ExternalConfirmation, "Reset retained mode or consent.");
            // No caught number is eligible for Boost in an empty round; finishing after the greens is valid.
            state.Begin(1010, true); Advance(1011); Advance(1023); Advance(1028); Advance(1034);
            Check(state.BeginSecondChance(1035) && state.Finish(1036), "Empty eligible Boost set cannot finish normally.");
        }

        [MenuItem("Tools/KINO VR/Experience/3 - Test and capture sessions")]
        public static void Run() { SessionState.SetBool(Key + "Endings", false); RunInternal(false); }
        public static void RunBatch() { SessionState.SetBool(Key + "Endings", false); RunInternal(true); }
        [MenuItem("Tools/KINO VR/Experience/4 - Check ending transparency and hands")]
        public static void RunEndingChecks()
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play before checking endings.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                Check(!EditorSceneManager.GetSceneAt(i).isDirty, "Save open scenes before checking endings.");
            KinoExperienceSetup.Apply();
            SessionState.SetBool(Key + "Endings", true);
            RunInternal(false);
        }
        public static void RunDesktopInOpenEditor()
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play before running desktop experience tests.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                Check(!EditorSceneManager.GetSceneAt(i).isDirty, "Save open scenes before running desktop experience tests.");
            RunInternal(false);
        }

        static void RunInternal(bool exit)
        {
            KinoExperienceSetup.Validate();
            EditorSceneManager.OpenScene("Assets/KinoRotunda/Scenes/KinoRotunda.unity");
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + "Exit", exit); SessionState.SetBool(Key + "Passed", false);
            DisableEditorXR();
            EditorApplication.isPlaying = true;
        }

        static void DisableEditorXR()
        {
            var activeGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            SessionState.SetInt(Key + "XRActiveGroup", (int)activeGroup);
            foreach (var group in new[] { BuildTargetGroup.Standalone, activeGroup }.Distinct())
            {
                var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
                if (!xr) continue;
                SessionState.SetBool(Key + "XRInit" + (int)group, xr.InitManagerOnStart);
                xr.InitManagerOnStart = false;
            }
            SessionState.SetBool(Key + "XRRestore", true);
        }

        static void RestoreEditorXR()
        {
            if (!SessionState.GetBool(Key + "XRRestore", false)) return;
            var activeGroup = (BuildTargetGroup)SessionState.GetInt(Key + "XRActiveGroup", (int)BuildTargetGroup.Standalone);
            foreach (var group in new[] { BuildTargetGroup.Standalone, activeGroup }.Distinct())
            {
                var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
                if (xr) xr.InitManagerOnStart = SessionState.GetBool(Key + "XRInit" + (int)group, true);
            }
            SessionState.SetBool(Key + "XRRestore", false);
        }

        static void PrepareRun(int index)
        {
            run = index; inputSent = handMoved = false; currentSession = null;
            completionPending = completionVerified = false; closingClock = closingRealtime = menuReturnedAt = -1;
            visited.Clear(); phases.Clear(); captured.Clear(); previous = KinoExperienceStage.Waiting;
            inputDeadline = EditorApplication.timeSinceStartup + 10;
        }

        static void Initialize(KinoExperienceController flow)
        {
            started = true; remountPending = false; results = closed = 0; completedSession = null;
            Directory.CreateDirectory(KinoExperienceSetup.Output);
            flow.writeLocalRecords = false;
            flow.startupSeconds = 1; flow.safetySeconds = 2;
            flow.brandingSeconds = 1.6f; flow.logoBlackSeconds = .7f; flow.logoFadeSeconds = .35f;
            flow.introductionSeconds = 2; flow.finaleSeconds = 2;
            Check(Mathf.Abs(flow.closingSeconds - 5) < .001f, "Closing instruction must retain its five real-second duration.");
            flow.round.showcaseBoostDuration = 4; flow.round.showcaseBoostInterval = .5f;
            flow.onResultReady.AddListener(json =>
            {
                results++;
                var record = JsonUtility.FromJson<KinoSessionRecord>(json);
                Check(record.sessionId == currentSession && record.includeBoost == (run == 1), "Wrong result session/mode.");
                Check(record.score == flow.round.State.Score && record.boostCatches == flow.round.State.BoostCatchCount,
                    "Result event did not include the final Boost score.");
            });
            flow.onSessionClosed.AddListener(() =>
            {
                closed++;
                completedBlackout = flow.BlackoutAlpha;
                completedAudio = flow.round.audioController.sessionVolume;
                completedLiveScore = flow.round.score.CurrentScore;
                completedRecord = JsonUtility.ToJson(flow.Record);
                // Complete is a one-frame handoff; observe it at its event even if an Editor tick misses that frame.
                ObserveStage(flow);
                completionPending = true;
            });
            Application.runInBackground = true;
            Time.timeScale = 6; Time.maximumDeltaTime = .1f;
            PrepareRun(0);
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline, "Session test timed out.");
                var flow = Object.FindFirstObjectByType<KinoExperienceController>();
                if (!flow) return;
                if (SessionState.GetBool(Key + "Endings", false)) { CheckEndings(flow); return; }
                if (remountPending)
                {
                    Check(EditorApplication.timeSinceStartup < inputDeadline, "Headset remount did not show mode selection.");
                    if (flow.Stage == KinoExperienceStage.Waiting) return;
                    Check(flow.Stage == KinoExperienceStage.ModeSelection && !flow.round.IsRunning && flow.round.launcher.ActiveBallCount == 0,
                        "Headset remount bypassed mode selection.");
                    CompleteTests(); return;
                }
                if (flow.Stage == KinoExperienceStage.Waiting) return;
                if (!started) Initialize(flow);
                if (completionPending) { VerifyAutomaticReturn(flow); return; }
                ObserveStage(flow);
                Check(VisibleAlpha(flow.kinoLogo, flow.content) < .001f,
                    "A duplicate KINO logo appeared after mode selection or on the final score.");
                ValidateBoardPresentation(flow);
                if (flow.Stage < KinoExperienceStage.Gameplay || flow.Stage >= KinoExperienceStage.Finale)
                    Check(!flow.round.IsRunning && flow.round.launcher.ActiveBallCount == 0, "Balls outside gameplay.");
                Check(!flow.round.restartButton.IsVisible, "Player restart visible in guided session.");
                if (flow.Stage == KinoExperienceStage.ModeSelection) { SelectMode(flow); return; }
                if (currentSession == null) VerifySelectedMode(flow);
                Check(flow.State.IncludeBoost == (run == 1) && flow.round.showcaseBoostAfterSecondChance == (run == 1), "Selected mode changed.");
                Check(!flow.normalModeButton.CanPress && !flow.boostModeButton.CanPress, "Mode controls active during a session.");
                if (run == 0) Check(flow.round.State.BoostLaunchCount == 0 && flow.Stage != KinoExperienceStage.Boost, "Normal mode entered Boost.");
                if (flow.round.IsRunning) phases.Add(flow.round.State.Phase);
                if (flow.Stage == KinoExperienceStage.Boost || flow.round.State.Phase == KinoRoundPhase.Boost)
                    Check(run == 1 && flow.round.State.SecondChanceCatchCount == 3 && flow.round.State.ResolvedSecondChanceCount == 3,
                        "Boost began before all three Second Chance balls resolved.");

                // Catch actual pooled balls; the round controls launch timing and transitions.
                foreach (var ball in Object.FindObjectsByType<KinoPooledBall>(FindObjectsSortMode.None))
                {
                    var catchable = ball.GetComponent<Catchable>();
                    if (ball.gameObject.activeInHierarchy && flow.round.IsRunning && catchable)
                        KinoMainSpecialTests.CatchAndCheck(flow.round, catchable);
                }
                float elapsed = (float)(GetClock(flow) - flow.State.EnteredAt);
                if (flow.Stage == KinoExperienceStage.Safety && elapsed > 1 && !captured.Contains("Enclosure-360"))
                {
                    ValidateEnclosureViews(flow); captured.Add("Enclosure-360");
                }
                if (flow.Stage == KinoExperienceStage.Branding) ValidateBranding(flow, elapsed);
                else
                {
                    string capture = flow.Stage.ToString();
                    if (flow.Stage == KinoExperienceStage.SecondChance) capture += "-" + flow.round.State.Phase;
                    if (elapsed > .35f && flow.Stage != KinoExperienceStage.Complete) CaptureOnce(flow, capture);
                }
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(KinoExperienceSetup.Output);
                File.WriteAllText(KinoExperienceSetup.Output + (SessionState.GetBool(Key + "Endings", false) ? "/ending-checks.txt" : "/play-test.txt"), "FAIL: " + error);
                Debug.LogException(error); End();
            }
        }

        static void ObserveStage(KinoExperienceController flow)
        {
            if (flow.Stage == previous) return;
            previous = flow.Stage; visited.Add(previous);
            Debug.Log("[Experience test] " + (run == 0 ? "Normal: " : "Boost: ") + previous);
            ValidateWorldCanvas(flow);
            if (flow.Stage == KinoExperienceStage.Closing)
            {
                closingClock = flow.State.EnteredAt;
                closingRealtime = Time.realtimeSinceStartupAsDouble;
            }
        }
        static void CheckEndings(KinoExperienceController flow)
        {
            if (flow.Stage == KinoExperienceStage.Waiting) return;
            if (!started)
            {
                started = true; captured.Clear();
                Application.runInBackground = true;
                Directory.CreateDirectory(KinoExperienceSetup.Output);
                File.WriteAllText(KinoExperienceSetup.Output + "/ending-checks.txt", "RUNNING\n");
                flow.writeLocalRecords = false;
                flow.finaleSeconds = 4;
                flow.BeginNormalSession();
                double now = GetClock(flow);
                for (int i = 0; i < 4; i++) flow.State.Advance(now, 0, 0, 0, 0, 0, 0, false);
                flow.State.BeginSecondChance(now);
                typeof(KinoExperienceController).GetMethod("FinishSessionRound", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).Invoke(flow, null);
                return;
            }
            float elapsed = (float)(GetClock(flow) - flow.State.EnteredAt);
            if (flow.Stage == KinoExperienceStage.Finale && elapsed > 1 && captured.Add("ending-score"))
            {
                Check(Mathf.Abs(flow.finaleBackgroundTransparency - 85) < .001f, "Score transparency is not 85% (15% black).");
                VerifyEndingPixels(flow, .85f);
                Capture(flow, "Ending-score-85");
                flow.finaleBackgroundTransparency = 35;
            }
            else if (flow.Stage == KinoExperienceStage.Finale && captured.Contains("ending-score") && !captured.Contains("live-setting"))
            {
                // Wait for one runtime Update to apply a changed Inspector setting.
                if (Mathf.Abs(flow.enclosure.BackgroundAlpha - .65f) > .001f) return;
                VerifyEndingPixels(flow, .35f);
                flow.finaleBackgroundTransparency = 85;
                captured.Add("live-setting");
            }
            if (flow.Stage == KinoExperienceStage.Closing && elapsed > 1 && captured.Add("ending-closing"))
            {
                Check(Mathf.Abs(flow.closingBackgroundTransparency - 45) < .001f, "Removal transparency is not 45% (55% black).");
                VerifyEndingPixels(flow, .45f);
                Capture(flow, "Ending-remove-headset-45");
            }
            if (flow.Stage == KinoExperienceStage.Closing && elapsed >= 4.65f && captured.Add("closing-held"))
            {
                Check(Mathf.Abs(flow.enclosure.BackgroundAlpha - .55f) < .001f && flow.BlackoutAlpha == 0,
                    "The removal instruction's 45% transparency was overridden by the closing fade.");
                Capture(flow, "Ending-remove-headset-45-last-moment");
            }
            if (flow.Stage != KinoExperienceStage.ModeSelection) return;
            Check(captured.Contains("ending-score") && captured.Contains("live-setting") && captured.Contains("ending-closing") && captured.Contains("closing-held"),
                "Ending checks missed a stage or the live Inspector setting update.");
            File.AppendAllText(KinoExperienceSetup.Output + "/ending-checks.txt",
                "PASS: score 85% visible (15% black), removal 45% visible (55% black), live setting update, original hand appearance above the background, fade covers hands, stable removal transparency until reset.\n");
            SessionState.SetBool(Key + "Passed", true); End();
        }
        static void VerifyEndingPixels(KinoExperienceController flow, float transmission)
        {
            var enclosure = flow.enclosure;
            Check(Mathf.Abs(enclosure.BackgroundAlpha - (1 - transmission)) < .001f && enclosure.FadeAlpha == 0, "Wrong runtime background/fade alpha.");
            var block = new MaterialPropertyBlock();
            enclosure.BackgroundRenderer.GetPropertyBlock(block);
            Check(Mathf.Abs(block.GetColor("_Color").a - (1 - transmission)) < .001f, "Wrong GPU background alpha.");
            Material handMaterial = null;
            var sourceRoot = AssetDatabase.LoadAssetAtPath<GameObject>(KinoExperienceSetup.PrefabPath);
            foreach (var catcher in flow.round.playerView.vrRig.GetComponentsInChildren<HandCatcher>(true))
            {
                var hand = catcher.GetComponentInParent<OVRHand>(true);
                if (!hand) continue;
                var renderer = hand.GetComponent<SkinnedMeshRenderer>();
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, flow.transform);
                var source = sourceRoot.transform.Find(path).GetComponent<SkinnedMeshRenderer>().sharedMaterial;
                handMaterial = renderer.sharedMaterial;
                Check(handMaterial.shader == source.shader && handMaterial.shaderKeywords.SequenceEqual(source.shaderKeywords) &&
                    handMaterial.renderQueue == 3000 && renderer.sortingOrder == 75, "Tracked hand appearance or draw order changed.");
                foreach (string colour in new[] { "_BaseColor", "_Color" })
                    if (source.HasProperty(colour)) Check(handMaterial.GetColor(colour) == source.GetColor(colour), "Hand tint changed.");
                foreach (string texture in source.GetTexturePropertyNames())
                    Check(handMaterial.GetTexture(texture) == source.GetTexture(texture), "Hand texture changed.");
            }
            Check(handMaterial, "No original hand material found.");
            var go = new GameObject("Ending render probe", typeof(Camera));
            var camera = go.GetComponent<Camera>(); camera.enabled = false;
            camera.transform.position = new Vector3(0, 1.35f, 0);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.white;
            camera.cullingMask = 1 << 31; camera.nearClipPlane = .05f; camera.farClipPlane = 20;
            int bgLayer = enclosure.BackgroundRenderer.gameObject.layer, fadeLayer = enclosure.FadeRenderer.gameObject.layer;
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            var oldTarget = RenderTexture.active;
            GameObject proxy = null;
            try
            {
                enclosure.BackgroundRenderer.gameObject.layer = enclosure.FadeRenderer.gameObject.layer = 31;
                camera.targetTexture = target;
                Color Sample()
                {
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
                    return pixels.GetPixel(32, 32);
                }
                foreach (var angle in new[] { Vector3.zero, new Vector3(0,90,0), new Vector3(0,180,0), new Vector3(0,270,0), new Vector3(-90,0,0), new Vector3(90,0,0) })
                {
                    camera.transform.eulerAngles = angle;
                    Color value = Sample();
                    Check(Mathf.Abs(value.r - transmission) < .025f && value.a > .99f,
                        "Sphere compositing mismatch at " + angle + ": " + value + ", expected " + transmission + " with opaque output alpha.");
                }
                camera.transform.rotation = Quaternion.identity;
                proxy = GameObject.CreatePrimitive(PrimitiveType.Sphere); proxy.layer = 31;
                proxy.name = "Hand material render probe";
                proxy.transform.position = camera.transform.position + Vector3.forward;
                proxy.transform.localScale = Vector3.one * .35f;
                var handRenderer = proxy.GetComponent<Renderer>(); handRenderer.sharedMaterial = handMaterial;
                handRenderer.sortingOrder = 75;
                var handColour = Sample();
                enclosure.SetBackground(0);
                var originalColour = Sample();
                enclosure.SetBackground(1 - transmission);
                Check(Mathf.Abs(handColour.r - originalColour.r) < .01f && Mathf.Abs(handColour.g - originalColour.g) < .01f &&
                    Mathf.Abs(handColour.b - originalColour.b) < .01f, "Black background changes the original hand appearance.");
                enclosure.SetSessionFade(1);
                var faded = Sample();
                Check(faded.maxColorComponent <= 1 && faded.r < .005f && faded.g < .005f && faded.b < .005f, "Full fade does not cover the hands.");
                File.AppendAllText(KinoExperienceSetup.Output + "/ending-checks.txt", transmission.ToString("P0") + " transmission: all six views + opaque output alpha + unchanged hand appearance PASS\n");
            }
            finally
            {
                enclosure.SetSessionFade(0);
                enclosure.SetBackground(1 - transmission);
                enclosure.BackgroundRenderer.gameObject.layer = bgLayer; enclosure.FadeRenderer.gameObject.layer = fadeLayer;
                RenderTexture.active = oldTarget;
                if (proxy) Object.DestroyImmediate(proxy);
                Object.DestroyImmediate(go); Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target);
            }
        }

        static void VerifyAutomaticReturn(KinoExperienceController flow)
        {
            if (!completionVerified)
            {
                Check(closingClock >= 0 && flow.State.EnteredAt - closingClock >= 5 - .001,
                    "Closing instruction was not shown for five unscaled seconds.");
                Check(Time.realtimeSinceStartupAsDouble - closingRealtime >= 4.8,
                    "Closing instruction was accelerated by gameplay timeScale.");
                VerifyCompletedSession(flow); completionVerified = true;
                completedSession = currentSession; inputDeadline = EditorApplication.timeSinceStartup + 3;
            }
            Check(EditorApplication.timeSinceStartup < inputDeadline, "Completed session did not return automatically to mode selection.");
            Check(results == run + 1 && closed == run + 1 && JsonUtility.ToJson(flow.Record) == completedRecord,
                "Automatic menu return repeated an event or changed the completed record.");
            if (flow.Stage == KinoExperienceStage.Complete) return;
            Check(flow.Stage == KinoExperienceStage.ModeSelection && !flow.round.IsRunning && flow.round.launcher.ActiveBallCount == 0,
                "Automatic return did not stop at the initial mode menu.");
            Check(flow.normalModeButton.gameObject.activeInHierarchy && flow.boostModeButton.gameObject.activeInHierarchy,
                "Automatic return is missing Normal or Boost.");
            if (menuReturnedAt < 0)
            {
                menuReturnedAt = Time.realtimeSinceStartupAsDouble;
                ValidateWorldCanvas(flow); Capture(flow, (run == 0 ? "Normal-" : "Boost-") + "Automatic-menu-return");
            }
            // Observe an idle menu after arming: it must not start another session or emit more events.
            if (Time.realtimeSinceStartupAsDouble - menuReturnedAt < .7) return;
            Check(flow.Record.sessionId == currentSession && !flow.State.IncludeBoost && !flow.round.showcaseBoostAfterSecondChance,
                "Automatic menu return retained an active mode or created another session.");
            if (run == 0) PrepareRun(1);
            else VerifyRemount(flow);
        }

        static void SelectMode(KinoExperienceController flow)
        {
            Check(EditorApplication.timeSinceStartup < inputDeadline, "Mode click/physical hand trigger did not start a session.");
            CaptureOnce(flow, "ModeSelection");
            Check(flow.normalModeButton.gameObject.activeInHierarchy && flow.boostModeButton.gameObject.activeInHierarchy,
                "Mode menu is missing a choice.");
            if (inputSent) return;
            var choice = run == 0 ? flow.normalModeButton : flow.boostModeButton;
            if (!choice.CanPress)
            {
                choice.Press();
                Check(flow.Stage == KinoExperienceStage.ModeSelection, "Mode button bypassed its arming delay.");
                return;
            }
            if (run == 0) { choice.button.onClick.Invoke(); inputSent = true; return; }
            if (!hand)
            {
                hand = new GameObject("Experience mode physical hand");
                hand.transform.position = choice.pressArea.bounds.center + Vector3.up;
                hand.AddComponent<HandCatcher>();
                var collider = hand.AddComponent<SphereCollider>(); collider.isTrigger = true; collider.radius = .04f;
                var body = hand.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Physics.SyncTransforms(); handCreatedAt = Time.fixedTime; return;
            }
            if (!handMoved && Time.fixedTime > handCreatedAt)
            {
                hand.transform.position = choice.pressArea.bounds.center; Physics.SyncTransforms();
                handMoved = inputSent = true;
            }
        }

        static void VerifySelectedMode(KinoExperienceController flow)
        {
            Check(inputSent && flow.Stage == KinoExperienceStage.Startup && flow.Record != null, "Session bypassed mode selection.");
            currentSession = flow.Record.sessionId;
            Check(currentSession != completedSession && flow.Record.includeBoost == (run == 1) && flow.Record.score == 0 && flow.Record.boostCatches == 0,
                "New session retained a result or selected the wrong mode.");
            double enteredAt = flow.State.EnteredAt;
            flow.normalModeButton.Press(); flow.boostModeButton.Press();
            flow.normalModeButton.button.onClick.Invoke(); flow.boostModeButton.button.onClick.Invoke();
            Check(flow.Record.sessionId == currentSession && flow.State.EnteredAt == enteredAt && flow.Record.includeBoost == (run == 1),
                "A duplicate mode press restarted or changed the active session.");
            if (hand) { Object.Destroy(hand); hand = null; }
        }

        static void ValidateWorldCanvas(KinoExperienceController flow)
        {
            Check(flow.contentCanvas.renderMode == RenderMode.WorldSpace,
                "Experience uses a non-VR canvas.");
            var view = flow.round.playerView.View;
            if (flow.Stage == KinoExperienceStage.Finale) ValidateBoardOverlay(flow.round, flow.contentCanvas);
            else
            {
                var heading = Quaternion.LookRotation(Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized);
                var offset = Quaternion.Inverse(heading) * (flow.contentCanvas.transform.position - view.position);
                Check(Vector3.Distance(offset, new Vector3(0, 0, 2.5f)) < .025f, "Experience instruction is not centered 2.5m in front of the player.");
                var rect = ((RectTransform)flow.contentCanvas.transform).rect;
                var scale = flow.contentCanvas.transform.lossyScale;
                Check(Mathf.Abs(rect.width * scale.x - 2.2f) < .025f && Mathf.Abs(rect.height * scale.y - 1.4f) < .025f,
                    "Experience instruction canvas did not restore its physical size after the board score.");
            }
            Check(flow.enclosure && flow.enclosure.BackgroundRenderer && flow.enclosure.FadeRenderer,
                "Experience is missing its 360-degree black enclosure.");
            Check(!flow.enclosure.BackgroundRenderer.transform.IsChildOf(view) && !flow.enclosure.FadeRenderer.transform.IsChildOf(view),
                "Black enclosure follows the player's head instead of the room.");
        }

        static void ValidateBoardPresentation(KinoExperienceController flow)
        {
            var board = flow.round.board;
            bool covered = flow.Stage == KinoExperienceStage.Finale ||
                flow.round.IsRunning && flow.round.State.Phase == KinoRoundPhase.SecondChanceReveal;
            Check(board.numberGrid && board.NumbersVisible != covered &&
                Mathf.Abs(board.numberGrid.alpha - (covered ? 0 : 1)) < .001f,
                "Board numbers are not hidden only for the reveal/finale or failed to return for gameplay/menu.");
            if (!covered) return;
            Check(board.caughtMarkers.Count(marker => marker && marker.activeSelf) == flow.round.State.UniqueCount,
                "Covering the number field erased caught-number state.");
            var canvas = flow.Stage == KinoExperienceStage.Finale ? flow.contentCanvas :
                flow.round.secondChancePresentation.announcementCanvas;
            ValidateBoardOverlay(flow.round, canvas);
        }

        public static void ValidateBoardOverlay(KinoRoundController round, Canvas canvas)
        {
            var board = round.board;
            var field = board.numberField;
            Check(field && field.name == "Live number field", "Missing board number-field anchor.");
            Check(canvas && canvas.renderMode == RenderMode.WorldSpace,
                "Board presentation is not a world-space canvas.");
            Check(Quaternion.Angle(canvas.transform.rotation, field.rotation) < .01f,
                "Board presentation does not follow the board plane.");
            var corners = new Vector3[4];
            ((RectTransform)canvas.transform).GetWorldCorners(corners);
            var projected = corners.Select(field.InverseTransformPoint).ToArray();
            Check(Mathf.Abs(projected.Min(corner => corner.x) - field.rect.xMin) < .05f &&
                Mathf.Abs(projected.Max(corner => corner.x) - field.rect.xMax) < .05f &&
                Mathf.Abs(projected.Min(corner => corner.y) - field.rect.yMin) < .05f &&
                Mathf.Abs(projected.Max(corner => corner.y) - field.rect.yMax) < .05f,
                "Presentation does not cover exactly the number field below the existing KINO header.");
            Check(projected.All(corner => corner.z < 0 && corner.z >= -3),
                "Presentation moved away from the board surface.");
            Check(!board.NumbersVisible && board.numberGrid && board.numberGrid.alpha < .001f,
                "Live numbers remain visible behind the board presentation.");
            Check(board.numberLabels.All(label => label && label.transform.IsChildOf(board.numberGrid.transform)) &&
                board.caughtMarkers.All(marker => marker && marker.transform.IsChildOf(board.numberGrid.transform)) &&
                board.multiplierLabels.All(label => label && label.transform.IsChildOf(board.numberGrid.transform)),
                "A live number, caught marker or multiplier escaped the hidden grid.");
            Check(round.secondChancePresentation.normalBrand && round.secondChancePresentation.normalBrand.activeInHierarchy,
                "The board's existing KINO header was hidden by its presentation.");
        }

        static float VisibleAlpha(RawImage logo, CanvasGroup group) =>
            logo.gameObject.activeInHierarchy ? logo.color.a * group.alpha : 0;

        static void ValidateEnclosureViews(KinoExperienceController flow)
        {
            Check(flow.enclosure.BackgroundAlpha > .99f && flow.BlackoutAlpha < .01f,
                "Safety instruction is not visible inside an opaque enclosure.");
            ValidateEnclosureMesh(flow);
            var camera = flow.round.playerView.desktopCamera;
            Check(camera, "A rendering camera is required to verify the enclosure.");
            var originalPosition = camera.transform.position;
            var originalRotation = camera.transform.rotation;
            var heading = Quaternion.LookRotation(Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized);
            try
            {
                // A visible forward instruction prevents blank/misconfigured rendering from passing black-view tests.
                var forward = RenderProbe(camera, (run == 0 ? "Normal-" : "Boost-") + "Enclosure-Forward");
                try
                {
                    Check(forward.GetPixels32().Count(pixel => pixel.r > 32 || pixel.g > 32 || pixel.b > 32) > 100,
                        "Instruction disappeared when the enclosure became opaque.");
                }
                finally { Object.DestroyImmediate(forward); }

                var directions = new[] { new Vector3(0, 90, 0), new Vector3(0, 180, 0), new Vector3(0, 270, 0),
                    new Vector3(-90, 0, 0), new Vector3(90, 0, 0) };
                var names = new[] { "Yaw90", "Yaw180", "Yaw270", "Up", "Down" };
                for (int i = 0; i < directions.Length; i++)
                {
                    camera.transform.rotation = heading * Quaternion.Euler(directions[i]);
                    var frame = RenderProbe(camera, (run == 0 ? "Normal-" : "Boost-") + "Enclosure-" + names[i]);
                    try
                    {
                        var pixels = frame.GetPixels32();
                        int bright = 0; long intensity = 0;
                        foreach (var pixel in pixels)
                        {
                            int maximum = Math.Max(pixel.r, Math.Max(pixel.g, pixel.b));
                            intensity += maximum;
                            if (maximum > 12) bright++;
                        }
                        Check((double)intensity / pixels.Length < 2 && bright <= pixels.Length / 1000,
                            "Room leaked through the opaque enclosure at " + names[i] + ": " + bright + " bright pixels.");
                    }
                    finally { Object.DestroyImmediate(frame); }
                }
            }
            finally { camera.transform.SetPositionAndRotation(originalPosition, originalRotation); }
        }

        static void ValidateEnclosureMesh(KinoExperienceController flow)
        {
            var enclosure = flow.enclosure;
            Check(Mathf.Abs(enclosure.BackgroundRenderer.transform.position.x) < .001f && Mathf.Abs(enclosure.BackgroundRenderer.transform.position.z) < .001f,
                "Enclosure is not anchored at world X/Z zero.");
            foreach (var renderer in new[] { enclosure.BackgroundRenderer, enclosure.FadeRenderer })
            {
                var filter = renderer.GetComponent<MeshFilter>();
                Check(filter && filter.sharedMesh, "Enclosure renderer has no shell mesh.");
                var mesh = filter.sharedMesh;
                var vertices = mesh.vertices.Select(vertex => filter.transform.TransformPoint(vertex)).ToArray();
                Check(vertices.Length > 20 && vertices.Min(vertex => vertex.y) >= -.001f,
                    "Enclosure extends beneath its floor or has no curved shell.");
                Check(Mathf.Abs(vertices.Min(vertex => vertex.y)) <= .005f && Mathf.Abs(vertices.Max(vertex => vertex.y) - 5) < .025f,
                    "Enclosure floor/height differs from world Y zero / five metres.");
                Check(Mathf.Abs(vertices.Min(vertex => vertex.x) + 6) < .025f && Mathf.Abs(vertices.Max(vertex => vertex.x) - 6) < .025f &&
                    Mathf.Abs(vertices.Min(vertex => vertex.z) + 6) < .025f && Mathf.Abs(vertices.Max(vertex => vertex.z) - 6) < .025f,
                    "Enclosure does not span six metres around world X/Z zero.");
                var indices = mesh.triangles;
                bool hasFloor = false;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var a = vertices[indices[i]]; var b = vertices[indices[i + 1]]; var c = vertices[indices[i + 2]];
                    if (Mathf.Abs(a.y) <= .005f && Mathf.Abs(b.y) <= .005f && Mathf.Abs(c.y) <= .005f &&
                        Vector3.Cross(b - a, c - a).sqrMagnitude > .001f) { hasFloor = true; break; }
                }
                Check(hasFloor, "Enclosure has no opaque floor cap for looking straight down.");
            }
        }

        static Texture2D RenderProbe(Camera camera, string name)
        {
            const int width = 800, height = 500;
            var target = new RenderTexture(width, height, 24);
            var old = camera.targetTexture; var active = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                Canvas.ForceUpdateCanvases(); camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes(KinoExperienceSetup.Output + "/" + name + ".png", image.EncodeToPNG());
                return image;
            }
            catch { Object.DestroyImmediate(image); throw; }
            finally { camera.targetTexture = old; RenderTexture.active = active; target.Release(); Object.DestroyImmediate(target); }
        }

        static void ValidateBranding(KinoExperienceController flow, float elapsed)
        {
            Check(Mathf.Abs(flow.BrandingDuration - flow.brandingSeconds) < .001f,
                "Branding retained the removed KINO logo or inter-logo pause.");
            float allwyn = VisibleAlpha(flow.allwynLogo, flow.content), kino = VisibleAlpha(flow.kinoLogo, flow.content);
            Check(kino < .001f, "The removed KINO logo is visible during branding.");
            Check(flow.enclosure.BackgroundAlpha > .99f && flow.content.alpha > .99f,
                "Branding does not have an opaque black enclosure.");
            Check(flow.title.text == "" && flow.body.text == "" && flow.footer.text == "", "Branding has extra screen text.");
            if (elapsed > flow.logoFadeSeconds && elapsed < flow.brandingSeconds - flow.logoFadeSeconds)
            {
                Check(allwyn > .99f && kino < .001f && flow.allwynLogo.rectTransform.anchoredPosition.sqrMagnitude < .01f,
                    "Allwyn is not centered during branding.");
                CaptureOnce(flow, "Branding-Allwyn");
            }
        }

        static void VerifyCompletedSession(KinoExperienceController flow)
        {
            var expected = new List<KinoExperienceStage> { KinoExperienceStage.ModeSelection, KinoExperienceStage.Startup,
                KinoExperienceStage.Safety, KinoExperienceStage.Branding, KinoExperienceStage.Introduction,
                KinoExperienceStage.Gameplay, KinoExperienceStage.SecondChance };
            if (run == 1) expected.Add(KinoExperienceStage.Boost);
            expected.AddRange(new[] { KinoExperienceStage.Finale, KinoExperienceStage.Closing, KinoExperienceStage.Complete });
            Check(visited.SequenceEqual(expected), "Session order changed: " + string.Join(",", visited));
            foreach (var phase in new[] { KinoRoundPhase.Main, KinoRoundPhase.Bonus, KinoRoundPhase.BoardHold, KinoRoundPhase.FadeOut,
                KinoRoundPhase.SecondChanceReveal, KinoRoundPhase.SecondChance }) Check(phases.Contains(phase), "Round phase missing: " + phase);
            Check(captured.Contains("Branding-Allwyn"), "Allwyn branding was not observable.");
            Check(captured.Contains("Enclosure-360"), "360-degree opaque instruction enclosure was not verified.");
            Check(results == run + 1 && closed == run + 1, "Result or close event repeated/missing.");
            Check(flow.Record.externalConfirmationUtc == null && flow.Record.safetyElapsedUtc != null, "Timed safety mislabeled.");
            Check(flow.Record.normalCatches == 20 && flow.Record.secondChanceCatches == 3 && flow.round.State.BonusCaughtNumber != 0,
                "Normal, red bonus or Second Chance catch quota mismatch.");
            Check(flow.Record.boostCatches == flow.round.State.BoostLaunchCount && (run == 0 ? flow.Record.boostCatches == 0 : flow.Record.boostCatches > 0),
                "Boost launches, catches or selected mode mismatch.");
            Check(flow.round.State.GlowCatchCount == flow.round.State.GlowTargetCount &&
                flow.round.State.MysteryCatchCount == flow.round.State.MysteryTargetCount, "Special catch quota mismatch.");
            int expectedScore = flow.round.State.MainScore + KinoRoundState.BonusMultiplier * (1 + 3 + flow.Record.boostCatches);
            Check(flow.Record.score == expectedScore && completedLiveScore == expectedScore, "Final score omits or duplicates a phase.");
            Check(completedBlackout == 1 && completedAudio == 0, "Exit is not black and silent.");
            Check(!flow.kinoLogo.gameObject.activeInHierarchy,
                "The removed KINO presentation logo was reactivated by session completion.");
        }

        static void VerifyRemount(KinoExperienceController flow)
        {
            Check(flow.Stage == KinoExperienceStage.ModeSelection, "Completed Boost session cannot return to menu.");
            flow.BeginSession();
            Check(flow.Stage == KinoExperienceStage.Startup && flow.Record.sessionId != currentSession && flow.Record.score == 0 &&
                !flow.State.IncludeBoost && !flow.Record.includeBoost, "Operator session did not reset to Normal.");
            flow.SendMessage("Unmounted");
            Check(flow.Stage == KinoExperienceStage.Waiting && flow.Record.abortedUtc != null && flow.BlackoutAlpha == 1 && !flow.round.IsRunning,
                "Headset removal failed.");
            flow.SendMessage("Mounted");
            remountPending = true; inputDeadline = EditorApplication.timeSinceStartup + 3;
        }

        static void CompleteTests()
        {
            File.WriteAllText(KinoExperienceSetup.Output + "/play-test.txt",
                "PASS: Normal click and Boost physical hand selection; debounce and duplicate-selection guards; no early balls; " +
                "both complete session orders; centered Allwyn branding on black with no repeated KINO logo; world-space instructions at 2.5m; " +
                "reveal/finale fitted to board number field, existing header retained, caught state preserved and numbers restored; " +
                "20 numbered catches including 2-6 glow + 2-6 extra Mystery + red bonus + 3 green catches per run; " +
                "live special appearance, material and halo reset; contact popup value/location and duplicate guards; " +
                "Normal skips Boost; selected Boost follows all greens and adds +3 per catch; " +
                "one result/close event per session; result mode and Boost score; safety record semantics; text fit; " +
                "floor-bound 360-degree enclosure at world origin; opaque instruction pixels verified at yaw 90/180/270 and straight up/down; " +
                "black/silent completion; five real-second closing instruction; automatic menu return with unchanged records/events; " +
                "fresh operator session, abort and remount to selection.\n");
            SessionState.SetBool(Key + "Passed", true); End();
        }

        static double GetClock(KinoExperienceController flow) => (double)typeof(KinoExperienceController)
            .GetField("clock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(flow);

        static void CaptureOnce(KinoExperienceController flow, string name)
        {
            if (!captured.Add(name)) return;
            Capture(flow, (run == 0 ? "Normal-" : "Boost-") + name);
            foreach (var label in new[] { flow.title, flow.body, flow.footer })
            { label.ForceMeshUpdate(); Check(!label.isTextOverflowing, "Text overflow: " + name + " / " + label.name); }
        }

        static void Capture(KinoExperienceController flow, string name)
        {
            var camera = flow.round.playerView.desktopCamera;
            if (!camera) return;
            var target = new RenderTexture(1600, 1000, 24);
            var old = camera.targetTexture; var active = RenderTexture.active;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            try
            {
                Canvas.ForceUpdateCanvases(); camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
                File.WriteAllBytes(KinoExperienceSetup.Output + "/" + name + ".png", image.EncodeToPNG());
            }
            finally { camera.targetTexture = old; RenderTexture.active = active; Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target); }
        }

        static void End()
        {
            if (hand) { Object.Destroy(hand); hand = null; }
            Time.timeScale = 1; Time.maximumDeltaTime = .3333333f;
            SessionState.SetBool(Key, false); EditorApplication.isPlaying = false;
            SessionState.SetBool(Key + "Endings", false);
        }
    }
}

