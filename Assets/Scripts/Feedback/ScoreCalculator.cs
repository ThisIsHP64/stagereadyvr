using UnityEngine;

namespace StageReadyVR.Feedback
{
    /// <summary>
    /// Turns raw SessionReport metrics into normalised 0..100 scores.
    /// All thresholds are public so they can be tuned per environment or
    /// per study condition without touching the math. Defaults are based on
    /// common public-speaking guidance (≈130-160 wpm ideal, <1 filler/min good).
    /// </summary>
    public static class ScoreCalculator
    {
        // --- tunable targets ---
        public const float IdealWpmLow = 130f;
        public const float IdealWpmHigh = 160f;
        public const float MaxWpmDeviation = 60f;   // wpm away from band => score 0
        public const float MaxFillersPerMin = 12f;   // at/above this => score 0
        public const float MaxPauseStdDev = 40f;    // wpm jitter for pace consistency
        public const float TargetVolumeDb = -18f;   // comfortable speaking RMS
        public const float VolumeToleranceDb = 12f;

        // --- weights for overall (sum = 1) ---
        public const float WPace = 0.20f;
        public const float WFiller = 0.25f;
        public const float WEye = 0.25f;
        public const float WVolume = 0.10f;
        public const float WComposure = 0.20f;


        // Volume readings at or below this are treated as "unavailable"
        // (mic volume sampling disabled), so volume is excluded from overall.
        public const float VolumeUnavailableDb = -79f;

        public static SessionScores Calculate(SessionReport r)
        {
            bool volumeAvailable = r.averageVolumeDb > VolumeUnavailableDb;

            var s = new SessionScores
            {
                pace = PaceScore(r),
                fillerControl = FillerScore(r),
                eyeContact = Mathf.Clamp01(r.eyeContactRatio) * 100f,
                volume = volumeAvailable ? VolumeScore(r) : 0f,
                composure = ComposureScore(r)
            };

            // Sum weighted scores; drop volume's weight when unavailable and
            // renormalize so the overall isn't penalized for a disabled metric.
            float wVolume = volumeAvailable ? WVolume : 0f;
            float totalWeight = WPace + WFiller + WEye + wVolume + WComposure;

            s.overall = (
                s.pace * WPace +
                s.fillerControl * WFiller +
                s.eyeContact * WEye +
                s.volume * wVolume +
                s.composure * WComposure
            ) / totalWeight;

            s.overall = Mathf.Round(s.overall);
            r.scores = s;
            return s;
        }

        static float PaceScore(SessionReport r)
        {
            // distance from the ideal band, then a consistency penalty
            float dist = 0f;
            if (r.averageWpm < IdealWpmLow) dist = IdealWpmLow - r.averageWpm;
            else if (r.averageWpm > IdealWpmHigh) dist = r.averageWpm - IdealWpmHigh;

            float bandScore = Mathf.Clamp01(1f - dist / MaxWpmDeviation);
            float consistency = Mathf.Clamp01(1f - r.wpmStdDev / MaxPauseStdDev);
            return Mathf.Lerp(bandScore, bandScore * consistency, 0.5f) * 100f;
        }

        static float FillerScore(SessionReport r)
        {
            float minutes = Mathf.Max(r.durationSeconds / 60f, 0.01f);
            float perMin = r.fillerWordCount / minutes;
            return Mathf.Clamp01(1f - perMin / MaxFillersPerMin) * 100f;
        }

        static float VolumeScore(SessionReport r)
        {
            float off = Mathf.Abs(r.averageVolumeDb - TargetVolumeDb);
            return Mathf.Clamp01(1f - off / VolumeToleranceDb) * 100f;
        }

        static float ComposureScore(SessionReport r)
        {
            float stability = Mathf.Clamp01(r.headStability) * 100f;
            if (r.distractorsFired <= 0) return stability;

            float recovery = (float)r.distractorsRecoveredQuickly / r.distractorsFired;
            return Mathf.Lerp(stability, stability * Mathf.Clamp01(recovery), 0.4f);
        }

        public static string Grade(float score)
        {
            if (score >= 85) return "Excellent";
            if (score >= 70) return "Strong";
            if (score >= 55) return "Developing";
            if (score >= 40) return "Needs work";
            return "Keep practicing";
        }
    }
}
