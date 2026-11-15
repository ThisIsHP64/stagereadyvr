using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using StageReadyVR.Feedback;
namespace StageReadyVR.HybridScoring
{
    public static class LiveSessionRelay
    {
        [Serializable] public sealed class Config { public string baseUrl; public string devToken; }
        public static bool HasConfig => Resources.Load<TextAsset>("live_relay_config") != null;
        public static bool TryLoad(out Config c, out string error)
        {
            c = null; error = "Live relay not configured (live_relay_config.json).";
            if (!HasConfig) return false;
            if (!Application.isEditor && !Debug.isDebugBuild) { error = "Development relay is disabled in release builds."; return false; }
            try { c = JsonUtility.FromJson<Config>(Resources.Load<TextAsset>("live_relay_config").text); } catch (Exception) { }
            if (c == null || string.IsNullOrWhiteSpace(c.devToken) || !Uri.TryCreate(c.baseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                (uri.Scheme == "http" && !Debug.isDebugBuild && !Application.isEditor))
            { error = "Invalid relay configuration; release builds require HTTPS."; return false; }
            error = null; return true;
        }
        public static UnityWebRequest Request(Config c, string route, string method, string json)
        {
            var req = new UnityWebRequest(c.baseUrl.TrimEnd('/') + route, method);
            req.downloadHandler = new DownloadHandlerBuffer(); req.timeout = 65; req.redirectLimit = 0;
            req.SetRequestHeader("Authorization", "Bearer " + c.devToken.Trim());
            if (json != null) { req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)); req.SetRequestHeader("Content-Type", "application/json"); }
            return req;
        }
        public static UnityWebRequest CreateCoachingRequest(SessionReport report, out string error)
        {
            if (TryLoad(out var c, out error)) return Request(c, "/coaching", "POST", JsonUtility.ToJson(report));
#if UNITY_EDITOR
            if (!HasConfig) return HybridOpenAIClient.CreateRequest(report, Environment.GetEnvironmentVariable("OPENAI_API_KEY"), out error);
#endif
            return null;
        }
    }
}
