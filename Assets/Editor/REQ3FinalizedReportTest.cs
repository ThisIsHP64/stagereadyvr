using StageReadyVR.Feedback;
using StageReadyVR.PerformanceScoring;
using UnityEditor;
using UnityEngine;

/// <summary>Proves the actual SessionReport pipeline without needing VR assets.</summary>
public static class REQ3FinalizedReportTest
{
    [MenuItem("StageReadyVR/REQ-3/Test Finalized SessionReport (Play Mode)")]
    private static void TestReport()
    {
        var sample = PresentationMetrics.CreateDemo();
        var report = new SessionReport
        {
            environmentName = "REQ-3 Synthetic Integration Test",
            transcript = sample.transcript,
            durationSeconds = sample.durationSeconds,
            eyeContactRatio = sample.eyeContactPercent / 100f,
            fillerWordCount = sample.fillerWordCount,
            averageWpm = sample.wordsPerMinute,
            wpmStdDev = sample.wpmStdDev,
            headStability = sample.headStabilityPercent / 100f,
            longPauseCount = sample.longPauseCount,
            totalWords = 270
        };
        ScoreCalculator.Calculate(report);
        REQ3SessionScoringBridge.OnSessionEnded(report, null);
        Debug.Log("[REQ-3] Test submitted a SYNTHETIC SessionReport to the REAL scoring pipeline.");
    }

    [MenuItem("StageReadyVR/REQ-3/Test Finalized SessionReport (Play Mode)", true)]
    private static bool ValidateTestReport() { return EditorApplication.isPlaying; }
}
