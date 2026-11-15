using System.IO;
using System.Linq;
using NUnit.Framework;
using StageReadyVR.Notes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StageReadyVR.Import.Tests
{
    /// <summary>
    /// Checks the saved session scenes, so a builder that was not re-run or a stale
    /// component shows up here rather than on the headset.
    /// </summary>
    public class SessionScenesTests
    {
        // the old teleprompter prototype, replaced by the speaker notes
        const string RemovedPrototypeGuid = "9fb23e2a30b3a4f66be434d0eed52eee";

        static readonly string[] SessionScenes =
        {
            "Assets/Conference Room 01_V2/Conference Room 01_V2.unity",
            "Assets/Lecture hall_Auditorium/Auditorium.unity",
            "Assets/Nimikko_UniversityClassroom/Scenes/UniversityClassroom.unity"
        };

        string previousScene;

        [SetUp]
        public void SetUp()
        {
            previousScene = EditorSceneManager.GetActiveScene().path;
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(previousScene) && File.Exists(previousScene))
                EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TestCaseSource(nameof(SessionScenes))]
        public void SpeakerNotesAreWiredToTheSessionManager(string path)
        {
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            // the session manager lives in Assembly-CSharp, which tests cannot reference, so it is found by name
            MonoBehaviour session = Object.FindObjectsOfType<MonoBehaviour>(true)
                .FirstOrDefault(m => m != null && m.GetType().Name == "SessionManager");
            Assert.IsNotNull(session, path + " has no SessionManager.");

            var so = new SerializedObject(session);
            var held = so.FindProperty("heldNotes").objectReferenceValue as HeldNotes;
            var prompter = so.FindProperty("teleprompter").objectReferenceValue as Teleprompter;
            Assert.IsNotNull(held, "heldNotes is not assigned, run StageReadyVR > Add Speaker Notes To Session Scenes.");
            Assert.IsNotNull(prompter, "teleprompter is not assigned, run StageReadyVR > Add Speaker Notes To Session Scenes.");
            Assert.AreSame(held.gameObject, prompter.gameObject);

            Component input = held.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "NotesInput");
            Assert.IsNotNull(input, "the notes have no stick input");

            Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(session.gameObject),
                "the SessionManager object still carries a removed script");
        }

        [TestCaseSource(nameof(SessionScenes))]
        public void OldPrototypeIsNotReferenced(string path)
        {
            StringAssert.DoesNotContain(RemovedPrototypeGuid, File.ReadAllText(path));
        }
    }
}
