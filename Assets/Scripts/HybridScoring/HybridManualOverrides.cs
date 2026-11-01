using System;
using StageReadyVR.Feedback;
using UnityEngine;

namespace StageReadyVR.HybridScoring
{
    /// <summary>
    /// Short-term REQ-3 development fallback. Never present these values as actual
    /// speech measurements. Use the live sources once teammates finish integration.
    /// </summary>
    [Serializable]
    public class HybridManualOverrides
    {
        [Tooltip("For the prototype only. Allows missing metrics to be replaced with explicitly labeled demo data.")]
        public bool enabled = true;

        [Header("Speech pace fallback")]
        public bool fillMissingPace = true;
        public bool forcePaceOverride = false;
        [Range(0f, 300f)] public float demoWordsPerMinute = 138f;
        [Range(0f, 120f)] public float demoWpmStandardDeviation = 15f;

        [Header("Filler-word fallback")]
        public bool fillMissingFillers = true;
        public bool forceFillerOverride = false;
        [Min(0)] public int demoFillerWordCount = 4;

        [Header("Microphone volume fallback")]
        public bool fillMissingVolume = true;
        public bool forceVolumeOverride = false;
        [Range(-79f, -1f)] public float demoVolumeDb = -20f;

        public void Apply(SessionReport r)
        {
            if (r == null) return;
            bool speechMissing = r.totalWords <= 0 || r.averageWpm <= 0f;
            // A reported zero dBFS is a sentinel in the current app, not
            // evidence of a calibrated microphone reading. Exclude it.
            bool volumeMissing = r.averageVolumeDb <= -79f || r.averageVolumeDb >= 0f;
            if (volumeMissing) r.averageVolumeDb = -80f;
            if (!enabled) return;

            if (fillMissingPace && (speechMissing || forcePaceOverride))
            {
                r.averageWpm = demoWordsPerMinute;
                r.wpmStdDev = demoWpmStandardDeviation;
                r.totalWords = Mathf.RoundToInt(demoWordsPerMinute * Mathf.Max(r.durationSeconds, 0f) / 60f);
                r.hybridPaceSource = "manual_demo";
            }

            if (fillMissingFillers && (speechMissing || forceFillerOverride))
            {
                r.fillerWordCount = demoFillerWordCount;
                r.hybridFillerSource = "manual_demo";
            }

            if (fillMissingVolume && (volumeMissing || forceVolumeOverride))
            {
                r.averageVolumeDb = demoVolumeDb;
                r.hybridVolumeSource = "manual_demo";
            }
        }
    }
}
