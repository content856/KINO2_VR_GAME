using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace KinoVR
{
    [Serializable]
    public sealed class KinoStoredTokens
    {
        public string apiBaseUrl;
        public KinoApiTokenPair tokens;
        // Written before rotating a single-use refresh token. An interrupted rotation
        // requires pairing again; replaying the old refresh token would revoke pairing.
        public bool refreshInFlight;
    }

    public interface IKinoTokenStorage
    {
        bool TryLoad(out KinoStoredTokens value, out string error);
        bool TrySave(KinoStoredTokens value, out string error);
        bool TryClear(out string error);
    }

    /// <summary>Android Keystore at rest; deliberately memory-only in the Editor/desktop.</summary>
    public sealed class KinoTokenStorage : IKinoTokenStorage
    {
        const string JavaClass = "com.kinovr.securestorage.KinoSecureStorage";
        static readonly Dictionary<string, string> SessionPayloads = new Dictionary<string, string>();
        readonly string key;
        public static string Mode => Application.platform == RuntimePlatform.Android && !Application.isEditor
            ? "Android Keystore" : "Session only (pair again after Editor/domain or app restart)";

        public KinoTokenStorage(string normalizedApiBaseUrl)
        {
            using (var sha = SHA256.Create())
                key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalizedApiBaseUrl))).Replace("-", "").ToLowerInvariant();
        }

        public bool TryLoad(out KinoStoredTokens value, out string error)
        {
            value = null; error = null;
            try
            {
                string json;
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var bridge = new AndroidJavaClass(JavaClass))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    json = bridge.CallStatic<string>("read", activity, key);
#else
                SessionPayloads.TryGetValue(key, out json);
#endif
                if (!string.IsNullOrEmpty(json))
                {
                    value = JsonUtility.FromJson<KinoStoredTokens>(json);
                    if (value == null) throw new InvalidOperationException();
                }
                return true;
            }
            catch (Exception) { error = "Secure pairing storage could not be read. Pair this headset again."; return false; }
        }

        public bool TrySave(KinoStoredTokens value, out string error)
        {
            error = null;
            try
            {
                string json = JsonUtility.ToJson(value);
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var bridge = new AndroidJavaClass(JavaClass))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    bridge.CallStatic("write", activity, key, json);
#else
                SessionPayloads[key] = json;
#endif
                return true;
            }
            catch (Exception) { error = "Pairing credentials could not be saved securely."; return false; }
        }

        public bool TryClear(out string error)
        {
            error = null;
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var bridge = new AndroidJavaClass(JavaClass))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    bridge.CallStatic("clear", activity, key);
#else
                SessionPayloads.Remove(key);
#endif
                return true;
            }
            catch (Exception) { error = "Stored pairing credentials could not be cleared."; return false; }
        }
    }
}
