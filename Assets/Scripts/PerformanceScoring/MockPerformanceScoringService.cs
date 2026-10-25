using System;
using System.Collections;
using UnityEngine;

namespace StageReadyVR.PerformanceScoring
{
    /// <summary>Offline placeholder; NEVER invokes an AI API.</summary>
    public sealed class MockPerformanceScoringService : IPerformanceScoringService
    {
        private readonly bool simulateFailure;
        public MockPerformanceScoringService(bool simulateFailure = false) => this.simulateFailure = simulateFailure;

        public IEnumerator ScoreSession(PresentationMetrics metrics,
            Action<PerformanceScores> onSuccess, Action<string> onError)
        {
            yield return null;
            string validationError = null;
            if (metrics == null || !metrics.TryValidate(out validationError))
            {
                onError?.Invoke(metrics == null ? "No session data was provided." : validationError);
                yield break;
            }
            if (simulateFailure)
            {
                onError?.Invoke("Performance scoring is temporarily unavailable (simulated failure). Please try again.");
                yield break;
            }
            float fillersPerMin = metrics.fillerWordCount / (metrics.durationSeconds / 60f);
            var scores = new PerformanceScores
            {
                eyeContact = Mathf.Clamp(Mathf.RoundToInt(metrics.eyeContactPercent), 0, 100),
                fillerWords = Mathf.Clamp(Mathf.RoundToInt(100f - fillersPerMin / 12f * 100f), 0, 100),
                confidence = Mathf.Clamp(Mathf.RoundToInt(metrics.headStabilityPercent), 0, 100),
                speechPacing = Mathf.Clamp(Mathf.RoundToInt(100f - Mathf.Abs(metrics.wordsPerMinute - 145f) * 0.8f
                                  - metrics.wpmStdDev * 0.35f), 0, 100),
                feedback = "MOCK ONLY: Reduce filler words, maintain steady pacing, and use intentional pauses."
            };
            onSuccess?.Invoke(scores);
        }
    }
}
