using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    [InitializeOnLoad]
    public static class KinoSecondChanceTests
    {
        const string Key = "KinoSecondChance.PlayTest";
        const string Folder = KinoGameplaySetup.SecondChanceOutput;
        static readonly Dictionary<KinoPooledBall, uint> leases = new Dictionary<KinoPooledBall, uint>();
        static readonly List<string> events = new List<string>();
        static int run, finished, normalSeen, greenSeen, boostSeen, bonusChanges, lastBonusNumber;
        static double timeout, phaseAt, startedAt, bonusChangedAt;
        static KinoRoundPhase previous;
        static KinoPooledBall bonus;
        static GameObject hand;
        static bool started, physicalCatchPending;
        static bool restartPending, restartInputSent;
        static double restartReadyAt, restartDeadline;
        static KinoSecondChanceTests()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                {
                    started = false; finished = 0;
                    timeout = EditorApplication.timeSinceStartup + 240;
                    Application.runInBackground = true;
                    Time.timeScale = 4;
                    Time.maximumDeltaTime = .05f;
                }
                if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".Exit", false))
                {
                    SessionState.SetBool(Key + ".Exit", false);
                    EditorApplication.Exit(SessionState.GetBool(Key + ".Passed", false) ? 0 : 1);
                }
            };
        }
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static KinoRoundState ResolvedDraw(bool catches)
        {
            var state = new KinoRoundState(); state.Begin(60, 0);
            for (int i = 0; i < 20; i++)
            {
                Check(!state.TryRegisterNormalLaunch(i * 3 - .001), "Early normal launch.");
                Check(state.TryRegisterNormalLaunch(i * 3), "Normal slot rejected.");
                if (catches && i < 3) Check(state.TryCatch(i == 1 ? 12 : 7, i * 3), "Normal catch rejected.");
                else if (i < 19) Check(state.TryMiss(false), "Normal miss rejected.");
            }
            Check(!state.TryRegisterNormalLaunch(60), "21st normal launch accepted.");
            state.Tick(60);
            Check(!state.TryBeginBonus(), "Bonus overlaps final ordinary flight.");
            Check(state.TryMiss(false), "Last normal miss rejected.");
            return state;
        }
        static double Reveal(KinoRoundState state, double start)
        {
            Check(state.TryBeginSecondChanceTransition(start), "Second Chance transition rejected.");
            state.Tick(start + 2.99);
            Check(state.Phase == KinoRoundPhase.BoardHold && !state.TryRegisterSecondChanceLaunch(start + 2.99), "Board did not stay visible for 3 seconds.");
            state.Tick(start + 3);
            Check(state.Phase == KinoRoundPhase.FadeOut, "Missing fade-out.");
            state.Tick(start + 4.49);
            Check(state.Phase == KinoRoundPhase.FadeOut, "Fade shorter than 1.5 seconds.");
            state.Tick(start + 4.5);
            Check(state.Phase == KinoRoundPhase.SecondChanceReveal && !state.TryRegisterSecondChanceLaunch(start + 4.5), "Launch behind title.");
            state.Tick(start + 7.99);
            Check(state.Phase == KinoRoundPhase.SecondChanceReveal && !state.TryRegisterSecondChanceLaunch(start + 7.99), "Title disappeared before the longer fade and reading hold ended.");
            state.Tick(start + 8);
            Check(state.Phase == KinoRoundPhase.SecondChance, "Missing green phase.");
            return start + 8;
        }
        public static void ValidateRules()
        {
            var state = ResolvedDraw(true);
            Check(state.Score == 3 && state.UniqueCount == 2, "Normal scoring/repeat memory.");
            Check(state.TryBeginBonus() && !state.TryCatch(7, 60, true), "Red bonus launch guard.");
            Check(state.TryRegisterBonusLaunch() && !state.TryRegisterBonusLaunch(), "More than one red bonus.");
            Check(!state.TryCatch(80, 60, true), "Uncaught red bonus number.");
            Check(state.TryCatch(7, 60, true) && !state.TryCatch(7, 60, true) && state.Score == 6, "Bonus must award +3 once.");
            double greenAt = Reveal(state, 60);
            for (int i = 0; i < 3; i++)
            {
                Check(!state.TryRegisterSecondChanceLaunch(greenAt + i * 3 - .001), "Early green launch.");
                Check(state.TryRegisterSecondChanceLaunch(greenAt + i * 3), "Missing green launch.");
                Check(!state.TryBeginShowcaseBoost(greenAt + i * 3, 25, 1), "Boost overlapped green flight.");
                Check(state.TryCatch(i == 1 ? 80 : 7, greenAt + i * 3, false, true), "Green catch failed.");
            }
            Check(state.NormalLaunchCount + state.SecondChanceLaunchCount == 23 && state.Score == 15 && state.UniqueCount == 3, "20+3 quota, green +3 or memory failure.");
            Check(!state.TryRegisterSecondChanceLaunch(100), "Fourth green launch.");
            Check(state.TryBeginShowcaseBoost(74, 25, 1), "Showcase did not start.");
            for (int i = 0; i < 2; i++)
            {
                Check(state.TryRegisterBoostLaunch(74 + i), "Boost launch missing.");
                Check(!state.TryCatch(79, 74 + i, false, false, true), "Boost introduced a new number.");
                Check(state.TryCatch(7, 74 + i, false, false, true), "Repeated Boost catch failed.");
            }
            Check(state.Score == 21 && state.BoostCatchCount == 2 && state.UniqueCount == 3, "Repeated Boost catch did not award +3 each time.");
            state.Tick(99);
            Check(state.Phase == KinoRoundPhase.BoostSettling && !state.TryRegisterBoostLaunch(99), "Boost deadline failed.");
            state.Stop(); state.Begin(60, 100);
            Check(state.Score == 0 && state.UniqueCount == 0 && state.SecondChanceLaunchCount == 0 && state.BoostLaunchCount == 0 && !state.BonusLaunched, "Restart retained phase data.");
            state = ResolvedDraw(false);
            Check(!state.TryBeginBonus() && state.IsRunning, "Empty main draw must still get Second Chance.");
            greenAt = Reveal(state, 60);
            for (int i = 0; i < 3; i++) { state.TryRegisterSecondChanceLaunch(greenAt + i * 3); state.TryMiss(false, true); }
            Check(state.ResolvedSecondChanceCount == 3 && !state.TryBeginShowcaseBoost(74, 25, 1), "Empty eligible set started Boost.");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/rules.txt", "PASS: normal 20 slots; last-flight grace; one red bonus before Second Chance; caught-only candidates; 3s board hold; 1.5s fade each way and 2s fully visible reading hold; reveal blocks launches; exactly 3 greens at 3s spacing; green +3; total 23 draw balls; optional timed Boost repeats caught numbers and awards +3 each catch; empty sets; deadlines; restart.\n");
        }
        public static void ValidateRulesAndAssets()
        {
            ValidateRules();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KINOVR/Prefabs/NumberedBall.prefab");
            var visual = prefab.GetComponent<KinoBallNumber>();
            Check(visual.kinoBonusMaterial && visual.secondChanceMaterial && visual.secondChanceMaterial.GetFloat("_SecondChanceGreen") == 1, "Missing ball palette.");
            Check(!ShaderUtil.ShaderHasError(visual.secondChanceMaterial.shader), "Ball shader error.");
            var color = prefab.GetComponent<Catchable>().secondChanceCatchVFX.GetComponent<ParticleSystem>().main.startColor.color;
            Check(color.g > .9f && color.r < .1f, "Catch effect is not green.");
            var round = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab").GetComponent<KinoRoundController>();
            Check(round.secondChancePresentation && round.secondChancePresentation.enclosure && round.secondChancePresentation.announcementCanvas && round.secondChancePresentation.announcement, "Missing transition references.");
            Check(round.board.secondChanceMarkerMaterial && round.board.multiplierLabels.All(t => t), "Missing green markers/multiplier labels.");
            Check(!ShaderUtil.ShaderHasError(round.board.secondChanceMarkerMaterial.shader) && !ShaderUtil.ShaderHasError(round.secondChancePresentation.enclosure.fadeMaterial.shader), "Board/fade shader error.");
            Check(round.secondChancePresentation.announcement.GetComponentInChildren<KinoSecondChancePanel>(true), "Missing Second Chance frame.");
            var title = round.secondChancePresentation.announcement.transform.Find("Second chance title");
            Check(title && title.GetComponent<TMPro.TMP_Text>().font.HasCharacters("ΕΥΚΑΙΡΙΑ"), "Missing Greek Second Chance title.");
            Check(round.restartButton && round.restartButton.round == round && round.restartButton.button &&
                round.restartButton.pressArea && !round.restartButton.gameObject.activeSelf, "Missing or initially visible restart button.");
        }
        [MenuItem("Tools/KINO VR/Second Chance/2 - Test full sequence")]
        public static void RunMenu() => Run();
        public static void Run(bool exitWhenDone = false)
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play first.");
            ValidateRulesAndAssets();
            SessionState.SetBool("KinoBonus.PlayTest", false);
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".Exit", exitWhenDone);
            SessionState.SetFloat(Key + ".TimeScale", Time.timeScale);
            SessionState.SetFloat(Key + ".MaxDelta", Time.maximumDeltaTime);
            File.WriteAllText(Folder + "/play-tests.txt", "RUNNING");
            EditorApplication.isPlaying = true;
        }
        static void StartDraw(KinoRoundController round, int index, bool beginRound = true)
        {
            // Round regressions exercise standalone restart/showcase; session flow has its own runner.
            if (round.experience && round.experience.enabled) round.experience.enabled = false;
            KinoAudioTests.ObserveSequence(round);
            run = index; normalSeen = greenSeen = boostSeen = bonusChanges = 0;
            leases.Clear(); bonus = null; physicalCatchPending = false;
            restartPending = restartInputSent = false;
            round.showcaseBoostAfterSecondChance = run == 0;
            round.enableMainSpecialBalls = false; // This runner isolates the legacy phase/audio contract.
            if (beginRound) round.BeginRound(60);
            startedAt = round.State.PhaseStartedAt;
            previous = KinoRoundPhase.Main; phaseAt = startedAt;
            Check(round.State.Score == 0 && round.board.caughtMarkers.All(m => !m.activeSelf) && round.secondChancePresentation.FadeAlpha == 0, "Restart did not reset score/board/fade.");
            Check(!round.restartButton.IsVisible && round.State.Phase == KinoRoundPhase.Main && round.State.UniqueCount == 0 &&
                round.State.CatchCount == 0 && !round.State.BonusLaunched && round.State.SecondChanceLaunchCount == 0 &&
                round.State.BoostLaunchCount == 0 && round.score.CurrentScore == 0 && round.State.RemainingSeconds > 59 &&
                round.launcher.FlightTimeMultiplier == 1 && !round.boostPresentation.IsBoostActive &&
                round.board.multiplierLabels.All(t => !t.gameObject.activeSelf), "Restart retained sequence data or UI.");
            round.restartButton.Press();
            Check(round.State.PhaseStartedAt == startedAt, "Hidden button restarted active gameplay.");
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
            try
            {
                Check(EditorApplication.timeSinceStartup < timeout, "Sequence integration timed out.");
                var round = Object.FindFirstObjectByType<KinoRoundController>();
                if (!round || !round.launcher || !round.launcher.player) return;
                if (restartPending)
                {
                    if (Time.realtimeSinceStartupAsDouble < restartReadyAt) return;
                    if (!restartInputSent)
                    {
                        Check(round.restartButton.CanPress, "Completed round restart did not arm.");
                        if (run == 0)
                        {
                            hand.transform.position = round.restartButton.transform.position;
                            Physics.SyncTransforms();
                        }
                        else round.restartButton.button.onClick.Invoke();
                        restartInputSent = true;
                        restartDeadline = EditorApplication.timeSinceStartup + 3;
                        return;
                    }
                    Check(EditorApplication.timeSinceStartup < restartDeadline, "Physical/click restart did not begin a new round.");
                    if (!round.IsRunning) return;
                    if (hand) { Object.Destroy(hand); hand = null; }
                    events.Add($"run {run}: restarted via " + (run == 0 ? "physical hand trigger" : "button click"));
                    StartDraw(round, run + 1, false);
                    return;
                }
                if (!started)
                {
                    started = true; events.Clear();
                    round.onRoundFinished.AddListener(() => finished++);
                    StartDraw(round, 0);
                }
                double now = Time.timeAsDouble;
                var state = round.State;
                if (state.IsRunning) Check(!round.restartButton.IsVisible, "Restart appeared before the sequence finished.");
                if (state.Phase != previous)
                {
                    events.Add($"run {run}: {previous} -> {state.Phase} at {now - startedAt:F3}s (phase {now - phaseAt:F3}s)");
                    if (previous == KinoRoundPhase.BoardHold) Check(now - phaseAt >= 2.95, "Short board hold.");
                    if (previous == KinoRoundPhase.FadeOut) Check(now - phaseAt >= 1.45, "Short fade.");
                    if (previous == KinoRoundPhase.SecondChanceReveal) Check(now - phaseAt >= 3.45, "Longer reveal did not preserve its reading hold.");
                    if (state.Phase == KinoRoundPhase.SecondChanceReveal) Check(round.secondChancePresentation.announcement.activeSelf, "Missing title.");
                    if (run == 0 && state.Phase == KinoRoundPhase.SecondChanceReveal)
                    {
                        Check(round.secondChancePresentation.FadeAlpha > .8f, "Fade did not reach black.");
                        KinoGameplaySetup.CaptureSequencePlayerView(round, "Live-blackout");
                    }
                    if (state.Phase == KinoRoundPhase.SecondChance) Check(round.secondChancePresentation.FadeAlpha == 0, "Green gameplay blacked out.");
                    if (run == 0 && state.Phase == KinoRoundPhase.SecondChance)
                        KinoGameplaySetup.CaptureSequencePlayerView(round, "Live-second-chance");
                    if (state.Phase == KinoRoundPhase.Boost)
                    {
                        Check(greenSeen == 3 && round.boostPresentation.IsBoostActive, "Wrong showcase order/presentation.");
                        Check(round.boostPresentation.panels.Where(p => p).All(p => p.material.GetFloat("_BoostStrength") == 1), "Boost palette did not cut immediately.");
                        KinoGameplaySetup.CaptureSequencePlayerView(round, "Live-boost-cut");
                    }
                    previous = state.Phase; phaseAt = now;
                }
                if (state.Phase == KinoRoundPhase.BoardHold || state.Phase == KinoRoundPhase.FadeOut || state.Phase == KinoRoundPhase.SecondChanceReveal)
                    Check(round.launcher.ActiveBallCount == 0 && !round.launcher.SpawnBall(), "A ball launched during transition.");
                foreach (var ball in round.launcher.GetComponentsInChildren<KinoPooledBall>())
                {
                    if (leases.TryGetValue(ball, out uint generation) && generation == ball.Generation) continue;
                    leases[ball] = ball.Generation;
                    var caught = ball.GetComponent<Catchable>();
                    var visual = ball.GetComponent<KinoBallNumber>();
                    if (caught.IsKinoBonus)
                    {
                        Check(normalSeen == 20 && greenSeen == 0 && state.ResolvedNormalCount == 20, "Bonus is out of order.");
                        bonus = ball; lastBonusNumber = caught.Number; bonusChangedAt = now;
                        Check(ball.GetComponent<MeshRenderer>().sharedMaterial == visual.kinoBonusMaterial, "Pooled red material failed.");
                    }
                    else if (caught.IsSecondChance)
                    {
                        greenSeen++;
                        Check(greenSeen <= 3 && normalSeen == 20, "Green quota/order failed.");
                        Check(ball.GetComponent<MeshRenderer>().sharedMaterial == visual.secondChanceMaterial && visual.numberLabel.color == Color.white, "Pooled green appearance failed.");
                        int before = state.Score;
                        if (run == 2) caught.Miss();
                        else
                        {
                            caught.Catch(); caught.Catch();
                            Check(state.Score == before + 3 && round.score.CurrentScore == state.Score, "Green scoring/double-catch failed.");
                        }
                    }
                    else if (caught.IsBoost)
                    {
                        boostSeen++;
                        Check(state.HasCaught(caught.Number), "Boost spawned an uncaught number.");
                        Check(ball.GetComponent<MeshRenderer>().sharedMaterial != visual.secondChanceMaterial && ball.GetComponent<MeshRenderer>().sharedMaterial != visual.kinoBonusMaterial, "Pooled Boost material failed.");
                        // Repeatedly catch the same eligible number to verify repeat rewards/effects.
                        int number = state.GetCaughtNumber(0);
                        caught.Configure(number, round, false, false, true); visual.SetNumber(number, round.launcher.player);
                        int before = state.Score; caught.Catch(); caught.Catch();
                        Check(state.Score == before + 3 && round.board.multiplierLabels[number - 1].gameObject.activeSelf, "Repeated Boost +3/popup failed.");
                    }
                    else
                    {
                        normalSeen++;
                        Check(normalSeen <= 20 && now - startedAt >= (normalSeen - 1) * 3 - .05, "Normal quota/timing failed.");
                        if (run != 2 && normalSeen <= 3)
                        {
                            int number = normalSeen == 2 ? 12 : 7;
                            caught.Configure(number, round); visual.SetNumber(number, round.launcher.player);
                            caught.Catch(); caught.Catch();
                        }
                        else caught.Miss();
                    }
                }
                if (bonus && bonus.gameObject.activeSelf && !physicalCatchPending)
                {
                    var caught = bonus.GetComponent<Catchable>();
                    Check(state.HasCaught(caught.Number) && bonus.GetComponent<KinoBallNumber>().Number == caught.Number, "Red number/display mismatch.");
                    if (caught.Number != lastBonusNumber)
                    {
                        Check(now - bonusChangedAt >= .8, "Bonus number changed too fast.");
                        lastBonusNumber = caught.Number; bonusChangedAt = now; bonusChanges++;
                    }
                    if (run == 1) caught.Miss();
                    else if (bonusChanges >= 2)
                    {
                        bonus.StopAirflow();
                        var body = bonus.GetComponent<Rigidbody>();
                        body.useGravity = false; body.linearVelocity = Vector3.zero; body.position = new Vector3(0, 30, 0);
                        hand = new GameObject("Sequence physical bonus catch"); hand.transform.position = body.position;
                        hand.AddComponent<HandCatcher>(); hand.AddComponent<SphereCollider>().isTrigger = true;
                        Physics.SyncTransforms(); physicalCatchPending = true;
                    }
                }
                if (physicalCatchPending && !bonus.gameObject.activeSelf)
                {
                    Check(state.BonusCaughtNumber == lastBonusNumber, "Physical catch failed to lock the displayed number.");
                    Object.Destroy(hand); physicalCatchPending = false; bonus = null;
                }
                if (state.IsRunning) return;
                Check(normalSeen == 20 && greenSeen == 3 && state.ResolvedSecondChanceCount == 3 && finished == run + 1, "Final quota/finish event failed.");
                Check(round.launcher.ActiveBallCount == 0 && !round.launcher.SpawnBall(), "Finished sequence left live balls.");
                int expected = run == 0 ? 3 + 3 + 9 + boostSeen * 3 : run == 1 ? 3 + 9 : 0;
                Check(state.Score == expected && round.score.CurrentScore == expected, "Cumulative score mismatch.");
                Check(run != 0 || boostSeen == 25, "Showcase must run 25 one-second launch slots.");
                Check(run == 0 || boostSeen == 0, "Production cycle did not finish after Second Chance.");
                Check(round.restartButton.IsVisible, "Finished sequence did not show Restart.");
                var view = round.playerView.View;
                Vector3 offset = Quaternion.Inverse(Quaternion.LookRotation(Vector3.ProjectOnPlane(view.forward, Vector3.up))) *
                    (round.restartButton.transform.position - view.position);
                Check(offset.x < 0 && offset.z > 0 && offset.magnitude < 1, "Restart is not reachable in front-left.");
                var camera = view.GetComponent<Camera>();
                Vector3 screenPoint = camera.WorldToScreenPoint(round.restartButton.transform.position);
                Check(round.restartButton.pressArea.Raycast(camera.ScreenPointToRay(screenPoint), out _, 10), "Desktop pointer cannot hit Restart.");
                if (run < 2)
                {
                    if (run == 0) KinoGameplaySetup.CaptureSequencePlayerView(round, "Live-restart-front-left");
                    round.restartButton.Press();
                    Check(!round.IsRunning, "Restart did not debounce the last catch.");
                    restartPending = true; restartInputSent = false;
                    restartReadyAt = Time.realtimeSinceStartupAsDouble + .45;
                    if (run == 0)
                    {
                        hand = new GameObject("Physical restart hand");
                        hand.transform.position = round.restartButton.transform.position + Vector3.up;
                        hand.AddComponent<HandCatcher>();
                        var collider = hand.AddComponent<SphereCollider>();
                        collider.isTrigger = true; collider.radius = .06f;
                    }
                    return;
                }
                KinoAudioTests.ValidateSequence(round);
                round.BeginRound(); round.launcher.SpawnBall(); round.enabled = false;
                Check(!round.IsRunning && round.launcher.ActiveBallCount == 0 && round.secondChancePresentation.FadeAlpha == 0 &&
                    !round.restartButton.IsVisible, "Disable left gameplay/fade/restart running.");
                File.WriteAllText(Folder + "/play-tests.txt", "PASS: three full 60s normal draws; 20+red bonus+hold+fade+title+3 green sequence; caught-only red cycling and physical catch; missed/empty bonus paths; green +3 and pool colors; 25s optional Boost, repeated caught numbers +3 and board effects; production ends at Second Chance; empty round still gets 3 greens; exact finish events; front-left Restart only after completion; physical hand and button-click restart; complete score/board/phase/timer reset; debounce and hidden-button guards; restart/disable.\n");
                End(true);
            }
            catch (Exception e) { File.WriteAllText(Folder + "/play-tests.txt", e.ToString()); Debug.LogException(e); End(false); }
        }
        static void End(bool passed)
        {
            File.WriteAllLines(Folder + "/phase-timing.txt", events);
            Time.timeScale = SessionState.GetFloat(Key + ".TimeScale", 1);
            Time.maximumDeltaTime = SessionState.GetFloat(Key + ".MaxDelta", .333333f);
            SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".Passed", passed);
            if (passed) Debug.Log("KINO_SECOND_CHANCE_SEQUENCE_TEST_PASSED");
            EditorApplication.isPlaying = false;
        }
    }
}
