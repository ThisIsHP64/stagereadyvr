using System;
using System.Collections;
using StageReadyVR.PerformanceScoring;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity Editor-only live AI test. No PICO scene, headset, or Play Mode needed.
/// The key is held in this window's memory and never written to Assets or project settings.
/// </summary>
public sealed class REQ3LiveScoringWindow : EditorWindow
{
    private string key = "";
    private string transcript = PresentationMetrics.CreateDemo().transcript;
    private float durationSeconds = 120f;
    private float eyeContact = 82f;
    private int fillerWords = 4;
    private float wordsPerMinute = 138f;
    private float wpmStdDev = 15f;
    private float headStability = 79f;
    private int longPauses = 1;
    private string status = "Ready. Mock mode costs nothing; real mode calls the OpenAI API.";

    private IEnumerator running;
    private AsyncOperation waitingFor;
    private bool completedWithCallback;

    [MenuItem("StageReadyVR/REQ-3/Live AI Scoring (No VR)")]
    public static void Open()
    {
        var window = GetWindow<REQ3LiveScoringWindow>("REQ-3 GPT-6 Luna");
        window.minSize = new Vector2(500f, 510f);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("REQ-3 — Real GPT-6 Luna scoring", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Paste an OpenAI API key for this Editor test. It is never committed or added to your APK. A production headset build needs a backend relay.", MessageType.Info);
        key = EditorGUILayout.PasswordField("OpenAI API key", key);
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Sample presentation transcript", EditorStyles.boldLabel);
        transcript = EditorGUILayout.TextArea(transcript, GUILayout.MinHeight(95));
        durationSeconds = EditorGUILayout.FloatField("Duration (seconds)", durationSeconds);
        eyeContact = EditorGUILayout.Slider("Audience-facing time (%)", eyeContact, 0f, 100f);
        fillerWords = EditorGUILayout.IntField("Filler word count", fillerWords);
        wordsPerMinute = EditorGUILayout.FloatField("Words per minute", wordsPerMinute);
        wpmStdDev = EditorGUILayout.FloatField("WPM standard deviation", wpmStdDev);
        headStability = EditorGUILayout.Slider("Head stability (%)", headStability, 0f, 100f);
        longPauses = EditorGUILayout.IntField("Long pause count", longPauses);
        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(running != null))
        {
            if (GUILayout.Button("RUN LIVE GPT-6 LUNA (API CALL)", GUILayout.Height(38))) RunLive();
            if (GUILayout.Button("Run Mock (no API call)")) RunMock(false);
            if (GUILayout.Button("Simulate Scoring Failure")) RunMock(true);
        }
        EditorGUILayout.HelpBox(status, MessageType.None);
        EditorGUILayout.LabelField("Results appear in Window > General > Console.", EditorStyles.miniLabel);
    }

    private PresentationMetrics MakeMetrics()
    {
        return new PresentationMetrics
        {
            transcript = transcript,
            durationSeconds = durationSeconds,
            eyeContactPercent = eyeContact,
            fillerWordCount = fillerWords,
            wordsPerMinute = wordsPerMinute,
            wpmStdDev = wpmStdDev,
            headStabilityPercent = headStability,
            longPauseCount = longPauses
        };
    }

    private void RunLive()
    {
        string suppliedKey = string.IsNullOrWhiteSpace(key)
            ? Environment.GetEnvironmentVariable("OPENAI_API_KEY") : key;
        if (string.IsNullOrWhiteSpace(suppliedKey))
        {
            status = "Enter an API key or set the OPENAI_API_KEY environment variable.";
            Debug.LogWarning("[REQ-3] Missing OpenAI API key.");
            Repaint();
            return;
        }
        StartRequest(new OpenAIPerformanceScoringService(suppliedKey.Trim()), "LIVE GPT-6 LUNA");
    }

    private void RunMock(bool fail)
    {
        StartRequest(new MockPerformanceScoringService(fail), "MOCK (not AI)");
    }

    private void StartRequest(IPerformanceScoringService service, string label)
    {
        if (running != null) return;
        completedWithCallback = false;
        PresentationMetrics metrics = MakeMetrics();
        if (!metrics.TryValidate(out string error))
        {
            status = error;
            Debug.LogError("[REQ-3] " + error);
            return;
        }
        status = "Scoring " + label + " ...";
        Debug.Log("[REQ-3] Started " + label + " scoring.");
        running = service.ScoreSession(metrics,
            scores =>
            {
                completedWithCallback = true;
                status = label + " successful. See the Unity Console.";
                Debug.Log("[REQ-3] " + label + " SUCCESS\n" +
                    "Eye Contact: " + scores.eyeContact + "/100\n" +
                    "Filler Words: " + scores.fillerWords + "/100\n" +
                    "Confidence/Command: " + scores.confidence + "/100\n" +
                    "Speech Pacing: " + scores.speechPacing + "/100\n" +
                    "Feedback: " + scores.feedback);
            },
            error =>
            {
                completedWithCallback = true;
                status = "Scoring failed: " + error;
                Debug.LogError("[REQ-3] " + error);
            });
        waitingFor = null;
        EditorApplication.update += Tick;
    }

    private void Tick()
    {
        if (running == null) { EditorApplication.update -= Tick; return; }
        if (waitingFor != null && !waitingFor.isDone) return;
        waitingFor = null;
        try
        {
            if (!running.MoveNext())
            {
                if (!completedWithCallback) status = "Scoring ended without a result.";
                StopRunner();
            }
            else if (running.Current is AsyncOperation op)
            {
                waitingFor = op;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[REQ-3] Scoring failed unexpectedly: " + ex.GetType().Name);
            status = "Unexpected scoring error; see Console.";
            StopRunner();
        }
        Repaint();
    }

    private void StopRunner()
    {
        EditorApplication.update -= Tick;
        (running as IDisposable)?.Dispose();
        running = null;
        waitingFor = null;
    }

    private void OnDisable()
    {
        // Unsubscribe and dispose in-flight request if the Editor window closes.
        StopRunner();
        key = "";
    }
}
