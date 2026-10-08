using System.IO;
using UnityEngine;

namespace StageReadyVR.Feedback
{
    /// <summary>
    /// Orchestrates feedback at the end of a session. Hook BeginSession() from
    /// SessionManager when a run starts, AddEvent() during the run for timeline
    /// markers, and EndSession() when the user finishes. It pulls final metrics
    /// from the assigned sources, scores them, persists JSON for the study, and
    /// drives the DashboardPanel.
    /// </summary>
    public class FeedbackController : MonoBehaviour
    {
        [Header("Metric sources (assign your managers in the inspector)")]
        [SerializeField] private MonoBehaviour speechSource;     // ISpeechMetricsSource
        [SerializeField] private MonoBehaviour gazeSource;       // IGazeMetricsSource
        [SerializeField] private MonoBehaviour distractorSource; // IDistractorMetricsSource

        [Header("UI")]
        [SerializeField] private DashboardPanel dashboard;
        [SerializeField] private StageReadyVR.Coaching.CoachingService coachingService;

        [Header("Logging")]
        [SerializeField] private bool persistJson = true;

        private SessionReport _report;
        private float _startTime;
        private bool _active;

        private ISpeechMetricsSource Speech => speechSource as ISpeechMetricsSource;
        private IGazeMetricsSource Gaze => gazeSource as IGazeMetricsSource;
        private IDistractorMetricsSource Distractors => distractorSource as IDistractorMetricsSource;

        public SessionReport CurrentReport => _report;

        public void BeginSession(string environmentName, string audienceMode = "", string scenario = "")
        {
            _report = new SessionReport { 
                environmentName = environmentName,
                audienceMode = audienceMode,
                scenario = scenario
            };
            _startTime = Time.time;
            _active = true;
            if (dashboard != null) dashboard.Hide();
        }

        /// <summary>Call from analyzers during the run to drop a timeline marker.</summary>
        public void AddEvent(EventKind kind, string label)
        {
            if (!_active || _report == null) return;
            _report.AddEvent(Time.time - _startTime, kind, label);
        }

        public void EndSession()
        {
            if (!_active || _report == null) return;
            _active = false;
            _report.durationSeconds = Time.time - _startTime;

            PullMetrics();
            ScoreCalculator.Calculate(_report);

            if (persistJson) Persist(_report);
            if (dashboard != null) dashboard.Show(_report);
            // Fire the LLM coaching call; dashboard shows a loading state until
            // the response returns, then ShowCoaching populates it.
            if (coachingService != null)
            {
                coachingService.GetCoaching(_report, fb =>
                {
                    if (dashboard != null) dashboard.ShowCoaching(fb);
                    if (!fb.ok) { Debug.LogWarning("[Coaching] Failed: " + fb.error); return; }

                    // store coaching into the report and re-save the file
                    _report.coachingSummary = fb.summary;
                    _report.coachingStrengths = fb.strengths;
                    _report.coachingRecommendations = fb.recommendations;
                    if (persistJson) Persist(_report);
                });
            }
        }

        private void PullMetrics()
        {
            if (Speech != null)
            {
                _report.totalWords = Speech.TotalWords;
                _report.fillerWordCount = Speech.FillerWordCount;
                _report.averageWpm = Speech.AverageWpm;
                _report.wpmStdDev = Speech.WpmStdDev;
                _report.averageVolumeDb = Speech.AverageVolumeDb;
                _report.longPauseCount = Speech.LongPauseCount;
                _report.longestPauseSeconds = Speech.LongestPauseSeconds;
                _report.transcript = Speech.Transcript;
            }
            if (Gaze != null)
            {
                _report.eyeContactRatio = Gaze.EyeContactRatio;
                _report.gazeDriftCount = Gaze.GazeDriftCount;
                _report.headStability = Gaze.HeadStability;
            }
            if (Distractors != null)
            {
                _report.distractorsFired = Distractors.DistractorsFired;
                _report.distractorsRecoveredQuickly = Distractors.DistractorsRecoveredQuickly;
            }
        }

        private static void Persist(SessionReport report)
        {
            string dir = Path.Combine(Application.persistentDataPath, "sessions");
            Directory.CreateDirectory(dir);
            string stamp = report.startedAt.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = Path.Combine(dir, $"session_{stamp}.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log($"[Timing] ReportSaved path={path} " +
          $"bytes={new FileInfo(path).Length} " +
          $"totalReports={Directory.GetFiles(dir, "*.json").Length}");
        }

#if UNITY_EDITOR
        // Quick inspector hook for testing without a full run.
        [ContextMenu("Debug: Show Sample Report")]
        private void DebugSample()
        {
            var r = new SessionReport
            {
                environmentName = "Conference Hall",
                durationSeconds = 240f,
                totalWords = 520, fillerWordCount = 9, averageWpm = 148f, wpmStdDev = 22f,
                averageVolumeDb = -20f, longPauseCount = 3, longestPauseSeconds = 4.2f,
                eyeContactRatio = 0.72f, gazeDriftCount = 6, headStability = 0.81f,
                distractorsFired = 2, distractorsRecoveredQuickly = 2
            };
            r.AddEvent(12f, EventKind.FillerWord, "um");
            r.AddEvent(48f, EventKind.Highlight, "Steady eye contact");
            r.AddEvent(95f, EventKind.LongPause, "4.2s pause");
            r.AddEvent(150f, EventKind.Distractor, "Phone rang");
            ScoreCalculator.Calculate(r);
            if (dashboard != null) dashboard.Show(r);
        }
#endif
    }
}
