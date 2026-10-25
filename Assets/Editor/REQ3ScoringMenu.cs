using System.Collections;
using StageReadyVR.PerformanceScoring;
using UnityEditor;
using UnityEngine;

// Runs without any headset, VR environment, GameObject, scene, or Play Mode.
public static class REQ3ScoringMenu
{
    [MenuItem("StageReadyVR/REQ-3/Run Mock Scoring")]
    public static void RunSuccess()
    {
        Run(false);
    }

    [MenuItem("StageReadyVR/REQ-3/Simulate API Failure")]
    public static void RunFailure()
    {
        Run(true);
    }

    private static void Run(bool simulateFailure)
    {
        var service = new MockPerformanceScoringService(simulateFailure);
        IEnumerator coroutine = service.ScoreSession(
            PresentationMetrics.CreateDemo(),
            scores => Debug.Log(
                "[REQ-3] MOCK SCORING SUCCESS (not AI-generated)\n" +
                "Eye Contact: " + scores.eyeContact + "/100\n" +
                "Filler Words: " + scores.fillerWords + "/100\n" +
                "Confidence/Command: " + scores.confidence + "/100\n" +
                "Speech Pacing: " + scores.speechPacing + "/100\n" +
                "Coaching Feedback: " + scores.feedback),
            error => Debug.LogError("[REQ-3] " + error));

        // Mock service yields one frame but does not depend on Unity's scene loop.
        while (coroutine.MoveNext()) { }
    }
}
