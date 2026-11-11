#if UNITY_EDITOR
using StageReadyVR.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds the projector screen and its controller bind to every session scene
/// and links it to the scene's SessionManager. Safe to run again, it replaces
/// its own object and only sets the SessionManager projectionScreen field.
///
/// Menu: StageReadyVR > Add Projection Screen To Session Scenes.
/// Batch: Unity -batchmode -nographics -quit -projectPath . -executeMethod ProjectionScreenBuilder.BuildAll
/// </summary>
public static class ProjectionScreenBuilder
{
    public const string ObjectName = "ProjectionScreen";

    public static readonly string[] SessionScenes =
    {
        "Assets/Conference Room 01_V2/Conference Room 01_V2.unity",
        "Assets/Lecture hall_Auditorium/Auditorium.unity",
        "Assets/Nimikko_UniversityClassroom/Scenes/UniversityClassroom.unity"
    };

    [MenuItem("StageReadyVR/Add Projection Screen To Session Scenes")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildAll();
    }

    public static void BuildAll()
    {
        foreach (string path in SessionScenes)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Build(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ProjectionScreenBuilder] Added the projection screen to " + path + ".");
        }
    }

    static void Build(Scene scene)
    {
        SessionManager session = Object.FindObjectOfType<SessionManager>(true);
        if (session == null) throw new System.InvalidOperationException(scene.path + " has no SessionManager.");
        PauseManager pause = Object.FindObjectOfType<PauseManager>(true);

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == ObjectName) Object.DestroyImmediate(root);
        }

        var go = new GameObject(ObjectName);
        SceneManager.MoveGameObjectToScene(go, scene);
        var screen = go.AddComponent<ProjectionScreen>();
        var clicker = go.AddComponent<PresentationClicker>();
        clicker.projectionScreen = screen;
        clicker.sessionManager = session;
        clicker.pauseManager = pause;

        var so = new SerializedObject(session);
        so.FindProperty("projectionScreen").objectReferenceValue = screen;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (session.centerEyeAnchor == null)
            Debug.LogWarning("[ProjectionScreenBuilder] " + scene.path + " SessionManager has no centerEyeAnchor, so the screen cannot be placed behind the player.");
    }
}
#endif
