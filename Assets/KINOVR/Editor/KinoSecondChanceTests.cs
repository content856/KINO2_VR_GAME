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
        static void Reveal(KinoRoundState state, double start)
        {
            Check(state.TryBeginSecondChanceTransition(start), "Second Chance transition rejected.");
            state.Tick(start + 2.99);
            Check(state.Phase == KinoRoundPhase.BoardHold && !state.TryRegisterSecondChanceLaunch(start + 2.99), "Board did not stay visible for 3 seconds.");
            state.Tick(start + 3);
            Check(state.Phase == KinoRoundPhase.FadeOut, "Missing fade-out.");
            state.Tick(start + 3.99);
            Check(state.Phase == KinoRoundPhase.FadeOut, "Fade shorter than one second.");
            state.Tick(start + 4);
            Check(state.Phase == KinoRoundPhase.SecondChanceReveal && !state.TryRegisterSecondChanceLaunch(start + 4), "Launch behind title.");
            state.Tick(start + 7);
            Check(state.Phase == KinoRoundPhase.SecondChance, "Missing green phase.");
        }
        public static void ValidateRules()
        {
            var state = ResolvedDraw(true);
            Check(state.Score == 3 && state.UniqueCount == 2, "Normal scoring/repeat memory.");
            Check(state.TryBeginBonus() && !state.TryCatch(7, 60, true), "Red bonus launch guard.");
            Check(state.TryRegisterBonusLaunch() && !state.TryRegisterBonusLaunch(), "More than one red bonus.");
            Check(!state.TryCatch(80, 60, true), "Uncaught red bonus number.");
            Check(state.TryCatch(7, 60, true) && !state.TryCatch(7, 60, true) && state.Score == 6, "Bonus must award +3 once.");
            Reveal(state, 60);
            for (int i = 0; i < 3; i++)
            {
                Check(!state.TryRegisterSecondChanceLaunch(67 + i * 3 - .001), "Early green launch.");
                Check(state.TryRegisterSecondChanceLaunch(67 + i * 3), "Missing green launch.");
                Check(!state.TryBeginShowcaseBoost(67 + i * 3, 25, 1), "Boost overlapped green flight.");
                Check(state.TryCatch(i == 1 ? 80 : 7, 67 + i * 3, false, true), "Green catch failed.");
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
            Reveal(state, 60);
            for (int i = 0; i < 3; i++) { state.TryRegisterSecondChanceLaunch(67 + i * 3); state.TryMiss(false, true); }
            Check(state.ResolvedSecondChanceCount == 3 && !state.TryBeginShowcaseBoost(74, 25, 1), "Empty eligible set started Boost.");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/rules.txt", "PASS: normal 20 slots; last-flight grace; one red bonus before Second Chance; caught-only candidates; 3s board hold; 1s fade; reveal blocks launches; exactly 3 greens at 3s spacing; green +3; total 23 draw balls; optional timed Boost repeats caught numbers and awards +3 each catch; empty sets; deadlines; restart.\n");
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
            Check(round.secondChancePresentation && round.secondChancePresentation.fadeImage && round.secondChancePresentation.announcement, "Missing transition references.");
            Check(round.board.secondChanceMarkerMaterial && round.board.multiplierLabels.All(t => t), "Missing green markers/multiplier labels.");
            Check(!ShaderUtil.ShaderHasError(round.board.secondChanceMarkerMaterial.shader) && !ShaderUtil.ShaderHasError(round.secondChancePresentation.fadeImage.material.shader), "Board/fade shader error.");
            Check(round.secondChancePresentation.announcement.GetComponentInChildren<RawImage>(true).texture, "Missing logo texture.");
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
        static void StartDraw(KinoRoundController round, int index)
        {
            run = index; normalSeen = greenSeen = boostSeen = bonusChanges = 0;
            leases.Clear(); bonus = null; physicalCatchPending = false;
            round.showcaseBoostAfterSecondChance = run == 0;
            round.BeginRound(60);
            startedAt = Time.timeAsDouble;
            previous = KinoRoundPhase.Main; phaseAt = startedAt;
            Check(round.State.Score == 0 && round.board.caughtMarkers.All(m => !m.activeSelf) && round.secondChancePresentation.FadeAlpha == 0, "Restart did not reset score/board/fade.");
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
            try
            {
                Check(EditorApplication.timeSinceStartup < timeout, "Sequence integration timed out.");
                var round = Object.FindFirstObjectByType<KinoRoundController>();
                if (!round || !round.launcher || !round.launcher.player) return;
                if (!started)
                {
                    started = true; events.Clear();
                    round.onRoundFinished.AddListener(() => finished++);
                    StartDraw(round, 0);
                }
                double now = Time.timeAsDouble;
                var state = round.State;
                if (state.Phase != previous)
                {
                    events.Add($"run {run}: {previous} -> {state.Phase} at {now - startedAt:F3}s (phase {now - phaseAt:F3}s)");
                    if (previous == KinoRoundPhase.BoardHold) Check(now - phaseAt >= 2.95, "Short board hold.");
                    if (previous == KinoRoundPhase.FadeOut) Check(now - phaseAt >= .95, "Short fade.");
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
                if (run < 2) { StartDraw(round, run + 1); return; }
                round.BeginRound(); round.launcher.SpawnBall(); round.enabled = false;
                Check(!round.IsRunning && round.launcher.ActiveBallCount == 0 && round.secondChancePresentation.FadeAlpha == 0, "Disable left gameplay/fade running.");
                File.WriteAllText(Folder + "/play-tests.txt", "PASS: three full 60s normal draws; 20+red bonus+hold+fade+title+3 green sequence; caught-only red cycling and physical catch; missed/empty bonus paths; green +3 and pool colors; 25s optional Boost, repeated caught numbers +3 and board effects; production ends at Second Chance; empty round still gets 3 greens; exact finish events; restart/disable.\n");
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
