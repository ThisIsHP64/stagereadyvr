using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only credential entry for automatic scoring at REAL session end.
/// Sets a process-only environment variable; no key is written to the project.
/// </summary>
public sealed class REQ3EditorSessionKeyWindow : EditorWindow
{
    private string key = "";
    private string message = "No key configured through this window yet.";

    [MenuItem("StageReadyVR/REQ-3/Set Editor Session API Key")]
    public static void Open()
    {
        var window = GetWindow<REQ3EditorSessionKeyWindow>("REQ-3 Session Key");
        window.minSize = new Vector2(470, 200);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("REQ-3 — Session-end scoring key", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("For Unity Editor testing only. The key is held in this Unity process, not saved to Assets, PlayerPrefs, or a scene. Never include a key in a PICO APK.", MessageType.Info);
        key = EditorGUILayout.PasswordField("OpenAI API key", key);
        if (GUILayout.Button("Use key for this Unity Editor session"))
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                message = "Enter an API key first.";
            }
            else
            {
                Environment.SetEnvironmentVariable("OPENAI_API_KEY", key.Trim(), EnvironmentVariableTarget.Process);
                key = ""; // do not retain a second copy in this window
                message = "Key ready for automatic session-end scoring (this Unity process only).";
            }
        }
        if (GUILayout.Button("Clear key from this Unity Editor process"))
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null, EnvironmentVariableTarget.Process);
            key = "";
            message = "Key cleared.";
        }
        EditorGUILayout.HelpBox(message, MessageType.None);
    }
    private void OnDisable() { key = ""; }
}
