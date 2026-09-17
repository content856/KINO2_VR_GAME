using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    [InitializeOnLoad]
    public static class KinoBoostTests
    {
        const string Key = "KinoBoost.PlayTest";
        const string Folder = "Artifacts/KinoGameplay/Boost";
        static int stage, finished;
        static double next, timeout, flightStarted;
        static KinoPooledBall testBall;
        static float nearest;
        static bool exited;
        static Material sourceMarble, boostMarble;
        static Color marbleBaseline;

        static KinoBoostTests()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode) { stage = finished = 0; next = Time.timeAsDouble + .25; timeout = EditorApplication.timeSinceStartup + 40; }
                if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".Exit", false))
                {
                    SessionState.SetBool(Key + ".Exit", false);
                    EditorApplication.Exit(SessionState.GetBool(Key + ".Passed", false) ? 0 : 1);
                }
            };
        }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        public static void ValidateRules()
        {
            var state = new KinoRoundState();
            state.Begin(75, 100, 6, 25);
            Check(state.TryCatch(1, 174.999) && state.Score == 1, "Main score must be +1.");
            Check(state.TryCatch(1, 175) && state.Phase == KinoRoundPhase.Calm && state.Score == 2, "Calm boundary/cumulative score failed.");
            Check(state.TryCatch(80, 181) && state.Phase == KinoRoundPhase.Boost && state.Score == 5 && state.CatchCount == 3 && state.UniqueCount == 2, "BOOST boundary must be +3, preserving catches/grid.");
            Check(!state.TryCatch(0, 182) && !state.TryCatch(81, 182) && state.Score == 5, "Invalid numbers awarded points.");
            Check(state.TryCatch(80, 205.999) && state.Score == 8 && state.CatchCount == 4, "Repeated BOOST catch failed.");
            Check(!state.TryCatch(1, 206) && !state.IsRunning && state.RemainingSeconds == 0, "Exact bonus deadline accepted a catch.");
            state.Begin(1, 0, 2, 3);
            state.Tick(5);
            Check(state.Phase == KinoRoundPhase.Boost && state.RemainingSeconds == 1, "Skipped frame extended/missed phase.");
            state.Tick(60);
            Check(state.Phase == KinoRoundPhase.Complete && !state.IsRunning, "Large clock jump did not stop.");
            state.Begin(1, 0, 0, 2);
            Check(state.TryCatch(7, 1) && state.Score == 3, "Zero calm duration failed.");
            state.Stop();
            Check(!state.TryCatch(1, 1.1), "Manual stop accepted a catch.");
            state.Begin(1, 100);
            Check(state.Score == 0 && state.CatchCount == 0 && state.UniqueCount == 0 && state.Multiplier == 1, "Restart did not reset.");
            Check(!state.TryCatch(7, 101), "Original single-round deadline changed.");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/rules.txt", "PASS: phase boundaries, +1/+3, cumulative score/catches, repeats, invalid numbers, exact final deadline, skipped frames, zero calm, stop, restart, disabled bonus.\n");
        }
        [MenuItem("Tools/KINO VR/BOOST/3 - Test bonus lifecycle")]
        public static void RunMenu() => Run();
        public static void Run(bool exitWhenDone = false)
        {
            ValidateRules();
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play first.");
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".Exit", exitWhenDone);
            SessionState.SetBool(Key + ".Passed", false);
            File.WriteAllText(Folder + "/play-tests.txt", "RUNNING");
            EditorApplication.isPlaying = true;
        }
        static void Catch(KinoRoundController round, int expectedScore, int expectedCount)
        {
            var ball = round.launcher.SpawnBall();
            Check(ball, "Could not lease ball.");
            var catchable = ball.GetComponent<Catchable>();
            int number = catchable.Number;
            catchable.Catch(); catchable.Catch();
            Check(round.State.Score == expectedScore && round.score.CurrentScore == expectedScore && round.State.CatchCount == expectedCount,
                "Score/catch count/double trigger failure.");
            Check(round.board.caughtMarkers[number - 1].activeSelf && !ball.activeSelf, "Marker/pool return failed.");
            Check(round.board.catchCountText.text == expectedScore.ToString("000"), "Board displays catches instead of score.");
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || Time.timeAsDouble < next) return;
            try
            {
                Check(EditorApplication.timeSinceStartup < timeout, "BOOST integration timed out.");
                var round = Object.FindFirstObjectByType<KinoRoundController>();
                var launcher = round.launcher;
                var presentation = round.boostPresentation;
                if (stage == 0)
                {
                    sourceMarble = AssetDatabase.LoadAssetAtPath<Material>("Assets/KinoRotunda/Materials/NeroMarble.mat");
                    marbleBaseline = sourceMarble.GetColor("_BaseColor");
                    Check(presentation && presentation.announcementAudio && presentation.announcementAudio.clip, "Presentation/audio not saved in prefab.");
                    round.enableBoostRound = true;
                    round.calmDuration = .6f; round.boostDuration = 7;
                    round.onRoundFinished.AddListener(() => finished++);
                    round.BeginRound(.6f); launcher.StopLaunching(true);
                    Check(launcher.PoolCount == 16 && launcher.IntervalMultiplier == 1, "Normal pace/pool changed.");
                    Catch(round, 1, 1);
                    stage = 1; next = Time.timeAsDouble + .7;
                }
                else if (stage == 1)
                {
                    round.RefreshClock();
                    Check(round.State.Phase == KinoRoundPhase.Calm && launcher.IntervalMultiplier > 1 && !presentation.IsBoostActive, "Calm phase failed.");
                    Catch(round, 2, 2);
                    stage = 2; next = Time.timeAsDouble + .65;
                }
                else if (stage == 2)
                {
                    round.RefreshClock();
                    Check(round.State.Phase == KinoRoundPhase.Boost && presentation.IsBoostActive && presentation.announcement.gameObject.activeSelf, "BOOST announcement failed.");
                    Check(launcher.EffectiveFlightTime < launcher.flightTime && launcher.IntervalMultiplier < 1 && launcher.EffectiveTubeRiseTime < launcher.tubeRiseTime, "BOOST not faster/more frequent.");
                    Catch(round, 5, 3);
                    Catch(round, 8, 4);
                    // Physical flight at the boosted pace must still reach the catch zone.
                    UnityEngine.Random.InitState(2419);
                    testBall = launcher.SpawnBall(0).GetComponent<KinoPooledBall>();
                    nearest = 100; exited = false; flightStarted = Time.timeAsDouble;
                    stage = 3;
                }
                else if (stage == 3)
                {
                    if (testBall && testBall.gameObject.activeSelf)
                    {
                        exited |= !testBall.IsRising;
                        nearest = Mathf.Min(nearest, Vector3.Distance(testBall.transform.position, launcher.player.position + Vector3.up * launcher.aimHeightOffset));
                        Check(float.IsFinite(testBall.transform.position.sqrMagnitude), "Non-finite BOOST trajectory.");
                    }
                    if (Time.timeAsDouble - flightStarted < 4.3) return;
                    Check(exited && nearest < 1.1f, "BOOST ball missed hand zone: " + nearest);
                    Check(!presentation.announcement.gameObject.activeSelf && presentation.activeBadge.activeSelf, "Announcement did not yield to playable board.");
                    Check(presentation.panels.All(p => p.material.GetFloat("_BoostStrength") > .99f), "Gold board palette not applied.");
                    boostMarble = Resources.FindObjectsOfTypeAll<Material>().Single(m => m.name == "NeroMarble (runtime boost)");
                    Check(boostMarble.GetColor("_BaseColor") != marbleBaseline && sourceMarble.GetColor("_BaseColor") == marbleBaseline,
                        "Quest material tint missing or shared source modified.");
                    Check(launcher.PoolCount == 16, "BOOST grew the pool.");
                    stage = 4;
                }
                else if (stage == 4)
                {
                    round.RefreshClock();
                    if (round.IsRunning) return;
                    Check(finished == 1 && launcher.ActiveBallCount == 0 && !launcher.IsLaunching && !presentation.IsBoostActive && !presentation.goldAccents.activeSelf, "Finish failed to stop/restore.");
                    Check(presentation.panels.All(p => p.material.GetFloat("_BoostStrength") == 0) && presentation.normalBrand.activeSelf, "Normal palette/logo not restored.");
                    Check(boostMarble.GetColor("_BaseColor") == marbleBaseline, "Original room tint not restored.");
                    Check(!round.TryCatch(5) && !launcher.SpawnBall() && round.score.CurrentScore == 8, "Late input changed result.");
                    round.FinishRound(); Check(finished == 1, "Finish event fired twice.");
                    round.BeginRound(1); launcher.StopLaunching(true);
                    Check(round.State.Score == 0 && round.score.CurrentScore == 0 && round.State.CatchCount == 0 && launcher.IntervalMultiplier == 1 && !presentation.activeBadge.activeSelf,
                        "Restart retained BOOST state.");
                    Check(round.board.caughtMarkers.All(m => !m.activeSelf), "Restart retained caught grid.");
                    round.enabled = false;
                    Check(!round.IsRunning && launcher.ActiveBallCount == 0 && launcher.IntervalMultiplier == 1, "Disable did not clean up.");
                    round.enabled = true;
                    round.enableBoostRound = false;
                    round.BeginRound(.1f);
                    stage = 5; next = Time.timeAsDouble + .2;
                }
                else
                {
                    round.RefreshClock();
                    Check(!round.IsRunning && finished == 2, "Disabled bonus changed single-round behavior.");
                    File.WriteAllText(Folder + "/play-tests.txt", $"PASS: saved prefab/audio, normal/calm/BOOST, cumulative +1/+3 scoring, physical catches vs score, duplicate guard, marker, pooled catches, shorter flight/tube time, boosted flight reaches hand zone ({nearest:F3} m), announcement fades, persistent x3 badge, deadline, finish once, late input, restart, disable, optional bonus off.\n");
                    SessionState.SetBool(Key + ".Passed", true);
                    SessionState.SetBool(Key, false);
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception e)
            {
                File.WriteAllText(Folder + "/play-tests.txt", "FAIL\n" + e);
                Debug.LogException(e);
                SessionState.SetBool(Key, false);
                EditorApplication.isPlaying = false;
            }
        }
    }
}
