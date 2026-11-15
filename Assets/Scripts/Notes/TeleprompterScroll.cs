using UnityEngine;

namespace StageReadyVR.Notes
{
    /// <summary>
    /// Pure scroll state for the teleprompter. Speed is set in words per minute,
    /// the same unit as the pace on the feedback report, and turned into distance
    /// using the average height one word takes up in the laid out script.
    /// </summary>
    public sealed class TeleprompterScroll
    {
        public const float MinWpm = 60f;
        public const float MaxWpm = 220f;
        public const float DefaultWpm = 140f;

        readonly float unitsPerWord;

        /// <param name="travel">How far the text can scroll before its last line reaches the reading band.</param>
        /// <param name="words">Words in the script, used to convert words per minute into distance.</param>
        public TeleprompterScroll(float travel, int words, float wpm = DefaultWpm)
        {
            Travel = Mathf.Max(0f, travel);
            unitsPerWord = words > 0 ? Travel / words : 0f;
            Wpm = Mathf.Clamp(wpm, MinWpm, MaxWpm);
        }

        public float Travel { get; }
        public float Offset { get; private set; }
        public float Wpm { get; private set; }
        public bool Paused { get; private set; }
        public bool AtEnd => Offset >= Travel - 1e-3f;
        public float UnitsPerSecond => Wpm / 60f * unitsPerWord;

        /// <summary>Scrolls for the elapsed time unless paused or already at the end.</summary>
        public void Step(float deltaTime)
        {
            if (Paused || deltaTime <= 0f) return;
            Offset = Mathf.Min(Travel, Offset + UnitsPerSecond * deltaTime);
        }

        public void ChangeSpeed(float deltaWpm)
        {
            Wpm = Mathf.Clamp(Wpm + deltaWpm, MinWpm, MaxWpm);
        }

        public void TogglePause()
        {
            Paused = !Paused;
        }

        /// <summary>Jumps whole lines forward (positive) or back (negative), for when the reader drifts.</summary>
        public void Nudge(int lines, float lineHeight)
        {
            Offset = Mathf.Clamp(Offset + lines * Mathf.Max(0f, lineHeight), 0f, Travel);
        }

        /// <summary>Counts words the way a reader would, so pace in words per minute stays honest.</summary>
        public static int CountWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0;
            bool inWord = false;
            foreach (char c in text)
            {
                bool space = char.IsWhiteSpace(c);
                if (!space && !inWord) count++;
                inWord = !space;
            }
            return count;
        }
    }
}
