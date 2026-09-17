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
    public static class KinoBonusTests
    {
        const string Key = "KinoBonus.PlayTest";
        const string Folder = KinoGameplaySetup.BonusOutput;
        static int stage, run, seen, lastNumber, changes, caughtNumber, finished;
        static double start, timeout, lastChange, droppedAt;
        static KinoPooledBall droppedBall;
        static KinoPooledBall bonus;
        static GameObject hand;
        static readonly Dictionary<KinoPooledBall, uint> leases = new Dictionary<KinoPooledBall, uint>();
        static readonly List<string> samples = new List<string>();

        static KinoBonusTests()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                {
                    stage = run = finished = 0;
                    timeout = EditorApplication.timeSinceStartup + 150;
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
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

        public static void ValidateRules()
        {
            var state = new KinoRoundState();
            Check(!state.TryCatch(1, 0), "Idle catch accepted.");
            state.Begin(60, 100);
            Check(!state.TryCatch(5, 100), "Catch accepted before any launch.");
            for (int i = 0; i < 20; i++)
            {
                double at = 100 + i * 3;
                Check(Math.Abs(state.NextNormalLaunchAt - at) < .00001, "Launch schedule drift.");
                Check(!state.TryRegisterNormalLaunch(at - .001), "Early launch accepted.");
                Check(state.TryRegisterNormalLaunch(at), "Scheduled launch rejected.");
                Check(!state.TryRegisterNormalLaunch(at), "Two ordinary balls shared a slot.");
                if (i < 3) Check(state.TryCatch(i == 1 ? 12 : 5, at), "Ordinary catch rejected.");
                else if (i < 19) Check(state.TryMiss(false), "Ordinary miss was not recorded.");
            }
            Check(state.NormalLaunchCount == 20 && !state.TryRegisterNormalLaunch(159), "More than 20 normal launches.");
            Check(state.Score == 3 && state.UniqueCount == 2 && state.NormalCatchCount == 3, "Repeat memory/scoring failed.");
            Check(!state.TryBeginBonus() && !state.TryRegisterBonusLaunch(), "Bonus appeared before 60 seconds.");
            state.Tick(160);
            Check(state.Phase == KinoRoundPhase.Settling && state.RemainingSeconds == 0, "Missing final-flight grace.");
            Check(state.TryCatch(34, 161), "Last ordinary flight cannot be caught after timer zero.");
            Check(state.TryBeginBonus() && state.UniqueCount == 3, "Missing bonus with caught numbers.");
            Check(!state.TryCatch(5, 161, true), "Bonus catch accepted before bonus launch.");
            Check(state.TryRegisterBonusLaunch() && !state.TryRegisterBonusLaunch(), "Bonus quota is not one.");
            Check(!state.TryRegisterNormalLaunch(162) && !state.TryCatch(80, 162), "Ordinary ball accepted in bonus phase.");
            Check(!state.TryCatch(80, 162, true) && !state.TryCatch(0, 162, true) && !state.TryCatch(81, 162, true), "Uncaught/invalid bonus number accepted.");
            Check(state.TryCatch(12, 162, true) && state.BonusCaughtNumber == 12 && state.Score == 7 &&
                state.NormalCatchCount == 4 && state.CatchCount == 5 && state.UniqueCount == 3, "Final bonus must reuse memory and award exactly +3.");
            Check(!state.TryCatch(5, 163, true), "Second bonus catch accepted.");
            state.Stop();
            Check(!state.TryCatch(12, 164, true), "Stopped catch accepted.");
            state.Begin(60, 200);
            Check(state.Score == 0 && state.UniqueCount == 0 && !state.BonusLaunched && state.NormalLaunchCount == 0, "Restart retained round state.");
            for (int i = 0; i < 20; i++)
            {
                Check(state.TryRegisterNormalLaunch(200 + i * 3), "Empty-round launch failed.");
                Check(state.TryMiss(false), "Empty-round miss failed.");
            }
            state.Tick(260);
            Check(!state.TryBeginBonus() && !state.IsRunning && !state.BonusLaunched, "Empty catch set must finish without bonus.");
            state.Begin(60, 300);
            state.TryRegisterNormalLaunch(300); state.TryCatch(7, 300);
            state.TryRegisterNormalLaunch(303); state.TryCatch(7, 303);
            Check(state.UniqueCount == 1 && state.GetCaughtNumber(0) == 7, "Duplicate candidates were not deduplicated.");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/rules.txt", "PASS: twenty 3-second slots at 0..57, early/extra launch rejection, ordinary +1, duplicate memory, final-flight grace, one final bonus, only caught candidates, +3 once, empty catch set skips bonus, restart.\n");
        }
        public static void ValidateRulesAndAssets()
        {
            ValidateRules();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KINOVR/Prefabs/NumberedBall.prefab");
            var visual = prefab.GetComponent<KinoBallNumber>();
            Check(visual.kinoBonusMaterial && visual.kinoBonusMaterial.GetFloat("_BonusRed") == 1, "Missing red ball material.");
            Check(!ShaderUtil.ShaderHasError(visual.kinoBonusMaterial.shader), "Ball shader error.");
            var catchable = prefab.GetComponent<Catchable>();
            var color = catchable.kinoBonusCatchVFX.GetComponent<ParticleSystem>().main.startColor.color;
            Check(color.r > .9f && color.g < .1f && color.b < .1f, "Bonus particles are not red.");
            var round = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab").GetComponent<KinoRoundController>();
            Check(round.roundDuration == 60 && round.bonusNumberInterval >= .25f, "Saved round defaults are wrong.");
            Check(round.board.kinoBonusMarkerMaterial && !ShaderUtil.ShaderHasError(round.board.kinoBonusMarkerMaterial.shader), "Missing/invalid red board marker.");
        }
        [MenuItem("Tools/KINO VR/KINO Bonus/2 - Test twenty-ball round and final bonus")]
        public static void RunMenu() => Run();
        public static void Run(bool exitWhenDone = false)
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play first.");
            ValidateRulesAndAssets();
            SessionState.SetBool("KinoGameplay.PlayTest", false);
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".Exit", exitWhenDone);
            SessionState.SetBool(Key + ".Passed", false);
            SessionState.SetFloat(Key + ".TimeScale", Time.timeScale);
            SessionState.SetFloat(Key + ".MaxDelta", Time.maximumDeltaTime);
            File.WriteAllText(Folder + "/play-tests.txt", "RUNNING");
            EditorApplication.isPlaying = true;
        }
        static void StartDraw(KinoRoundController round, int index)
        {
            run = index; seen = changes = 0; leases.Clear();
            bonus = null;
            round.launcher.ballLifetime = 6;
            round.bonusNumberInterval = 1;
            round.BeginRound(60);
            start = Time.timeAsDouble;
            Check(round.State.UniqueCount == 0 && round.State.NormalCatchCount == 0 && !round.State.BonusLaunched, "Restart retained memory/quota.");
            Check(round.board.caughtMarkers.All(m => !m.activeSelf), "Restart retained board markers.");
            stage = 1;
        }
        static void ObserveNormals(KinoRoundController round)
        {
            foreach (var ball in round.launcher.GetComponentsInChildren<KinoPooledBall>())
            {
                if (leases.TryGetValue(ball, out uint generation) && generation == ball.Generation) continue;
                leases[ball] = ball.Generation;
                var catchable = ball.GetComponent<Catchable>();
                if (catchable.IsKinoBonus) { bonus = ball; continue; }
                seen++;
                double elapsed = Time.timeAsDouble - start;
                Check(seen <= 20 && elapsed >= (seen - 1) * 3 - .05 && elapsed < (seen - 1) * 3 + .75, "Ordinary draw count/spacing drift: " + seen + " at " + elapsed);
                samples.Add(FormattableString.Invariant($"{run},{seen},{elapsed:F4},{catchable.Number}"));
                bool catchThis = run == 0 ? seen <= 4 : run == 2 && seen == 20;
                if (!catchThis)
                {
                    if (run == 1 && seen == 20) DropOnFloor(ball);
                    continue;
                }
                int number = run == 0 ? new[] { 5, 12, 5, 34 }[seen - 1] : 7;
                catchable.Configure(number, round);
                ball.GetComponent<KinoBallNumber>().SetNumber(number, round.launcher.player);
                catchable.Catch(); catchable.Catch();
                Check(!ball.gameObject.activeSelf, "Catch did not return ball to pool.");
            }
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
            try
            {
                Check(EditorApplication.timeSinceStartup < timeout, "Round integration timed out.");
                var round = Object.FindFirstObjectByType<KinoRoundController>();
                if (!round || !round.launcher || !round.launcher.player) return;
                var launcher = round.launcher;
                if (stage == 0)
                {
                    samples.Clear(); samples.Add("run,ordinary_launch,elapsed_seconds,number");
                    round.onRoundFinished.AddListener(() => finished++);
                    // Test slot deferral without changing the production pool or quota.
                    launcher.StopLaunching(true); launcher.round = null;
                    var full = Enumerable.Range(0, launcher.PoolCount).Select(_ => launcher.SpawnBall()).ToArray();
                    Check(full.All(b => b) && !launcher.SpawnBall(), "Pool prewarm/exhaustion failed.");
                    launcher.round = round; round.State.Begin(60, Time.timeAsDouble);
                    Check(!launcher.SpawnBall() && round.State.NormalLaunchCount == 0, "Full pool consumed a draw slot.");
                    var pooled = full[0].GetComponent<KinoPooledBall>(); uint stale = pooled.Generation;
                    pooled.ReturnToPool(stale);
                    var leased = launcher.SpawnBall();
                    Check(leased && round.State.NormalLaunchCount == 1, "Deferred slot did not launch.");
                    pooled.ReturnToPool(stale);
                    Check(leased.activeSelf, "Stale generation returned the current lease.");
                    StartDraw(round, 0);
                }
                else if (stage == 1)
                {
                    ObserveNormals(round);
                    if (run == 1 && seen == 20 && droppedBall && droppedBall.gameObject.activeSelf)
                        Check(Time.timeAsDouble - droppedAt < .7, "Grounded ordinary ball did not disappear immediately.");
                    Check(round.State.NormalLaunchCount <= 20, "Ordinary quota exceeded.");
                    if (round.State.Phase == KinoRoundPhase.Main || round.State.Phase == KinoRoundPhase.Settling)
                    {
                        Check(!launcher.HasLaunchedKinoBonus, "Bonus launched before ordinary flights resolved.");
                        return;
                    }
                    if (run == 1)
                    {
                        Check(droppedBall && !droppedBall.gameObject.activeSelf && round.State.NormalMissCount == 20, "Grounded last ball was not recorded as lost.");
                        Check(!round.IsRunning && seen == 20 && !launcher.HasLaunchedKinoBonus && round.State.Score == 0 &&
                            round.board.caughtMarkers.All(m => !m.activeSelf), "Zero catches must finish after 20 without board marks or bonus.");
                        StartDraw(round, 2); return;
                    }
                    if (!bonus) return;
                    Check(seen == 20 && launcher.ActiveBallCount == 1 && round.State.Phase == KinoRoundPhase.Bonus, "Final bonus overlaps ordinary balls.");
                    Check(!launcher.SpawnBall(), "A second bonus was allowed.");
                    Check(round.board.timeText.text == "00:00" && round.board.statusText.text.Contains("KINO BONUS"), "Wrong final bonus UI.");
                    Check(!round.boostPresentation || !round.boostPresentation.IsBoostActive, "Legacy BOOST activated.");
                    Check(round.State.UniqueCount == (run == 0 ? 3 : 1), "Wrong frozen candidate set.");
                    Check(round.board.caughtMarkers.Count(m => m.activeSelf) == round.State.UniqueCount, "Missed balls appeared on board.");
                    lastNumber = bonus.GetComponent<Catchable>().Number;
                    lastChange = Time.timeAsDouble; stage = run == 0 ? 2 : 5;
                }
                else if (stage == 2)
                {
                    Check(bonus.gameObject.activeSelf, "Bonus expired before cycle check.");
                    var catchable = bonus.GetComponent<Catchable>();
                    var visual = bonus.GetComponent<KinoBallNumber>();
                    int number = catchable.Number;
                    Check(round.State.HasCaught(number) && visual.Number == number && visual.numberLabel.text == number.ToString(), "Bonus display/value/candidate mismatch.");
                    Check(visual.numberLabel.color == Color.white && bonus.GetComponent<MeshRenderer>().sharedMaterial == visual.kinoBonusMaterial, "Wrong bonus appearance.");
                    if (number != lastNumber)
                    {
                        Check(Time.timeAsDouble - lastChange >= .8, "Bonus changed too quickly.");
                        lastNumber = number; lastChange = Time.timeAsDouble; changes++;
                    }
                    if (changes < 2) return;
                    caughtNumber = number;
                    KinoGameplaySetup.CaptureFinalBonusTestView(round, bonus, "Live-bonus-" + number);
                    bonus.StopAirflow();
                    var body = bonus.GetComponent<Rigidbody>();
                    body.useGravity = false; body.linearVelocity = Vector3.zero; body.position = new Vector3(0, 30, 0);
                    hand = new GameObject("Final bonus physical catch");
                    hand.transform.position = body.position;
                    hand.AddComponent<HandCatcher>(); hand.AddComponent<SphereCollider>().isTrigger = true;
                    Physics.SyncTransforms();
                    stage = 3;
                }
                else if (stage == 3)
                {
                    if (bonus.gameObject.activeSelf || round.IsRunning) return;
                    Object.Destroy(hand);
                    Check(round.State.BonusCaughtNumber == caughtNumber && round.State.Score == 7 && round.score.CurrentScore == 7 &&
                        round.State.NormalCatchCount == 4 && round.State.CatchCount == 5, "Caught visible number/+3/double-trigger failure.");
                    Check(round.board.caughtMarkers.Count(m => m.activeSelf && m.GetComponent<Graphic>().material == round.board.kinoBonusMarkerMaterial) == 1 &&
                        round.board.caughtMarkers[caughtNumber - 1].GetComponent<Graphic>().material == round.board.kinoBonusMarkerMaterial, "Wrong board number turned red.");
                    Check(round.board.numberLabels[caughtNumber - 1].color == Color.white, "Bonus board number is not white.");
                    Check(Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Any(p => p.name == "KinoBonusCatch(Clone)"), "Missing red catch effect.");
                    Check(finished == 1 && !launcher.SpawnBall() && !round.TryCatch(caughtNumber, Catchable.BallType.KinoBonus), "Finish repeated or late input accepted.");
                    KinoGameplaySetup.CaptureFinalBonusTestView(round, null, "Board-final-bonus-" + caughtNumber);
                    StartDraw(round, 1);
                }
                else if (stage == 5)
                {
                    Check(bonus.gameObject.activeSelf && bonus.GetComponent<Catchable>().Number == 7 && bonus.GetComponent<KinoBallNumber>().Number == 7, "Single candidate must stay 7.");
                    if (Time.timeAsDouble - lastChange < 1.2) return;
                    DropOnFloor(bonus); stage = 6;
                }
                else if (stage == 6)
                {
                    Check(Time.timeAsDouble - droppedAt < .7, "Grounded bonus did not disappear immediately.");
                    if (round.IsRunning) return;
                    Check(finished == 3 && round.State.BonusCaughtNumber == 0 && round.State.Score == 1 &&
                        launcher.HasLaunchedKinoBonus && launcher.ActiveBallCount == 0 && round.State.BonusMissed &&
                        round.State.ResolvedNormalCount == 20, "Missed bonus failed to finish exactly once.");
                    Check(round.board.caughtMarkers[6].GetComponent<Graphic>().material != round.board.kinoBonusMarkerMaterial, "Missed bonus turned board red.");
                    round.BeginRound(); launcher.SpawnBall(); round.enabled = false;
                    Check(launcher.ActiveBallCount == 0 && !round.IsRunning, "Disable left live balls.");
                    File.WriteAllLines(Folder + "/draw-timing.csv", samples);
                    File.WriteAllText(Folder + "/play-tests.txt", "PASS: three full 60-second draws, exactly 20 normal balls each at 3-second spacing; final bonus only after all ordinary flights; caught-only unique memory; visible number changes once/second and locks on physical catch; +3 once; only selected board marker red; empty round skips bonus; one-candidate bonus stays fixed; missed bonus finishes; restart, pool deferral/reuse/stale guard, disable, board/UI, legacy BOOST off.\n");
                    File.AppendAllText(Folder + "/play-tests.txt", "PASS: normal and bonus disappear on physical floor contact; catches + misses resolve all 20; missed bonus awards no points and leaves marker yellow.\n");
                    End(true);
                }
            }
            catch (Exception e)
            {
                File.WriteAllText(Folder + "/play-tests.txt", e.ToString());
                File.WriteAllLines(Folder + "/draw-timing.csv", samples);
                Debug.LogException(e); End(false);
            }
        }
        static void DropOnFloor(KinoPooledBall ball)
        {
            droppedBall = ball; droppedAt = Time.timeAsDouble;
            ball.StopAirflow();
            var body = ball.GetComponent<Rigidbody>();
            body.position = new Vector3(3, .3f, -3);
            body.linearVelocity = Vector3.down * 2;
            Physics.SyncTransforms();
        }
        static void End(bool passed)
        {
            Time.timeScale = SessionState.GetFloat(Key + ".TimeScale", 1);
            Time.maximumDeltaTime = SessionState.GetFloat(Key + ".MaxDelta", .333333f);
            SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".Passed", passed);
            if (passed) Debug.Log("KINO_TWENTY_BALL_FINAL_BONUS_TEST_PASSED");
            EditorApplication.isPlaying = false;
        }
    }
}
