using System;

namespace KinoVR
{
    // Field names intentionally match the Quest OpenAPI contract for JsonUtility.
    [Serializable] public sealed class KinoApiTokenPair
    {
        public string token_type, access_token, access_token_expires_at, refresh_token, refresh_token_expires_at;
        public int expires_in;
        public KinoApiContext context;
    }
    [Serializable] public sealed class KinoApiContext
    {
        public KinoApiDevice device;
        public KinoApiCampaign campaign;
        public KinoApiStore store;
        public KinoApiEvent active_event;
        public string store_selected_at, session_expires_at, server_time;
        public string[] capabilities;
    }
    [Serializable] public sealed class KinoApiDevice { public string id, type, label, brand_id; }
    [Serializable] public sealed class KinoApiCampaign
    {
        public string id, name, starts_on, ends_on;
        public KinoApiStore[] stores;
    }
    [Serializable] public sealed class KinoApiStore
    {
        public string id, name, code, city, region, timezone;
        public bool has_open_event;
    }
    [Serializable] public sealed class KinoApiEvent { public string id, name, event_date, status; }
    [Serializable] public sealed class KinoApiSelectableStores { public KinoApiCampaign[] campaigns; }
    [Serializable] public sealed class KinoApiPlayers { public string event_id; public KinoApiPlayer[] players; }
    [Serializable] public sealed class KinoApiPlayer { public string registration_id, nickname, status, registered_at; }
    [Serializable] public sealed class KinoApiGameSession
    {
        public string game_session_id, status, registration_id, nickname, started_at, expires_at, ended_at;
        public KinoApiScore score;
    }
    [Serializable] public sealed class KinoApiScore { public string score_id; public int score; }
    [Serializable] public sealed class KinoApiCompleteResponse
    {
        public bool success;
        public string score_id, game_session_id, achieved_at;
        public int score;
    }
    [Serializable] public sealed class KinoApiActivateRequest { public string pairing_code; public string device_type = "quest"; }
    [Serializable] public sealed class KinoApiRefreshRequest { public string refresh_token; }
    [Serializable] public sealed class KinoApiSelectStoreRequest { public string campaign_id, store_id; }
    [Serializable] public sealed class KinoApiStartRequest { public string registration_id; }
    [Serializable] public sealed class KinoApiCompleteRequest { public string game_session_id, submission_id; public int score; }
    [Serializable] public sealed class KinoApiErrorEnvelope { public KinoApiError error; }
    [Serializable] public sealed class KinoApiError { public string code, message; }

    public sealed class KinoApiResult<T> where T : class
    {
        public T Value;
        public string Code, Message;
        public long Status;
        public bool Success, Retryable;
        public double RetryAfterSeconds;

        public static KinoApiResult<T> Ok(T value, long status = 200) =>
            new KinoApiResult<T> { Value = value, Status = status, Success = true };
        public static KinoApiResult<T> Fail(string code, string message, long status = 0, bool retryable = false, double retryAfter = 0) =>
            new KinoApiResult<T> { Code = code, Message = message, Status = status, Retryable = retryable, RetryAfterSeconds = retryAfter };
    }
}
