using System;
using System.Collections;
using StageReadyVR.Feedback;
using UnityEngine;

namespace StageReadyVR.PerformanceScoring
{
    /// <summary>Optional MonoBehaviour integration for testing or VR session end.</summary>
    public sealed class PerformanceScoringDemo : MonoBehaviour
    {
        [Header("REQ-3 settings (Editor-only direct-key testing)")]
        [SerializeField] private bool runDemoOnStart = false;
        [SerializeField] private bool useRealOpenAI = false;
        [SerializeField] private bool simulateMockFailure = false;
        private bool isScoring;

        private void Start()
        {
            if (runDemoOnStart) OnSessionEnded(PresentationMetrics.CreateDemo());
        }

        public void OnSessionEnded(SessionReport report)
        {
            OnSessionEnded(SessionReportScoringAdapter.FromReport(report));
        }

        public void OnSessionEnded(PresentationMetrics metrics)
        {
            if (isScoring)
            {
                Debug.LogWarning("[REQ-3] Already scoring a session; duplicate request ignored.");
                return;
            }
            StartCoroutine(ScoreRoutine(metrics));
        }

        private IEnumerator ScoreRoutine(PresentationMetrics metrics)
        {
            isScoring = true;
            bool mock = !useRealOpenAI;
            // Only for Editor testing: OS variable is not available by default in headset APK.
            var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            IPerformanceScoringService service = mock
                ? (IPerformanceScoringService)new MockPerformanceScoringService(simulateMockFailure)
                : new OpenAIPerformanceScoringService(key);
            Debug.Log(mock ? "[REQ-3] Running MOCK scoring." : "[REQ-3] Requesting REAL GPT-6 Luna scoring...");
            yield return StartCoroutine(service.ScoreSession(metrics,
                scores => Debug.Log("[REQ-3] " + (mock ? "MOCK" : "REAL GPT-6 LUNA") + " SUCCESS\n" +
                    "Eye Contact: " + scores.eyeContact + "/100\n" +
                    "Filler Words: " + scores.fillerWords + "/100\n" +
                    "Confidence/Command: " + scores.confidence + "/100\n" +
                    "Speech Pacing: " + scores.speechPacing + "/100\n" +
                    "Feedback: " + scores.feedback),
                error => Debug.LogError("[REQ-3] " + error)));
            isScoring = false;
        }
    }
}
