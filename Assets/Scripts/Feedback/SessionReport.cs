using System;
using System.Collections.Generic;
using UnityEngine;

namespace StageReadyVR.Feedback
{
    /// <summary>
    /// A single notable moment during a session (filler word, long pause,
    /// gaze drift, distractor event). Rendered as a marker on the timeline.
    /// </summary>
    [Serializable]
    public struct TimelineEvent
    {
        public float timeSeconds;     // offset from session start
        public EventKind kind;
        public string label;          // short human-readable note

        public TimelineEvent(float t, EventKind kind, string label)
        {
            this.timeSeconds = t;
            this.kind = kind;
            this.label = label;
        }
    }

    public enum EventKind
    {
        FillerWord,
        LongPause,
        GazeDrift,      // looked away from audience too long
        Distractor,     // an external distractor fired
        PaceSpike,      // spoke too fast
        Highlight       // a good moment (sustained eye contact, steady pace)
    }

    /// <summary>
    /// Raw metrics for one practice session. Populated by SpeechAnalyzer,
    /// HeadMovementLogger and SessionManager during the run, then handed to
    /// ScoreCalculator and the dashboard at the end.
    /// </summary>
    [Serializable]
    public class SessionReport
    {
        public string sessionId = Guid.NewGuid().ToString("N");
        public DateTime startedAt = DateTime.Now;
        public string environmentName = "Boardroom";

        public string audienceMode = "";     // Supportive / Distracting / Mixed
        public string scenario = "";          // e.g. "job interview", "conference talk"

        // --- duration ---
        public float durationSeconds;

        // --- speech (from SpeechAnalyzer) ---
        public int totalWords;
        public int fillerWordCount;
        public float averageWpm;
        public float wpmStdDev;             // pace consistency; lower is steadier
        public float averageVolumeDb;       // mic RMS in dBFS, negative
        public int longPauseCount;          // pauses over PauseThreshold
        public float longestPauseSeconds;

        // --- gaze / posture (from HeadMovementLogger) ---
        public float eyeContactRatio;       // 0..1, time facing audience
        public float gazeDriftCount;        // distinct look-away episodes
        public float headStability;         // 0..1, 1 = very still (jitter low)

        // --- distractors (from SessionManager) ---
        public int distractorsFired;
        public int distractorsRecoveredQuickly;

        public string transcript = "";

        // --- LLM coaching (populated after the API responds) ---
        public string coachingSummary = "";
        public string[] coachingStrengths;
        public string[] coachingRecommendations;

        // --- computed by ScoreCalculator (filled in post-session) ---
        public SessionScores scores;

        public List<TimelineEvent> events = new List<TimelineEvent>();

        public void AddEvent(float t, EventKind kind, string label)
            => events.Add(new TimelineEvent(t, kind, label));
    }

    /// <summary>
    /// Normalised 0..100 scores. Overall is a weighted blend of the sub-scores.
    /// </summary>
    [Serializable]
    public struct SessionScores
    {
        public float overall;
        public float pace;
        public float fillerControl;
        public float eyeContact;
        public float volume;
        public float composure;   // steadiness + distractor recovery
    }
}
