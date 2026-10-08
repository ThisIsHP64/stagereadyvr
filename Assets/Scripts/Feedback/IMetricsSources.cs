namespace StageReadyVR.Feedback
{
    /// <summary>
    /// Implement these on your existing managers so FeedbackController can pull
    /// final numbers at session end without hard references. Keeps the feedback
    /// module decoupled from SpeechAnalyzer / HeadMovementLogger internals.
    /// </summary>
    public interface ISpeechMetricsSource
    {
        int TotalWords { get; }
        int FillerWordCount { get; }
        float AverageWpm { get; }
        float WpmStdDev { get; }
        float AverageVolumeDb { get; }
        int LongPauseCount { get; }
        float LongestPauseSeconds { get; }
        string Transcript { get; }
    }

    public interface IGazeMetricsSource
    {
        float EyeContactRatio { get; }   // 0..1
        float GazeDriftCount { get; }
        float HeadStability { get; }     // 0..1
    }

    public interface IDistractorMetricsSource
    {
        int DistractorsFired { get; }
        int DistractorsRecoveredQuickly { get; }
    }
}
