#if UNITY_EDITOR
using System.Linq;
using StageReadyVR.Import;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds the Import Files screen to the main menu scene: an ImportPanel under
/// the menu canvas and an "Import Files" button on the environment panel.
/// Safe to run again, it replaces its own objects and touches nothing else.
///
/// Menu: StageReadyVR > Build Import Screen.
/// Batch: Unity -batchmode -nographics -quit -projectPath . -executeMethod ImportScreenBuilder.BuildMainMenu
/// </summary>
public static class ImportScreenBuilder
{
    const string ScenePath = "Assets/MainMenu.unity";
    const string PanelName = "ImportPanel";
    const string OpenButtonName = "ImportFilesButton";

    // matches the existing menu: dark gray button text, white title
    static readonly Color ButtonText = new Color(0.196f, 0.196f, 0.196f, 1f);
    static readonly Color Light = Color.white;
    static readonly Color PanelTint = new Color(1f, 1f, 1f, 0.392f);

    [MenuItem("StageReadyVR/Build Import Screen")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildMainMenu();
    }

    public static void BuildMainMenu()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Canvas canvas = Object.FindObjectsOfType<Canvas>().FirstOrDefault(c => c.name == "Canvas");
        if (canvas == null) throw new System.InvalidOperationException("MainMenu has no object named Canvas.");

        Transform menu = canvas.transform.Find("EnvironmentPanel");
        if (menu == null) throw new System.InvalidOperationException("MainMenu has no EnvironmentPanel.");

        RemoveExisting(canvas.transform.Find(PanelName));
        RemoveExisting(menu.Find(OpenButtonName));

        TMP_FontAsset font = LoadMenuFont();

        // panel
        var panel = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
        panel.layer = canvas.gameObject.layer;
        panel.transform.SetParent(canvas.transform, false);
        Stretch((RectTransform)panel.transform);
        var background = panel.GetComponent<Image>();
        background.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        background.type = Image.Type.Sliced;
        background.color = PanelTint;

        var rect = (RectTransform)panel.transform;
        Text(rect, "Title", "Import Files", 36, Light, FontStyles.Normal, font, 230, 50, 600);
        TMP_Text instruction = Text(rect, "Instruction",
            "On a laptop or phone on the same Wi-Fi, open this address and enter the PIN:", 22, Light, FontStyles.Normal, font, 165, 60, 700);
        TMP_Text url = Text(rect, "Url", string.Empty, 30, Light, FontStyles.Bold, font, 110, 40, 700);
        TMP_Text pin = Text(rect, "Pin", string.Empty, 48, Light, FontStyles.Bold, font, 55, 60, 700);
        TMP_Text status = Text(rect, "Status", string.Empty, 22, Light, FontStyles.Normal, font, 0, 50, 700);
        Text(rect, "FilesHeader", "Imported files", 24, Light, FontStyles.Bold, font, -45, 36, 700);
        TMP_Text files = Text(rect, "Files", string.Empty, 22, Light, FontStyles.Normal, font, -125, 120, 700);

        Button back = NewButton(rect, "BackButton", "Back", font, -250, 300);

        // behaviour
        var controller = panel.AddComponent<ImportController>();
        var screen = panel.AddComponent<ImportScreen>();

        var so = new SerializedObject(screen);
        so.FindProperty("controller").objectReferenceValue = controller;
        so.FindProperty("menuPanel").objectReferenceValue = menu.gameObject;
        so.FindProperty("instructionText").objectReferenceValue = instruction;
        so.FindProperty("urlText").objectReferenceValue = url;
        so.FindProperty("pinText").objectReferenceValue = pin;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("filesText").objectReferenceValue = files;
        so.ApplyModifiedPropertiesWithoutUndo();

        UnityEventTools.AddPersistentListener(back.onClick, screen.Close);

        // entry point on the main menu, below the three environment buttons
        Button open = NewButton((RectTransform)menu, OpenButtonName, "Import Files", font, -250, 400);
        UnityEventTools.AddPersistentListener(open.onClick, screen.Open);

        panel.SetActive(false);

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveScene(canvas.gameObject.scene);
        Debug.Log("[ImportScreenBuilder] Added ImportPanel and the Import Files button to MainMenu.");
    }

    static void RemoveExisting(Transform existing)
    {
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
    }

    // reuse the menu's own font so the new screen looks like the rest
    static TMP_FontAsset LoadMenuFont()
    {
        string path = AssetDatabase.GUIDToAssetPath("8f586378b4e144a9851e7b34d9b748ee");
        TMP_FontAsset font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        return font != null ? font : TMP_Settings.defaultFontAsset;
    }

    static TMP_Text Text(RectTransform parent, string name, string text, float size, Color color,
        FontStyles style, TMP_FontAsset font, float y, float height, float width)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(width, height);

        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        if (font != null) t.font = font;
        return t;
    }

    static Button NewButton(RectTransform parent, string name, string label, TMP_FontAsset font, float y, float width)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(width, 60f);

        var image = go.GetComponent<Image>();
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Sliced;

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;

        TMP_Text text = Text(rt, "Text (TMP)", label, 24, ButtonText, FontStyles.Normal, font, 0, 0, 0);
        Stretch((RectTransform)text.transform);
        return button;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
#endif
