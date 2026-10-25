using StageReadyVR.Feedback;
using UnityEngine;

namespace StageReadyVR.PerformanceScoring
{
    /// <summary>Maps the EXISTING project SessionReport to REQ-3 scoring inputs.</summary>
    public static class SessionReportScoringAdapter
    {
        public static PresentationMetrics FromReport(SessionReport report)
        {
            if (report == null) return null;
            return new PresentationMetrics
            {
                transcript = report.transcript,
                durationSeconds = report.durationSeconds,
                eyeContactPercent = Mathf.Clamp01(report.eyeContactRatio) * 100f,
                fillerWordCount = report.fillerWordCount,
                wordsPerMinute = report.averageWpm,
                wpmStdDev = report.wpmStdDev,
                headStabilityPercent = Mathf.Clamp01(report.headStability) * 100f,
                longPauseCount = report.longPauseCount
            };
        }
    }
}
