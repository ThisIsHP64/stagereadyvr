using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace StageReadyVR.Feedback
{
    /// <summary>
    /// World-space VR results panel. Attach to a Canvas (Render Mode = World Space)
    /// placed ~2m in front of the speaker spot. Wire the serialized fields to the
    /// child UI elements. Renders overall score, color-coded sub-score bars, a
    /// summary, scrollable AI coaching, and retry / next buttons.
    ///
    /// Built by the DashboardBuilder menu tool.
    public class DashboardPanel : MonoBehaviour
    {
        [Header("Overall")]
        [SerializeField] private Image overallFill;       // radial filled image
        [SerializeField] private TMP_Text overallLabel;
        [SerializeField] private TMP_Text gradeLabel;

        [Header("Sub-score bars (order: pace, filler, eye, volume, composure)")]
        [SerializeField] private Image[] barFills = new Image[5];
        [SerializeField] private TMP_Text[] barLabels = new TMP_Text[5];

        [Header("Summary + actions")]
        [SerializeField] private TMP_Text summaryText;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button nextButton;

        [Header("AI Coaching")]
        [SerializeField] private TMP_Text coachingStatus;       // "Generating..." / hidden when done
        [SerializeField] private TMP_Text strengthsText;
        [SerializeField] private TMP_Text recommendationsText;

        [Header("Backdrop")]
        [SerializeField] private DashboardBackdrop backdrop;     // dims scene behind the panel

        [Header("Animation")]
        [SerializeField] private float fillDuration = 0.8f;

        public System.Action OnRetry;
        public System.Action OnNext;

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only wiring entry point used by the DashboardBuilder menu tool
        /// to assign the generated UI references. Not used at runtime.
        /// </summary>
        public void EditorWire(
            Image overallFill, TMP_Text overallLabel, TMP_Text gradeLabel,
            Image[] barFills, TMP_Text[] barLabels,
            TMP_Text summaryText, Button retryButton, Button nextButton,
            TMP_Text coachingStatus, TMP_Text strengthsText, TMP_Text recommendationsText,
            DashboardBackdrop backdrop)
        {
            this.overallFill = overallFill;
            this.overallLabel = overallLabel;
            this.gradeLabel = gradeLabel;
            this.barFills = barFills;
            this.barLabels = barLabels;
            this.summaryText = summaryText;
            this.retryButton = retryButton;
            this.nextButton = nextButton;
            this.coachingStatus = coachingStatus;
            this.strengthsText = strengthsText;
            this.recommendationsText = recommendationsText;
            this.backdrop = backdrop;
        }
#endif

        private void Awake()
        {
            if (retryButton != null) retryButton.onClick.AddListener(() => OnRetry?.Invoke());
            if (nextButton != null) nextButton.onClick.AddListener(() => OnNext?.Invoke());
            Hide();
        }

        public void Hide()
        {
            if (backdrop != null) backdrop.Hide();
            gameObject.SetActive(false);
        }

        public void Show(SessionReport report)
        {
            if (backdrop != null) backdrop.FadeIn();   // dim the scene behind the panel
            gameObject.SetActive(true);

            var s = report.scores;
            if (gradeLabel != null) gradeLabel.text = ScoreCalculator.Grade(s.overall);

            float[] vals = { s.pace, s.fillerControl, s.eyeContact, s.volume, s.composure };
            string[] names = { "Pace", "Filler control", "Eye contact", "Volume", "Composure" };

            for (int i = 0; i < barFills.Length && i < vals.Length; i++)
            {
                if (barLabels != null && i < barLabels.Length && barLabels[i] != null)
                    barLabels[i].text = $"{names[i]}  {Mathf.RoundToInt(vals[i])}";
            }

            if (summaryText != null) summaryText.text = BuildSummary(report);

            // Coaching starts in a loading state; FeedbackController fills it in
            // when the LLM response returns via ShowCoaching().
            if (coachingStatus != null)
            {
                coachingStatus.gameObject.SetActive(true);
                coachingStatus.text = "Generating personalized feedback\u2026";
            }
            if (strengthsText != null) strengthsText.text = "";
            if (recommendationsText != null) recommendationsText.text = "";

            StopAllCoroutines();
            StartCoroutine(AnimateFills(s, vals));
        }

        /// <summary>
        /// Called by FeedbackController when the LLM coaching response returns.
        /// Populates summary/strengths/recommendations and clears loading state.
        /// </summary>
        public void ShowCoaching(StageReadyVR.Coaching.CoachingFeedback fb)
        {
            if (fb == null || !fb.ok)
            {
                if (coachingStatus != null)
                {
                    coachingStatus.gameObject.SetActive(true);
                    coachingStatus.text = "Personalized feedback unavailable right now.";
                }
                return;
            }

            if (coachingStatus != null) coachingStatus.gameObject.SetActive(false);

            // Summary as a short neutral paragraph.
            if (strengthsText != null)
            {
                string summary = string.IsNullOrEmpty(fb.summary) ? "" : fb.summary + "\n\n";
                strengthsText.text =
                    summary +
                    "<b><color=#269244>Strengths</color></b>\n" +         // green
                    ColoredBullets(fb.strengths, "#269244");
            }

            if (recommendationsText != null)
                recommendationsText.text =
                    "<b><color=#F36153>To improve</color></b>\n" +         // red (lighter)
                    ColoredBullets(fb.recommendations, "#F36153");
        }

        private static string ColoredBullets(string[] items, string hex)
        {
            if (items == null || items.Length == 0) return "\u2014";
            var sb = new StringBuilder();
            foreach (var item in items)
                sb.Append("<color=").Append(hex).Append(">\u2022</color> ")
                  .Append(item).Append('\n');
            return sb.ToString().TrimEnd();
        }

        private System.Collections.IEnumerator AnimateFills(SessionScores s, float[] vals)
        {
            float t = 0f;
            while (t < fillDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / fillDuration);

                if (overallFill != null) overallFill.fillAmount = (s.overall / 100f) * k;
                if (overallLabel != null) overallLabel.text = Mathf.RoundToInt(s.overall * k).ToString();

                for (int i = 0; i < barFills.Length && i < vals.Length; i++)
                    if (barFills[i] != null)
                    {
                        barFills[i].fillAmount = (vals[i] / 100f) * k;
                        barFills[i].color = ScoreColor(vals[i]);
                    }

                yield return null;
            }
        }

        // Palette tiers: green (#269244) good, amber (#FFCC33) mid, red (#F36153) poor.
        private static Color ScoreColor(float score)
        {
            if (score >= 70f) return HexColor(0x26, 0x92, 0x44);   // green
            if (score >= 40f) return HexColor(0xFF, 0xCC, 0x33);   // amber
            return HexColor(0xF3, 0x61, 0x53);                     // red (lighter)
        }

        private static Color HexColor(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f);
        }


        private static string BuildSummary(SessionReport r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<b>{r.environmentName}</b>  ·  {Mathf.RoundToInt(r.durationSeconds)}s");
            sb.AppendLine($"Words: {r.totalWords}   ·   Avg pace: {Mathf.RoundToInt(r.averageWpm)} wpm");
            sb.AppendLine($"Filler words: {r.fillerWordCount}   ·   Eye contact: {Mathf.RoundToInt(r.eyeContactRatio * 100)}%");
            if (r.distractorsFired > 0)
                sb.AppendLine($"Distractors handled: {r.distractorsRecoveredQuickly}/{r.distractorsFired}");
            return sb.ToString();
        }
    }
}