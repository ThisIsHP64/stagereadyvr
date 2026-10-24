using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace StageReadyVR.Import.Tests
{
    public class ImportScreenTests
    {
        GameObject menu;
        GameObject panel;
        ImportScreen screen;

        [SetUp]
        public void SetUp()
        {
            menu = new GameObject("menu");
            panel = new GameObject("panel");
            screen = panel.AddComponent<ImportScreen>();

            var so = new SerializedObject(screen);
            so.FindProperty("menuPanel").objectReferenceValue = menu;
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(menu);
            Object.DestroyImmediate(panel);
        }

        static ImportedFile File(string name, ImportKind kind, long size)
        {
            return new ImportedFile { id = name, displayName = name, kind = kind, sizeBytes = size };
        }

        [Test]
        public void OpenShowsScreenAndHidesMenu()
        {
            screen.Open();
            Assert.IsTrue(panel.activeSelf);
            Assert.IsFalse(menu.activeSelf);
        }

        [Test]
        public void CloseRestoresMenuAndHidesScreen()
        {
            screen.Open();
            screen.Close();
            Assert.IsFalse(panel.activeSelf);
            Assert.IsTrue(menu.activeSelf);
        }

        [Test]
        public void EmptyListSaysSo()
        {
            Assert.AreEqual("No files imported yet.", ImportScreen.FormatList(new List<ImportedFile>(), 5));
            Assert.AreEqual("No files imported yet.", ImportScreen.FormatList(null, 5));
        }

        [Test]
        public void ListShowsKindAndSizeInGivenOrder()
        {
            var list = new List<ImportedFile>
            {
                File("deck.pdf", ImportKind.Presentation, 2 * 1024 * 1024),
                File("talk.txt", ImportKind.Script, 300)
            };
            Assert.AreEqual("deck.pdf (pdf, 2.0 MB)\ntalk.txt (txt, 300 B)", ImportScreen.FormatList(list, 5));
        }

        [Test]
        public void ListIsCappedWithAMoreNote()
        {
            var list = new List<ImportedFile>();
            for (int i = 0; i < 7; i++) list.Add(File("f" + i + ".txt", ImportKind.Script, 10));

            string text = ImportScreen.FormatList(list, 5);
            StringAssert.Contains("f4.txt", text);
            StringAssert.DoesNotContain("f5.txt", text);
            StringAssert.EndsWith("+2 more", text);
        }

        [TestCase(0L, "0 B")]
        [TestCase(1023L, "1023 B")]
        [TestCase(1024L, "1 KB")]
        [TestCase(1025L, "2 KB")]
        [TestCase(1536L * 1024, "1.5 MB")]
        public void FormatsSizes(long bytes, string expected)
        {
            Assert.AreEqual(expected, ImportScreen.FormatSize(bytes));
        }
    }
}
