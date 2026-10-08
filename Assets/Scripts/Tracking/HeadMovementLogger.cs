using UnityEngine;
using System.Collections.Generic;
using StageReadyVR.Feedback;

/// <summary>
/// Captures and analyzes head movement data from the VR camera during a session.
/// Samples the CenterEyeAnchor transform at a configurable rate to derive
/// orientation metrics, stability scores, and gaze behavior.
/// Implements IGazeMetricsSource so the feedback module can pull final values.
/// </summary>
public class HeadMovementLogger : MonoBehaviour, IGazeMetricsSource
{
    [Header("References")]
    [Tooltip("The CenterEyeAnchor transform from the OVR Camera Rig")]
    public Transform centerEyeAnchor;

    [Header("Sampling Settings")]
    [Tooltip("How often to capture a head sample in seconds (0.1 = 10 times per second)")]
    public float sampleRate = 0.1f;

    [Header("Gaze Settings")]
    [Tooltip("Angle threshold (degrees) within which the player is considered facing the audience")]
    public float gazeAudienceThreshold = 45f;
    [Tooltip("Pitch angle below which the player is considered looking down (at notes/floor)")]
    public float lookDownThreshold = -15f;

    // ── Internal State ────────────────────────────────────────────────
    private List<HeadSample> samples = new List<HeadSample>();
    private bool isLogging = false;
    private float sampleTimer = 0f;
    private float sessionStartTime = 0f;
    private HeadMetrics cachedMetrics;

    // ── Data Structures ───────────────────────────────────────────────

    /// <summary>
    /// A single head orientation snapshot captured during the session.
    /// </summary>
    [System.Serializable]
    public class HeadSample
    {
        public float timestamp;     // Time since session start (seconds)
        public float pitch;         // Up/down rotation (X axis)
        public float yaw;           // Left/right rotation (Y axis)
        public float roll;          // Tilt rotation (Z axis)
        public Vector3 forward;     // World-space forward direction of the head
    }

    /// <summary>
    /// Computed summary of all head movement data collected during the session.
    /// </summary>
    public class HeadMetrics
    {
        public int totalSamples;
        public float sessionDuration;           // Total logged duration in seconds

        // Stability
        public float headStabilityScore;        // 0-1, higher = more stable
        public float composureScore;            // 0-1, derived from overall variance

        // Rotation variance
        public float pitchVariance;             // How much head nodded up/down
        public float yawVariance;               // How much head shook left/right
        public float rollVariance;              // How much head tilted

        // Gaze behavior
        public float gazeStabilityScore;        // 0-1, how steady the gaze direction was
        public float eyeContactPercentage;      // % of time facing toward audience
        public float lookingDownPercentage;     // % of time looking down (notes/floor)

        // Peak movements
        public float peakPitchMovement;         // Largest single-frame pitch change
        public float peakYawMovement;           // Largest single-frame yaw change

        // Confidence score (composite)
        public float confidenceScore;           // 0-1 weighted composite metric
    }

    // ── IGazeMetricsSource implementation ─────────────────────────────

    public float EyeContactRatio =>
        cachedMetrics != null ? cachedMetrics.eyeContactPercentage / 100f : 0f;

    public float HeadStability =>
        cachedMetrics != null ? cachedMetrics.headStabilityScore : 0f;

    public float GazeDriftCount => CountGazeDrifts();

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// Begins a new logging session. Clears any previous data.
    /// Called by SessionManager when the session starts.
    /// </summary>
    public void StartLogging()
    {
        if (centerEyeAnchor == null)
        {
            Camera cam = Camera.main;

            if (cam != null)
            {
                centerEyeAnchor = cam.transform;
                Debug.Log("Reacquired camera");
            }
        }

        samples.Clear();
        sampleTimer = 0f;
        cachedMetrics = null;
        sessionStartTime = Time.time;
        isLogging = true;
    }

    /// <summary>
    /// Stops the logging session and caches computed metrics so the feedback
    /// module can read them immediately afterward.
    /// Called by SessionManager when the session ends.
    /// </summary>
    public void StopLogging()
    {
        isLogging = false;
        cachedMetrics = ComputeMetrics();
        Debug.Log($"[HeadMovementLogger] Logging stopped. {samples.Count} samples collected.");
    }

    /// <summary>
    /// Returns the raw list of head samples captured during the session.
    /// </summary>
    public List<HeadSample> GetSamples() => samples;

    /// <summary>
    /// Computes and returns all derived head movement metrics from the session data.
    /// Should be called after StopLogging().
    /// </summary>
    public HeadMetrics ComputeMetrics()
    {
        HeadMetrics metrics = new HeadMetrics();

        if (samples.Count < 2)
        {
            Debug.LogWarning("[HeadMovementLogger] Not enough samples to compute metrics.");
            return metrics;
        }

        metrics.totalSamples = samples.Count;
        metrics.sessionDuration = samples[samples.Count - 1].timestamp - samples[0].timestamp;

        // ── Variance Calculation ──────────────────────────────────────
        float pitchSum = 0f, yawSum = 0f, rollSum = 0f;
        float peakPitch = 0f, peakYaw = 0f;
        float gazeDeviationSum = 0f;
        int audienceFacingCount = 0;
        int lookingDownCount = 0;

        for (int i = 1; i < samples.Count; i++)
        {
            // Frame-to-frame deltas
            float pitchDelta = Mathf.Abs(NormalizeAngle(samples[i].pitch) - NormalizeAngle(samples[i - 1].pitch));
            float yawDelta = Mathf.Abs(NormalizeAngle(samples[i].yaw) - NormalizeAngle(samples[i - 1].yaw));
            float rollDelta = Mathf.Abs(NormalizeAngle(samples[i].roll) - NormalizeAngle(samples[i - 1].roll));

            pitchSum += pitchDelta;
            yawSum += yawDelta;
            rollSum += rollDelta;

            // Track peak single-frame movements
            if (pitchDelta > peakPitch) peakPitch = pitchDelta;
            if (yawDelta > peakYaw) peakYaw = yawDelta;

            // Gaze deviation from forward (audience-facing direction)
            float gazeDeviation = Vector3.Angle(samples[i].forward, Vector3.forward);
            gazeDeviationSum += gazeDeviation;

            // Eye contact — within threshold angle of facing audience
            if (gazeDeviation <= gazeAudienceThreshold)
                audienceFacingCount++;

            // Looking down — pitch below threshold
            if (NormalizeAngle(samples[i].pitch) < lookDownThreshold)
                lookingDownCount++;
        }

        int frameCount = samples.Count - 1;

        // Average variances per frame
        metrics.pitchVariance = pitchSum / frameCount;
        metrics.yawVariance = yawSum / frameCount;
        metrics.rollVariance = rollSum / frameCount;
        metrics.peakPitchMovement = peakPitch;
        metrics.peakYawMovement = peakYaw;

        // ── Stability Scores ──────────────────────────────────────────

        // Head stability: lower variance = higher score
        // Normalized against 45 degrees as "very unstable"
        float totalVariance = (metrics.pitchVariance + metrics.yawVariance) / 2f;
        metrics.headStabilityScore = Mathf.Clamp01(1f - (totalVariance / 45f));

        // Composure: penalizes sudden large movements
        float peakPenalty = Mathf.Clamp01((peakPitch + peakYaw) / 90f);
        metrics.composureScore = Mathf.Clamp01(metrics.headStabilityScore - (peakPenalty * 0.3f));

        // ── Gaze Metrics ──────────────────────────────────────────────

        float avgGazeDeviation = gazeDeviationSum / frameCount;
        metrics.gazeStabilityScore = Mathf.Clamp01(1f - (avgGazeDeviation / 90f));
        metrics.eyeContactPercentage = (float)audienceFacingCount / frameCount * 100f;
        metrics.lookingDownPercentage = (float)lookingDownCount / frameCount * 100f;

        // ── Confidence Score (Composite) ──────────────────────────────
        // Weighted average: 40% head stability, 40% eye contact, 20% composure
        float eyeContactNormalized = metrics.eyeContactPercentage / 100f;
        metrics.confidenceScore = Mathf.Clamp01(
            (metrics.headStabilityScore * 0.4f) +
            (eyeContactNormalized * 0.4f) +
            (metrics.composureScore * 0.2f)
        );

        Debug.Log($"[HeadMovementLogger] Metrics computed. Confidence: {metrics.confidenceScore:P0}, Eye Contact: {metrics.eyeContactPercentage:F1}%");

        return metrics;
    }

    // ── Unity Lifecycle ───────────────────────────────────────────────

    void Update()
    {
        Debug.Log("Update running");

        if (!isLogging)
        {
            Debug.Log("Not logging");
            return;
        }

        if (centerEyeAnchor == null)
        {
            Debug.Log("Head is null");
            return;
        }

        Debug.Log($"deltaTime={Time.deltaTime}");

        sampleTimer += Time.deltaTime;

        Debug.Log($"timer={sampleTimer}");

        if (sampleTimer >= sampleRate)
        {
            Debug.Log("Capturing sample");

            sampleTimer = 0f;
            CaptureSample();
        }
    }

    // ── Private Helpers ───────────────────────────────────────────────

    /// <summary>
    /// Captures a single head orientation snapshot and adds it to the sample list.
    /// </summary>
    /// 

    //private bool TryResolveHead()
    //{
    //    if (/* your camera/head reference */ != null) return true;

    //    var cam = Camera.main;
    //    if (cam == null) return false;               // rig not up yet — retry next frame

    ///* your camera/head reference */ = cam.transform;
    //    Debug.Log("[HeadMovementLogger] Head transform acquired late.");
    //    return true;
    //}
    private void CaptureSample()
    {
        Vector3 euler = centerEyeAnchor.eulerAngles;
        samples.Add(new HeadSample
        {
            timestamp = Time.time - sessionStartTime,
            pitch = euler.x,
            yaw = euler.y,
            roll = euler.z,
            forward = centerEyeAnchor.forward
        });

        Debug.Log("[HeadMovementLogger] Sample captured");
        Debug.Log("[HeadMovementLogger] CaptureSample()");
    }

    /// <summary>
    /// Counts distinct look-away episodes: each time gaze crosses from
    /// within the audience threshold to outside it counts as one drift.
    /// </summary>
    private float CountGazeDrifts()
    {
        if (samples.Count < 2) return 0f;

        int drifts = 0;
        bool wasFacing = true;

        foreach (var s in samples)
        {
            float deviation = Vector3.Angle(s.forward, Vector3.forward);
            bool facing = deviation <= gazeAudienceThreshold;

            if (wasFacing && !facing) drifts++;   // crossed from facing -> away
            wasFacing = facing;
        }
        return drifts;
    }

    /// <summary>
    /// Converts Unity's 0-360 euler angle to -180 to 180 range
    /// for accurate delta calculations.
    /// </summary>
    private float NormalizeAngle(float angle)
    {
        if (angle > 180f) angle -= 360f;
        return angle;
    }
}
