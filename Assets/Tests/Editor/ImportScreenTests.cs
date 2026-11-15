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
            // stored names keep the uploaded extension, which is how the list tells txt from pdf
            return new ImportedFile { id = name, displayName = name, kind = kind, sizeBytes = size, storedFileName = name };
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
            Assert.AreEqual("deck.pdf (slides, pdf, 2.0 MB)\ntalk.txt (script, txt, 300 B)", ImportScreen.FormatList(list, 5));
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

        [Test]
        public void PresentationCycleGoesNoneThenEachDeckThenNone()
        {
            var list = new List<ImportedFile>
            {
                File("a.pdf", ImportKind.Presentation, 1),
                File("talk.txt", ImportKind.Script, 1),
                File("b.pdf", ImportKind.Presentation, 1)
            };
            Assert.AreEqual("a.pdf", ImportScreen.NextPresentationId(list, null));
            Assert.AreEqual("b.pdf", ImportScreen.NextPresentationId(list, "a.pdf"));
            Assert.IsNull(ImportScreen.NextPresentationId(list, "b.pdf"));
        }

        [Test]
        public void PresentationCycleWithNoDecksStaysNone()
        {
            var list = new List<ImportedFile> { File("talk.txt", ImportKind.Script, 1) };
            Assert.IsNull(ImportScreen.NextPresentationId(list, null));
            Assert.IsNull(ImportScreen.NextPresentationId(null, null));
        }

        [Test]
        public void PresentationCycleRestartsFromUnknownId()
        {
            var list = new List<ImportedFile> { File("a.pdf", ImportKind.Presentation, 1) };
            Assert.AreEqual("a.pdf", ImportScreen.NextPresentationId(list, "deleted"));
        }

        [Test]
        public void PresentationLabelNamesDeckOrNone()
        {
            Assert.AreEqual("Presentation: none", ImportScreen.FormatPresentation(null));
            Assert.AreEqual("Presentation: deck.pdf", ImportScreen.FormatPresentation(File("deck.pdf", ImportKind.Presentation, 1)));
        }
    
        [Test]
        public void ListNamesIndexCardsAndPdfScripts()
        {
            var list = new List<ImportedFile>
            {
                File("cards.txt", ImportKind.IndexCards, 2048),
                File("speech.pdf", ImportKind.Script, 10)
            };
            Assert.AreEqual("cards.txt (cards, txt, 2 KB)\nspeech.pdf (script, pdf, 10 B)", ImportScreen.FormatList(list, 5));
        }

        [Test]
        public void NotesCycleGoesNoneThenEachScriptOrCardSetThenNone()
        {
            var list = new List<ImportedFile>
            {
                File("cards.txt", ImportKind.IndexCards, 1),
                File("deck.pdf", ImportKind.Presentation, 1),
                File("talk.pdf", ImportKind.Script, 1)
            };
            Assert.AreEqual("cards.txt", ImportScreen.NextNotesId(list, null));
            Assert.AreEqual("talk.pdf", ImportScreen.NextNotesId(list, "cards.txt"));
            Assert.IsNull(ImportScreen.NextNotesId(list, "talk.pdf"));
            Assert.AreEqual("cards.txt", ImportScreen.NextNotesId(list, "deleted"));
        }

        [Test]
        public void NotesCycleWithOnlyDecksStaysNone()
        {
            var list = new List<ImportedFile> { File("deck.pdf", ImportKind.Presentation, 1) };
            Assert.IsNull(ImportScreen.NextNotesId(list, null));
            Assert.IsNull(ImportScreen.NextNotesId(null, null));
        }

        [Test]
        public void NotesLabelNamesTheNotesAndTheirKind()
        {
            Assert.AreEqual("Notes: none", ImportScreen.FormatNotes(null));
            Assert.AreEqual("Notes: cards.txt (cards)", ImportScreen.FormatNotes(File("cards.txt", ImportKind.IndexCards, 1)));
        }

        [Test]
        public void NotesLabelShowsTheScriptStyle()
        {
            ImportedFile text = File("talk.txt", ImportKind.Script, 1);
            ImportedFile pdf = File("talk.pdf", ImportKind.Script, 1);
            ImportedFile cards = File("cards.txt", ImportKind.IndexCards, 1);

            Assert.AreEqual("Notes: talk.txt (script, held)", ImportScreen.FormatNotes(text, ScriptDisplay.Held));
            Assert.AreEqual("Notes: talk.txt (script, teleprompter)", ImportScreen.FormatNotes(text, ScriptDisplay.Teleprompter));
            Assert.AreEqual("Notes: talk.pdf (script, held: pdf scripts cannot scroll)", ImportScreen.FormatNotes(pdf, ScriptDisplay.Teleprompter));
            Assert.AreEqual("Notes: cards.txt (cards)", ImportScreen.FormatNotes(cards, ScriptDisplay.Teleprompter));
        }
    }
}
