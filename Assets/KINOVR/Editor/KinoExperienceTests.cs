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
        static double deadline, inputDeadline;
        static bool started, inputSent, handMoved, remountPending;
        static int run, results, closed;
        static string currentSession, completedSession;
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
                if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + "Exit", false))
                {
                    SessionState.SetBool(Key + "Exit", false);
                    var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
                    if (xr) xr.InitManagerOnStart = SessionState.GetBool(Key + "XRInit", true);
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
        public static void Run() => RunInternal(false);
        public static void RunBatch() => RunInternal(true);
        static void RunInternal(bool exit)
        {
            KinoExperienceSetup.Validate();
            EditorSceneManager.OpenScene("Assets/KinoRotunda/Scenes/KinoRotunda.unity");
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + "Exit", exit); SessionState.SetBool(Key + "Passed", false);
            if (Application.isBatchMode)
            {
                var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
                if (xr) { SessionState.SetBool(Key + "XRInit", xr.InitManagerOnStart); xr.InitManagerOnStart = false; }
            }
            EditorApplication.isPlaying = true;
        }

        static void PrepareRun(int index)
        {
            run = index; inputSent = handMoved = false; currentSession = null;
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
            flow.introductionSeconds = 2; flow.finaleSeconds = 2; flow.closingSeconds = 2.5f;
            flow.round.showcaseBoostDuration = 4; flow.round.showcaseBoostInterval = .5f;
            flow.onResultReady.AddListener(json =>
            {
                results++;
                var record = JsonUtility.FromJson<KinoSessionRecord>(json);
                Check(record.sessionId == currentSession && record.includeBoost == (run == 1), "Wrong result session/mode.");
                Check(record.score == flow.round.State.Score && record.boostCatches == flow.round.State.BoostCatchCount,
                    "Result event did not include the final Boost score.");
            });
            flow.onSessionClosed.AddListener(() => closed++);
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
                if (flow.Stage != previous)
                {
                    previous = flow.Stage; visited.Add(previous);
                    Debug.Log("[Experience test] " + (run == 0 ? "Normal: " : "Boost: ") + previous);
                    ValidateWorldCanvas(flow);
                }
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
                    if (ball.gameObject.activeInHierarchy && flow.round.IsRunning && catchable) catchable.Catch();
                }
                float elapsed = (float)(GetClock(flow) - flow.State.EnteredAt);
                if (flow.Stage == KinoExperienceStage.Branding) ValidateBranding(flow, elapsed);
                else
                {
                    string capture = flow.Stage.ToString();
                    if (flow.Stage == KinoExperienceStage.SecondChance) capture += "-" + flow.round.State.Phase;
                    if (elapsed > .35f && flow.Stage != KinoExperienceStage.Complete) CaptureOnce(flow, capture);
                }
                if (flow.Stage != KinoExperienceStage.Complete) return;
                VerifyCompletedSession(flow);
                if (run == 0)
                {
                    completedSession = currentSession;
                    PrepareRun(1); flow.ShowModeSelection();
                    Check(flow.Stage == KinoExperienceStage.ModeSelection && !flow.round.IsRunning, "Cannot return to the mode menu.");
                    return;
                }
                VerifyRemount(flow);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(KinoExperienceSetup.Output);
                File.WriteAllText(KinoExperienceSetup.Output + "/play-test.txt", "FAIL: " + error);
                Debug.LogException(error); End();
            }
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
            Check(flow.contentCanvas.renderMode == RenderMode.WorldSpace && flow.blackoutCanvas.renderMode == RenderMode.WorldSpace,
                "Experience uses a non-VR canvas.");
            var view = flow.round.playerView.View;
            var heading = Quaternion.LookRotation(Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized);
            var offset = Quaternion.Inverse(heading) * (flow.contentCanvas.transform.position - view.position);
            Check(Vector3.Distance(offset, new Vector3(0, 0, 2.5f)) < .025f, "Experience screen is not centered 2.5m in front of the player.");
            var rect = ((RectTransform)flow.contentCanvas.transform).rect;
            var scale = flow.contentCanvas.transform.lossyScale;
            Check(Mathf.Abs(rect.width * scale.x - 2.2f) < .025f && Mathf.Abs(rect.height * scale.y - 1.4f) < .025f,
                "Experience canvas changed its physical size.");
        }

        static float VisibleAlpha(RawImage logo, CanvasGroup group) =>
            logo.gameObject.activeInHierarchy ? logo.color.a * group.alpha : 0;

        static void ValidateBranding(KinoExperienceController flow, float elapsed)
        {
            Check(Mathf.Abs(flow.BrandingDuration - (2 * flow.brandingSeconds + flow.logoBlackSeconds)) < .001f,
                "Branding duration does not include both logos and the black pause.");
            float allwyn = VisibleAlpha(flow.allwynLogo, flow.content), kino = VisibleAlpha(flow.kinoLogo, flow.content);
            Check(allwyn < .001f || kino < .001f, "Allwyn and KINO are visible together.");
            Check(flow.backdrop.gameObject.activeInHierarchy && flow.backdrop.color == Color.black && flow.content.alpha > .99f,
                "Branding does not have an opaque black backdrop.");
            Check(flow.title.text == "" && flow.body.text == "" && flow.footer.text == "", "Branding has extra screen text.");
            if (elapsed > flow.logoFadeSeconds && elapsed < flow.brandingSeconds - flow.logoFadeSeconds)
            {
                Check(allwyn > .99f && kino < .001f && flow.allwynLogo.rectTransform.anchoredPosition.sqrMagnitude < .01f,
                    "Allwyn is not the first centered logo.");
                CaptureOnce(flow, "Branding-Allwyn");
            }
            else if (elapsed > flow.brandingSeconds + .05f && elapsed < flow.brandingSeconds + flow.logoBlackSeconds - .05f)
            {
                Check(allwyn < .001f && kino < .001f, "Logos did not fade fully to black between brands.");
                CaptureOnce(flow, "Branding-Black");
            }
            else if (elapsed > flow.brandingSeconds + flow.logoBlackSeconds + flow.logoFadeSeconds &&
                elapsed < flow.BrandingDuration - flow.logoFadeSeconds)
            {
                Check(kino > .99f && allwyn < .001f && flow.kinoLogo.rectTransform.anchoredPosition.sqrMagnitude < .01f,
                    "KINO is not the second centered logo.");
                CaptureOnce(flow, "Branding-KINO");
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
            foreach (var frame in new[] { "Branding-Allwyn", "Branding-Black", "Branding-KINO" })
                Check(captured.Contains(frame), "Brand frame was not observable: " + frame);
            Check(results == run + 1 && closed == run + 1, "Result or close event repeated/missing.");
            Check(flow.Record.externalConfirmationUtc == null && flow.Record.safetyElapsedUtc != null, "Timed safety mislabeled.");
            Check(flow.Record.normalCatches == 20 && flow.Record.secondChanceCatches == 3 && flow.round.State.BonusCaughtNumber != 0,
                "Normal, red bonus or Second Chance catch quota mismatch.");
            Check(flow.Record.boostCatches == flow.round.State.BoostLaunchCount && (run == 0 ? flow.Record.boostCatches == 0 : flow.Record.boostCatches > 0),
                "Boost launches, catches or selected mode mismatch.");
            int expectedScore = 20 + KinoRoundState.BonusMultiplier * (1 + 3 + flow.Record.boostCatches);
            Check(flow.Record.score == expectedScore && flow.round.score.CurrentScore == expectedScore, "Final score omits or duplicates a phase.");
            Check(flow.blackout.color.a == 1 && flow.round.audioController.sessionVolume == 0, "Exit is not black and silent.");
            Check(Mathf.Abs(flow.kinoLogo.rectTransform.rect.width / flow.kinoLogo.rectTransform.rect.height -
                (float)flow.kinoLogo.texture.width / flow.kinoLogo.texture.height) < .01f, "KINO logo aspect changed.");
        }

        static void VerifyRemount(KinoExperienceController flow)
        {
            flow.ShowModeSelection();
            Check(flow.Stage == KinoExperienceStage.ModeSelection, "Completed Boost session cannot return to menu.");
            flow.BeginSession();
            Check(flow.Stage == KinoExperienceStage.Startup && flow.Record.sessionId != currentSession && flow.Record.score == 0 &&
                !flow.State.IncludeBoost && !flow.Record.includeBoost, "Operator session did not reset to Normal.");
            flow.SendMessage("Unmounted");
            Check(flow.Stage == KinoExperienceStage.Waiting && flow.Record.abortedUtc != null && flow.blackout.color.a == 1 && !flow.round.IsRunning,
                "Headset removal failed.");
            flow.SendMessage("Mounted");
            remountPending = true; inputDeadline = EditorApplication.timeSinceStartup + 3;
        }

        static void CompleteTests()
        {
            File.WriteAllText(KinoExperienceSetup.Output + "/play-test.txt",
                "PASS: Normal click and Boost physical hand selection; debounce and duplicate-selection guards; no early balls; " +
                "both complete session orders; Allwyn -> black -> KINO with exclusive centered logos on black; world-space screens at 2.5m; " +
                "20 normal catches + red bonus + 3 green catches per run; Normal skips Boost; selected Boost follows all greens and adds +3 per catch; " +
                "one result/close event per session; result mode and Boost score; safety record semantics; text fit; " +
                "black/silent completion; menu return, fresh operator session, abort and remount to selection.\n");
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
        }
    }
}

