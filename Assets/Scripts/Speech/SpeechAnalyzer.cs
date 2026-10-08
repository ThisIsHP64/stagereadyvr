using System.Collections.Generic;
using UnityEngine;
using Oculus.Voice.Dictation;
using StageReadyVR.Feedback;

/// <summary>
/// Continuous speech analysis for a practice session using the Meta Voice SDK
/// (AppDictationExperience / Wit.ai). Accumulates transcription, tracks words
/// per minute over a sliding window, counts filler words and long pauses, and
/// measures microphone RMS volume. Implements ISpeechMetricsSource so the
/// feedback module can pull final values at session end.
///
/// Scene setup:
///   1. Assets > Create > Voice SDK > Add App Dictation Experience to Scene.
///   2. Assign its Wit configuration (your wit.ai dictation app token).
///   3. Drag the AppDictationExperience into 'dictation' below.
///   4. Optionally wire FeedbackController so AddEvent markers appear.
/// Because Wit dictation auto-deactivates after silence, this component
/// re-activates it whenever it stops while a session is active, giving a
/// continuous transcription stream across the whole talk.
/// </summary>
public class SpeechAnalyzer : MonoBehaviour, ISpeechMetricsSource
{
    [Header("Voice SDK")]
    [Tooltip("AppDictationExperience component from the Meta Voice SDK")]
    public AppDictationExperience dictation;

    [Header("Feedback (optional)")]
    [Tooltip("Drops timeline markers for fillers / pauses if assigned")]
    public FeedbackController feedbackController;

    [Header("Volume Sampling")]
    [Tooltip("Microphone device name; empty = system default")]
    public string microphoneDevice = "";
    [Tooltip("How often to sample mic RMS in seconds")]
    public float volumeSampleRate = 0.1f;

    [Header("Pace Window")]
    [Tooltip("Sliding window (seconds) used to compute instantaneous WPM")]
    public float wpmWindowSeconds = 10f;

    [Header("Pause Detection")]
    [Tooltip("Silence longer than this (seconds) counts as a long pause")]
    public float pauseThreshold = 2.5f;

    [Header("Filler Words")]
    [Tooltip("Words/phrases counted as fillers (lowercase, no punctuation)")]
    public List<string> fillerWords = new List<string>
    {
        "um", "uh", "er", "ah", "like", "you know", "so", "actually",
        "basically", "literally", "right", "okay", "well", "hmm"
    };

    // ── Internal state ────────────────────────────────────────────────
    private bool isAnalyzing = false;
    private float sessionStartTime = 0f;
    private float lastWordTime = 0f;

    private int totalWords = 0;
    private int fillerCount = 0;
    private int longPauseCount = 0;
    private float longestPause = 0f;

    private string committedTranscript = "";   // accumulated finalized text
    private string lastPartial = "";

    // pace tracking: timestamp of each word for sliding-window WPM
    private readonly List<float> wordTimestamps = new List<float>();
    private readonly List<float> wpmSamples = new List<float>();

    // volume tracking
    private AudioClip micClip;
    private float volumeTimer = 0f;
    private readonly List<float> volumeSamplesDb = new List<float>();
    private const int MicSampleWindow = 256;

    // [TIMING] Wit.ai transcription-lag measurement.
    // _lastStoppedTime marks when the SDK stopped listening (utterance end);
    // OnFull logs the delta to the finalized transcript. Values are collected
    // in _witLagSamples and summarized at StopAnalysis for the paper's
    // latency reporting (mean / min / max / n).
    private float _lastStoppedTime = -1f;
    private readonly List<float> _witLagSamples = new List<float>();

    // ── ISpeechMetricsSource implementation ───────────────────────────

    public int TotalWords => totalWords;
    public int FillerWordCount => fillerCount;
    public float AverageWpm => ComputeAverageWpm();
    public float WpmStdDev => ComputeWpmStdDev();
    public float AverageVolumeDb => ComputeAverageVolumeDb();
    public int LongPauseCount => longPauseCount;
    public float LongestPauseSeconds => longestPause;
    public string Transcript => GetTranscript();

    // ── Public API ────────────────────────────────────────────────────

    public void StartAnalysis()
    {
        ResetState();
        isAnalyzing = true;
        sessionStartTime = Time.time;
        lastWordTime = Time.time;

        SubscribeDictation();
        //StartMicrophone();
        ActivateDictation();

        Debug.Log("[SpeechAnalyzer] Analysis started.");
    }

    public void StopAnalysis()
    {
        isAnalyzing = false;

        UnsubscribeDictation();
        if (dictation != null) dictation.Deactivate();
        //StopMicrophone();
        Debug.Log($"[SpeechAnalyzer] Transcript: {GetTranscript()}");

        Debug.Log($"[SpeechAnalyzer] Analysis stopped. Words: {totalWords}, " +
                  $"Fillers: {fillerCount}, Avg WPM: {AverageWpm:F0}");

        // [TIMING] Summarize Wit.ai transcription lag for this session.
        LogWitLagSummary();
    }

    // ── Dictation wiring ──────────────────────────────────────────────

    private void SubscribeDictation()
    {
        if (dictation == null)
        {
            Debug.LogError("[SpeechAnalyzer] No AppDictationExperience assigned.");
            return;
        }
        dictation.DictationEvents.OnPartialTranscription.AddListener(OnPartial);
        dictation.DictationEvents.OnFullTranscription.AddListener(OnFull);
        dictation.DictationEvents.OnStoppedListening.AddListener(OnStoppedListening);
    }

    private void UnsubscribeDictation()
    {
        if (dictation == null) return;
        dictation.DictationEvents.OnPartialTranscription.RemoveListener(OnPartial);
        dictation.DictationEvents.OnFullTranscription.RemoveListener(OnFull);
        dictation.DictationEvents.OnStoppedListening.RemoveListener(OnStoppedListening);
    }

    private void ActivateDictation()
    {
        if (dictation != null && isAnalyzing)
            dictation.Activate();
    }

    // Wit dictation drops out after each finalized utterance / silence. To keep
    // a continuous transcription across a whole speech, restart it — but on a
    // short delay so the SDK finishes its stop cycle first, and guarded so the
    // multiple OnStoppedListening callbacks don't queue several restarts.
    private bool _reactivateQueued;

    private void OnStoppedListening()
    {
        // [TIMING] Mark utterance end before any guard, so every stop event
        // stamps the reference time even if a reactivation is already queued.
        _lastStoppedTime = Time.realtimeSinceStartup;

        if (!isAnalyzing || _reactivateQueued) return;
        _reactivateQueued = true;
        StartCoroutine(ReactivateAfterDelay());
    }

    private System.Collections.IEnumerator ReactivateAfterDelay()
    {
        // Small gap lets the SDK fully release before we re-activate.
        yield return new WaitForSeconds(0.15f);
        _reactivateQueued = false;
        if (isAnalyzing)
            ActivateDictation();
    }

    private void OnPartial(string text)
    {
        lastPartial = text;
    }

    // Full transcription is the finalized chunk for an utterance. We append
    // each chunk rather than trusting a single end-of-session string, which
    // Wit is known to truncate.
    private void OnFull(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // [TIMING] Lag from utterance end (OnStoppedListening) to finalized
        // transcript arrival. Only logged when a stop was stamped and the
        // delta is sane (>0, < 30 s) — a final chunk can occasionally arrive
        // before the stop event, which would give a meaningless value.
        if (_lastStoppedTime > 0f)
        {
            float lag = Time.realtimeSinceStartup - _lastStoppedTime;
            if (lag > 0f && lag < 30f)
            {
                _witLagSamples.Add(lag);
                Debug.Log($"[Timing] WitFinalTranscript lag={lag:F2}s " +
                          $"words={TokenizeLower(text).Length}");
            }
            _lastStoppedTime = -1f;
        }

        ProcessFinalizedChunk(text);
        committedTranscript += " " + text;
        lastPartial = "";
    }

    // [TIMING] Per-session summary line — grep logcat for "[Timing] WitLagSummary".
    private void LogWitLagSummary()
    {
        if (_witLagSamples.Count == 0)
        {
            Debug.Log("[Timing] WitLagSummary n=0");
            return;
        }
        float sum = 0f, min = float.MaxValue, max = 0f;
        foreach (var s in _witLagSamples)
        {
            sum += s;
            if (s < min) min = s;
            if (s > max) max = s;
        }
        Debug.Log($"[Timing] WitLagSummary n={_witLagSamples.Count} " +
                  $"mean={sum / _witLagSamples.Count:F2}s min={min:F2}s max={max:F2}s");
    }

    // ── Transcript processing ─────────────────────────────────────────

    private void ProcessFinalizedChunk(string chunk)
    {
        string[] words = TokenizeLower(chunk);
        if (words.Length == 0) return;

        float now = Time.time;

        // Pause detection: gap since last spoken word.
        float gap = now - lastWordTime;
        if (gap >= pauseThreshold)
        {
            longPauseCount++;
            if (gap > longestPause) longestPause = gap;
            feedbackController?.AddEvent(EventKind.LongPause, $"{gap:F1}s pause");
        }

        // Multi-word filler phrases first, then single tokens.
        int i = 0;
        while (i < words.Length)
        {
            bool matchedPhrase = false;

            foreach (var filler in fillerWords)
            {
                if (!filler.Contains(" ")) continue;
                string[] parts = filler.Split(' ');
                if (i + parts.Length > words.Length) continue;

                bool all = true;
                for (int k = 0; k < parts.Length; k++)
                    if (words[i + k] != parts[k]) { all = false; break; }

                if (all)
                {
                    RegisterFiller(filler);
                    i += parts.Length;
                    matchedPhrase = true;
                    break;
                }
            }
            if (matchedPhrase) continue;

            if (fillerWords.Contains(words[i]))
                RegisterFiller(words[i]);

            i++;
        }

        // Word + pace bookkeeping.
        foreach (var _ in words)
            wordTimestamps.Add(now);

        totalWords += words.Length;
        lastWordTime = now;
    }

    private void RegisterFiller(string filler)
    {
        fillerCount++;
        feedbackController?.AddEvent(EventKind.FillerWord, filler);
    }

    private static string[] TokenizeLower(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new string[0];
        string cleaned = "";
        foreach (char c in text.ToLowerInvariant())
            cleaned += char.IsLetterOrDigit(c) || c == ' ' ? c : ' ';
        return cleaned.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
    }

    // ── Pace computation ──────────────────────────────────────────────

    private bool _inPaceSpike;

    private void SampleSlidingWpm()
    {
        float now = Time.time;
        float cutoff = now - wpmWindowSeconds;

        int inWindow = 0;
        for (int i = wordTimestamps.Count - 1; i >= 0; i--)
        {
            if (wordTimestamps[i] < cutoff) break;
            inWindow++;
        }

        float windowMinutes = wpmWindowSeconds / 60f;
        float wpm = inWindow / windowMinutes;
        wpmSamples.Add(wpm);

        if (wpm > 200f)
        {
            if (!_inPaceSpike)
            {
                _inPaceSpike = true;
                feedbackController?.AddEvent(EventKind.PaceSpike, $"{wpm:F0} wpm");
            }
        }
        else
        {
            _inPaceSpike = false;
        }
    }

    private float ComputeAverageWpm()
    {
        float elapsed = (isAnalyzing ? Time.time : lastWordTime) - sessionStartTime;
        float minutes = Mathf.Max(elapsed / 60f, 0.01f);
        return totalWords / minutes;
    }

    private float ComputeWpmStdDev()
    {
        if (wpmSamples.Count < 2) return 0f;
        float mean = 0f;
        foreach (var s in wpmSamples) mean += s;
        mean /= wpmSamples.Count;

        float sumSq = 0f;
        foreach (var s in wpmSamples) sumSq += (s - mean) * (s - mean);
        return Mathf.Sqrt(sumSq / wpmSamples.Count);
    }

    // ── Volume (microphone RMS) ───────────────────────────────────────

    private void StartMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[SpeechAnalyzer] No microphone detected; volume disabled.");
            return;
        }
        string device = string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice;
        micClip = Microphone.Start(device, true, 1, AudioSettings.outputSampleRate);
    }

    private void StopMicrophone()
    {
        if (Microphone.IsRecording(string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice))
            Microphone.End(string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice);
        micClip = null;
    }

    private void SampleVolume()
    {
        if (micClip == null) return;

        string device = string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice;
        int pos = Microphone.GetPosition(device) - MicSampleWindow;
        if (pos < 0) return;

        float[] buffer = new float[MicSampleWindow];
        micClip.GetData(buffer, pos);

        float sumSq = 0f;
        foreach (float sample in buffer) sumSq += sample * sample;
        float rms = Mathf.Sqrt(sumSq / MicSampleWindow);
        float db = rms > 0.0001f ? 20f * Mathf.Log10(rms) : -80f;

        volumeSamplesDb.Add(db);
    }

    private float ComputeAverageVolumeDb()
    {
        if (volumeSamplesDb.Count == 0) return -80f;

        // Only count samples above the noise floor so silence doesn't drag
        // the average down to the floor value.
        float sum = 0f;
        int counted = 0;
        foreach (var db in volumeSamplesDb)
        {
            if (db <= -60f) continue;
            sum += db;
            counted++;
        }
        return counted > 0 ? sum / counted : -80f;
    }

    // ── Unity lifecycle ───────────────────────────────────────────────

    void Update()
    {
        if (!isAnalyzing) return;

        volumeTimer += Time.deltaTime;
        if (volumeTimer >= volumeSampleRate)
        {
            volumeTimer = 0f;
            //SampleVolume();
            SampleSlidingWpm();
        }
    }

    void OnDestroy()
    {
        UnsubscribeDictation();
        StopMicrophone();
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private void ResetState()
    {
        totalWords = 0;
        fillerCount = 0;
        longPauseCount = 0;
        longestPause = 0f;
        committedTranscript = "";
        lastPartial = "";
        wordTimestamps.Clear();
        wpmSamples.Clear();
        volumeSamplesDb.Clear();
        volumeTimer = 0f;
        _inPaceSpike = false;
        // [TIMING] reset lag capture per session
        _lastStoppedTime = -1f;
        _witLagSamples.Clear();
    }

    public string GetTranscript() => committedTranscript.Trim();
}