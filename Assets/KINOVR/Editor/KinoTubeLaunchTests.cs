using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    [InitializeOnLoad]
    public static class KinoTubeLaunchTests
    {
        const string Key = "KinoTubeAirflow.Test";
        const string Folder = "Artifacts/KinoGameplay/TubeAirflow";
        static int stage, shot;
        static double at, started;
        static GameObject reused;
        static KinoPooledBall[] flying;
        static Vector3[] last;
        static bool[] exited;
        static float[] nearest;
        static float maxSpeed, maxStep, clearance;
        static float maxTilt;
        static double[] exitTimes;
        static bool[] accelerated;
        static readonly List<string> samples = new List<string>();

        static KinoTubeLaunchTests()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode) { stage = shot = 0; at = Time.timeAsDouble + 1; }
            };
        }
        [MenuItem("Tools/KINO VR/7 - Test tube airflow and pool")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            KinoTubeLaunchSetup.Validate();
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/result.txt", "RUNNING");
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }
        public static void RunStrongTurbulence()
        {
            SessionState.SetBool(Key + ".Strong", true);
            Run();
        }
        static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || Time.timeAsDouble < at) return;
            try
            {
                var round = Object.FindFirstObjectByType<KinoRoundController>();
                var launcher = round.launcher;
                if (stage == 0)
                {
                    round.enableMainSpecialBalls = false;
                    round.BeginRound(60); launcher.StopLaunching(true);
                    launcher.round = null; // Exercise pool/physics independently of the fixed 20-slot draw.
                    if (SessionState.GetBool(Key + ".Strong", false)) { launcher.turbulence = 1.2f; launcher.turbulenceFrequency = 1.5f; }
                    Check(launcher.PoolCount == 16 && launcher.ActiveBallCount == 0, "Incorrect prewarm.");
                    var all = new List<GameObject>();
                    for (int i = 0; i < 16; i++) all.Add(launcher.SpawnBall());
                    Check(all.All(b => b) && all.Distinct().Count() == 16, "Pool handed out duplicate/null leases.");
                    Check(!launcher.SpawnBall() && launcher.PoolCount == 16, "Exhaustion grew pool or stole a ball.");
                    launcher.StopLaunching(true);
                    var first = launcher.SpawnBall();
                    var pooled = first.GetComponent<KinoPooledBall>();
                    uint old = pooled.Generation;
                    first.GetComponent<Catchable>().Catch();
                    reused = launcher.SpawnBall();
                    Check(reused == first && reused.activeSelf, "Caught object was not reused.");
                    pooled.ReturnToPool(old);
                    Check(reused.activeSelf, "Stale lease returned a reused object.");
                    reused.GetComponent<Catchable>().Catch(); reused.GetComponent<Catchable>().Catch();
                    Check(round.score.CurrentScore == 2 && launcher.ActiveBallCount == 0, "Catch reset/double-trigger failure.");
                    launcher.ballLifetime = 1;
                    reused = launcher.SpawnBall();
                    stage++; at = Time.timeAsDouble + .7;
                }
                else if (stage == 1)
                {
                    var old = reused;
                    reused.GetComponent<Catchable>().Catch();
                    launcher.ballLifetime = 2;
                    reused = launcher.SpawnBall();
                    Check(old == reused, "Expiry test did not reuse the same object.");
                    stage++; at = Time.timeAsDouble + .6;
                }
                else if (stage == 2)
                {
                    Check(reused && reused.activeSelf, "Old deadline expired the new lease.");
                    stage++; at = Time.timeAsDouble + 1.6;
                }
                else if (stage == 3)
                {
                    Check(reused && !reused.activeSelf && launcher.ActiveBallCount == 0, "New lease did not expire into pool.");
                    launcher.ballLifetime = 6;
                    round.BeginRound(60); launcher.StopLaunching(true);
                    launcher.round = null;
                    flying = new[] { launcher.SpawnBall(0).GetComponent<KinoPooledBall>(), launcher.SpawnBall(1).GetComponent<KinoPooledBall>() };
                    last = flying.Select(b => b.transform.position).ToArray();
                    exited = new bool[2]; nearest = new[] { 100f, 100f };
                    exitTimes = new double[2]; accelerated = new bool[2]; maxTilt = 0;
                    for (int i = 0; i < 2; i++)
                        Check(Vector3.Distance(last[i], launcher.spawnPoints[i].position) < .0001f, "Spawn did not use exact marker.");
                    samples.Clear(); samples.Add("time,tube,x,y,z,rising,speed");
                    maxSpeed = maxStep = clearance = 0;
                    started = Time.timeAsDouble; stage++;
                }
                else if (stage == 4)
                {
                    double elapsed = Time.timeAsDouble - started;
                    for (int i = 0; i < 2; i++)
                    {
                        var ball = flying[i];
                        if (!ball.gameObject.activeSelf) continue;
                        Vector3 p = ball.transform.position;
                        var body = ball.GetComponent<Rigidbody>();
                        Check(float.IsFinite(p.sqrMagnitude), "Non-finite flight.");
                        float tilt = Vector3.Angle(ball.transform.up, Vector3.up);
                        maxTilt = Mathf.Max(maxTilt, tilt);
                        Check(tilt <= launcher.wobbleAngle * 1.3f + .5f, "Ball tumbled out of its level pose: " + tilt);
                        if (ball.IsRising)
                        {
                            float radial = Vector3.ProjectOnPlane(p - launcher.spawnPoints[i].position, Vector3.up).magnitude;
                            clearance = Mathf.Max(clearance, radial);
                            Check(radial < .035f, "Ball leaves tube interior during rise.");
                            Check(p.y >= last[i].y - .02f, "Rise reversed unexpectedly.");
                        }
                        else if (!exited[i])
                        {
                            Check(p.y >= launcher.exitPoints[i].position.y - .12f, "Ball turned before clearing rim.");
                            exited[i] = true;
                            exitTimes[i] = Time.timeAsDouble;
                        }
                        if (exited[i] && !accelerated[i] && Time.timeAsDouble - exitTimes[i] > .35)
                        {
                            var toward = Vector3.ProjectOnPlane(launcher.player.position - p, Vector3.up).normalized;
                            Check(Vector3.Dot(body.linearVelocity, toward) > 2.5f, "No prompt acceleration towards player after exit.");
                            accelerated[i] = true;
                        }
                        if (Vector3.Distance(p, launcher.player.position) > 1)
                        {
                            var face = ball.GetComponent<KinoBallNumber>().face;
                            Check(Vector3.Dot(face.forward, (p - launcher.player.position).normalized) > .97f, "Number stopped facing player.");
                        }
                        nearest[i] = Mathf.Min(nearest[i], Vector3.Distance(p, launcher.player.position + Vector3.up * launcher.aimHeightOffset));
                        maxStep = Mathf.Max(maxStep, Vector3.Distance(last[i], p));
                        maxSpeed = Mathf.Max(maxSpeed, body.linearVelocity.magnitude);
                        last[i] = p;
                        samples.Add(FormattableString.Invariant($"{elapsed:F4},{KinoTubeLaunchSetup.LaunchBays[i]},{p.x:F5},{p.y:F5},{p.z:F5},{ball.IsRising},{body.linearVelocity.magnitude:F4}"));
                    }
                    if (shot < 3 && elapsed >= new[] { 1f, 2.15f, 3.6f }[shot]) { Capture(shot++); }
                    if (elapsed < 6.25) return;
                    File.WriteAllLines(Folder + "/trajectories.csv", samples);
                    Check(exited.All(v => v), "A tube never released its ball.");
                    Check(accelerated.All(v => v), "Missing exit acceleration sample.");
                    Check(nearest.All(d => d < 1.1f), "Airflow missed the hand zone: " + string.Join(",", nearest));
                    Check(launcher.ActiveBallCount == 0 && launcher.PoolCount == 16, "Flight expiry leaked or grew pool.");
                    round.BeginRound(.1f); launcher.SpawnBall();
                    stage++; at = Time.timeAsDouble + .35;
                }
                else
                {
                    round.FinishRound();
                    Check(!round.IsRunning && launcher.ActiveBallCount == 0 && !launcher.SpawnBall(), "Deadline did not clear/stop spawns.");
                    round.BeginRound(10); launcher.StopLaunching(false);
                    Check(launcher.SpawnBall(), "Restart did not reactivate pool.");
                    launcher.enabled = false;
                    Check(launcher.ActiveBallCount == 0, "Disabling launcher left live balls.");
                    File.WriteAllText(Folder + "/result.txt", $"PASS: 16 prewarmed objects; exhaustion; same-object reuse; stale lease protection; double catch; old/new deadlines; exact two spawn markers; rise containment; clear rim exits; hand-zone arrival; expiry; round restart; disable.\nMax radial offset {clearance:F4} m; max speed {maxSpeed:F3} m/s; nearest hand-zone distances {string.Join(", ", nearest.Select(d => d.ToString("F3")))} m.\n");
                    File.AppendAllText(Folder + "/result.txt", $"Horizontal pose and player-facing numbers retained; max tilt {maxTilt:F2} degrees; both balls accelerate towards player within 0.35 s of exit. Turbulence {launcher.turbulence:F2}, frequency {launcher.turbulenceFrequency:F2}.\n");
                    SessionState.SetBool(Key, false);
                    SessionState.SetBool(Key + ".Strong", false);
                    Debug.Log("KINO_TUBE_AIRFLOW_POOL_TEST_PASSED");
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception e)
            {
                File.WriteAllLines(Folder + "/trajectories.csv", samples);
                File.WriteAllText(Folder + "/result.txt", e.ToString());
                SessionState.SetBool(Key, false);
                SessionState.SetBool(Key + ".Strong", false);
                Debug.LogException(e);
                EditorApplication.isPlaying = false;
            }
        }

        static void Capture(int index)
        {
            var go = new GameObject("Tube test camera");
            var camera = go.AddComponent<Camera>(); camera.enabled = false;
            camera.transform.position = new Vector3(0, 2, -4);
            camera.transform.LookAt(new Vector3(0, 3.6f, 10));
            camera.fieldOfView = 66; camera.nearClipPlane = .05f; camera.farClipPlane = 80;
            camera.allowHDR = false;
            var rt = RenderTexture.GetTemporary(1200, 800, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(1200, 800, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
                RenderTexture.active = rt; pixels.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); pixels.Apply();
                File.WriteAllBytes(Folder + "/flight-" + index + ".png", pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; camera.targetTexture = null; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(pixels); Object.DestroyImmediate(go); }
        }
    }
}
