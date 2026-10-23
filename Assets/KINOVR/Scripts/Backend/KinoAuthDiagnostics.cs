using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace KinoVR
{
    /// <summary>Bounded local metadata only. Never serialize token pairs or response bodies.</summary>
    public static class KinoAuthDiagnostics
    {
        const long MaxBytes = 128 * 1024;
        static readonly object Gate = new object();

        [Serializable] sealed class Entry
        {
            public string utc, action, code, endpoint, deviceId, accessExpiresAt, refreshExpiresAt, version, buildGuid, storage;
            public long httpStatus;
            public bool hasTokens, refreshInFlight;
        }

        public static string Format(string action, string code, long status, string endpoint, KinoApiTokenPair tokens, bool inFlight)
        {
            return JsonUtility.ToJson(new Entry {
                utc = DateTimeOffset.UtcNow.ToString("O"), action = action, code = SafeCode(code), endpoint = endpoint,
                deviceId = tokens?.context?.device?.id, httpStatus = status, hasTokens = tokens != null, refreshInFlight = inFlight,
                accessExpiresAt = Date(tokens?.access_token_expires_at), refreshExpiresAt = Date(tokens?.refresh_token_expires_at),
                version = Application.version, buildGuid = Application.buildGUID, storage = KinoTokenStorage.Mode
            });
        }

        public static void Write(string action, string code, long status, string endpoint, KinoApiTokenPair tokens, bool inFlight)
        {
            string line = Format(action, code, status, endpoint, tokens, inFlight);
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "[KINO auth] {0}", line);
            if (Application.isEditor) return;
            try
            {
                lock (Gate)
                {
                    string directory = Path.Combine(Application.persistentDataPath, "KinoBackend");
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, "authentication.jsonl");
                    if (File.Exists(path) && new FileInfo(path).Length >= MaxBytes)
                    {
                        File.Copy(path, path + ".previous", true);
                        File.WriteAllText(path, "");
                    }
                    File.AppendAllText(path, line + "\n");
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Diagnostic storage must never alter authentication or disclose exception data.
            }
        }

        static string Date(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("O") : null;

        static string SafeCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            if (code.Length > 64) return "UNKNOWN_ERROR";
            foreach (char character in code)
                if (!(character >= 'A' && character <= 'Z' || character >= '0' && character <= '9' || character == '_')) return "UNKNOWN_ERROR";
            return code;
        }
    }
}
