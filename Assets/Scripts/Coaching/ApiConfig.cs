using UnityEngine;

namespace StageReadyVR.Coaching
{
    /// <summary>
    /// Holds the Anthropic API key for development. Loaded from a text asset in
    /// a Resources folder so the key is NOT hardcoded in source.
    ///
    /// SETUP:
    ///   1. Create folder: Assets/Resources/
    ///   2. Create a text file there named "anthropic_key.txt"
    ///   3. Paste ONLY your API key into it (no quotes, no spaces, no newline).
    ///   4. Add "anthropic_key.txt" to .gitignore so it is never committed.
    ///
    /// SECURITY NOTE: a key bundled in a build can be extracted from the APK.
    /// This config-file approach is for development and your own testing only.
    /// For any shared or participant-facing deployment, replace this with a
    /// relay server that holds the key and forwards requests.
    /// </summary>
    public static class ApiConfig
    {
        private const string ResourceName = "anthropic_key";
        private static string _cachedKey;

        public static string AnthropicKey
        {
            get
            {
                if (!string.IsNullOrEmpty(_cachedKey)) return _cachedKey;

                var asset = Resources.Load<TextAsset>(ResourceName);
                if (asset == null)
                {
                    Debug.LogError(
                        "[ApiConfig] Missing Assets/Resources/anthropic_key.txt. " +
                        "Create it and paste your Anthropic API key inside.");
                    return null;
                }
                _cachedKey = asset.text.Trim();
                return _cachedKey;
            }
        }

        public static bool HasKey => !string.IsNullOrEmpty(AnthropicKey);
    }
}