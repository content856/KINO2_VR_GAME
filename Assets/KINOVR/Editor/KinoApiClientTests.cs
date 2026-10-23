using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KinoVR.Editor
{
    public static class KinoApiClientTests
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        [MenuItem("Tools/KINO VR/Backend/Validate API authentication rules")]
        public static void Validate()
        {
            ValidateRules();
            ValidateStorageRecovery();
            ValidateTransientReadRecovery();
            ValidateRefreshStorageFailure();
            ValidateInterruptedRefresh();
            ValidateDiagnosticPrivacy();
            Debug.Log("PASS: KINO API rules, pairing reload, read-failure recovery, resume, pre-send storage retry, interrupted rotation and diagnostic privacy. No network requests.");
        }

        public static void RunBatch()
        {
            const string report = "Artifacts/KinoBackend/authentication-tests.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            try
            {
                Validate();
                File.WriteAllText(report, "PASS\nPairing reload and endpoint isolation.\nTemporary read failure: throttled retry, automatic recovery, resume and no premature pairing.\nStorage failure before refresh: credentials retained, no HTTP sent, safe retry succeeds.\nAmbiguous refresh: no reuse after network/429/503 failures or restart.\nDiagnostics exclude access tokens, refresh tokens and response messages.\nNo live API requests. Android Keystore and physical sleep/update still need headset validation.\n");
            }
            catch (Exception error) { File.WriteAllText(report, "FAIL\n" + error); throw; }
        }

        public static void ValidateRules()
        {
            var now = DateTimeOffset.Parse("2026-10-14T12:00:00Z");
            Check(KinoApiRules.IsRetryable(0) && KinoApiRules.IsRetryable(429) && KinoApiRules.IsRetryable(503), "Transient errors must permit retry.");
            Check(!KinoApiRules.IsRetryable(409) && !KinoApiRules.IsRetryable(422) && !KinoApiRules.IsRetryable(401), "Business/auth errors must not be blindly retried.");
            Check(KinoApiRules.RetryAfter("12", now) == 12 && KinoApiRules.RetryAfter("Wed, 14 Oct 2026 12:00:30 GMT", now) == 30,
                "Retry-After must support seconds and HTTP dates.");
            Check(KinoApiRules.RetryAfter("-4", now) == 0 && KinoApiRules.RetryAfter("garbage", now) == 0, "Invalid Retry-After values must be harmless.");
            Check(KinoApiRules.ExpiresSoon("2026-10-14T15:00:30+03:00", now) && !KinoApiRules.ExpiresSoon("2026-10-14T13:00:00Z", now),
                "Token expiry must respect offsets and the refresh margin.");
            Check(KinoApiRules.ExpiresSoon(null, now) && KinoApiRules.RequiresPairing("REFRESH_TOKEN_REUSED") &&
                !KinoApiRules.RequiresPairing("TOKEN_EXPIRED"), "Refresh expiry/reuse must not be confused with access-token expiry.");
            Check(KinoApiRules.TryNormalizeBaseUrl("https://example.com/api/v1/", out var normalized) && normalized == "https://example.com/api/v1",
                "Base URL normalization failed.");
            Check(KinoApiRules.TryNormalizeBaseUrl("http://127.0.0.1:12345/api/v1", out _) &&
                !KinoApiRules.TryNormalizeBaseUrl("http://example.com/api/v1", out _) &&
                !KinoApiRules.TryNormalizeBaseUrl("https://user:password@example.com/api/v1", out _) &&
                !KinoApiRules.TryNormalizeBaseUrl("https://example.com/api/v1?token=bad", out _), "Insecure or credential-bearing URLs must be rejected.");
            var players = JsonUtility.FromJson<KinoApiPlayers>("{\"event_id\":null,\"players\":[]}");
            Check(players != null && string.IsNullOrEmpty(players.event_id) && players.players != null && players.players.Length == 0,
                "An eventless empty player list is valid (JsonUtility may normalize null strings to empty strings).");
            var stores = JsonUtility.FromJson<KinoApiSelectableStores>("{\"campaigns\":[{\"id\":\"test\",\"stores\":[{\"id\":\"store\",\"region\":null,\"has_open_event\":false}]}]}");
            Check(string.IsNullOrEmpty(stores.campaigns[0].stores[0].region), "An absent region must not require an ID.");
        }

        public static void ValidateStorageRecovery()
        {
            string endpoint = "http://127.0.0.1:12345/kinovr-test-" + Guid.NewGuid().ToString("N");
            var storage = new KinoTokenStorage(endpoint);
            var other = new KinoTokenStorage(endpoint + "-other");
            var saved = new KinoStoredTokens
            {
                apiBaseUrl = endpoint,
                tokens = new KinoApiTokenPair { token_type = "Bearer", access_token = "synthetic-access", refresh_token = "synthetic-refresh",
                    expires_in = 3600, access_token_expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
                    refresh_token_expires_at = DateTimeOffset.UtcNow.AddDays(1).ToString("O") }
            };
            GameObject first = null, restarted = null;
            try
            {
                Check(storage.TrySave(saved, out _), "Synthetic pairing could not be saved.");
                Check(other.TryLoad(out var absent, out _) && absent == null, "Credentials leaked into another API endpoint scope.");
                first = new GameObject("KINO API storage test");
                var client = first.AddComponent<KinoApiClient>(); client.apiBaseUrl = endpoint;
                Check(client.IsPaired, "Stored synthetic pairing did not load.");
                restarted = new GameObject("KINO API clean restart test");
                var cleanRestart = restarted.AddComponent<KinoApiClient>(); cleanRestart.apiBaseUrl = endpoint;
                Check(cleanRestart.IsPaired, "A clean client restart must restore the saved pairing.");
                UnityEngine.Object.DestroyImmediate(restarted);
                saved.refreshInFlight = true;
                Check(storage.TrySave(saved, out _), "Could not persist simulated interrupted refresh.");
                restarted = new GameObject("KINO API recovery test");
                var recovery = restarted.AddComponent<KinoApiClient>(); recovery.apiBaseUrl = endpoint;
                Check(!recovery.IsPaired && recovery.LastErrorCode == "REFRESH_INTERRUPTED", "Restart must not reuse a possibly consumed refresh token.");
                client.apiBaseUrl = endpoint + "-other";
                Check(!client.IsPaired, "Changing API endpoint must discard in-memory credentials from the old endpoint.");
            }
            finally
            {
                storage.TryClear(out _); other.TryClear(out _);
                if (first) UnityEngine.Object.DestroyImmediate(first);
                if (restarted) UnityEngine.Object.DestroyImmediate(restarted);
            }
        }

        static KinoStoredTokens SavedTokens(string endpoint, bool expiredAccess = false) => new KinoStoredTokens {
            apiBaseUrl = endpoint,
            tokens = new KinoApiTokenPair {
                token_type = "Bearer", access_token = "synthetic-access-do-not-log", refresh_token = "synthetic-refresh-do-not-log",
                expires_in = 3600, access_token_expires_at = DateTimeOffset.UtcNow.AddHours(expiredAccess ? -1 : 1).ToString("O"),
                refresh_token_expires_at = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                context = new KinoApiContext { device = new KinoApiDevice { id = "synthetic-device" } }
            }
        };

        static KinoAuthTestClient Client(GameObject root, string endpoint, IKinoTokenStorage storage)
        {
            var client = root.AddComponent<KinoAuthTestClient>();
            client.apiBaseUrl = endpoint; client.Storage = storage;
            return client;
        }

        static void ValidateTransientReadRecovery()
        {
            string endpoint = "http://127.0.0.1:12345/read-recovery-" + Guid.NewGuid().ToString("N");
            var backing = new KinoTokenStorage(endpoint);
            var faults = new KinoAuthTestStorage(backing) { ReadFailures = 1 };
            var root = new GameObject("KINO transient storage test");
            var resumed = new GameObject("KINO resume storage test");
            try
            {
                Check(backing.TrySave(SavedTokens(endpoint), out _), "Could not seed read recovery.");
                var client = Client(root, endpoint, faults);
                var flow = root.AddComponent<KinoBackendFlow>();
                flow.Initialize(null, client, Path.GetFullPath("Artifacts/KinoBackend/auth-read-tests/" + Guid.NewGuid().ToString("N")));
                flow.EnterStep(KinoSequenceStep.CampaignSelection, null);
                Check(flow.ErrorCode == "TOKEN_STORAGE_FAILED", "A storage read failure must enter recovery, not the pairing screen.");
                Check(!client.IsPaired && faults.Reads == 1, "Storage reads must be throttled while recovery is pending.");
                KinoApiResult<KinoApiTokenPair> activation = null;
                Drain(client.Activate("TESTCODE", value => activation = value));
                Check(activation != null && !activation.Success && activation.Code == "TOKEN_STORAGE_FAILED" && faults.Writes == 0 && client.Requests == 0,
                    "A read failure must not allow activation to overwrite an existing pairing.");
                client.Clock += 1;
                Check(client.IsPaired && faults.Reads == 2 && client.Context.device.id == "synthetic-device",
                    "A temporary read failure remained cached instead of restoring the saved pairing.");
                Check(client.Requests == 0 && faults.Writes == 0, "Read recovery must not call the API or replace credentials.");

                faults.ReadFailures = 1;
                var afterRestart = Client(resumed, endpoint, faults);
                Check(!afterRestart.IsPaired, "The restart read failure was not simulated.");
                afterRestart.SendMessage("OnApplicationPause", true);
                afterRestart.SendMessage("OnApplicationPause", false);
                Check(afterRestart.IsPaired, "Resume must allow immediate storage recovery without a new code.");
                int reads = faults.Reads;
                afterRestart.SendMessage("OnApplicationPause", true);
                afterRestart.SendMessage("OnApplicationPause", false);
                Check(afterRestart.IsPaired && faults.Reads == reads, "Sleep/resume must keep an already loaded pairing intact.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(resumed);
                backing.TryClear(out _);
            }
        }

        static void ValidateRefreshStorageFailure()
        {
            string endpoint = "http://127.0.0.1:12345/write-recovery-" + Guid.NewGuid().ToString("N");
            var backing = new KinoTokenStorage(endpoint);
            var faults = new KinoAuthTestStorage(backing) { WriteFailures = 1 };
            var root = new GameObject("KINO refresh storage test");
            var restarted = new GameObject("KINO refreshed restart test");
            try
            {
                Check(backing.TrySave(SavedTokens(endpoint, true), out _), "Could not seed refresh recovery.");
                var client = Client(root, endpoint, faults);
                client.NextTokens = SavedTokens(endpoint).tokens;
                client.NextTokens.access_token = "synthetic-next-access"; client.NextTokens.refresh_token = "synthetic-next-refresh";
                KinoApiResult<KinoApiContext> result = null;
                Drain(client.Request<KinoApiContext>("GET", "/device/context", null, value => result = value));
                Check(result != null && !result.Success && result.Code == "TOKEN_STORAGE_FAILED" && result.Retryable && result.Status == 401,
                    "A pre-send storage failure must remain a retryable authentication preflight error.");
                Check(client.IsPaired && client.Requests == 0, "A failed marker write must preserve pairing and must not send refresh.");
                Check(backing.TryLoad(out var unchanged, out _) && !unchanged.refreshInFlight && unchanged.tokens.refresh_token == "synthetic-refresh-do-not-log",
                    "The original saved credentials were changed before a refresh was sent.");
                Drain(client.Request<KinoApiContext>("GET", "/device/context", null, value => result = value));
                Check(result.Success && client.Requests == 2, "Retry after storage recovery must refresh once and complete the original request.");
                Check(backing.TryLoad(out var updated, out _) && !updated.refreshInFlight && updated.tokens.refresh_token == "synthetic-next-refresh",
                    "The new refresh token must be durably saved and the marker cleared.");
                Check(Client(restarted, endpoint, backing).IsPaired, "The refreshed pairing must survive client recreation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(restarted);
                backing.TryClear(out _);
            }
        }

        static void ValidateInterruptedRefresh()
        {
            foreach (long status in new long[] { 0, 429, 503 })
            {
                string endpoint = "http://127.0.0.1:12345/interrupted-" + Guid.NewGuid().ToString("N");
                var storage = new KinoTokenStorage(endpoint);
                var root = new GameObject("KINO interrupted refresh test");
                var restarted = new GameObject("KINO interrupted restart test");
                try
                {
                    Check(storage.TrySave(SavedTokens(endpoint, true), out _), "Could not seed interrupted refresh.");
                    var client = Client(root, endpoint, storage);
                    client.RefreshFailureStatus = status; client.FailRefresh = true;
                    KinoApiResult<KinoApiContext> result = null;
                    Drain(client.Request<KinoApiContext>("GET", "/device/context", null, value => result = value));
                    Check(!result.Success && !result.Retryable && !client.IsPaired && client.Requests == 1,
                        "An ambiguous exchange must not replay a potentially consumed refresh token.");
                    var recovery = Client(restarted, endpoint, storage);
                    Check(!recovery.IsPaired && recovery.LastErrorCode == "REFRESH_INTERRUPTED", "Restart bypassed the durable rotation marker.");
                    Drain(recovery.Request<KinoApiContext>("GET", "/device/context", null, value => result = value));
                    Check(recovery.Requests == 0, "Restart retried a possibly consumed refresh token.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(restarted);
                    storage.TryClear(out _);
                }
            }
        }

        static void ValidateDiagnosticPrivacy()
        {
            var tokens = SavedTokens("http://127.0.0.1:12345/privacy").tokens;
            string entry = KinoAuthDiagnostics.Format("refresh_response", "raw secret response", 503, "http://127.0.0.1:12345/privacy", tokens, true);
            Check(!entry.Contains(tokens.access_token) && !entry.Contains(tokens.refresh_token) && !entry.Contains("raw secret response"),
                "Authentication diagnostics leaked credentials or an untrusted response message.");
            Check(entry.Contains("synthetic-device") && entry.Contains("503") && entry.Contains("refreshExpiresAt"),
                "Authentication diagnostics must retain useful device/status/expiry metadata.");
        }

        static void Drain(IEnumerator routine)
        {
            using (routine as IDisposable)
            {
                while (routine.MoveNext())
                {
                    if (routine.Current is IEnumerator nested) Drain(nested);
                    else Check(routine.Current == null, "The synthetic transport unexpectedly started an asynchronous operation.");
                }
            }
        }
    }

    public sealed class KinoAuthTestStorage : IKinoTokenStorage
    {
        readonly IKinoTokenStorage inner;
        public int ReadFailures, WriteFailures, Reads, Writes;
        public KinoAuthTestStorage(IKinoTokenStorage inner) { this.inner = inner; }
        public bool TryLoad(out KinoStoredTokens value, out string error)
        {
            Reads++;
            if (ReadFailures > 0) { ReadFailures--; value = null; error = "Synthetic read failure"; return false; }
            return inner.TryLoad(out value, out error);
        }
        public bool TrySave(KinoStoredTokens value, out string error)
        {
            Writes++;
            if (WriteFailures > 0) { WriteFailures--; error = "Synthetic write failure"; return false; }
            return inner.TrySave(value, out error);
        }
        public bool TryClear(out string error) => inner.TryClear(out error);
    }

    public sealed class KinoAuthTestClient : KinoApiClient
    {
        public IKinoTokenStorage Storage;
        public double Clock = 100;
        public int Requests;
        public KinoApiTokenPair NextTokens;
        public bool FailRefresh;
        public long RefreshFailureStatus;
        protected override IKinoTokenStorage CreateTokenStorage(string endpoint) => Storage;
        protected override double AuthenticationClock => Clock;
        protected override IEnumerator Send<T>(string endpoint, string method, string path, object body, string accessToken, Action<KinoApiResult<T>> done)
        {
            Requests++;
            if (path == "/auth/refresh")
            {
                if (FailRefresh) done(KinoApiResult<T>.Fail("SYNTHETIC_REFRESH_ERROR", "Synthetic failure", RefreshFailureStatus, true));
                else done(KinoApiResult<T>.Ok(NextTokens as T));
            }
            else if (path == "/device/context") done(KinoApiResult<T>.Ok(NextTokens.context as T));
            else throw new InvalidOperationException("Unexpected synthetic API path.");
            yield break;
        }
    }
}
