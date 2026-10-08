#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using TMPro;

/// <summary>
/// One-click builder for the session UI: the "Get Ready / 3-2-1-GO" countdown
/// panel and the left-peripheral time-remaining HUD. Both are world-space
/// canvases placed in the scene (NOT parented to the rig, so they survive rig
/// changes). SessionManager positions the HUD at runtime via PositionHUD().
///
/// Menu: StageReadyVR > Build Session UI.
/// After building, assign the four objects into SessionManager's slots:
///   countdownPanel  -> CountdownPanel
///   countdownText   -> CountdownText (TMP)
///   sessionHUD      -> SessionHUD
///   timerText       -> TimerText (TMP)
/// </summary>
public static class SessionUIBuilder
{
    static readonly Color Panel = new Color(0.08f, 0.09f, 0.12f, 0.85f);
    static readonly Color TextHi = new Color(0.96f, 0.97f, 0.99f, 1f);
    static readonly Color Accent = new Color(0.32f, 0.62f, 0.94f, 1f);

    [MenuItem("StageReadyVR/Build Session UI")]
    public static void Build()
    {
        // ── Countdown panel (center, in front of spawn) ──
        var countdownGO = new GameObject("CountdownPanel",
            typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        countdownGO.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var ccrt = (RectTransform)countdownGO.transform;
        ccrt.sizeDelta = new Vector2(600, 400);
        countdownGO.transform.localScale = Vector3.one * 0.003f;
        countdownGO.transform.position = new Vector3(0f, 1.6f, 2f);   // in front, eye height

        var cdText = NewText(ccrt, "CountdownText", "3", 200, Accent, FontStyles.Bold);
        Stretch(cdText.rectTransform);
        cdText.alignment = TextAlignmentOptions.Center;

        // ── Session HUD (timer; positioned at runtime by SessionManager) ──
        var hudGO = new GameObject("SessionHUD",
            typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        hudGO.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var hrt = (RectTransform)hudGO.transform;
        hrt.sizeDelta = new Vector2(300, 120);
        hudGO.transform.localScale = Vector3.one * 0.003f;
        hudGO.transform.position = new Vector3(-0.6f, 1.4f, 1.2f);   // placeholder; SessionManager repositions

        var hudBg = NewImage(hrt, "Background", Panel);
        Stretch(hudBg.rectTransform);
        Rounded(hudBg);

        var timerText = NewText(hrt, "TimerText", "05:00 left", 48, TextHi, FontStyles.Bold);
        Stretch(timerText.rectTransform);
        timerText.alignment = TextAlignmentOptions.Center;

        Selection.activeGameObject = countdownGO;
        Undo.RegisterCreatedObjectUndo(countdownGO, "Build Session UI");
        Undo.RegisterCreatedObjectUndo(hudGO, "Build Session UI");
        Debug.Log("[SessionUIBuilder] Built CountdownPanel and SessionHUD. " +
                  "Assign them + their text children into SessionManager's slots.");
    }

    static TMP_Text NewText(RectTransform parent, string name, string text, float size, Color color, FontStyles style)
    {
        var go = new GameObject(name, typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = color; t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        return t;
    }

    static UnityEngine.UI.Image NewImage(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(UnityEngine.UI.Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<UnityEngine.UI.Image>();
        img.color = color;
        return img;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void Rounded(UnityEngine.UI.Image img)
    {
        var s = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (s != null) { img.sprite = s; img.type = UnityEngine.UI.Image.Type.Sliced; }
    }
}
#endif