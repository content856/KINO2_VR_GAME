using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace KinoVR.Editor
{
    [InitializeOnLoad]
    public static class KinoBonusTests
    {
        const string Key = "KinoBonus.PlayTest";
        const string Folder = KinoGameplaySetup.BonusOutput;
        static int stage, eventCount, lastAward;
        static Catchable.BallType lastType;
        static double next;
        static GameObject physicalBall, hand;

        static KinoBonusTests()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode) { stage = eventCount = 0; next = Time.timeAsDouble + .5; }
            };
        }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        public static void ValidateRulesAndAssets()
        {
            var state = new KinoRoundState();
            state.Begin(10, 100, 2, 5);
            Check(state.TryCatch(7, 101) && state.Score == 1, "Ordinary catch changed.");
            Check(state.TryCatch(7, 102, true) && state.Score == 4 && state.CatchCount == 2 && state.UniqueCount == 1, "Bonus/repeat must award +3 but count once.");
            Check(state.TryCatch(80, 110, true) && state.Phase == KinoRoundPhase.Calm && state.Score == 7, "Calm bonus must be +3.");
            Check(state.TryCatch(80, 112, true) && state.Phase == KinoRoundPhase.Boost && state.Score == 16, "BOOST bonus must be +9 at boundary.");
            Check(state.TryCatch(1, 113) && state.Score == 19, "Ordinary BOOST must stay +3.");
            Check(!state.TryCatch(0, 114, true) && !state.TryCatch(81, 114, true) && state.Score == 19, "Invalid bonus changed score.");
            Check(!state.TryCatch(7, 117, true) && state.Score == 19 && state.CatchCount == 5, "Bonus accepted at deadline.");
            state.Begin(1, 200);
            Check(state.Score == 0 && state.CatchCount == 0 && state.UniqueCount == 0, "Restart failed.");
            state.Stop();
            Check(!state.TryCatch(1, 200, true), "Stopped round accepted bonus.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KINOVR/Prefabs/NumberedBall.prefab");
            var visual = prefab.GetComponent<KinoBallNumber>();
            Check(visual.kinoBonusMaterial && visual.kinoBonusMaterial.GetFloat("_BonusRed") == 1, "Missing red material.");
            Check(!ShaderUtil.ShaderHasError(visual.kinoBonusMaterial.shader), "Bonus shader failed.");
            var catchable = prefab.GetComponent<Catchable>();
            Check(catchable.kinoBonusCatchVFX && catchable.kinoBonusCatchVFX != catchable.catchVFX, "Bonus effect must be separate.");
            var color = catchable.kinoBonusCatchVFX.GetComponent<ParticleSystem>().main.startColor.color;
            Check(color.r > .9f && color.g < .1f && color.b < .1f, "Bonus particles are not red.");
            Check(catchable.catchVFX.GetComponent<ParticleSystem>().main.startColor.color.g > .5f, "Ordinary particles were recolored.");
            var board = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab").GetComponentInChildren<KinoNumberBoard>(true);
            Check(board.kinoBonusMarkerMaterial && board.kinoBonusMarkerMaterial.GetFloat("_BonusRed") == 1, "Missing saved red board marker.");
            Check(!ShaderUtil.ShaderHasError(board.kinoBonusMarkerMaterial.shader), "Board bonus shader failed.");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/rules.txt", "PASS: normal +1, bonus +3, calm +3, BOOST bonus +9, normal BOOST +3, repeats/count, invalid numbers, exact deadline, restart, stop, saved red material and red effect.\n");
        }

        [MenuItem("Tools/KINO VR/KINO Bonus/2 - Test bonus scoring and pool")]
        public static void Run()
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play first.");
            ValidateRulesAndAssets();
            File.WriteAllText(Folder + "/play-tests.txt", "RUNNING");
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        static GameObject Spawn(KinoRoundController round, bool bonus, int number = 7)
        {
            var ball = round.launcher.SpawnBall();
            Check(ball, "Spawn failed.");
            var catchable = ball.GetComponent<Catchable>();
            var visual = ball.GetComponent<KinoBallNumber>();
            bool spawnedBonus = catchable.IsKinoBonus;
            Check(catchable.Number >= 1 && catchable.Number <= 80 && visual.numberLabel.text == catchable.Number.ToString(), "Invalid/mismatched number.");
            Check(visual.numberLabel.color == (spawnedBonus ? Color.white : Color.black), "Label color leaked across lease.");
            Check(ball.GetComponent<MeshRenderer>().sharedMaterial == (spawnedBonus ? visual.kinoBonusMaterial : round.launcher.ballPrefab.GetComponent<MeshRenderer>().sharedMaterial), "Material leaked across lease.");
            // A repeat number exercises the board upgrade through the real catch path.
            catchable.Configure(number, round, bonus);
            visual.SetBonus(bonus);
            visual.SetNumber(number, round.launcher.player);
            return ball;
        }
        static void CheckMarker(KinoNumberBoard board, int number, bool bonus)
        {
            var marker = board.caughtMarkers[number - 1];
            Check(marker.activeSelf, "Caught marker is hidden.");
            var material = marker.GetComponent<UnityEngine.UI.Graphic>().material;
            Check(bonus ? material == board.kinoBonusMarkerMaterial : material != board.kinoBonusMarkerMaterial, "Wrong marker palette for number " + number);
            Check(board.numberLabels[number - 1].color == (bonus ? board.bonusCaughtColor : board.caughtColor), "Wrong board number contrast.");
        }
        static void Catch(KinoRoundController round, GameObject ball, int score, int count, int award)
        {
            var catchable = ball.GetComponent<Catchable>();
            int number = catchable.Number;
            bool bonus = catchable.IsKinoBonus;
            int eventsBefore = eventCount;
            catchable.Catch(); catchable.Catch();
            Check(round.State.Score == score && round.score.CurrentScore == score && round.State.CatchCount == count, "Score/count/double catch mismatch.");
            Check(eventCount == eventsBefore + 1 && lastAward == award && lastType == (bonus ? Catchable.BallType.KinoBonus : Catchable.BallType.Normal), "Wrong score event amount/type/count.");
            Check(round.board.caughtMarkers[number - 1].activeSelf && round.board.catchCountText.text == score.ToString("000"), "Board did not update.");
            Check(!ball.activeSelf && !catchable.IsKinoBonus, "Pool did not reset bonus type.");
            string effectName = bonus ? "KinoBonusCatch(Clone)" : "Explosion(Clone)";
            Check(Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Any(p => p.name == effectName), "Catch did not emit the correct effect.");
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || Time.timeAsDouble < next) return;
            try
            {
                var round = Object.FindFirstObjectByType<KinoRoundController>();
                var launcher = round.launcher;
                if (stage == 0)
                {
                    round.enableBoostRound = false;
                    round.BeginRound(60); launcher.StopLaunching(true);
                    round.score.onBallCaught ??= new UnityEngine.Events.UnityEvent<int, Catchable.BallType>();
                    round.score.onBallCaught.AddListener((points, type) => { eventCount++; lastAward = points; lastType = type; });
                    Catch(round, Spawn(round, false), 1, 1, 1);
                    CheckMarker(round.board, 7, false);
                    var bonus = Spawn(round, true);
                    uint oldGeneration = bonus.GetComponent<KinoPooledBall>().Generation;
                    Catch(round, bonus, 4, 2, 3);
                    CheckMarker(round.board, 7, true);
                    var normal = Spawn(round, false);
                    Check(normal == bonus, "Test did not reuse bonus object.");
                    normal.GetComponent<KinoPooledBall>().ReturnToPool(oldGeneration);
                    Check(normal.activeSelf, "Stale bonus lease removed ordinary lease.");
                    Catch(round, normal, 5, 3, 1);
                    CheckMarker(round.board, 7, true);
                    Catch(round, Spawn(round, true, 80), 8, 4, 3);
                    CheckMarker(round.board, 80, true);
                    round.board.SetBoostColors(true); round.board.SetBoostColors(false);
                    CheckMarker(round.board, 7, true);
                    // A full pool must defer, rather than consume, the round's guaranteed bonus.
                    var fullPool = Enumerable.Range(0, launcher.PoolCount).Select(_ => launcher.SpawnBall()).ToArray();
                    launcher.PrepareRoundBonus(0);
                    Check(!launcher.SpawnBall() && !launcher.HasLaunchedKinoBonus, "Pool exhaustion consumed the bonus.");
                    var freeSlot = fullPool[0].GetComponent<KinoPooledBall>();
                    freeSlot.ReturnToPool(freeSlot.Generation);
                    var guaranteed = launcher.SpawnBall();
                    Check(guaranteed.GetComponent<Catchable>().IsKinoBonus && launcher.HasLaunchedKinoBonus, "Guaranteed bonus was not emitted.");
                    launcher.StopLaunching(true); // Missing/clearing the bonus still consumes its one appearance.
                    var randomState = Random.state;
                    try
                    {
                        Random.InitState(91726);
                        for (int i = 0; i < 2000; i++)
                        {
                            var sample = launcher.SpawnBall();
                            Check(!sample.GetComponent<Catchable>().IsKinoBonus, "A second bonus appeared in the same round.");
                            var pooled = sample.GetComponent<KinoPooledBall>();
                            pooled.ReturnToPool(pooled.Generation);
                        }
                    }
                    finally { Random.state = randomState; }
                    Check(launcher.PoolCount == 16 && launcher.ActiveBallCount == 0, "Sampling grew/leaked pool.");
                    physicalBall = Spawn(round, true);
                    var body = physicalBall.GetComponent<Rigidbody>();
                    physicalBall.GetComponent<KinoPooledBall>().StopAirflow();
                    body.useGravity = false; body.linearVelocity = Vector3.zero;
                    body.position = new Vector3(0, 30, 0);
                    hand = new GameObject("Bonus physical hand trigger");
                    hand.transform.position = body.position;
                    hand.AddComponent<HandCatcher>();
                    hand.AddComponent<SphereCollider>().isTrigger = true;
                    Physics.SyncTransforms();
                    stage = 1; next = Time.timeAsDouble + .25;
                }
                else if (stage == 1)
                {
                    Check(!physicalBall.activeSelf && round.State.Score == 11 && round.State.CatchCount == 5, "Physical hand did not award +3 once.");
                    Object.Destroy(hand);
                    // Enter BOOST via the state clock, then run the real controller/catch path.
                    round.State.Begin(1, Time.timeAsDouble - 2, 0, 30);
                    round.score.ResetScore(); round.board.ResetBoard(); round.RefreshClock();
                    Catch(round, Spawn(round, true), 9, 1, 9);
                    Catch(round, Spawn(round, false), 12, 2, 3);
                    CheckMarker(round.board, 7, true);
                    physicalBall = Spawn(round, true);
                    launcher.ballLifetime = 1;
                    physicalBall.GetComponent<KinoPooledBall>().ReturnToPool(physicalBall.GetComponent<KinoPooledBall>().Generation);
                    physicalBall = Spawn(round, true);
                    stage = 2; next = Time.timeAsDouble + 1.15;
                }
                else if (stage == 2)
                {
                    Check(!physicalBall.activeSelf && !physicalBall.GetComponent<Catchable>().IsKinoBonus, "Expired bonus did not reset.");
                    round.FinishRound();
                    CheckMarker(round.board, 7, true);
                    Check(!round.TryCatch(80, Catchable.BallType.KinoBonus) && !round.board.caughtMarkers[79].activeSelf, "Rejected catch recolored board.");
                    round.BeginRound(10); launcher.StopLaunching(true);
                    Check(round.State.Score == 0 && round.score.CurrentScore == 0, "Restart did not reset score.");
                    Check(!launcher.HasLaunchedKinoBonus, "Restart did not rearm the one bonus.");
                    Check(round.board.caughtMarkers.All(m => !m.activeSelf && m.GetComponent<UnityEngine.UI.Graphic>().material != round.board.kinoBonusMarkerMaterial), "Restart retained bonus markers.");
                    physicalBall = Spawn(round, true);
                    round.FinishRound(); physicalBall.GetComponent<Catchable>().Catch();
                    Check(!physicalBall.activeSelf && launcher.ActiveBallCount == 0 && round.State.Score == 0 && !launcher.SpawnBall(), "Finish accepted bonus or leaked pool.");
                    round.BeginRound(10); launcher.StopLaunching(true);
                    Catch(round, Spawn(round, false), 1, 1, 1);
                    CheckMarker(round.board, 7, false);
                    launcher.ballLifetime = 6;
                    round.BeginRound(2);
                    launcher.minSpawnInterval = launcher.maxSpawnInterval = 10;
                    launcher.SetPace(1, 1, 1);
                    stage = 3; next = Time.timeAsDouble + .25;
                }
                else if (stage == 3)
                {
                    Check(launcher.HasLaunchedKinoBonus && launcher.ActiveBallCount == 1, "Scheduled bonus did not launch automatically before a long ordinary interval.");
                    Check(Object.FindObjectsByType<Catchable>(FindObjectsSortMode.None).Count(c => c.IsKinoBonus) == 1, "Automatic bonus count is not one.");
                    stage = 4; next = Time.timeAsDouble + 2;
                }
                else if (stage == 4)
                {
                    Check(!round.IsRunning && launcher.HasLaunchedKinoBonus && launcher.ActiveBallCount == 0, "Short round did not finish with exactly one bonus appearance.");
                    File.WriteAllText(Folder + "/play-tests.txt", "PASS: exactly one bonus, exhausted pool defers bonus, 2000 subsequent launches contain no bonus even after a miss, restart rearms bonus, automatic guaranteed launch despite long ordinary interval, numbered red/white and gold/black visuals, +3/+9 bonus scoring, +1/+3 ordinary scoring, events, board, physical hand, pool reuse, stale lease, expiry and finish.\n");
                    File.AppendAllText(Folder + "/play-tests.txt", "PASS: board first bonus red, ordinary yellow -> bonus red, ordinary repeat retains red, white numbers on red, BOOST/finish retain red, rejected catch leaves board unchanged, restart clears markers and restores yellow on next ordinary catch.\n");
                    SessionState.SetBool(Key, false);
                    Debug.Log("KINO_BONUS_PLAY_TEST_PASSED");
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception e)
            {
                File.WriteAllText(Folder + "/play-tests.txt", e.ToString());
                SessionState.SetBool(Key, false);
                Debug.LogException(e);
                EditorApplication.isPlaying = false;
            }
        }
    }
}
