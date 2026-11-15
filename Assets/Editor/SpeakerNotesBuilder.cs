#if UNITY_EDITOR
using StageReadyVR.Notes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds the speaker notes (held cards or script, and the teleprompter) with
/// their stick input to every session scene and links them to the scene's
/// SessionManager. Safe to run again, it replaces its own object and only sets
/// the SessionManager heldNotes and teleprompter fields.
///
/// Menu: StageReadyVR > Add Speaker Notes To Session Scenes.
/// Batch: Unity -batchmode -nographics -quit -projectPath . -executeMethod SpeakerNotesBuilder.BuildAll
/// </summary>
public static class SpeakerNotesBuilder
{
    public const string ObjectName = "SpeakerNotes";

    // names this builder used before, removed so a re-run never leaves two copies
    static readonly string[] OldObjectNames = { "HeldNotes" };

    [MenuItem("StageReadyVR/Add Speaker Notes To Session Scenes")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildAll();
    }

    public static void BuildAll()
    {
        foreach (string path in ProjectionScreenBuilder.SessionScenes)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Build(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[SpeakerNotesBuilder] Added speaker notes to " + path + ".");
        }
    }

    static void Build(Scene scene)
    {
        SessionManager session = Object.FindObjectOfType<SessionManager>(true);
        if (session == null) throw new System.InvalidOperationException(scene.path + " has no SessionManager.");
        PauseManager pause = Object.FindObjectOfType<PauseManager>(true);

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == ObjectName || System.Array.IndexOf(OldObjectNames, root.name) >= 0)
                Object.DestroyImmediate(root);
        }

        var go = new GameObject(ObjectName);
        SceneManager.MoveGameObjectToScene(go, scene);
        var notes = go.AddComponent<HeldNotes>();
        var prompter = go.AddComponent<Teleprompter>();
        var input = go.AddComponent<NotesInput>();
        input.heldNotes = notes;
        input.teleprompter = prompter;
        input.sessionManager = session;
        input.pauseManager = pause;

        var so = new SerializedObject(session);
        so.FindProperty("heldNotes").objectReferenceValue = notes;
        so.FindProperty("teleprompter").objectReferenceValue = prompter;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (session.centerEyeAnchor == null)
            Debug.LogWarning("[SpeakerNotesBuilder] " + scene.path + " SessionManager has no centerEyeAnchor, so the notes cannot be placed.");
    }
}
#endif
