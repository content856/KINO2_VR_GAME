using System;
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
            Debug.Log("PASS: KINO API URL isolation, retry rules, token expiry, durable rotation marker and scoped session storage. No network requests.");
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
    }
}
