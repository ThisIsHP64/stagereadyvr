using TMPro;
using UnityEngine;
using UnityEngine.UI;
using StageReadyVR.Controllers;
using System.Collections.Generic;

// Runtime-only panel, so shared scene files do not need to be modified.
public sealed class LiveSpeechHud : MonoBehaviour
{
    private GameObject panel;
    private TextMeshProUGUI label;
    private SpeechAnalyzer source;
    private float nextUpdate;
    private bool finishing, finished;
    private int page;
    private readonly List<string> pages = new List<string>();
    public void Begin(SpeechAnalyzer analyzer, Transform head, TMP_FontAsset font)
    {
        source = analyzer;
        if (source == null || head == null) return;
        panel = new GameObject("Live speech captions", typeof(RectTransform), typeof(Canvas));
        panel.transform.SetParent(transform, false);
        var canvas = panel.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        var rect = panel.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(600, 230);
        rect.localScale = Vector3.one * 0.001f;
        Vector3 forward = head.forward; forward.y = 0; forward.Normalize();
        Vector3 right = head.right; right.y = 0; right.Normalize();
        rect.position = head.position + forward * 1.45f - right * 0.55f - Vector3.up * 0.5f;
        rect.rotation = Quaternion.LookRotation(rect.position - head.position);
        var bg = panel.AddComponent<Image>(); bg.color = new Color(0.03f, 0.05f, 0.09f, 0.9f); bg.raycastTarget = false;
        var text = new GameObject("Live transcript and WPM", typeof(RectTransform), typeof(TextMeshProUGUI));
        text.transform.SetParent(panel.transform, false);
        label = text.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(16, 12); label.rectTransform.offsetMax = new Vector2(-16, -12);
        label.fontSize = 24; label.enableWordWrapping = true; label.richText = false; label.raycastTarget = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        Refresh();
    }
    private void Update()
    {
        if (finished && pages.Count > 1 && XRControllerInput.GetDown(ControllerButton.LeftStickClick))
        { page = (page + 1) % pages.Count; Refresh(); }
        if (Time.unscaledTime >= nextUpdate) Refresh();
    }
    private void Refresh()
    {
        nextUpdate = Time.unscaledTime + 0.25f;
        if (label == null || source == null) return;
        if (finished)
        {
            label.text = "FINAL TRANSCRIPT | " + (source.TotalWords > 0 ? Mathf.RoundToInt(source.AverageWpm) + " avg WPM" : "— WPM") +
                "\n" + (source.SpeechDataComplete ? "Finalized" : "Incomplete speech data") + " | " + (page+1) + "/" + pages.Count +
                (pages.Count > 1 ? " — left stick click: next" : "") + "\n" + pages[page];
            return;
        }
        if (finishing) { label.text = "Finalizing speech…"; return; }
        string captions = source.LiveCaption;
        if (captions.Length > 155) captions = "…" + captions.Substring(captions.Length - 155);
        label.text = "LIVE SPEECH  |  " + (source.TotalWords > 0 ? Mathf.RoundToInt(source.AverageWpm) + " avg WPM" : "— WPM") +
            "\n" + source.LiveSpeechStatus + "\n" + (string.IsNullOrWhiteSpace(captions) ? "Waiting for speech…" : captions);
    }
    public void BeginFinalizing() { finishing = true; Refresh(); }
    public void ShowFinal()
    {
        finishing = false; finished = true; page = 0; pages.Clear();
        string full = source == null ? "" : source.Transcript;
        for (int start = 0; start < full.Length;)
        {
            int length = Mathf.Min(140, full.Length - start);
            if (start + length < full.Length)
            {
                int split = full.LastIndexOf(' ', start + length - 1, length);
                if (split > start) length = split - start + 1;
            }
            pages.Add(full.Substring(start, length).Trim()); start += length;
        }
        if (pages.Count == 0) pages.Add("No finalized transcript available.");
        Refresh();
    }
    private void OnDestroy() { if (panel != null) Destroy(panel); }
}
