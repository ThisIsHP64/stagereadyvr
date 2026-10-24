using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StageReadyVR.Import.Tests
{
    /// <summary>Checks that the Import screen is actually built and wired in the main menu scene.</summary>
    public class MainMenuSceneTests
    {
        Scene scene;
        Transform canvas;

        [SetUp]
        public void SetUp()
        {
            // additive keeps whatever scene the editor already has open
            scene = EditorSceneManager.OpenScene("Assets/MainMenu.unity", OpenSceneMode.Additive);
            canvas = scene.GetRootGameObjects().Select(g => g.transform).First(t => t.name == "Canvas");
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        static void AssertCalls(Button button, ImportScreen target, string method)
        {
            UnityEvent click = button.onClick;
            Assert.AreEqual(1, click.GetPersistentEventCount());
            Assert.AreSame(target, click.GetPersistentTarget(0));
            Assert.AreEqual(method, click.GetPersistentMethodName(0));
        }

        [Test]
        public void ImportPanelExistsStartsHiddenAndIsFullyWired()
        {
            Transform panel = canvas.Find("ImportPanel");
            Assert.IsNotNull(panel, "ImportPanel is missing, run StageReadyVR > Build Import Screen.");
            Assert.IsFalse(panel.gameObject.activeSelf);
            Assert.IsNotNull(panel.GetComponent<ImportController>());

            var screen = panel.GetComponent<ImportScreen>();
            Assert.IsNotNull(screen);

            var so = new SerializedObject(screen);
            foreach (string field in new[] { "controller", "menuPanel", "instructionText", "urlText", "pinText", "statusText", "filesText" })
                Assert.IsNotNull(so.FindProperty(field).objectReferenceValue, field + " is not assigned");

            Assert.AreEqual("EnvironmentPanel", ((GameObject)so.FindProperty("menuPanel").objectReferenceValue).name);
        }

        [Test]
        public void MenuButtonOpensTheScreenAndBackButtonClosesIt()
        {
            Transform panel = canvas.Find("ImportPanel");
            var screen = panel.GetComponent<ImportScreen>();

            var open = canvas.Find("EnvironmentPanel/ImportFilesButton").GetComponent<Button>();
            var back = panel.Find("BackButton").GetComponent<Button>();

            AssertCalls(open, screen, "Open");
            AssertCalls(back, screen, "Close");
        }

        [Test]
        public void ExistingMenuPanelsAreUntouched()
        {
            Assert.IsTrue(canvas.Find("EnvironmentPanel").gameObject.activeSelf);
            Assert.IsFalse(canvas.Find("SessionSettingsPanel").gameObject.activeSelf);
            Assert.IsFalse(canvas.Find("AudiencePanel").gameObject.activeSelf);
        }

        static string TextOf(SerializedObject so, string field)
        {
            return ((TMP_Text)so.FindProperty(field).objectReferenceValue).text;
        }

        // drives the real panel from the scene: start it, read what the user would see, upload, read again
        [Test]
        public void ScreenShowsAddressAndListsAnUploadedFile()
        {
            if (NetworkAddress.GetLanIPv4() == null) Assert.Ignore("No network address on this machine.");

            string dir = Path.Combine(Application.temporaryCachePath, "menu-test-" + Guid.NewGuid().ToString("N"));
            Transform panel = canvas.Find("ImportPanel");
            var screen = panel.GetComponent<ImportScreen>();
            var controller = panel.GetComponent<ImportController>();
            var so = new SerializedObject(screen);
            BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;

            // keep test files out of the real app storage
            typeof(ImportController).GetField("store", any).SetValue(controller, new ImportStore(dir));

            try
            {
                screen.Open();
                typeof(ImportScreen).GetMethod("OnEnable", any).Invoke(screen, null);

                StringAssert.StartsWith("http://", TextOf(so, "urlText"));
                StringAssert.IsMatch("^PIN [0-9]{6}$", TextOf(so, "pinText"));
                Assert.AreEqual("No files imported yet.", TextOf(so, "filesText"));

                using (var client = new HttpClient())
                {
                    var request = new HttpRequestMessage(HttpMethod.Post,
                        "http://127.0.0.1:" + new Uri(controller.Url).Port + "/upload?name=speech.txt")
                    {
                        Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("Hello everyone."))
                    };
                    request.Headers.Add("X-Import-Pin", controller.Pin);
                    Assert.IsTrue(client.SendAsync(request).GetAwaiter().GetResult().IsSuccessStatusCode);
                }

                // the server reports on a background thread and the controller raises it from Update
                typeof(ImportController).GetMethod("Update", any).Invoke(controller, null);

                Assert.AreEqual("Imported speech.txt.", TextOf(so, "statusText"));
                StringAssert.StartsWith("speech.txt (txt,", TextOf(so, "filesText"));

                screen.Close();
                Assert.IsFalse(controller.IsImporting);
                Assert.IsTrue(canvas.Find("EnvironmentPanel").gameObject.activeSelf);
            }
            finally
            {
                controller.EndImport();
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
