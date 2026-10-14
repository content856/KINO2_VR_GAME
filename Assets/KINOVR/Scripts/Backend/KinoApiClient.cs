using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace KinoVR
{
    public static class KinoApiRules
    {
        public static bool IsRetryable(long status) => status == 0 || status == 429 || status >= 500 && status <= 599;
        public static bool RequiresPairing(string code) => code == "UNAUTHENTICATED" || code == "DEVICE_REVOKED" ||
            code == "DEVICE_INACTIVE" || code == "REFRESH_TOKEN_INVALID" || code == "REFRESH_TOKEN_REUSED";
        public static bool ExpiresSoon(string timestamp, DateTimeOffset now, int leadSeconds = 60) =>
            !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry) ||
            expiry <= now.AddSeconds(leadSeconds);
        public static double RetryAfter(string header, DateTimeOffset now)
        {
            if (double.TryParse(header, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                !double.IsNaN(seconds) && !double.IsInfinity(seconds)) return Math.Max(0, seconds);
            return DateTimeOffset.TryParse(header, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? Math.Max(0, (date - now).TotalSeconds) : 0;
        }
        public static bool TryNormalizeBaseUrl(string value, out string normalized)
        {
            normalized = null;
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
                !(uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
            normalized = uri.AbsoluteUri.TrimEnd('/');
            return true;
        }
        public static bool ValidTokens(KinoApiTokenPair value) => value != null && value.token_type == "Bearer" &&
            !string.IsNullOrEmpty(value.access_token) && !string.IsNullOrEmpty(value.refresh_token) && value.expires_in > 0 &&
            DateTimeOffset.TryParse(value.access_token_expires_at, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
            DateTimeOffset.TryParse(value.refresh_token_expires_at, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    /// <summary>One HTTP attempt plus at most one authenticated retry. The coordinator owns backoff/results.</summary>
    [DisallowMultipleComponent]
    public class KinoApiClient : MonoBehaviour
    {
        [Tooltip("Android stores encrypted credentials in Keystore. Editor/desktop pairing is memory-only for the current session.")]
        public string apiBaseUrl = "https://kinovr.digitaltribes.gr/api/v1";
        [Min(1)] public int requestTimeoutSeconds = 20;
        public virtual bool IsPaired => EnsureLoaded() && tokens != null;
        public KinoApiContext Context { get; private set; }
        public string LastErrorCode { get; private set; }
        public string LastErrorMessage { get; private set; }
        public string TokenStorageMode => KinoTokenStorage.Mode;

        KinoTokenStorage storage;
        KinoApiTokenPair tokens;
        string loadedBase;
        bool loadSucceeded, refreshing, activating;
        int credentialVersion;
        KinoApiResult<KinoApiTokenPair> lastRefresh;

        // Lazy loading permits changing apiBaseUrl after AddComponent. Credentials are scoped
        // to the complete normalized endpoint and never migrated to another host/environment.
        public bool EnsureLoaded()
        {
            if (!KinoApiRules.TryNormalizeBaseUrl(apiBaseUrl, out var normalized))
            { SetError("INVALID_API_URL", "Use an HTTPS API URL (HTTP is allowed only for loopback tests)."); return false; }
            if (loadedBase == normalized) return loadSucceeded;
            if (refreshing || activating)
            { SetError("AUTH_BUSY", "Wait for the current pairing operation before changing the API URL."); return false; }
            loadedBase = normalized; tokens = null; Context = null; credentialVersion++;
            storage = new KinoTokenStorage(normalized);
            loadSucceeded = storage.TryLoad(out var saved, out var error);
            if (!loadSucceeded) { SetError("TOKEN_STORAGE_FAILED", error); return false; }
            if (saved == null) { ClearError(); return true; }
            if (saved.refreshInFlight)
            { SetError("REFRESH_INTERRUPTED", "A token exchange was interrupted. Pair this headset again."); return true; }
            if (saved.apiBaseUrl != normalized || !KinoApiRules.ValidTokens(saved.tokens))
            { SetError("PAIRING_INVALID", "Stored pairing is invalid. Pair this headset again."); return true; }
            if (KinoApiRules.ExpiresSoon(saved.tokens.refresh_token_expires_at, DateTimeOffset.UtcNow, 0))
            { SetError("REFRESH_TOKEN_INVALID", "Pairing has expired. Pair this headset again."); return true; }
            tokens = saved.tokens; Context = tokens.context; ClearError(); return true;
        }

        public virtual bool ForgetPairing()
        {
            if (refreshing || activating) { SetError("AUTH_BUSY", "Wait for the current pairing operation."); return false; }
            EnsureLoaded();
            tokens = null; Context = null; credentialVersion++;
            if (storage != null && !storage.TryClear(out var error)) { SetError("TOKEN_STORAGE_FAILED", error); return false; }
            ClearError(); return true;
        }

        public virtual IEnumerator Activate(string code, Action<KinoApiResult<KinoApiTokenPair>> done)
        {
            while (refreshing || activating) yield return null;
            if (!KinoApiRules.TryNormalizeBaseUrl(apiBaseUrl, out var endpoint))
            { done?.Invoke(Failure<KinoApiTokenPair>("INVALID_API_URL", "Use an HTTPS API URL.")); yield break; }
            EnsureLoaded();
            if (storage == null || loadedBase != endpoint)
            { done?.Invoke(Failure<KinoApiTokenPair>("TOKEN_STORAGE_FAILED", "Secure pairing storage is unavailable.")); yield break; }
            if (string.IsNullOrWhiteSpace(code))
            { done?.Invoke(Failure<KinoApiTokenPair>("PAIRING_CODE_INVALID", "Enter the pairing code from the admin.")); yield break; }
            activating = true;
            KinoApiResult<KinoApiTokenPair> result = null;
            try
            {
                // Re-pairing revokes earlier credentials. A lost activation response must not
                // resurrect an older token pair after restart either.
                tokens = null; Context = null; credentialVersion++;
                if (!storage.TrySave(new KinoStoredTokens { apiBaseUrl = endpoint, refreshInFlight = true }, out var error))
                    result = Failure<KinoApiTokenPair>("TOKEN_STORAGE_FAILED", error);
                else
                {
                    yield return Send<KinoApiTokenPair>(endpoint, "POST", "/devices/activate",
                        new KinoApiActivateRequest { pairing_code = code.Trim().ToUpperInvariant() }, null, value => result = value);
                    if (result.Success) result = AcceptTokens(result, endpoint);
                    else
                    {
                        // Activation codes are single-use too. Do not have generic callers
                        // automatically replay a code after an ambiguous network outcome.
                        result.Retryable = false;
                        SetError(result.Code, result.Message);
                    }
                }
            }
            finally { activating = false; }
            done?.Invoke(result);
        }

        public virtual IEnumerator Request<T>(string method, string relativePath, object body, Action<KinoApiResult<T>> done) where T : class
        {
            while (activating || refreshing) yield return null;
            if (!EnsureLoaded())
            { done?.Invoke(Failure<T>(LastErrorCode, LastErrorMessage)); yield break; }
            if (tokens == null)
            { done?.Invoke(Failure<T>(LastErrorCode ?? "PAIRING_REQUIRED", LastErrorMessage ?? "Pair this headset first.")); yield break; }
            if (relativePath == "/auth/refresh" || relativePath == "/devices/activate")
            { done?.Invoke(Failure<T>("AUTH_ENDPOINT_RESERVED", "Use the pairing client for token operations.")); yield break; }
            string endpoint = loadedBase;
            if (KinoApiRules.ExpiresSoon(tokens.access_token_expires_at, DateTimeOffset.UtcNow))
            {
                KinoApiResult<KinoApiTokenPair> refreshed = null;
                yield return Refresh(false, credentialVersion, value => refreshed = value);
                if (!refreshed.Success) { done?.Invoke(CopyFailure<T>(refreshed)); yield break; }
            }
            if (!SameEndpoint(endpoint) || tokens == null)
            { done?.Invoke(Failure<T>("API_CONFIGURATION_CHANGED", "API configuration changed during the request.")); yield break; }
            int usedVersion = credentialVersion;
            KinoApiResult<T> result = null;
            yield return Send<T>(endpoint, method, relativePath, body, tokens.access_token, value => result = value);
            // A concurrent refresh invalidates the access token used by requests already in
            // flight. Join that rotation instead of revoking the newly saved credentials.
            if (result.Status == 401 && (result.Code == "TOKEN_EXPIRED" || refreshing || usedVersion != credentialVersion))
            {
                KinoApiResult<KinoApiTokenPair> refreshed = null;
                yield return Refresh(true, usedVersion, value => refreshed = value);
                if (!refreshed.Success) { done?.Invoke(CopyFailure<T>(refreshed)); yield break; }
                if (!SameEndpoint(endpoint) || tokens == null)
                { done?.Invoke(Failure<T>("API_CONFIGURATION_CHANGED", "API configuration changed during the request.")); yield break; }
                usedVersion = credentialVersion;
                yield return Send<T>(endpoint, method, relativePath, body, tokens.access_token, value => result = value);
            }
            if (result.Success)
            {
                if (result.Value is KinoApiContext context) Context = context;
                if (relativePath == "/devices/logout") ForgetPairing();
                ClearError();
            }
            else
            {
                if (result.Status == 401 && KinoApiRules.RequiresPairing(result.Code) && usedVersion == credentialVersion)
                    InvalidatePairing();
                SetError(result.Code, result.Message);
            }
            done?.Invoke(result);
        }

        IEnumerator Refresh(bool force, int observedVersion, Action<KinoApiResult<KinoApiTokenPair>> done)
        {
            if (refreshing)
            {
                while (refreshing) yield return null;
                done(tokens != null ? KinoApiResult<KinoApiTokenPair>.Ok(tokens) : lastRefresh ??
                    Failure<KinoApiTokenPair>("PAIRING_REQUIRED", "Pair this headset again."));
                yield break;
            }
            if (tokens == null)
            { done(Failure<KinoApiTokenPair>("PAIRING_REQUIRED", "Pair this headset again.")); yield break; }
            if (observedVersion != credentialVersion || !force && !KinoApiRules.ExpiresSoon(tokens.access_token_expires_at, DateTimeOffset.UtcNow))
            { done(KinoApiResult<KinoApiTokenPair>.Ok(tokens)); yield break; }
            refreshing = true;
            string endpoint = loadedBase;
            try
            {
                if (KinoApiRules.ExpiresSoon(tokens.refresh_token_expires_at, DateTimeOffset.UtcNow, 0))
                {
                    InvalidatePairing();
                    lastRefresh = Failure<KinoApiTokenPair>("REFRESH_TOKEN_INVALID", "Pairing has expired. Pair this headset again.");
                }
                else if (!storage.TrySave(new KinoStoredTokens { apiBaseUrl = endpoint, tokens = tokens, refreshInFlight = true }, out var error))
                {
                    tokens = null; Context = null; credentialVersion++;
                    lastRefresh = Failure<KinoApiTokenPair>("TOKEN_STORAGE_FAILED", error);
                }
                else
                {
                    var request = new KinoApiRefreshRequest { refresh_token = tokens.refresh_token };
                    // The durable marker is present before sending. No retry of this request,
                    // including after a timeout/429/5xx: its token may already be consumed.
                    yield return Send<KinoApiTokenPair>(endpoint, "POST", "/auth/refresh", request, null, value => lastRefresh = value);
                    if (lastRefresh.Success) lastRefresh = AcceptTokens(lastRefresh, endpoint);
                    else
                    {
                        tokens = null; Context = null; credentialVersion++;
                        lastRefresh.Retryable = false;
                        if (lastRefresh.Status == 0 || lastRefresh.Status >= 500)
                        {
                            lastRefresh.Code = "REFRESH_INTERRUPTED";
                            lastRefresh.Message = "The token exchange outcome is unknown. Pair this headset again.";
                        }
                        SetError(lastRefresh.Code, lastRefresh.Message);
                    }
                }
            }
            finally { refreshing = false; }
            done(lastRefresh);
        }

        KinoApiResult<KinoApiTokenPair> AcceptTokens(KinoApiResult<KinoApiTokenPair> result, string endpoint)
        {
            if (!KinoApiRules.ValidTokens(result.Value))
            {
                tokens = null; Context = null; credentialVersion++;
                return Failure<KinoApiTokenPair>("INVALID_TOKEN_RESPONSE", "The service did not return a valid Quest token pair. Pair again.", result.Status);
            }
            var next = result.Value;
            if (next.context == null) next.context = Context;
            if (!storage.TrySave(new KinoStoredTokens { apiBaseUrl = endpoint, tokens = next }, out var error))
            {
                tokens = null; Context = null; credentialVersion++;
                return Failure<KinoApiTokenPair>("TOKEN_STORAGE_FAILED", error);
            }
            // Saving the new refresh token must succeed before the access token is used.
            tokens = next; Context = next.context; credentialVersion++; loadSucceeded = true; ClearError();
            return result;
        }

        bool SameEndpoint(string endpoint) => KinoApiRules.TryNormalizeBaseUrl(apiBaseUrl, out var current) && current == endpoint && loadedBase == endpoint;
        void InvalidatePairing()
        {
            tokens = null; Context = null; credentialVersion++;
            // Persist a tombstone rather than allowing an interrupted clear to restore tokens.
            storage?.TrySave(new KinoStoredTokens { apiBaseUrl = loadedBase, refreshInFlight = true }, out _);
        }
        void SetError(string code, string message) { LastErrorCode = code; LastErrorMessage = message; }
        void ClearError() { LastErrorCode = LastErrorMessage = null; }
        KinoApiResult<T> Failure<T>(string code, string message, long status = 0) where T : class
        { SetError(code, message); return KinoApiResult<T>.Fail(code, message, status); }
        static KinoApiResult<T> CopyFailure<T>(KinoApiResult<KinoApiTokenPair> value) where T : class =>
            // This is an authentication preflight failure, not the status of the caller's
            // score/start operation (which was not sent with the failed credentials).
            // In particular refresh 429/422 must never be mistaken for a rejected score.
            KinoApiResult<T>.Fail(value.Code, value.Message, 401, false, value.RetryAfterSeconds);

        IEnumerator Send<T>(string endpoint, string method, string relativePath, object body, string accessToken,
            Action<KinoApiResult<T>> done) where T : class
        {
            if (string.IsNullOrEmpty(relativePath) || !relativePath.StartsWith("/", StringComparison.Ordinal) ||
                relativePath.StartsWith("//", StringComparison.Ordinal) || relativePath.Contains("\\") || relativePath.Contains("#") ||
                !Uri.TryCreate(endpoint + relativePath, UriKind.Absolute, out var target) ||
                !target.AbsoluteUri.StartsWith(endpoint + "/", StringComparison.Ordinal))
            { done(KinoApiResult<T>.Fail("INVALID_API_PATH", "The API path must remain inside the configured endpoint.")); yield break; }

            UnityWebRequest request = null;
            KinoApiResult<T> setupError = null;
            try
            {
                request = new UnityWebRequest(target, method);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, requestTimeoutSeconds);
                request.redirectLimit = 0; // Never forward the bearer token to a redirected host.
                request.SetRequestHeader("Accept", "application/json");
                if (!string.IsNullOrEmpty(accessToken)) request.SetRequestHeader("Authorization", "Bearer " + accessToken);
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
            }
            catch (Exception) { setupError = KinoApiResult<T>.Fail("REQUEST_INVALID", "The API request could not be prepared."); }
            if (setupError != null) { request?.Dispose(); done(setupError); yield break; }
            using (request)
            {
                UnityWebRequestAsyncOperation operation = null;
                try { operation = request.SendWebRequest(); }
                catch (Exception) { setupError = KinoApiResult<T>.Fail("NETWORK_ERROR", "The game service could not be reached.", 0, true); }
                if (setupError != null) { done(setupError); yield break; }
                yield return operation;
                long status = request.responseCode;
                string json = request.downloadHandler.text;
                if (status >= 200 && status <= 299 && request.result == UnityWebRequest.Result.Success)
                {
                    T value = null;
                    try { value = JsonUtility.FromJson<T>(json); } catch (Exception) { }
                    if (ValidResponse(value)) done(KinoApiResult<T>.Ok(value, status));
                    else done(KinoApiResult<T>.Fail("INVALID_RESPONSE", "The service returned an incomplete or unreadable response.", status, true));
                }
                else
                {
                    KinoApiErrorEnvelope envelope = null;
                    try { envelope = JsonUtility.FromJson<KinoApiErrorEnvelope>(json); } catch (Exception) { }
                    bool transportFailure = request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError;
                    done(KinoApiResult<T>.Fail(envelope?.error?.code ?? (transportFailure || status == 0 ? "NETWORK_ERROR" : "HTTP_ERROR"),
                        envelope?.error?.message ?? "The game service request failed.", status, transportFailure || KinoApiRules.IsRetryable(status),
                        KinoApiRules.RetryAfter(request.GetResponseHeader("Retry-After"), DateTimeOffset.UtcNow)));
                }
            }
        }

        static bool ValidResponse<T>(T value) where T : class
        {
            if (value == null) return false;
            if (value is KinoApiTokenPair pair) return KinoApiRules.ValidTokens(pair);
            if (value is KinoApiContext context) return !string.IsNullOrEmpty(context.device?.id);
            if (value is KinoApiSelectableStores stores) return stores.campaigns != null;
            if (value is KinoApiPlayers players) return players.players != null;
            if (value is KinoApiGameSession session) return !string.IsNullOrEmpty(session.game_session_id) && !string.IsNullOrEmpty(session.registration_id) &&
                (session.status == "active" || session.status == "completed" || session.status == "expired" || session.status == "abandoned");
            if (value is KinoApiCompleteResponse complete) return complete.success && !string.IsNullOrEmpty(complete.score_id) && !string.IsNullOrEmpty(complete.game_session_id);
            return true;
        }
    }
}
