using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    [InitializeOnLoad]
    public static class KinoAudioTests
    {
        const string Key = "KinoAudio.PlayTests";
        static readonly Dictionary<KinoSound, int> counts = new Dictionary<KinoSound, int>();
        static KinoAudioController observed;
        static KinoRoundController round;
        static GameObject ball;
        static int stage;
        static double timeout;
        static double nextStage;
        static float chillTime;

        static KinoAudioTests()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (change == PlayModeStateChange.EnteredPlayMode)
                {
                    stage = 0;
                    timeout = EditorApplication.timeSinceStartup + 55;
                    Application.runInBackground = true;
                }
                if (change != PlayModeStateChange.EnteredEditMode) return;
                string part = SessionState.GetString(Key + ".Part", "");
                if (part == "smoke-done" && SessionState.GetBool(Key + ".FullSequence", true))
                {
                    SessionState.SetString(Key + ".Part", "sequence");
                    EditorApplication.delayCall += () => KinoSecondChanceTests.Run();
                }
                else if (part == "sequence" || part == "failed" || part == "smoke-done")
                {
                    bool passed = part == "smoke-done" || (part == "sequence" && File.ReadAllText("Artifacts/KinoGameplay/SecondChance/play-tests.txt").StartsWith("PASS", StringComparison.Ordinal));
                    SessionState.SetBool(Key, false);
                    RestoreBatchXR();
                    if (passed) Debug.Log("KINO_AUDIO_PLAY_TESTS_PASSED");
                    if (SessionState.GetBool(Key + ".Exit", false)) EditorApplication.Exit(passed ? 0 : 1);
                }
            };
        }

        [MenuItem("Tools/KINO VR/Audio/5 - Test playback and full sequence")]
        public static void RunMenu() => Run();

        public static void Run(bool exitWhenDone = false, bool fullSequence = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            KinoAudioSetup.Validate();
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".Exit", exitWhenDone);
            SessionState.SetBool(Key + ".FullSequence", fullSequence);
            SessionState.SetString(Key + ".Part", "smoke");
            if (Application.isBatchMode)
            {
                var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
                if (xr)
                {
                    SessionState.SetBool(Key + ".XRInit", xr.InitManagerOnStart);
                    xr.InitManagerOnStart = false;
                }
            }
            File.WriteAllText(KinoAudioSetup.Output + "/playback-tests.txt", "RUNNING\n");
            EditorApplication.isPlaying = true;
        }

        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        static void RestoreBatchXR()
        {
            if (!Application.isBatchMode) return;
            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (xr) xr.InitManagerOnStart = SessionState.GetBool(Key + ".XRInit", true);
        }

        static int Count(KinoSound sound) => counts.TryGetValue(sound, out int value) ? value : 0;
        static void Record(KinoSound sound) => counts[sound] = Count(sound) + 1;

        public static void ObserveSequence(KinoRoundController target)
        {
            if (!target.audioController || observed == target.audioController) return;
            if (observed) observed.Played -= Record;
            observed = target.audioController;
            counts.Clear();
            observed.Played += Record;
        }

        public static void ValidateSequence(KinoRoundController target)
        {
            if (!target.audioController) return;
            Check(Count(KinoSound.CatchNormal) == 6 && Count(KinoSound.CatchGreen) == 6 &&
                Count(KinoSound.CatchBonus) == 1 && Count(KinoSound.CatchBoost) == 25,
                "Catch audio missing or duplicated by double-hand/double-catch calls.");
            Check(Count(KinoSound.BonusReveal) == 2 && Count(KinoSound.SecondChanceOut) == 3 &&
                Count(KinoSound.SecondChanceReveal) == 3 && Count(KinoSound.BoostStart) == 1 &&
                Count(KinoSound.BoostEnd) == 1 && Count(KinoSound.RoundComplete) >= 2 && Count(KinoSound.Restart) == 2,
                "Phase, finish or restart audio missing/duplicated.");
            Check(target.GetComponentsInChildren<AudioSource>(true).Length == 21, "Audio sources grew during the full sequence.");
            File.WriteAllText(KinoAudioSetup.Output + "/sequence-audio.txt", "PASS: actual gameplay emits one cue per accepted catch, three Second Chance transitions, one Boost entry/exit, round finishes and two restarts; fixed 21-source budget.\n" +
                string.Join("\n", counts.OrderBy(p => p.Key).Select(p => p.Key + ": " + p.Value)) + "\n");
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || SessionState.GetString(Key + ".Part", "") != "smoke" || !EditorApplication.isPlaying) return;
            try
            {
                Check(EditorApplication.timeSinceStartup < timeout, "Audio flight/playback test timed out.");
                if (!round) round = Object.FindFirstObjectByType<KinoRoundController>();
                if (!round || !round.launcher || !round.launcher.player) return;
                var audio = round.audioController;
                Check(audio, "Scene has no audio controller.");
                if (stage == 0)
                {
                    ObserveSequence(round);
                    round.enableMainSpecialBalls = false;
                    round.BeginRound(60);
                    round.launcher.StopLaunching(true);
                    ball = round.launcher.SpawnBall(0);
                    Check(ball && Count(KinoSound.TubeRise) == 1 && Count(KinoSound.TubeExit) == 0,
                        "Tube rise is not synchronized to the actual launch.");
                    stage = 1;
                    return;
                }
                if (stage == 1)
                {
                    if (Count(KinoSound.BallWhoosh) == 0) return;
                    Check(Count(KinoSound.TubeExit) == 1 && Count(KinoSound.BallWhoosh) == 1, "Flight cues must fire once at exit/approach.");
                    Check(audio.chillMusic.isPlaying && audio.outdoorAmbience.isPlaying && audio.machine.isPlaying,
                        "Baked music/ambience did not start in Play mode.");
                    Check(audio.chillMusic.time > 0 && !audio.boostMusic.isPlaying, "Wrong initial music state.");
                    ball.GetComponent<Catchable>().Catch();
                    ball.GetComponent<Catchable>().Catch();
                    Check(Count(KinoSound.CatchNormal) == 1 && round.State.Score == 1, "Duplicate catch feedback.");
                    Check(!audio.voices.Any(v => v.isPlaying && v.clip && (v.clip.name.StartsWith("BallWhoosh") || v.clip.name.StartsWith("TubeRise"))),
                        "Returned ball left a following sound alive.");
                    stage = 2;
                }
                if (stage == 2)
                {
                    if (!round.State.IsNormalLaunchDue(Time.timeAsDouble)) return;
                    var second = round.launcher.SpawnBall(1);
                    Check(second, "Second smoke-test flight was not launched.");
                    second.GetComponent<Catchable>().Miss(true);
                    second.GetComponent<Catchable>().Miss(true);
                    Check(Count(KinoSound.BallMiss) == 1, "Floor impact audio duplicated.");
                    var randomBefore = UnityEngine.Random.state;
                    audio.Play(KinoSound.Bird, round.transform.position);
                    Check(UnityEngine.Random.state.Equals(randomBefore), "Audio changes the gameplay random stream.");
                    foreach (KinoSound sound in Enum.GetValues(typeof(KinoSound))) audio.Play(sound, round.transform.position);
                    Check(round.GetComponentsInChildren<AudioSource>(true).Length == 21, "Playback instantiated additional sources.");
                    round.enabled = false;
                    Check(round.GetComponentsInChildren<AudioSource>(true).All(s => !s.isPlaying), "Disable left audio playing.");
                    round.enabled = true;
                    round.BeginRound();
                    Check(audio.chillMusic.isPlaying, "Audio did not recover after disable/restart.");
                    round.launcher.StopLaunching(true);
                    round.State.Begin(60, Time.timeAsDouble - 3.1);
                    int caughtBefore = Count(KinoSound.CatchNormal);
                    var left = round.launcher.SpawnBall(0);
                    var right = round.launcher.SpawnBall(1);
                    Check(left && right, "Cannot prepare simultaneous catches.");
                    left.GetComponent<Catchable>().Catch();
                    right.GetComponent<Catchable>().Catch();
                    Check(round.State.Score == 2 && Count(KinoSound.CatchNormal) == caughtBefore + 2,
                        "Two valid catches in one frame lost an audio reward.");
                    PrepareBoost(round);
                    nextStage = Time.realtimeSinceStartupAsDouble + 1.6;
                    stage = 3;
                    return;
                }
                if (stage == 3)
                {
                    if (Time.realtimeSinceStartupAsDouble < nextStage) return;
                    Check(audio.boostMusic.isPlaying && audio.boostMusic.volume > audio.chillMusic.volume, "Boost music did not crossfade in.");
                    audio.announcement.Play();
                    nextStage = Time.realtimeSinceStartupAsDouble + .25;
                    stage = 4;
                    return;
                }
                if (stage == 4)
                {
                    if (Time.realtimeSinceStartupAsDouble < nextStage) return;
                    Check(audio.announcement.isPlaying && audio.boostMusic.volume < audio.musicVolume * .4f,
                        "Music did not duck below the Boost voice.");
                    chillTime = audio.chillMusic.time;
                    round.FinishRound();
                    nextStage = Time.realtimeSinceStartupAsDouble + 1.7;
                    stage = 5;
                    return;
                }
                if (stage == 5)
                {
                    if (Time.realtimeSinceStartupAsDouble < nextStage) return;
                    Check(!audio.boostMusic.isPlaying && audio.chillMusic.isPlaying && audio.chillMusic.volume > 0 && audio.chillMusic.time > chillTime,
                        "Finish failed to return to the continuing Chillout timeline.");
                    round.gameObject.SetActive(false);
                    round.gameObject.SetActive(true);
                    nextStage = Time.realtimeSinceStartupAsDouble + .25;
                    stage = 6;
                    return;
                }
                if (stage == 6)
                {
                    if (Time.realtimeSinceStartupAsDouble < nextStage) return;
                    Check(audio.chillMusic.isPlaying && audio.chillMusic.volume > 0 && !audio.boostMusic.isPlaying,
                        "Re-enabling the gameplay root retained a stale Boost music state.");
                    File.WriteAllText(KinoAudioSetup.Output + "/playback-tests.txt", "PASS: actual tube rise/exit/approach timing; loaded music and ambience playing; exactly one accepted catch and floor cue; both simultaneous valid catches are audible; recycled ball stops following audio; audio preserves gameplay RNG; fixed source count; disable/restart and root re-enable; Boost crossfade, voice ducking and return to continuing Chillout.\n");
                    SessionState.SetString(Key + ".Part", "smoke-done");
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception e)
            {
                File.WriteAllText(KinoAudioSetup.Output + "/playback-tests.txt", e.ToString());
                Debug.LogException(e);
                SessionState.SetString(Key + ".Part", "failed");
                EditorApplication.isPlaying = false;
            }
        }

        static void PrepareBoost(KinoRoundController target)
        {
            // Seed resolved history, then let the real round controller enter Boost.
            var state = target.State;
            double start = Time.timeAsDouble - 80;
            state.Begin(60, start);
            for (int i = 0; i < 20; i++)
            {
                Check(state.TryRegisterNormalLaunch(start + i * 3), "Cannot seed draw.");
                Check(state.TryCatch(7, start + i * 3), "Cannot seed catch.");
            }
            state.Tick(start + 60);
            Check(state.TryBeginBonus() && state.TryRegisterBonusLaunch() && state.TryCatch(7, start + 60, true), "Cannot seed bonus.");
            Check(state.TryBeginSecondChanceTransition(start + 61), "Cannot seed transition.");
            double revealAt = start + 61 + KinoRoundState.BoardHoldSeconds + KinoRoundState.FadeSeconds;
            double greenAt = revealAt + KinoRoundState.RevealSeconds;
            state.Tick(start + 64); state.Tick(revealAt); state.Tick(greenAt);
            for (int i = 0; i < 3; i++)
            {
                Check(state.TryRegisterSecondChanceLaunch(greenAt + i * 3), "Cannot seed green draw.");
                Check(state.TryCatch(7, greenAt + i * 3, false, true), "Cannot seed green catch.");
            }
            target.showcaseBoostAfterSecondChance = true;
            target.RefreshClock();
            Check(state.Phase == KinoRoundPhase.BoostIntro, "Seeded history did not enter the Boost introduction.");
        }
    }
}
