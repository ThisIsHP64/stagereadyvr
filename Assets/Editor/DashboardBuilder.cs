#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using StageReadyVR.Feedback;

/// <summary>
/// One-click builder for the StageReadyVR feedback dashboard.
/// Menu: StageReadyVR > Build Feedback Dashboard.
///
/// Three-column landscape layout:
///   Left   : overall ring + grade, sub-score bars, timeline
///   Middle : session summary
///   Right  : AI coaching (status / strengths / recommendations)
/// Plus a full-view dark backdrop that fades in to dim the scene and remove
/// audience occlusion. Everything is wired into a DashboardPanel component.
/// </summary>
public static class DashboardBuilder
{
    // Palette: https://coolors.co/2e4057-a1b5ce-ffcc33-269244-f36153-fff8f0
    static readonly Color Bg = Hex(0x2E, 0x40, 0x57, 0.98f);   // dark slate
    static readonly Color Card = Hex(0x26, 0x38, 0x4D, 1f);      // slightly darker slate
    static readonly Color Track = Hex(0x3A, 0x4E, 0x66, 1f);      // muted slate (empty bar)
    static readonly Color Accent = Hex(0x26, 0x92, 0x44, 1f);      // green
    static readonly Color AccentDim = Hex(0x26, 0x92, 0x44, 0.25f);   // green faded (ring bg)
    static readonly Color Amber = Hex(0xFF, 0xCC, 0x33, 1f);      // amber (mid tier)
    static readonly Color TextHi = Hex(0xFF, 0xF8, 0xF0, 1f);      // warm off-white (headings)
    static readonly Color TextLo = Hex(0xA1, 0xB5, 0xCE, 1f);      // powder blue (secondary text)
    static readonly Color Dim = Hex(0x1A, 0x24, 0x31, 1f);      // deep slate (backdrop)

    static Color Hex(int r, int g, int b, float a)
        => new Color(r / 255f, g / 255f, b / 255f, a);

    const float PanelW = 1500f;
    const float PanelH = 820f;
    const float Pad = 56f;
    const float ColGap = 40f;

    [MenuItem("StageReadyVR/Build Feedback Dashboard")]
    public static void Build()
    {
        // Root canvas
        var canvasGO = new GameObject("FeedbackDashboard",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var crt = (RectTransform)canvasGO.transform;
        crt.sizeDelta = new Vector2(PanelW, PanelH);
        canvasGO.transform.localScale = Vector3.one * 0.0022f;
        canvasGO.transform.position = new Vector3(0f, 1.5f, 2f);

        // ── Backdrop (separate canvas, larger, sits behind, dims scene) ──
        var backdropGO = new GameObject("DashboardBackdrop",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(DashboardBackdrop));
        backdropGO.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var bdrt = (RectTransform)backdropGO.transform;
        bdrt.sizeDelta = new Vector2(6000f, 4000f);   // large enough to fill view
        backdropGO.transform.localScale = Vector3.one * 0.0022f;
        // Slightly behind the dashboard so the panel renders in front.
        backdropGO.transform.position = new Vector3(0f, 1.5f, 2.2f);

        var overlayRT = NewImage(bdrt, "Overlay", Dim, out var overlayImg);
        Stretch(overlayRT);
        var backdrop = backdropGO.GetComponent<DashboardBackdrop>();
        backdrop.overlay = overlayImg;
        backdrop.targetAlpha = 0.7f;   // dark dim; scene faintly visible
        var oc = overlayImg.color; oc.a = 0f; overlayImg.color = oc;
        backdropGO.SetActive(false);

        // ── Dashboard background + content ──
        var bgRT = NewImage(crt, "Background", Bg, out _);
        Stretch(bgRT);

        var content = NewRect(bgRT, "Content");
        Stretch(content);
        content.offsetMin = new Vector2(Pad, Pad);
        content.offsetMax = new Vector2(-Pad, -Pad);

        // Column geometry: three equal columns.
        float innerW = PanelW - Pad * 2f;
        float colW = (innerW - ColGap * 2f) / 3f;
        float leftX = 0f;
        float midX = colW + ColGap;
        float rightX = (colW + ColGap) * 2f;

        // Title (spans top)
        var titleRT = NewText(content, "Title", "Session Feedback", 40, TextHi, FontStyles.Bold, out var titleT);
        Col(titleRT, leftX, innerW, 0, 56);
        titleT.alignment = TextAlignmentOptions.Left;

        // ── LEFT COLUMN: ring + grade, bars, timeline ──
        var ringRT = NewRect(content, "OverallRing");
        ringRT.anchorMin = new Vector2(0, 1);
        ringRT.anchorMax = new Vector2(0, 1);
        ringRT.pivot = new Vector2(0, 1);
        ringRT.anchoredPosition = new Vector2(leftX, -82);
        ringRT.sizeDelta = new Vector2(150, 150);

        var ringBackRT = NewImage(ringRT, "RingBackground", AccentDim, out var ringBack);
        Stretch(ringBackRT);
        ringBack.sprite = Knob();
        ringBack.type = Image.Type.Filled;
        ringBack.fillMethod = Image.FillMethod.Radial360;
        ringBack.fillAmount = 1f;

        var ringFillRT = NewImage(ringRT, "RingFill", Accent, out var overallFill);
        Stretch(ringFillRT);
        overallFill.sprite = Knob();
        overallFill.type = Image.Type.Filled;
        overallFill.fillMethod = Image.FillMethod.Radial360;
        overallFill.fillOrigin = (int)Image.Origin360.Top;
        overallFill.fillClockwise = true;
        overallFill.fillAmount = 0f;

        var overallLabelRT = NewText(ringRT, "OverallLabel", "0", 52, TextHi, FontStyles.Bold, out var overallLabel);
        Stretch(overallLabelRT);
        overallLabel.alignment = TextAlignmentOptions.Center;

        var gradeRT = NewText(content, "GradeLabel", "\u2014", 30, Accent, FontStyles.Bold, out var gradeLabel);
        gradeRT.anchorMin = new Vector2(0, 1);
        gradeRT.anchorMax = new Vector2(0, 1);
        gradeRT.pivot = new Vector2(0, 1);
        gradeRT.anchoredPosition = new Vector2(leftX + 168, -130);
        gradeRT.sizeDelta = new Vector2(colW - 168, 48);
        gradeLabel.alignment = TextAlignmentOptions.Left;

        string[] barNames = { "Pace", "Filler control", "Eye contact", "Volume", "Composure" };
        var barFills = new Image[5];
        var barLabels = new TMP_Text[5];

        float barTop = 260f, barRowH = 56f;
        for (int i = 0; i < 5; i++)
        {
            var rowRT = NewRect(content, "Bar_" + barNames[i]);
            Col(rowRT, leftX, colW, barTop + i * barRowH, barRowH - 14);

            var labelRT = NewText(rowRT, "Label", barNames[i], 21, TextLo, FontStyles.Normal, out var label);
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(1, 1);
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = new Vector2(0, 21);
            label.alignment = TextAlignmentOptions.Left;
            barLabels[i] = label;

            var trackRT = NewImage(rowRT, "Track", Track, out var trackImg);
            trackRT.anchorMin = new Vector2(0, 0);
            trackRT.anchorMax = new Vector2(1, 0);
            trackRT.pivot = new Vector2(0.5f, 0);
            trackRT.sizeDelta = new Vector2(0, 12);
            trackRT.anchoredPosition = Vector2.zero;
            Rounded(trackImg);

            var fillRT = NewImage(trackRT, "Fill", Accent, out var fill);
            Stretch(fillRT);
            Rounded(fill);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            barFills[i] = fill;
        }

        // (Timeline removed per design feedback — the left column ends at the bars.)

        // ── MIDDLE COLUMN: summary ──
        var midLabelRT = NewText(content, "SummaryLabel", "Summary", 22, TextHi, FontStyles.Bold, out var midLabel);
        Col(midLabelRT, midX, colW, 80, 30);
        midLabel.alignment = TextAlignmentOptions.Left;

        var summaryRT = NewImage(content, "SummaryCard", Card, out var summaryImg);
        Col(summaryRT, midX, colW, 118, 560);
        Rounded(summaryImg);

        var summaryTextRT = NewText(summaryRT, "SummaryText", "Session summary appears here.", 21, TextLo, FontStyles.Normal, out var summaryText);
        Stretch(summaryTextRT);
        summaryTextRT.offsetMin = new Vector2(20, 16);
        summaryTextRT.offsetMax = new Vector2(-20, -16);
        summaryText.alignment = TextAlignmentOptions.TopLeft;

        // ── RIGHT COLUMN: coaching (scrollable) ──
        var coachLabelRT = NewText(content, "CoachingLabel", "AI Coaching", 22, TextHi, FontStyles.Bold, out var coachLabel);
        Col(coachLabelRT, rightX, colW, 80, 30);
        coachLabel.alignment = TextAlignmentOptions.Left;

        // Scroll view: viewport (masked) holds a vertical content block.
        var scrollGO = new GameObject("CoachingScroll",
            typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(Mask));
        scrollGO.transform.SetParent(content, false);
        var scrollRT = (RectTransform)scrollGO.transform;
        Col(scrollRT, rightX, colW, 118, 560);          // the visible window
        var scrollBg = scrollGO.GetComponent<Image>();
        scrollBg.color = new Color(0.12f, 0.14f, 0.18f, 1f);
        scrollGO.GetComponent<Mask>().showMaskGraphic = true;

        // Content holder: anchored to top, height driven by a ContentSizeFitter
        // so it grows with the coaching text and scrolls when taller than viewport.
        var contentHolder = new GameObject("Content",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentHolder.transform.SetParent(scrollRT, false);
        var chRT = (RectTransform)contentHolder.transform;
        chRT.anchorMin = new Vector2(0, 1);
        chRT.anchorMax = new Vector2(1, 1);
        chRT.pivot = new Vector2(0.5f, 1);
        chRT.anchoredPosition = Vector2.zero;
        chRT.sizeDelta = new Vector2(0, 0);

        var vlg = contentHolder.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(18, 18, 16, 16);
        vlg.spacing = 14;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var fitter = contentHolder.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Wire the ScrollRect (vertical only).
        var scroll = scrollGO.GetComponent<ScrollRect>();
        scroll.content = chRT;
        scroll.viewport = scrollRT;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 20f;

        // Coaching texts now live inside the scroll content.
        var coachStatusRT = NewText(chRT, "CoachingStatus", "Generating personalized feedback\u2026", 19, Accent, FontStyles.Italic, out var coachingStatus);
        coachingStatus.alignment = TextAlignmentOptions.Left;
        AddLayoutText(coachStatusRT);

        var strengthsRT = NewText(chRT, "StrengthsText", "", 19, TextHi, FontStyles.Normal, out var strengthsText);
        strengthsText.alignment = TextAlignmentOptions.TopLeft;
        AddLayoutText(strengthsRT);

        var recsRT = NewText(chRT, "RecommendationsText", "", 19, TextHi, FontStyles.Normal, out var recommendationsText);
        recommendationsText.alignment = TextAlignmentOptions.TopLeft;
        AddLayoutText(recsRT);

        // ── Buttons (bottom, spanning) ──
        var retryButton = MakeButton(content, "RetryButton", "Retry", 0f);
        var nextButton = MakeButton(content, "NextButton", "Next", 1f);

        // Wire
        var panel = canvasGO.AddComponent<DashboardPanel>();
        panel.EditorWire(
            overallFill, overallLabel, gradeLabel,
            barFills, barLabels,
            summaryText, retryButton, nextButton,
            coachingStatus, strengthsText, recommendationsText,
            backdrop);

        Selection.activeGameObject = canvasGO;
        Undo.RegisterCreatedObjectUndo(canvasGO, "Build Feedback Dashboard");
        Undo.RegisterCreatedObjectUndo(backdropGO, "Build Feedback Dashboard");
        Debug.Log("[DashboardBuilder] Three-column dashboard + backdrop built and wired. " +
                  "Position the FeedbackDashboard to face the user; the backdrop follows at the same spot.");
    }

    // ── Helpers ──

    static void Col(RectTransform rt, float x, float width, float top, float height)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -top);
        rt.sizeDelta = new Vector2(width, height);
    }

    // Make a text element size to its preferred height inside a layout group.
    static void AddLayoutText(RectTransform rt)
    {
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.flexibleHeight = 0;
        var tmp = rt.GetComponent<TMP_Text>();
        if (tmp != null) tmp.enableAutoSizing = false;
    }

    static RectTransform NewRect(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static RectTransform NewImage(RectTransform parent, string name, Color color, out Image img)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        img = go.GetComponent<Image>();
        img.color = color;
        return (RectTransform)go.transform;
    }

    static RectTransform NewText(RectTransform parent, string name, string text, float size, Color color, FontStyles style, out TMP_Text t)
    {
        var go = new GameObject(name, typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.enableWordWrapping = true;
        t = tmp;
        return (RectTransform)go.transform;
    }

    static Button MakeButton(RectTransform parent, string name, string text, float side)
    {
        var rt = NewImage(parent, name, Card, out var img);
        img.gameObject.AddComponent<Button>();
        Rounded(img);
        rt.anchorMin = new Vector2(side, 0);
        rt.anchorMax = new Vector2(side, 0);
        rt.pivot = new Vector2(side, 0);
        rt.sizeDelta = new Vector2(240, 60);
        rt.anchoredPosition = Vector2.zero;

        var labelRT = NewText(rt, "Label", text, 24, TextHi, FontStyles.Bold, out var label);
        Stretch(labelRT);
        label.alignment = TextAlignmentOptions.Center;

        var btn = img.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = Card;
        colors.highlightedColor = new Color(0.20f, 0.23f, 0.29f);
        colors.pressedColor = Accent;
        btn.colors = colors;
        return btn;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Rounded(Image img)
    {
        var s = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (s != null) { img.sprite = s; img.type = Image.Type.Sliced; }
    }

    static Sprite Knob()
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
    }
}
#endif