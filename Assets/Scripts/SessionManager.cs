using System.Collections;
using TMPro;
using UnityEngine;
using StageReadyVR.Feedback;

/// <summary>
/// Manages the full session lifecycle:
/// scene load → "Get Ready" countdown → active session (with left peripheral
/// HUD showing time remaining) → session end → feedback dashboard.
/// Coordinates HeadMovementLogger, SpeechAnalyzer, and FeedbackController.
/// Implements IDistractorMetricsSource so the feedback module can pull
/// distractor handling stats.
/// </summary>
public class SessionManager : MonoBehaviour, IDistractorMetricsSource
{
    [Header("Tracking & Analysis")]
    public HeadMovementLogger headLogger;
    public SpeechAnalyzer speechAnalyzer;

    [Header("Feedback")]
    public FeedbackController feedbackController;

    [Header("UI - Countdown")]
    public GameObject countdownPanel;
    public TextMeshProUGUI countdownText;

    [Header("UI - Left Peripheral HUD")]
    [Tooltip("World Space canvas positioned to the player's left")]
    public GameObject sessionHUD;
    public TextMeshProUGUI timerText;
    [Tooltip("CenterEyeAnchor; HUD is positioned relative to this")]
    public Transform centerEyeAnchor;
    [Tooltip("How far left of the player's view the HUD sits")]
    public float hudLeftOffset = 0.5f;
    [Tooltip("How far in front the HUD sits")]
    public float hudForwardOffset = 1.2f;
    [Tooltip("Vertical offset so the HUD sits slightly below eye level")]
    public float hudVerticalOffset = -0.25f;

    [Header("Session")]
    public int sessionDuration;                 // seconds, read from PlayerPrefs
    public float countdownStartDelay = 1.5f;    // breath before "Get Ready"

    [Header("Environment")]
    [Tooltip("Display name used on the report; e.g. Conference Room")]

    [Header("Audience")]
    public AudienceManager audienceManager;

    public string environmentName = "Conference Room";

    private float timeRemaining;
    
    private bool sessionActive = false;
    private bool sessionEnded = false;

    // distractor tracking (wire your distractor system to these)
    private int distractorsFired = 0;
    private int distractorsRecoveredQuickly = 0;

    // ── IDistractorMetricsSource ──────────────────────────────────────
    public int DistractorsFired => distractorsFired;
    public int DistractorsRecoveredQuickly => distractorsRecoveredQuickly;

    /// <summary>Call from your distractor system when a distractor triggers.</summary>
    public void RegisterDistractor(bool recoveredQuickly)
    {
        distractorsFired++;
        if (recoveredQuickly) distractorsRecoveredQuickly++;
        feedbackController?.AddEvent(EventKind.Distractor, "Distractor fired");
    }

    // ── Lifecycle ─────────────────────────────────────────────────────

    void Start()
    {
        sessionDuration = PlayerPrefs.GetInt("SessionDuration", 5) * 60;
        timeRemaining = sessionDuration;

        if (countdownPanel != null) countdownPanel.SetActive(false);
        if (sessionHUD != null) sessionHUD.SetActive(false);

        // No marker — countdown begins automatically once the scene is up.
        StartCoroutine(Countdown());
    }

    IEnumerator Countdown()
    {
        if (countdownPanel != null) countdownPanel.SetActive(true);

        if (countdownText != null) countdownText.text = "Get Ready!";
        yield return new WaitForSeconds(countdownStartDelay);

        if (countdownText != null) countdownText.text = "Your speech starts in...";
        yield return new WaitForSeconds(1f);

        if (countdownText != null) countdownText.text = "3";
        yield return new WaitForSeconds(1f);
        if (countdownText != null) countdownText.text = "2";
        yield return new WaitForSeconds(1f);
        if (countdownText != null) countdownText.text = "1";
        yield return new WaitForSeconds(1f);
        if (countdownText != null) countdownText.text = "GO!";
        yield return new WaitForSeconds(0.5f);

        if (countdownPanel != null) countdownPanel.SetActive(false);
        StartSession();
    }

    void StartSession()
    {
        Debug.Log("[SessionManager] StartSession reached");
        sessionActive = true;
        sessionEnded = false;

        if (sessionHUD != null)
        {
            sessionHUD.SetActive(true);
            PositionHUD();
        }

        Debug.Log("[SessionManager] about to BeginSession, feedbackController null? " + (feedbackController == null));
        feedbackController?.BeginSession(environmentName, audienceManager != null ? audienceManager.audienceMode.ToString() : "",
    "");

        Debug.Log("[SessionManager] about to StartLogging, headLogger null? " + (headLogger == null));
        if (headLogger != null) headLogger.StartLogging();

        if (speechAnalyzer != null) speechAnalyzer.StartAnalysis();
    }

    void Update()
    {
        if (!sessionActive || sessionEnded) return;

        timeRemaining -= Time.deltaTime;
        UpdateTimerUI();

        // Manual end with A button.
        if (OVRInput.GetDown(OVRInput.Button.One))
            EndSession();

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndSession();
        }
    }

    // ── HUD ───────────────────────────────────────────────────────────

    /// <summary>
    /// Anchors the HUD to the player's left, at a fixed spot in world space
    /// captured at session start so it doesn't follow head rotation jitter.
    /// </summary>
    void PositionHUD()
    {
        if (sessionHUD == null || centerEyeAnchor == null) return;

        Vector3 forward = centerEyeAnchor.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 left = -centerEyeAnchor.right;
        left.y = 0f;
        left.Normalize();

        Vector3 pos = centerEyeAnchor.position
                      + forward * hudForwardOffset
                      + left * hudLeftOffset
                      + Vector3.up * hudVerticalOffset;

        sessionHUD.transform.position = pos;

        // Face the player.
        Vector3 lookDir = sessionHUD.transform.position - centerEyeAnchor.position;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.001f)
            sessionHUD.transform.rotation = Quaternion.LookRotation(lookDir);
    }

    void UpdateTimerUI()
    {
        if (timerText == null) return;
        int minutes = Mathf.FloorToInt(timeRemaining / 60f);
        int seconds = Mathf.FloorToInt(timeRemaining % 60f);
        timerText.text = string.Format("{0:00}:{1:00} left", minutes, seconds);
    }

    // ── End of session ────────────────────────────────────────────────

    public void EndSession()
    {
        if (sessionEnded) return;
        sessionActive = false;
        sessionEnded = true;

        if (sessionHUD != null) sessionHUD.SetActive(false);

        if (headLogger != null) headLogger.StopLogging();
        if (speechAnalyzer != null) speechAnalyzer.StopAnalysis();

        // audience reacts based on performance
        if (audienceManager != null && headLogger != null)
        {
            var metrics = headLogger.ComputeMetrics();
            audienceManager.OnSessionEnded(metrics.confidenceScore);
        }

        feedbackController?.EndSession();

        Debug.Log("[SessionManager] Session ended.");
    }

    // Hook these to the HUD buttons (or A/B controller buttons).
    public void RestartSession()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }

    public void ReturnToMainMenu()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }
}