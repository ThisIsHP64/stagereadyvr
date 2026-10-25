using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using StageReadyVR.Feedback;
using UnityEngine;

namespace StageReadyVR.PerformanceScoring
{
    /// <summary>
    /// REQ-3: score the finalized REAL SessionReport once when a presentation ends.
    /// The runner survives scene changes. Direct OpenAI use is restricted to the
    /// Unity Editor prototype; a PICO build needs a secure backend/relay.
    /// </summary>
    public sealed class REQ3SessionScoringBridge : MonoBehaviour
    {
        private static REQ3SessionScoringBridge instance;
        private readonly HashSet<string> startedSessionIds = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; }

        public static void OnSessionEnded(SessionReport report, FeedbackController owner)
        {
            if (report == null)
            {
                Debug.LogWarning("[REQ-3] No finalized SessionReport; scoring was skipped.");
                return;
            }
            if (instance == null)
            {
                var gameObject = new GameObject("REQ-3 Session Scoring Runner");
                DontDestroyOnLoad(gameObject);
                instance = gameObject.AddComponent<REQ3SessionScoringBridge>();
            }
            instance.Begin(report, owner);
        }

        private void Begin(SessionReport report, FeedbackController owner)
        {
            string id = string.IsNullOrEmpty(report.sessionId) ? "report-" + report.GetHashCode() : report.sessionId;
            if (!startedSessionIds.Add(id))
            {
                Debug.LogWarning("[REQ-3] Duplicate session scoring request ignored.");
                return;
            }

            report.req3AiScores = null;
            report.req3AiStatus = "pending";
            report.req3AiError = "";
            Commit(report, owner);
            StartCoroutine(Score(report, owner));
        }

        private IEnumerator Score(SessionReport report, FeedbackController owner)
        {
            var metrics = SessionReportScoringAdapter.FromReport(report);
            string validationError = null;
            if (metrics == null || !metrics.TryValidate(out validationError))
            {
                Fail(report, owner, metrics == null ? "Session report was unavailable." : validationError);
                yield break;
            }

#if UNITY_EDITOR
            // Set only in the running Unity Editor process using the included
            // StageReadyVR > REQ-3 > Set Editor Session API Key window.
            string key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(key))
            {
                Fail(report, owner, "OpenAI API key not configured for this Unity Editor session.");
                yield break;
            }
            var client = new OpenAIPerformanceScoringService(key);
            Debug.Log("[REQ-3] Scoring finalized SessionReport using GPT-6 Luna.");
            yield return StartCoroutine(client.ScoreSession(metrics,
                scores =>
                {
                    report.req3AiScores = scores;
                    report.req3AiStatus = "completed";
                    report.req3AiError = "";
                    Commit(report, owner);
                    Debug.Log("[REQ-3] SESSION REPORT SAVED — GPT-6 Luna SUCCESS\n" +
                              "Eye Contact: " + scores.eyeContact + "/100\n" +
                              "Filler Words: " + scores.fillerWords + "/100\n" +
                              "Confidence/Command: " + scores.confidence + "/100\n" +
                              "Speech Pacing: " + scores.speechPacing + "/100\n" +
                              "Feedback: " + scores.feedback);
                },
                error => Fail(report, owner, error)));
#else
            // Do not bundle an OpenAI key in the APK. Implement a server-side
            // authenticated relay before turning on real headset requests.
            Fail(report, owner, "AI scoring is not configured for this headset build. A secure relay is required.");
            yield break;
#endif
        }

        private static void Fail(SessionReport report, FeedbackController owner, string message)
        {
            report.req3AiScores = null;
            report.req3AiStatus = "failed";
            report.req3AiError = string.IsNullOrWhiteSpace(message) ? "AI scoring unavailable. Please try again." : message;
            Commit(report, owner);
            Debug.LogWarning("[REQ-3] AI scoring unavailable: " + report.req3AiError);
        }

        private static void Commit(SessionReport report, FeedbackController owner)
        {
            // Match FeedbackController's existing session filename so both
            // the original scores and additional REQ-3 scores live together.
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "sessions");
                Directory.CreateDirectory(directory);
                string stamp = report.startedAt.ToString("yyyy-MM-dd_HH-mm-ss");
                string path = Path.Combine(directory, "session_" + stamp + ".json");
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                Debug.Log("[REQ-3] Report status=" + report.req3AiStatus + "; saved to " + path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[REQ-3] Unable to save scoring report: " + ex.GetType().Name);
            }
            if (owner != null)
                owner.ShowReq3Status(report);
        }
    }
}
