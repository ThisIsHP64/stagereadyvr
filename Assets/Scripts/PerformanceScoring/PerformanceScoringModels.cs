using System;
using System.Collections;

namespace StageReadyVR.PerformanceScoring
{
    /// <summary>Session metrics already captured by other systems (or test placeholders).</summary>
    [Serializable]
    public sealed class PresentationMetrics
    {
        public string transcript;
        public float durationSeconds;
        public float eyeContactPercent;
        public int fillerWordCount;
        public float wordsPerMinute;
        public float wpmStdDev;
        public float headStabilityPercent;
        public int longPauseCount;

        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                error = "There is no transcript to score.";
                return false;
            }
            if (transcript.Length > 40000)
            {
                error = "Transcript is too large for this prototype (max 40,000 characters).";
                return false;
            }
            if (!IsFinite(durationSeconds) || durationSeconds <= 0f)
            {
                error = "Session duration must be greater than zero.";
                return false;
            }
            if (!InPercentRange(eyeContactPercent) || !InPercentRange(headStabilityPercent))
            {
                error = "Eye-contact and head-stability percentages must be between 0 and 100.";
                return false;
            }
            if (fillerWordCount < 0 || longPauseCount < 0 || !IsFinite(wordsPerMinute) || wordsPerMinute < 0f
                || !IsFinite(wpmStdDev) || wpmStdDev < 0f)
            {
                error = "Filler counts, pauses, words-per-minute, and pace variability must be nonnegative.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool IsFinite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        private static bool InPercentRange(float x) => IsFinite(x) && x >= 0f && x <= 100f;

        public static PresentationMetrics CreateDemo()
        {
            return new PresentationMetrics
            {
                transcript = "Good afternoon everyone. Today I'll share three strategies for " +
                             "speaking clearly in public. First, prepare your main ideas. " +
                             "Second, make thoughtful eye contact. Third, practice with " +
                             "feedback. Um, with steady preparation we can all improve. " +
                             "Thank you for listening.",
                durationSeconds = 120f,
                eyeContactPercent = 82f,
                fillerWordCount = 4,
                wordsPerMinute = 138f,
                wpmStdDev = 15f,
                headStabilityPercent = 79f,
                longPauseCount = 1
            };
        }
    }

    [Serializable]
    public sealed class PerformanceScores
    {
        public int eyeContact;
        public int fillerWords;
        public int confidence;
        public int speechPacing;
        public string feedback;

        public bool IsValid()
        {
            return InRange(eyeContact) && InRange(fillerWords) && InRange(confidence) &&
                   InRange(speechPacing) && !string.IsNullOrWhiteSpace(feedback);
        }
        private static bool InRange(int value) => value >= 0 && value <= 100;
    }

    public interface IPerformanceScoringService
    {
        IEnumerator ScoreSession(PresentationMetrics metrics,
            Action<PerformanceScores> onSuccess, Action<string> onError);
    }
}
