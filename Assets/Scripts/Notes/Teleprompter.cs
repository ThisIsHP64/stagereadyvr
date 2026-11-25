using StageReadyVR.Import;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StageReadyVR.Notes
{
    /// <summary>
    /// Scrolls a text script at eye level, placed once toward the audience when the
    /// session starts, so reading it means looking out at the room. It only loads
    /// when the chosen notes are a text script and the Import screen's script style
    /// is Teleprompter. Look and placement defaults come from Lucas Deutsch's prototype.
    /// </summary>
    public class Teleprompter : MonoBehaviour
    {
        // the canvas is built in millimetres so ui sizes read as physical sizes
        const float UnitsPerMetre = 1000f;
        const float Margin = 30f;
        const float StatusHeight = 26f;
        const string SpeedPref = "TeleprompterWpm";

        [Header("Placement")]
        [Tooltip("Metres in front of the speaker's eyes")]
        [SerializeField] float distance = 1.8f;
        [Tooltip("Metres above (positive) or below (negative) eye level")]
        [SerializeField] float verticalOffset = -0.05f;

        [Header("Size")]
        [Tooltip("Panel size in metres")]
        [SerializeField] Vector2 panelSize = new Vector2(1.1f, 0.55f);
        [Tooltip("Text size in millimetres; about 32 reads comfortably at 1.8 m")]
        [SerializeField] float fontSize = 32f;

        [Header("Control")]
        [SerializeField] int nudgeLines = 3;
        [SerializeField] float speedStep = 10f;

        [Header("Appearance")]
        [SerializeField] Color panelColor = new Color(0.04f, 0.05f, 0.08f, 0.94f);
        [SerializeField] Color textColor = new Color(0.96f, 0.97f, 1f, 1f);
        [SerializeField] Color bandColor = new Color(1f, 1f, 1f, 0.08f);
        [SerializeField] Color statusColor = new Color(0.62f, 0.67f, 0.78f, 1f);

        ImportStore storeOverride;
        bool rememberSpeed = true;
        bool loaded;
        bool halted = true;

        Canvas canvas;
        RectTransform script;
        TMP_Text scriptText;
        TMP_Text status;
        float bandTop;
        float lineHeight;
        TeleprompterScroll scroll;

        /// <summary>True when a text script is loaded for teleprompter display.</summary>
        public bool HasScript { get; private set; }

        public bool IsShown => canvas != null && canvas.gameObject.activeSelf;
        public float Offset => scroll != null ? scroll.Offset : 0f;
        public float Wpm => scroll != null ? scroll.Wpm : TeleprompterScroll.DefaultWpm;
        public bool Paused => scroll != null && scroll.Paused;
        public bool AtEnd => scroll != null && scroll.AtEnd;

        /// <summary>Lets tests read from a temporary store instead of the app storage.</summary>
        public void UseStore(ImportStore store)
        {
            storeOverride = store;
        }

        /// <summary>Tests turn this off so they neither read nor change the saved reading speed.</summary>
        public void UseSpeedMemory(bool enabled)
        {
            rememberSpeed = enabled;
        }

        void Start()
        {
            Load();
        }

        void Update()
        {
            Step(Time.deltaTime);
        }

        /// <summary>Places the teleprompter ahead of where the head faces now. It stays there for the session.</summary>
        public void Begin(Transform head)
        {
            Load();
            if (!HasScript || canvas == null || head == null) return;

            Pose p = HeldPose.Ahead(head.position, head.forward, head.up, distance, verticalOffset);
            canvas.transform.SetPositionAndRotation(p.position, p.rotation);
            canvas.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        /// <summary>Stops scrolling while the session is not running or is paused, without changing the speaker's own pause.</summary>
        public void SetHalted(bool value)
        {
            halted = value;
        }

        public void Faster()
        {
            ChangeSpeed(speedStep);
        }

        public void Slower()
        {
            ChangeSpeed(-speedStep);
        }

        public void TogglePause()
        {
            if (scroll == null) return;
            scroll.TogglePause();
            UpdateStatus();
        }

        /// <summary>Jumps a few lines forward (positive direction) or back (negative).</summary>
        public void Nudge(int direction)
        {
            if (scroll == null || direction == 0) return;
            scroll.Nudge(direction > 0 ? nudgeLines : -nudgeLines, lineHeight);
            ApplyScroll();
            UpdateStatus();
        }

        void Step(float deltaTime)
        {
            if (scroll == null || halted || !IsShown) return;

            bool wasAtEnd = scroll.AtEnd;
            scroll.Step(deltaTime);
            ApplyScroll();
            if (scroll.AtEnd != wasAtEnd) UpdateStatus();
        }

        void ChangeSpeed(float delta)
        {
            if (scroll == null) return;
            scroll.ChangeSpeed(delta);
            SaveSpeed(scroll.Wpm);
            UpdateStatus();
        }

        void ApplyScroll()
        {
            if (script != null) script.anchoredPosition = new Vector2(0f, -bandTop + scroll.Offset);
        }

        void UpdateStatus()
        {
            if (status == null || scroll == null) return;
            string state = scroll.AtEnd ? "End   " : scroll.Paused ? "Paused   " : string.Empty;
            status.text = state + Mathf.RoundToInt(scroll.Wpm) + " wpm";
        }

        void Load()
        {
            if (loaded) return;
            loaded = true;

            ImportStore store = storeOverride ?? ImportStore.CreateDefault();
            ImportedFile active = store.GetActiveNotes();
            if (!store.GetTeleprompterEnabled() || !ShowsOnTeleprompter(active, store.GetScriptDisplay())) return;

            HasScript = true;
            BuildCanvas();

            // text that has never been active skips layout, so it is measured first and hidden in the same frame
            try
            {
                if (!store.TryReadText(active.id, out string text, out string error))
                {
                    scriptText.alignment = TextAlignmentOptions.Center;
                    scriptText.text = error;
                    return;
                }
                LayOut(NotesText.Normalize(text).Trim());
            }
            finally
            {
                canvas.gameObject.SetActive(false);
            }
        }

        /// <summary>Only text scripts scroll; cards and pdf pages stay held.</summary>
        public static bool ShowsOnTeleprompter(ImportedFile notes, ScriptDisplay display)
        {
            return notes != null && notes.kind == ImportKind.Script && !notes.IsPdf && display == ScriptDisplay.Teleprompter;
        }

        void LayOut(string text)
        {
            scriptText.text = text;
            scriptText.ForceMeshUpdate(true, true);

            float height = Mathf.Max(scriptText.preferredHeight, 1f);
            int lines = Mathf.Max(1, scriptText.textInfo != null ? scriptText.textInfo.lineCount : 1);
            lineHeight = height / lines;
            script.sizeDelta = new Vector2(0f, height);

            // the last line may scroll up to the reading band and no further
            scroll = new TeleprompterScroll(height - lineHeight, TeleprompterScroll.CountWords(text), LoadSpeed(store));
            ApplyScroll();
            UpdateStatus();
        }

        float LoadSpeed(ImportStore store)
        {
            if (!rememberSpeed) return TeleprompterScroll.DefaultWpm;
            try
            {
                if (PlayerPrefs.HasKey(SpeedPref))
                    return PlayerPrefs.GetFloat(SpeedPref, TeleprompterScroll.DefaultWpm);

                switch (store.GetTeleprompterPace())
                {
                    case TeleprompterPace.Slow: return 90f;
                    case TeleprompterPace.Fast: return 190f;
                    default: return 140f;
                }
            }
            catch (UnityException e)
            {
                Debug.LogWarning("[Teleprompter] saved speed unreadable: " + e.Message);
                return TeleprompterScroll.DefaultWpm;
            }
        }

        void SaveSpeed(float wpm)
        {
            if (!rememberSpeed) return;
            try
            {
                PlayerPrefs.SetFloat(SpeedPref, wpm);
            }
            catch (UnityException e)
            {
                Debug.LogWarning("[Teleprompter] speed not saved: " + e.Message);
            }
        }

        void BuildCanvas()
        {
            var root = new GameObject("TeleprompterCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);

            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            root.transform.localScale = Vector3.one / UnitsPerMetre;
            Vector2 size = panelSize * UnitsPerMetre;
            ((RectTransform)root.transform).sizeDelta = size;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.sizeDelta = size;
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = panelColor;
            panelImage.raycastTarget = false;

            // the mask keeps scrolled text inside the panel
            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(panelRect, false);
            var viewport = (RectTransform)viewportObject.transform;
            Inset(viewport, Margin, Margin + StatusHeight, Margin, Margin);
            float viewportHeight = size.y - Margin * 2f - StatusHeight;

            // a third of the way down keeps upcoming lines in view below the line being read
            bandTop = viewportHeight / 3f;

            var band = new GameObject("ReadingBand", typeof(RectTransform), typeof(Image));
            band.transform.SetParent(viewport, false);
            var bandRect = (RectTransform)band.transform;
            bandRect.anchorMin = new Vector2(0f, 1f);
            bandRect.anchorMax = new Vector2(1f, 1f);
            bandRect.pivot = new Vector2(0.5f, 1f);
            bandRect.anchoredPosition = new Vector2(0f, -bandTop + fontSize * 0.1f);
            bandRect.sizeDelta = new Vector2(0f, fontSize * 1.3f);
            var bandImage = band.GetComponent<Image>();
            bandImage.color = bandColor;
            bandImage.raycastTarget = false;

            var scriptObject = new GameObject("Script", typeof(RectTransform), typeof(TextMeshProUGUI));
            scriptObject.transform.SetParent(viewport, false);
            script = (RectTransform)scriptObject.transform;
            script.anchorMin = new Vector2(0f, 1f);
            script.anchorMax = new Vector2(1f, 1f);
            script.pivot = new Vector2(0.5f, 1f);
            script.anchoredPosition = new Vector2(0f, -bandTop);
            script.sizeDelta = new Vector2(0f, viewportHeight);

            scriptText = scriptObject.GetComponent<TextMeshProUGUI>();
            scriptText.fontSize = fontSize;
            scriptText.color = textColor;
            scriptText.alignment = TextAlignmentOptions.TopLeft;
            scriptText.enableWordWrapping = true;
            scriptText.overflowMode = TextOverflowModes.Overflow;
            scriptText.raycastTarget = false;

            // imported text is shown exactly as written, so angle brackets cannot restyle or hide it
            scriptText.richText = false;

            var statusObject = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI));
            statusObject.transform.SetParent(panelRect, false);
            status = statusObject.GetComponent<TextMeshProUGUI>();
            status.fontSize = 16f;
            status.color = statusColor;
            status.alignment = TextAlignmentOptions.BottomRight;
            status.raycastTarget = false;
            status.text = string.Empty;
            RectTransform statusRect = status.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(1f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.offsetMin = new Vector2(Margin, Margin * 0.5f);
            statusRect.offsetMax = new Vector2(-Margin, Margin * 0.5f + StatusHeight);
        }

        static void Inset(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
