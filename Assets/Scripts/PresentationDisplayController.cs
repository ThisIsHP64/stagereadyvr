using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimal display prototype for the teleprompter and index-card user stories.
///
/// Add this component to the same GameObject as SessionManager in a scene.
/// It creates world-space UI at runtime, so no image asset or prefab is needed
/// for the first display test.
///
/// Keyboard testing in the Unity Game view:
///   T = teleprompter, I = index cards, B = both, Left/Right = change card.
/// </summary>
public class PresentationDisplayController : MonoBehaviour
{
    public enum DisplayMode
    {
        Both,
        Teleprompter,
        IndexCards
    }

    [Header("Prototype content")]
    [TextArea(8, 24)]
    public string speechText =
        "Welcome everyone to today’s presentation.\n\n" +
        "Main Idea\n" +
        "- Explain the problem\n" +
        "- Show the proposed solution\n" +
        "- Describe the expected result\n\n" +
        "Conclusion\n" +
        "Thank you for listening.";

    public DisplayMode displayMode = DisplayMode.Both;

    [Header("Placement")]
    public Transform cameraAnchor;
    public float distanceFromCamera = 1.8f;
    public float verticalOffset = -0.05f;

    [Header("Appearance")]
    public Color panelColor = new Color(0.04f, 0.05f, 0.08f, 0.94f);
    public Color textColor = new Color(0.96f, 0.97f, 1f, 1f);
    public float textSize = 30f;

    private Canvas canvas;
    private GameObject teleprompterPanel;
    private GameObject cardsPanel;
    private TMP_Text teleprompterText;
    private TMP_Text cardTitleText;
    private TMP_Text cardBodyText;
    private readonly List<string> cards = new List<string>();
    private int currentCard;

    private void Start()
    {
        BuildDisplay();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T)) SetDisplayMode(DisplayMode.Teleprompter);
        if (Input.GetKeyDown(KeyCode.I)) SetDisplayMode(DisplayMode.IndexCards);
        if (Input.GetKeyDown(KeyCode.B)) SetDisplayMode(DisplayMode.Both);

        if (Input.GetKeyDown(KeyCode.LeftArrow)) ShowCard(currentCard - 1);
        if (Input.GetKeyDown(KeyCode.RightArrow)) ShowCard(currentCard + 1);
    }

    /// <summary>Allows the future upload flow to replace the prototype text.</summary>
    public void SetSpeechText(string text)
    {
        speechText = text ?? string.Empty;
        ParseCards();
        if (teleprompterText != null) teleprompterText.text = speechText;
        ShowCard(0);
    }

    public void SetDisplayMode(DisplayMode mode)
    {
        displayMode = mode;
        if (teleprompterPanel != null)
            teleprompterPanel.SetActive(mode == DisplayMode.Both || mode == DisplayMode.Teleprompter);
        if (cardsPanel != null)
            cardsPanel.SetActive(mode == DisplayMode.Both || mode == DisplayMode.IndexCards);
    }

    private void BuildDisplay()
    {
        if (canvas != null) return;

        if (cameraAnchor == null && Camera.main != null)
            cameraAnchor = Camera.main.transform;

        canvas = new GameObject("PresentationDisplays", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
            .GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.transform.localScale = Vector3.one * 0.0025f;
        canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(1200f, 700f);

        teleprompterPanel = CreatePanel("Teleprompter", new Vector2(-270f, 0f), new Vector2(520f, 620f));
        teleprompterText = CreateText(teleprompterPanel.transform, "TeleprompterText", speechText, textSize);
        Stretch(teleprompterText.rectTransform, 35f);
        teleprompterText.alignment = TextAlignmentOptions.TopLeft;
        teleprompterText.enableWordWrapping = true;

        cardsPanel = CreatePanel("IndexCards", new Vector2(300f, 0f), new Vector2(520f, 420f));
        cardTitleText = CreateText(cardsPanel.transform, "CardTitle", "Index Card", 34f);
        SetRect(cardTitleText.rectTransform, 30f, 25f, 460f, 65f);
        cardTitleText.fontStyle = FontStyles.Bold;

        cardBodyText = CreateText(cardsPanel.transform, "CardBody", string.Empty, 30f);
        SetRect(cardBodyText.rectTransform, 30f, 105f, 460f, 270f);
        cardBodyText.alignment = TextAlignmentOptions.TopLeft;
        cardBodyText.enableWordWrapping = true;

        ParseCards();
        ShowCard(0);
        SetDisplayMode(displayMode);
        RepositionInFrontOfCamera();
    }

    private void LateUpdate()
    {
        if (canvas == null || cameraAnchor == null) return;
        RepositionInFrontOfCamera();
    }

    private void RepositionInFrontOfCamera()
    {
        Vector3 forward = cameraAnchor.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        canvas.transform.position = cameraAnchor.position
            + forward * distanceFromCamera
            + Vector3.up * verticalOffset;

        Vector3 lookDirection = canvas.transform.position - cameraAnchor.position;
        lookDirection.y = 0f;
        if (lookDirection.sqrMagnitude > 0.001f)
            canvas.transform.rotation = Quaternion.LookRotation(lookDirection);
    }

    private void ParseCards()
    {
        cards.Clear();
        string normalized = (speechText ?? string.Empty).Replace("\r\n", "\n");
        string[] sections = normalized.Split(new[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries);

        foreach (string section in sections)
        {
            string trimmed = section.Trim();
            if (!string.IsNullOrEmpty(trimmed)) cards.Add(trimmed);
        }

        if (cards.Count == 0) cards.Add("Add speech text in the Inspector.");
    }

    private void ShowCard(int index)
    {
        if (cards.Count == 0) return;
        currentCard = Mathf.Clamp(index, 0, cards.Count - 1);
        if (cardTitleText == null || cardBodyText == null) return;

        string[] lines = cards[currentCard].Split('\n');
        cardTitleText.text = "Index Card " + (currentCard + 1) + " / " + cards.Count;

        var body = new StringBuilder();
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("-") || trimmed.StartsWith("•"))
                body.AppendLine("• " + trimmed.TrimStart('-', '•', ' '));
            else
                body.AppendLine(trimmed);
        }
        cardBodyText.text = body.ToString().Trim();
    }

    private GameObject CreatePanel(string name, Vector2 position, Vector2 size)
    {
        var panel = new GameObject(name, typeof(Image));
        panel.transform.SetParent(canvas.transform, false);
        var rect = panel.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        panel.GetComponent<Image>().color = panelColor;
        return panel;
    }

    private TMP_Text CreateText(Transform parent, string name, string value, float size)
    {
        var text = new GameObject(name, typeof(TextMeshProUGUI));
        text.transform.SetParent(parent, false);
        var tmp = text.GetComponent<TextMeshProUGUI>();
        tmp.text = value;
        tmp.fontSize = size;
        tmp.color = textColor;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }

    private static void Stretch(RectTransform rect, float margin)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(margin, margin);
        rect.offsetMax = new Vector2(-margin, -margin);
    }

    private static void SetRect(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
