using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Standalone, development-only microphone diagnostic for StageReadyVR / PICO 4.
/// Does NOT call AssemblyAI or OpenAI, and does NOT affect scoring.
/// Attach this component to a GameObject in a dedicated MicTest scene.
/// Build that scene to Android and inspect [PicoMicProbe] messages in adb logcat.
/// A short WAV is written to Application.persistentDataPath for playback on a PC.
/// Do not leave the probe enabled in the production presentation scene.
/// </summary>
public sealed class PicoMicProbe : MonoBehaviour
{
    [Range(3, 20)] public int recordingSeconds = 8;
    [Range(8000, 48000)] public int requestedSampleRate = 16000;
    [Tooltip("Use only in a dedicated test scene; do not run alongside AssemblyTranscriber.")]
    public bool runOnStart = true;

    private AudioClip _clip;
    private bool _starting;
#if UNITY_ANDROID && !UNITY_EDITOR
    private PermissionCallbacks _permissionCallbacks;
#endif

    private void Start()
    {
        if (runOnStart) BeginTest();
    }

    public void BeginTest()
    {
        if (_starting)
        {
            Debug.LogWarning("[PicoMicProbe] Test already running.");
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Debug.LogWarning("[PicoMicProbe] RECORD_AUDIO permission not granted; requesting now.");
            _permissionCallbacks = new PermissionCallbacks();
            _permissionCallbacks.PermissionGranted += permission =>
            {
                Debug.Log("[PicoMicProbe] Microphone permission GRANTED.");
                StartCoroutine(RecordMicrophone());
            };
            _permissionCallbacks.PermissionDenied += permission =>
                Debug.LogError("[PicoMicProbe] Microphone permission DENIED. Enable it in app settings.");
            _permissionCallbacks.PermissionDeniedAndDontAskAgain += permission =>
                Debug.LogError("[PicoMicProbe] Microphone permission permanently denied. Enable it in app settings.");
            Permission.RequestUserPermission(Permission.Microphone, _permissionCallbacks);
            return;
        }
        Debug.Log("[PicoMicProbe] Microphone permission already GRANTED.");
#endif
        StartCoroutine(RecordMicrophone());
    }

    private IEnumerator RecordMicrophone()
    {
        if (_starting) yield break;
        _starting = true;
        string[] devices = Microphone.devices;
        Debug.Log("[PicoMicProbe] Devices=" + (devices == null ? "null" : devices.Length.ToString()) +
                  " | Using default device | RequestedRate=" + requestedSampleRate);

        try
        {
            // Buffer exceeds test length so the recording never wraps during the test.
            _clip = Microphone.Start(null, true, recordingSeconds + 3, requestedSampleRate);
        }
        catch (Exception e)
        {
            Debug.LogError("[PicoMicProbe] Microphone.Start EXCEPTION: " + e);
            _starting = false;
            yield break;
        }

        if (_clip == null)
        {
            Debug.LogError("[PicoMicProbe] Microphone.Start returned null.");
            _starting = false;
            yield break;
        }

        float timeout = Time.realtimeSinceStartup + 5f;
        while (Microphone.GetPosition(null) <= 0 && Time.realtimeSinceStartup < timeout)
            yield return null;

        if (Microphone.GetPosition(null) <= 0)
        {
            Debug.LogError("[PicoMicProbe] Microphone never advanced past position 0. Check RECORD_AUDIO permission and competing recorders.");
            FinishMicrophone();
            _starting = false;
            yield break;
        }

        Debug.Log("[PicoMicProbe] CAPTURE STARTED. clipFreq=" + _clip.frequency +
                  " channels=" + _clip.channels + ". Speak normally for " + recordingSeconds + " seconds.");
        float finishAt = Time.realtimeSinceStartup + recordingSeconds;
        int lastPosition = 0;
        while (Time.realtimeSinceStartup < finishAt)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (_clip == null) yield break;
            if (!Microphone.IsRecording(null))
            {
                Debug.LogError("[PicoMicProbe] Microphone stopped unexpectedly.");
                break;
            }

            int position = Microphone.GetPosition(null);
            if (position < 0) position = 0;
            lastPosition = Mathf.Max(lastPosition, position);
            int frames = Mathf.Min(1024, position);
            if (frames <= 0) continue;
            float[] samples = new float[frames * Mathf.Max(1, _clip.channels)];
            bool gotData = _clip.GetData(samples, position - frames);
            if (!gotData)
            {
                Debug.LogError("[PicoMicProbe] AudioClip.GetData failed.");
                continue;
            }
            double sumSquares = 0;
            float peak = 0f;
            foreach (float sample in samples)
            {
                sumSquares += sample * sample;
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
            double rms = Math.Sqrt(sumSquares / samples.Length);
            double dbfs = rms > 0.000001 ? 20.0 * Math.Log10(rms) : -120.0;
            Debug.Log(string.Format("[PicoMicProbe] CAPTURING pos={0} rms={1:F5} dBFS={2:F1} peak={3:F3}",
                position, rms, dbfs, peak));
        }

        if (_clip != null && lastPosition > 0)
        {
            int frames = Mathf.Min(lastPosition, _clip.samples);
            float[] samples = new float[frames * Mathf.Max(1, _clip.channels)];
            if (_clip.GetData(samples, 0))
            {
                try
                {
                    string outputPath = Path.Combine(Application.persistentDataPath, "PicoMicTest.wav");
                    WriteWaveFile(outputPath, samples, _clip.channels, _clip.frequency);
                    Debug.Log("[PicoMicProbe] WAV SAVED: " + outputPath);
                    Debug.Log("[PicoMicProbe] You can copy this WAV from the headset with adb pull and listen on your PC.");
                }
                catch (Exception e)
                {
                    Debug.LogError("[PicoMicProbe] Failed to save WAV: " + e);
                }
            }
            else
            {
                Debug.LogError("[PicoMicProbe] Could not read recorded clip for WAV export.");
            }
        }
        else
        {
            Debug.LogError("[PicoMicProbe] No recorded samples available.");
        }

        FinishMicrophone();
        Debug.Log("[PicoMicProbe] TEST FINISHED.");
        _starting = false;
    }

    private static void WriteWaveFile(string path, float[] samples, int channels, int rate)
    {
        int dataBytes = samples.Length * 2;
        using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16); // PCM header size
            writer.Write((short)1); // PCM format
            writer.Write((short)channels);
            writer.Write(rate);
            writer.Write(rate * channels * 2); // bytes per second
            writer.Write((short)(channels * 2)); // block align
            writer.Write((short)16); // bits per sample
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);
            foreach (float value in samples)
                writer.Write((short)(Mathf.Clamp(value, -1f, 1f) * 32767f));
        }
    }

    private void FinishMicrophone()
    {
        if (Microphone.IsRecording(null)) Microphone.End(null);
        _clip = null;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        FinishMicrophone();
        _starting = false;
    }
}
