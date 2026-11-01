#if UNITY_EDITOR
using StageReadyVR.Coaching;
using StageReadyVR.Feedback;
using StageReadyVR.HybridScoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Edit-mode REQ-3 test panel. Works even in a blank Unity scene.
/// The API key is never saved to the project, EditorPrefs, or files.
/// </summary>
public class StageReadyVRHybridEditorWindow : EditorWindow
{
    private string editorApiKey = "";
    private float durationSeconds = 120f;
    private float audienceFacingPercent = 90f;
    private float headStabilityPercent = 90f;
    private float wordsPerMinute = 138f;
    private float wpmDeviation = 15f;
    private int fillerWordCount = 4;
    private float volumeDb = -20f;
    private int longPauses = 1;
    private string transcript = "";
    private string environmentName = "Conference Room";
    private bool gazeFromHeadset = false;
    private bool stabilityFromHeadset = false;

    private SessionReport preview;
    private CoachingFeedback coaching;
    private UnityWebRequest pending;
    private string status = "Enter sample metrics, then calculate and request coaching.";
    private Vector2 scroll;

    [MenuItem("StageReadyVR/REQ-3/Hybrid Scoring + AI Coaching (Option C)")]
    public static void Open()
    {
        GetWindow<StageReadyVRHybridEditorWindow>("REQ-3 Hybrid Coach").minSize = new Vector2(450, 560);
    }

    private void OnEnable() { EditorApplication.update += Tick; }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        if (pending != null) { pending.Dispose(); pending = null; }
        editorApiKey = "";
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("StageReadyVR | Option C", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Unity calculates all 0-100 scores. OpenAI provides the summary, strengths and recommendations, not a second set of scores. The values below are DEMO inputs unless loaded from an active Play Mode session.", MessageType.Info);

        if (GUILayout.Button("Load active scene's current SessionReport (Play Mode)")) LoadActiveSession();
        GUILayout.Space(6f);

        environmentName = EditorGUILayout.TextField("Environment", environmentName);
        durationSeconds = EditorGUILayout.FloatField("Duration (seconds)", durationSeconds);
        audienceFacingPercent = EditorGUILayout.Slider("Audience-facing (%)", audienceFacingPercent, 0f, 100f);
        headStabilityPercent = EditorGUILayout.Slider("Head stability (%)", headStabilityPercent, 0f, 100f);
        wordsPerMinute = EditorGUILayout.FloatField("Speech pace (WPM)", wordsPerMinute);
        wpmDeviation = EditorGUILayout.FloatField("WPM standard deviation", wpmDeviation);
        fillerWordCount = EditorGUILayout.IntField("Filler words (count)", fillerWordCount);
        volumeDb = EditorGUILayout.Slider("Volume (dBFS)", volumeDb, -79f, -1f);
        longPauses = EditorGUILayout.IntField("Long pauses", longPauses);
        EditorGUILayout.LabelField("Optional transcript (leave empty if missing)");
        transcript = EditorGUILayout.TextArea(transcript, GUILayout.MinHeight(65));

        if (GUILayout.Button("1. Calculate Hybrid Score Locally", GUILayout.Height(32))) Calculate();

        if (preview != null)
        {
            var s = preview.scores;
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("UNITY SCORE: " + Mathf.RoundToInt(s.overall) + "/100  —  " + ScoreCalculator.Grade(s.overall), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Pace " + Mathf.RoundToInt(s.pace) + " | Filler " + Mathf.RoundToInt(s.fillerControl) + " | Facing " + Mathf.RoundToInt(s.eyeContact));
            EditorGUILayout.LabelField("Volume " + Mathf.RoundToInt(s.volume) + " | Composure " + Mathf.RoundToInt(s.composure));
            EditorGUILayout.HelpBox("WPM, filler words and volume are manually provided demo measurements. Audience-facing and composure are also demo unless loaded directly from the running scene.", MessageType.Warning);
        }

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("OpenAI (Unity Editor ONLY)", EditorStyles.boldLabel);
        editorApiKey = EditorGUILayout.PasswordField("OpenAI API key", editorApiKey);
        if (GUILayout.Button("Use this key for Play Mode (until Unity closes)"))
        {
            if (string.IsNullOrWhiteSpace(editorApiKey))
                status = "Enter your API key first.";
            else
            {
                System.Environment.SetEnvironmentVariable("OPENAI_API_KEY", editorApiKey.Trim());
                status = "API key configured in this Unity Editor process (not saved to disk).";
            }
        }
        EditorGUI.BeginDisabledGroup(pending != null);
        if (GUILayout.Button("2. Generate REAL AI Coaching", GUILayout.Height(36))) RequestCoaching();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox(status, pending != null ? MessageType.Info : MessageType.None);
        if (coaching != null)
        {
            if (coaching.ok)
            {
                EditorGUILayout.LabelField("AI Coaching — Success", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(coaching.summary, EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Strengths", EditorStyles.boldLabel);
                foreach (var item in coaching.strengths) EditorGUILayout.LabelField("• " + item, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Recommendations", EditorStyles.boldLabel);
                foreach (var item in coaching.recommendations) EditorGUILayout.LabelField("• " + item, EditorStyles.wordWrappedLabel);
            }
            else EditorGUILayout.HelpBox(coaching.error, MessageType.Error);
        }
        EditorGUILayout.EndScrollView();
    }

    private void LoadActiveSession()
    {
        if (!EditorApplication.isPlaying)
        {
            status = "Enter Play Mode and start or finish a real session first. Otherwise, use the demo fields.";
            return;
        }
        var fc = Object.FindObjectOfType<FeedbackController>();
        var r = fc != null ? fc.CurrentReport : null;
        if (r == null)
        {
            status = "No live SessionReport found. Is FeedbackController present and a session started?";
            return;
        }
        environmentName = r.environmentName;
        durationSeconds = Mathf.Max(1f, r.durationSeconds);
        audienceFacingPercent = Mathf.Clamp01(r.eyeContactRatio) * 100f;
        headStabilityPercent = Mathf.Clamp01(r.headStability) * 100f;
        gazeFromHeadset = true;
        stabilityFromHeadset = true;
        if (r.averageWpm > 0f) { wordsPerMinute = r.averageWpm; wpmDeviation = r.wpmStdDev; }
        if (r.totalWords > 0) fillerWordCount = r.fillerWordCount;
        if (r.averageVolumeDb < 0f && r.averageVolumeDb > -79f) volumeDb = r.averageVolumeDb;
        longPauses = r.longPauseCount;
        transcript = r.transcript ?? "";
        status = "Loaded running SessionReport. Speech/volume fields are editable demonstration overrides.";
        preview = null;
        coaching = null;
    }

    private void Calculate()
    {
        if (durationSeconds <= 0f || wordsPerMinute < 0f || wpmDeviation < 0f || fillerWordCount < 0 || longPauses < 0)
        {
            status = "Duration must be positive; word speed, pace variation, fillers and pauses cannot be negative.";
            preview = null;
            return;
        }
        preview = new SessionReport
        {
            environmentName = environmentName,
            durationSeconds = durationSeconds,
            eyeContactRatio = Mathf.Clamp01(audienceFacingPercent / 100f),
            headStability = Mathf.Clamp01(headStabilityPercent / 100f),
            averageWpm = wordsPerMinute,
            wpmStdDev = wpmDeviation,
            totalWords = Mathf.RoundToInt(wordsPerMinute * durationSeconds / 60f),
            fillerWordCount = fillerWordCount,
            averageVolumeDb = volumeDb,
            longPauseCount = longPauses,
            transcript = transcript ?? "",
            hybridPaceSource = "manual_demo",
            hybridFillerSource = "manual_demo",
            hybridVolumeSource = "manual_demo",
            hybridAudienceFacingSource = gazeFromHeadset ? "live" : "demo",
            hybridComposureSource = stabilityFromHeadset ? "live" : "demo"
        };
        ScoreCalculator.Calculate(preview);
        status = "Computed " + preview.scores.overall + "/100 locally. No API call made yet.";
        coaching = null;
    }

    private void RequestCoaching()
    {
        // Always calculate from the currently displayed fields first.
        Calculate();
        if (preview == null) return;
        string key = string.IsNullOrWhiteSpace(editorApiKey)
            ? System.Environment.GetEnvironmentVariable("OPENAI_API_KEY") : editorApiKey;
        string error;
        pending = HybridOpenAIClient.CreateRequest(preview, key, out error);
        if (pending == null) { status = error; return; }
        status = "Calling OpenAI " + HybridOpenAIClient.Model + " for coaching…";
        pending.SendWebRequest();
    }

    private void Tick()
    {
        if (pending == null || !pending.isDone) return;
        coaching = HybridOpenAIClient.ReadResponse(pending);
        status = coaching.ok ? "Real API coaching returned successfully." : "API coaching failed: " + coaching.error;
        pending.Dispose();
        pending = null;
        Repaint();
    }
}
#endif
