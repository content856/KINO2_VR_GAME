using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;
using Object = UnityEngine.Object;

namespace KinoVR.Editor
{
    // Runs the real backend coordinator against a deterministic, in-memory API.
    // No paired device credentials, remote endpoints or existing score journals are used.
    [InitializeOnLoad]
    public static class KinoBackendTests
    {
        const string Key = "KINO.BackendTests";
        const string Output = "Artifacts/KinoBackend";
        static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        static readonly List<string> passed = new List<string>();
        static double deadline;
        static bool started;

        [Serializable] sealed class SavedScene { public string path; public bool loaded, active; }
        [Serializable] sealed class SceneBackup { public SavedScene[] scenes; }

        static KinoBackendTests()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                { started = false; deadline = EditorApplication.timeSinceStartup + 90; }
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".OfflineRegression", false))
                {
                    SessionState.SetBool(Key + ".OfflineRegression", false);
                    RestoreOfflineBuildProfile();
                    EditorApplication.Exit(SessionState.GetBool("KINO.ExperienceTestPassed", false) ? 0 : 1);
                    return;
                }
                if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + ".Restore", false)) return;
                SessionState.SetBool(Key + ".Restore", false);
                var backup = JsonUtility.FromJson<SceneBackup>(SessionState.GetString(Key + ".Scenes", "{}"));
                if (backup.scenes != null && backup.scenes.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(backup.scenes.Select(scene => new SceneSetup
                        { path = scene.path, isLoaded = scene.loaded, isActive = scene.active }).ToArray());
                RestoreXR();
                if (SessionState.GetBool(Key + ".Exit", false))
                {
                    SessionState.SetBool(Key + ".Exit", false);
                    EditorApplication.Exit(SessionState.GetBool(Key + ".Passed", false) ? 0 : 1);
                }
            };
        }

        [MenuItem("Tools/KINO VR/Backend/Test visitor and score lifecycle")]
        public static void Run() => Begin(false);
        public static void RunBatch() => Begin(true);

        // Exercise the existing full gameplay suite using the project's base settings.
        // The saved Quest development profile has unrelated legacy branding overrides.
        public static void RunOfflineExperienceBatch()
        {
            var previous = BuildProfile.GetActiveBuildProfile();
            SessionState.SetString(Key + ".OfflineProfile", previous ? AssetDatabase.GetAssetPath(previous) : "");
            SessionState.SetBool(Key + ".OfflineRegression", true);
            try
            {
                BuildProfile.SetActiveBuildProfile(null);
                KinoExperienceTests.Run();
            }
            catch (Exception error)
            {
                SessionState.SetBool(Key + ".OfflineRegression", false);
                RestoreOfflineBuildProfile();
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        static void RestoreOfflineBuildProfile()
        {
            string path = SessionState.GetString(Key + ".OfflineProfile", "");
            BuildProfile.SetActiveBuildProfile(string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<BuildProfile>(path));
        }

        static void Begin(bool exit)
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play before running backend tests.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                Check(!EditorSceneManager.GetSceneAt(i).isDirty, "Save open scenes before running backend tests.");
            SessionState.SetString(Key + ".Scenes", JsonUtility.ToJson(new SceneBackup
            {
                scenes = EditorSceneManager.GetSceneManagerSetup().Select(scene => new SavedScene
                    { path = scene.path, loaded = scene.isLoaded, active = scene.isActive }).ToArray()
            }));
            SessionState.SetBool(Key + ".Restore", true);
            SessionState.SetBool(Key + ".Exit", exit);
            SessionState.SetBool(Key + ".Passed", false);
            SessionState.SetBool(Key, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            DisableXR();
            EditorApplication.isPlaying = true;
        }

        static void DisableXR()
        {
            var active = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            SessionState.SetInt(Key + ".XRGroup", (int)active);
            foreach (var group in new[] { BuildTargetGroup.Standalone, active }.Distinct())
            {
                var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
                if (!settings) continue;
                SessionState.SetBool(Key + ".XR" + (int)group, settings.InitManagerOnStart);
                settings.InitManagerOnStart = false;
            }
        }

        static void RestoreXR()
        {
            var active = (BuildTargetGroup)SessionState.GetInt(Key + ".XRGroup", (int)BuildTargetGroup.Standalone);
            foreach (var group in new[] { BuildTargetGroup.Standalone, active }.Distinct())
            {
                var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
                if (settings) settings.InitManagerOnStart = SessionState.GetBool(Key + ".XR" + (int)group, true);
            }
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline, "Backend lifecycle tests timed out.");
                if (!started)
                {
                    started = true; passed.Clear(); routines.Clear();
                    Directory.CreateDirectory(Output);
                    File.WriteAllText(Output + "/request-trace.txt", "Synthetic test requests only.\n");
                    routines.Push(Suite());
                }
                while (routines.Count > 0)
                {
                    var current = routines.Peek();
                    if (!current.MoveNext()) { (current as IDisposable)?.Dispose(); routines.Pop(); continue; }
                    if (current.Current is IEnumerator nested) { routines.Push(nested); continue; }
                    return;
                }
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        static void Finish(Exception error)
        {
            while (routines.Count > 0) (routines.Pop() as IDisposable)?.Dispose();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/lifecycle-tests.txt", error == null
                ? "PASS\n" + string.Join("\n", passed) + "\n"
                : "FAIL\n" + error + "\nPassed before failure:\n" + string.Join("\n", passed));
            if (error == null) Debug.Log("[KINO backend tests] PASS: " + string.Join("; ", passed));
            else Debug.LogException(error);
            SessionState.SetBool(Key + ".Passed", error == null);
            SessionState.SetBool(Key, false);
            EditorApplication.isPlaying = false;
        }

        static IEnumerator Suite()
        {
            KinoExperienceTests.ValidateState();
            passed.Add("Existing Normal/Boost experience state transitions and headset-remount baseline.");
            KinoApiClientTests.Validate();
            passed.Add("API authentication, URL isolation, retry classification, expiry and secure storage recovery rules.");
            JournalSurvivesRestart();
            yield return SingleCampaignIsAutomatic();
            yield return VisitorAndScoreLifecycle();
            yield return UnmountDuringStart();
            yield return TerminalScoreRejection();
            yield return RateLimitRetryFloor();
            yield return RecoverPendingScoreAfterRestart();
            yield return ExperiencePrefabVisitorLoop();
        }

        static void JournalSurvivesRestart()
        {
            string directory = NewStoragePath();
            const string endpoint = "https://isolated-tests.invalid/api/v1";
            var original = new KinoBackendTransaction
            {
                campaignId = "campaign-one", storeId = "store-one", registrationId = "reg_one",
                gameSessionId = "session-one", submissionId = Guid.NewGuid().ToString("D"), score = 73, pendingScore = true
            };
            var writer = new KinoBackendJournal(directory, endpoint);
            for (int i = 0; i < 32; i++) { original.score = i; writer.Save(original); }
            original.score = 73; writer.Save(original);
            var afterRestart = new KinoBackendJournal(directory, endpoint).Load();
            Check(afterRestart.submissionId == original.submissionId && afterRestart.score == 73 && afterRestart.pendingScore &&
                afterRestart.gameSessionId == original.gameSessionId, "Restart lost the pending score identity or body.");
            Check(new KinoBackendJournal(directory, "https://another-environment.invalid/api/v1").Load() == null,
                "A different backend environment reused another environment's pending score.");
            var journal = new KinoBackendJournal(directory, endpoint);
            journal.Archive(afterRestart);
            Check(journal.Load() == null && Directory.GetFiles(directory, "receipt-*.json", SearchOption.AllDirectories).Length == 1,
                "Archiving a resolved result lost its receipt or retained an active transaction.");
            var fresh = new KinoBackendJournal(directory, endpoint);
            Check(fresh.Load() == null, "A resolved receipt resurrected an older pending score after restart.");
            original.score = 99; fresh.Save(original);
            Check(new KinoBackendJournal(directory, endpoint).Load().score == 99, "A new visitor could not save after a resolved receipt.");
            string latest = Directory.GetFiles(directory, "entry-*.json", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).Last();
            File.WriteAllText(latest + ".tmp", "incomplete write");
            Check(new KinoBackendJournal(directory, endpoint).Load().score == 99, "An uncommitted temporary file was treated as a score.");
            File.WriteAllText(latest, "{}");
            bool corruptBlocked = false;
            try { new KinoBackendJournal(directory, endpoint).Load(); }
            catch (IOException) { corruptBlocked = true; }
            Check(corruptBlocked, "A corrupt latest entry silently fell back to an older transaction.");
            passed.Add("32 immutable journal updates survive reload, isolate environments, retain receipts, allow a new result after receipt, ignore uncommitted files and block corrupt latest entries.");
        }

        static IEnumerator VisitorAndScoreLifecycle()
        {
            using (var fixture = new Fixture())
            {
                yield return SelectStore(fixture);
                yield return SelectPlayer(fixture, "reg_one");
                fixture.api.Enqueue("/game-sessions/complete", Reply.Error(503, "SERVICE_UNAVAILABLE", true));
                var record = new KinoSessionRecord { sessionId = "local-one", score = 73, completedUtc = DateTime.UtcNow.ToString("o") };
                fixture.flow.BindRecord(record);
                fixture.flow.SubmitResult(record);
                yield return Until(() => fixture.api.Completed("/game-sessions/complete") == 1 && !fixture.flow.Busy, "first score submission");
                Check(fixture.flow.HasUnresolvedResult, "A failed score upload was discarded.");
                int ready = fixture.ready, starts = fixture.api.Count("/game-sessions/start");
                fixture.flow.EnterStep(KinoSequenceStep.PlayerSelection, () => fixture.ready++);
                fixture.flow.ChoosePlayer("reg_two");
                yield return Frames(3);
                Check(fixture.api.Count("/game-sessions/start") == starts && fixture.ready == ready,
                    "A pending score allowed a new session, which can abandon the previous one on the server.");
                fixture.flow.Retry();
                yield return Until(() => !fixture.flow.HasUnresolvedResult, "score retry acceptance");
                var uploads = fixture.api.calls.Where(call => call.path == "/game-sessions/complete").ToArray();
                Check(uploads.Length == 2 && uploads[0].body == uploads[1].body,
                    "Retry changed the score, session or idempotency submission_id.");
                Check(!string.IsNullOrEmpty(JsonUtility.FromJson<SubmissionBody>(uploads[0].body).submission_id), "No stable submission_id.");
                yield return SelectPlayer(fixture, "reg_two");
                Check(fixture.flow.StoreReady && fixture.api.Count("/device/session-store") == 1 &&
                    fixture.api.Count("/device/selectable-stores") == 1, "Next visitor repeated campaign/region/store setup.");
                Check(fixture.flow.ActiveSession.registration_id == "reg_two", "Previous visitor's session leaked to the new player.");
                fixture.flow.SuspendVisitor();
                yield return Until(() => fixture.api.Completed("/game-sessions/session-two/abandon") == 1, "visitor abandonment");
                Check(!fixture.flow.PlayerReady && fixture.flow.StoreReady, "Headset removal lost the store or retained the old visitor.");
                fixture.api.AssertNoUnexpectedCalls();
            }
            passed.Add("Campaign/region/store selected once; separate session per visitor; pending score blocks start; retry preserves submission body; unmount abandons visitor and retains store.");
        }

        static IEnumerator SingleCampaignIsAutomatic()
        {
            using (var fixture = new Fixture())
            {
                fixture.api.Enqueue("/device/selectable-stores", new Reply
                {
                    json = "{\"campaigns\":[{\"id\":\"campaign-one\",\"name\":\"Only campaign\",\"stores\":[{\"id\":\"store-one\",\"name\":\"Athens\",\"region\":\"Attica\",\"has_open_event\":true}]}]}"
                });
                int selected = 0;
                fixture.flow.EnterStep(KinoSequenceStep.CampaignSelection, () => selected++);
                yield return Until(() => selected == 1, "automatic single campaign selection");
                Check(fixture.flow.SelectedCampaignId == "campaign-one" && !fixture.flow.StoreReady,
                    "A single campaign must be automatic while store selection remains required.");
                fixture.api.AssertNoUnexpectedCalls();
            }
            passed.Add("A single available campaign is selected automatically without skipping required store setup.");
        }

        static IEnumerator UnmountDuringStart()
        {
            using (var fixture = new Fixture())
            {
                yield return SelectStore(fixture);
                fixture.api.holdStart = true;
                fixture.flow.EnterStep(KinoSequenceStep.PlayerSelection, () => fixture.ready++);
                yield return Until(() => fixture.api.Completed("/players") > 0 && !fixture.flow.Busy, "available players");
                fixture.flow.ChoosePlayer("reg_one");
                yield return Until(() => fixture.api.Count("/game-sessions/start") == 1, "delayed start request");
                fixture.flow.SuspendVisitor();
                fixture.api.holdStart = false;
                yield return Until(() => fixture.api.Completed("/game-sessions/start") == 1 && !fixture.flow.Busy, "late start response");
                Check(fixture.ready == 0 && !fixture.flow.PlayerReady, "A late response after headset removal resumed Ready.");
                fixture.flow.EnterStep(KinoSequenceStep.PlayerSelection, () => fixture.ready++);
                yield return Until(() => fixture.api.Completed("/game-sessions/session-one/abandon") == 1, "late session abandonment");
                Check(fixture.ready == 0 && !fixture.flow.PlayerReady && fixture.flow.StoreReady,
                    "A late response after headset removal resumed Ready or lost the selected store.");
                fixture.api.AssertNoUnexpectedCalls();
            }
            passed.Add("Headset removal during start suppresses the stale Ready callback and abandons the late server session.");
        }

        static IEnumerator TerminalScoreRejection()
        {
            foreach (var rejection in new[] { Reply.Error(409, "EVENT_CLOSED", false), Reply.Error(422, "SCORE_OUT_OF_RANGE", false) })
            {
                using (var fixture = new Fixture())
                {
                    yield return SelectStore(fixture);
                    yield return SelectPlayer(fixture, "reg_one");
                    fixture.api.Enqueue("/game-sessions/complete", rejection);
                    var record = new KinoSessionRecord { sessionId = "local-rejected", score = 91, completedUtc = DateTime.UtcNow.ToString("o") };
                    fixture.flow.BindRecord(record); fixture.flow.SubmitResult(record);
                    yield return Until(() => fixture.api.Completed("/game-sessions/complete") == 1 && !fixture.flow.Busy, "terminal score response");
                    Check(fixture.flow.HasUnresolvedResult, "Terminal rejection was silently discarded.");
                    fixture.flow.EnterStep(KinoSequenceStep.PlayerSelection, () => fixture.ready++);
                    fixture.flow.Retry();
                    yield return Frames(4);
                    Check(fixture.api.Count("/game-sessions/complete") == 1, "A terminal 4xx score response was retried unchanged.");
                    fixture.flow.AcknowledgeRejectedResult();
                    yield return Until(() => !fixture.flow.HasUnresolvedResult, "operator acknowledgement");
                    fixture.api.AssertNoUnexpectedCalls();
                }
            }
            passed.Add("409 EVENT_CLOSED and 422 SCORE_OUT_OF_RANGE stay blocked until explicit acknowledgement and never retry unchanged.");
        }

        static IEnumerator RecoverPendingScoreAfterRestart()
        {
            string directory = NewStoragePath();
            string firstBody;
            using (var before = new Fixture(directory))
            {
                yield return SelectStore(before);
                yield return SelectPlayer(before, "reg_one");
                before.api.Enqueue("/game-sessions/complete", Reply.Error(0, "NETWORK_ERROR", true));
                var record = new KinoSessionRecord { sessionId = "local-restart", score = 73, completedUtc = DateTime.UtcNow.ToString("o") };
                before.flow.BindRecord(record); before.flow.SubmitResult(record);
                yield return Until(() => before.api.Completed("/game-sessions/complete") == 1 && !before.flow.Busy, "disconnected score submission");
                firstBody = before.api.calls.Single(call => call.path == "/game-sessions/complete").body;
            }
            using (var after = new Fixture(directory))
            {
                Check(after.flow.HasUnresolvedResult, "Restart did not load the unresolved score.");
                int selected = 0;
                after.flow.EnterStep(KinoSequenceStep.CampaignSelection, () => selected++);
                yield return Until(() => after.api.Completed("/game-sessions/complete") == 1 && !after.flow.HasUnresolvedResult, "persisted score replay");
                Check(after.api.calls.Single(call => call.path == "/game-sessions/complete").body == firstBody,
                    "Restart replay changed the original submission_id or score body.");
                Check(!after.flow.StoreReady && selected == 0 && after.api.Count("/game-sessions/start") == 0,
                    "Restart replay silently reused a store or started a new visitor before setup.");
                after.api.AssertNoUnexpectedCalls();
            }
            passed.Add("Restart replays a pending score with the identical submission_id and still requires fresh store setup before another visitor.");
        }

        static IEnumerator RateLimitRetryFloor()
        {
            using (var fixture = new Fixture())
            {
                yield return SelectStore(fixture);
                yield return SelectPlayer(fixture, "reg_one");
                var limit = Reply.Error(429, "RATE_LIMITED", true); limit.retryAfter = 1.5;
                fixture.api.Enqueue("/game-sessions/complete", limit);
                var record = new KinoSessionRecord { sessionId = "local-rate-limit", score = 73, completedUtc = DateTime.UtcNow.ToString("o") };
                fixture.flow.BindRecord(record); fixture.flow.SubmitResult(record);
                yield return Until(() => fixture.api.Completed("/game-sessions/complete") == 1 && !fixture.flow.Busy, "rate limit response");
                fixture.flow.SuspendVisitor();
                fixture.flow.EnterStep(KinoSequenceStep.PlayerSelection, () => fixture.ready++);
                fixture.flow.Retry();
                double startedWaiting = Time.realtimeSinceStartupAsDouble;
                while (Time.realtimeSinceStartupAsDouble - startedWaiting < .75)
                {
                    Check(fixture.api.Count("/game-sessions/complete") == 1, "Retry bypassed the backend Retry-After delay.");
                    yield return null;
                }
                yield return Until(() => !fixture.flow.HasUnresolvedResult, "rate limit recovery");
                Check(fixture.api.Count("/game-sessions/complete") == 2, "Rate limit recovery submitted more than once.");
                fixture.api.AssertNoUnexpectedCalls();
            }
            passed.Add("Manual Retry respects the server's Retry-After floor before resending the durable score.");
        }

        static IEnumerator ExperiencePrefabVisitorLoop()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KINOVR/Prefabs/KinoTimedGameplay.prefab");
            Check(prefab != null, "The gameplay prefab is missing.");
            var host = new GameObject("Isolated experience prefab test");
            host.SetActive(false);
            try
            {
                var instance = Object.Instantiate(prefab, host.transform);
                var experience = instance.GetComponentInChildren<KinoExperienceController>(true);
                Check(experience != null && experience.round && experience.contentCanvas && experience.footer,
                    "The actual gameplay prefab is missing its Experience Sequence or screen references.");
                experience.useLeaderboard = true;
                experience.writeLocalRecords = false;
                experience.brandingSeconds = .1f; experience.logoFadeSeconds = .02f;
                experience.offerBoostSelection = false;
                var api = experience.gameObject.AddComponent<KinoBackendTestApi>();
                var flow = experience.gameObject.AddComponent<KinoBackendFlow>();
                flow.Initialize(experience, api, NewStoragePath());
                host.SetActive(true);
                yield return Until(() => experience.Stage == KinoExperienceStage.CampaignSelection &&
                    api.Completed("/device/selectable-stores") == 1 && !flow.Busy, "prefab startup campaign screen");
                flow.ChooseCampaign("campaign-one");
                yield return Until(() => experience.Stage == KinoExperienceStage.RegionSelection, "prefab region screen");
                flow.ChooseRegion("Attica");
                yield return Until(() => experience.Stage == KinoExperienceStage.StoreSelection, "prefab store screen");
                flow.ChooseStore("store-one");
                yield return Until(() => experience.Stage == KinoExperienceStage.PlayerSelection && flow.Players.Length > 0 && !flow.Busy,
                    "prefab first visitor screen");
                flow.ChoosePlayer("reg_one");
                yield return Until(() => experience.Stage == KinoExperienceStage.Standby && flow.PlayerReady && !flow.Busy, "prefab Ready");
                experience.useLeaderboard = false;
                Check(experience.LeaderboardEnabled, "Changing an Inspector field detached an active backend session at runtime.");
                experience.useLeaderboard = true;
                Check(experience.footer.text.Contains(flow.ActiveSession.nickname), "Ready does not show the selected player's nickname.");
                Check(experience.Record == null && !experience.round.IsRunning, "Choosing a player started gameplay before Ready.");
                experience.SendMessage("Unmounted");
                Check(experience.Stage == KinoExperienceStage.Waiting, "Headset removal did not suspend the actual experience.");
                experience.SendMessage("Mounted");
                yield return Until(() => experience.Stage == KinoExperienceStage.PlayerSelection && flow.Players.Length > 0 && !flow.Busy,
                    "prefab remount visitor screen");
                Check(flow.StoreReady && api.Count("/device/session-store") == 1 && api.Count("/device/selectable-stores") == 1,
                    "The actual experience repeated store setup after remount.");
                Check(!flow.PlayerReady && api.Completed("/game-sessions/session-one/abandon") == 1,
                    "The actual experience retained the previous visitor after remount.");
                flow.ChoosePlayer("reg_two");
                yield return Until(() => experience.Stage == KinoExperienceStage.Standby && flow.PlayerReady && !flow.Busy, "second visitor Ready");
                api.holdReady = true;
                experience.StartSelectedSession();
                yield return Until(() => api.Count("/game-sessions/session-two") == 1, "Ready reservation verification");
                experience.SendMessage("Unmounted");
                api.holdReady = false;
                yield return Until(() => api.Completed("/game-sessions/session-two") == 1 && !flow.Busy, "late Ready verification");
                Check(experience.Stage == KinoExperienceStage.Waiting && experience.Record == null && !experience.round.IsRunning,
                    "A stale Ready verification started a local game after headset removal.");
                api.AssertNoUnexpectedCalls();
            }
            finally { if (host) Object.DestroyImmediate(host); }
            passed.Add("Actual gameplay prefab runs campaign → region → store → player → Ready, shows nickname, returns only to player on remount, and ignores stale Ready verification.");
        }

        static IEnumerator SelectStore(Fixture fixture)
        {
            int campaigns = 0, regions = 0, stores = 0;
            fixture.flow.EnterStep(KinoSequenceStep.CampaignSelection, () => campaigns++);
            yield return Until(() => fixture.api.Completed("/device/selectable-stores") > 0 && !fixture.flow.Busy, "campaign list");
            Check(campaigns == 0, "Multiple campaigns were selected without an operator choice.");
            fixture.flow.ChooseCampaign("campaign-one");
            yield return Until(() => campaigns == 1, "campaign selection");
            fixture.flow.EnterStep(KinoSequenceStep.RegionSelection, () => regions++);
            fixture.flow.ChooseRegion("Attica");
            yield return Until(() => regions == 1, "region selection");
            fixture.flow.EnterStep(KinoSequenceStep.StoreSelection, () => stores++);
            fixture.flow.ChooseStore("store-one");
            yield return Until(() => stores == 1 && fixture.flow.StoreReady && !fixture.flow.Busy, "store selection");
            var body = JsonUtility.FromJson<SelectionBody>(fixture.api.calls.Single(call => call.path == "/device/session-store").body);
            Check(body.campaign_id == "campaign-one" && body.store_id == "store-one", "Wrong campaign/store association sent to the API.");
        }

        static IEnumerator SelectPlayer(Fixture fixture, string id)
        {
            int previous = fixture.ready, previousLists = fixture.api.Completed("/players");
            fixture.flow.EnterStep(KinoSequenceStep.PlayerSelection, () => fixture.ready++);
            yield return Until(() => fixture.api.Completed("/players") > previousLists && !fixture.flow.Busy, "refreshed player list");
            fixture.flow.ChoosePlayer(id);
            yield return Until(() => fixture.ready == previous + 1 && fixture.flow.PlayerReady && !fixture.flow.Busy, "player reservation");
        }

        static IEnumerator Until(Func<bool> condition, string operation)
        {
            double end = EditorApplication.timeSinceStartup + 8;
            while (!condition())
            {
                if (EditorApplication.timeSinceStartup >= end)
                {
                    string storage = string.Join("; ", Object.FindObjectsByType<KinoBackendFlow>(FindObjectsSortMode.None)
                        .Where(flow => !string.IsNullOrEmpty(flow.LastStorageError)).Select(flow => flow.LastStorageError));
                    throw new InvalidOperationException("Timed out waiting for " + operation + ". " + storage);
                }
                yield return null;
            }
        }
        static IEnumerator Frames(int frames) { while (frames-- > 0) yield return null; }
        static string NewStoragePath() => Path.GetFullPath(Output + "/isolated-sessions/" + Guid.NewGuid().ToString("N"));
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        [Serializable] sealed class SelectionBody { public string campaign_id, store_id; }
        [Serializable] sealed class SubmissionBody { public string submission_id; }

        sealed class Fixture : IDisposable
        {
            readonly GameObject root;
            public readonly KinoBackendFlow flow;
            public readonly KinoBackendTestApi api;
            public int ready;
            public Fixture(string directory = null)
            {
                root = new GameObject("Isolated backend test");
                api = root.AddComponent<KinoBackendTestApi>();
                flow = root.AddComponent<KinoBackendFlow>();
                flow.Initialize(null, api, directory ?? NewStoragePath(), null);
            }
            public void Dispose()
            {
                Directory.CreateDirectory(Output);
                File.AppendAllText(Output + "/request-trace.txt", "Fixture state: busy=" + flow.Busy + ", error=" + flow.ErrorCode + ", storageError=" + flow.LastStorageError +
                    ", store=" + flow.StoreReady + ", player=" + flow.PlayerReady + ", result=" + flow.HasUnresolvedResult + "\n" +
                    string.Join("\n", api.calls.Select(call => call.method + " " + call.path + " complete=" + call.completed + " " + call.body)) + "\n\n");
                if (root) Object.DestroyImmediate(root);
            }
        }

        public sealed class Reply
        {
            public string json, code;
            public long status = 200;
            public bool retryable;
            public double retryAfter;
            public static Reply Error(long status, string code, bool retryable) => new Reply { status = status, code = code, retryable = retryable };
        }
    }

    public sealed class KinoBackendTestApi : KinoApiClient
    {
        public sealed class Call { public string method, path, body; public bool completed; }
        public readonly List<Call> calls = new List<Call>();
        readonly Dictionary<string, Queue<KinoBackendTests.Reply>> queued = new Dictionary<string, Queue<KinoBackendTests.Reply>>();
        readonly List<string> unexpected = new List<string>();
        public bool holdStart, holdReady;
        public override bool IsPaired => true;
        public int Count(string path) => calls.Count(call => call.path == path);
        public int Completed(string path) => calls.Count(call => call.path == path && call.completed);
        public void Enqueue(string path, KinoBackendTests.Reply reply)
        {
            if (!queued.TryGetValue(path, out var responses)) queued[path] = responses = new Queue<KinoBackendTests.Reply>();
            responses.Enqueue(reply);
        }
        public void AssertNoUnexpectedCalls()
        { if (unexpected.Count > 0) throw new InvalidOperationException("Unexpected API calls: " + string.Join(", ", unexpected)); }

        public override IEnumerator Request<T>(string method, string relativePath, object body, Action<KinoApiResult<T>> done)
        {
            string path = relativePath.StartsWith("/v1/") ? relativePath.Substring(3) : "/" + relativePath.TrimStart('/');
            var call = new Call { method = method, path = path, body = body == null ? "" : JsonUtility.ToJson(body) };
            calls.Add(call);
            yield return null;
            while (path == "/game-sessions/start" && holdStart) yield return null;
            while (method == "GET" && path.StartsWith("/game-sessions/") && holdReady) yield return null;
            KinoBackendTests.Reply reply;
            if (queued.TryGetValue(path, out var responses) && responses.Count > 0) reply = responses.Dequeue();
            else reply = DefaultReply(call);
            var result = new KinoApiResult<T>
            {
                Status = reply.status, Code = reply.code, Message = reply.code,
                Success = reply.status >= 200 && reply.status < 300, Retryable = reply.retryable, RetryAfterSeconds = reply.retryAfter,
                Value = string.IsNullOrEmpty(reply.json) ? null : JsonUtility.FromJson<T>(reply.json)
            };
            done(result);
            call.completed = true;
        }

        KinoBackendTests.Reply DefaultReply(Call call)
        {
            string json;
            switch (call.path)
            {
                case "/device/selectable-stores":
                    json = "{\"campaigns\":[{\"id\":\"campaign-one\",\"name\":\"Demo\",\"stores\":[{\"id\":\"store-one\",\"name\":\"Athens\",\"code\":\"ATH01\",\"city\":\"Athens\",\"region\":\"Attica\",\"has_open_event\":true},{\"id\":\"store-two\",\"name\":\"Volos\",\"region\":\"Thessaly\",\"has_open_event\":true}]},{\"id\":\"campaign-two\",\"name\":\"Second campaign\",\"stores\":[]}]}";
                    break;
                case "/device/context":
                case "/device/session-store":
                    json = "{\"device\":{\"id\":\"device-test\",\"type\":\"quest\"},\"campaign\":{\"id\":\"campaign-one\",\"name\":\"Demo\"},\"store\":{\"id\":\"store-one\",\"name\":\"Athens\",\"code\":\"ATH01\",\"region\":\"Attica\"},\"active_event\":{\"id\":\"event-one\",\"status\":\"open\"}}";
                    break;
                case "/players":
                    json = "{\"event_id\":\"event-one\",\"players\":[{\"registration_id\":\"reg_one\",\"nickname\":\"Player One\",\"status\":\"registered\"},{\"registration_id\":\"reg_two\",\"nickname\":\"Player Two\",\"status\":\"registered\"}]}";
                    break;
                case "/game-sessions/start":
                case "/game-sessions/session-one":
                case "/game-sessions/session-two":
                    bool second = call.body.Contains("reg_two") || call.path == "/game-sessions/session-two";
                    json = "{\"game_session_id\":\"session-" + (second ? "two" : "one") + "\",\"status\":\"active\",\"registration_id\":\"reg_" + (second ? "two" : "one") + "\",\"nickname\":\"Player\",\"expires_at\":\"" + DateTime.UtcNow.AddMinutes(20).ToString("o") + "\"}";
                    break;
                case "/game-sessions/complete":
                    json = "{\"success\":true,\"score_id\":\"score-one\",\"score\":73,\"game_session_id\":\"session-one\"}";
                    break;
                case "/game-sessions/session-one/abandon":
                case "/game-sessions/session-two/abandon":
                    bool abandonedSecond = call.path == "/game-sessions/session-two/abandon";
                    json = "{\"game_session_id\":\"session-" + (abandonedSecond ? "two" : "one") + "\",\"status\":\"abandoned\",\"registration_id\":\"reg_" + (abandonedSecond ? "two" : "one") + "\",\"nickname\":\"Player\"}";
                    break;
                default:
                    unexpected.Add(call.method + " " + call.path);
                    return KinoBackendTests.Reply.Error(500, "UNEXPECTED_TEST_REQUEST", false);
            }
            return new KinoBackendTests.Reply { json = json };
        }
    }
}
