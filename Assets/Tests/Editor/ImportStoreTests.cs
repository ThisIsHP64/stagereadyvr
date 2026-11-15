using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace StageReadyVR.Import.Tests
{
    public class ImportStoreTests
    {
        string dir;
        ImportStore store;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(UnityEngine.Application.temporaryCachePath, "import-tests-" + Guid.NewGuid().ToString("N"));
            store = new ImportStore(dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        static byte[] Text(string s) { return new UTF8Encoding(false).GetBytes(s); }

        [Test]
        public void SavesAndReadsBackText()
        {
            SaveResult r = store.Save(Text("Hello team."), "talk.txt", ImportKind.Script);
            Assert.IsTrue(r.Success, r.Message);

            Assert.IsTrue(store.TryReadText(r.File.id, out string text, out string error), error);
            Assert.AreEqual("Hello team.", text);
        }

        [Test]
        public void SavesPdfAndExposesPath()
        {
            SaveResult r = store.Save(ImportValidatorTests.MinimalPdf(), "deck.pdf", ImportKind.Presentation);
            Assert.IsTrue(r.Success, r.Message);

            string path = store.GetPresentationPath(r.File.id);
            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(path.StartsWith(Path.GetFullPath(dir)));
        }

        [Test]
        public void StoredNameIsGeneratedNotClientControlled()
        {
            SaveResult r = store.Save(Text("x"), "../../escape.txt", ImportKind.Script);
            Assert.IsTrue(r.Success);
            Assert.AreEqual("escape.txt", r.File.displayName);
            StringAssert.IsMatch("^[0-9a-f]{32}\\.txt$", r.File.storedFileName);
            Assert.IsFalse(File.Exists(Path.Combine(dir, "..", "..", "escape.txt")));
        }

        [Test]
        public void ListSurvivesReloadNewestFirst()
        {
            store.Save(Text("one"), "one.txt", ImportKind.Script);
            store.Save(Text("two"), "two.txt", ImportKind.Script);

            var reloaded = new ImportStore(dir);
            var list = reloaded.List();
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("two.txt", list[0].displayName);
        }

        [Test]
        public void InvalidFileIsNotStored()
        {
            SaveResult r = store.Save(Text("   "), "blank.txt", ImportKind.Script);
            Assert.IsFalse(r.Success);
            Assert.AreEqual(0, store.List().Count);
        }

        [Test]
        public void CorruptIndexRecoversToEmptyAndStillSaves()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "index.json"), "{ not json at all");

            Assert.AreEqual(0, store.List().Count);
            Assert.IsTrue(store.Save(Text("fresh"), "a.txt", ImportKind.Script).Success);
            Assert.AreEqual(1, store.List().Count);
        }

        [Test]
        public void TamperedIndexEntryWithTraversalIsIgnored()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "index.json"),
                "{\"files\":[{\"id\":\"x\",\"displayName\":\"p\",\"kind\":0,\"storedFileName\":\"../../secret.txt\",\"sizeBytes\":1,\"importedAtUtc\":\"\"}]}");

            Assert.AreEqual(0, store.List().Count);
            Assert.IsFalse(store.TryReadText("x", out _, out _));
        }

        [Test]
        public void MissingFileIsHiddenAndReadFailsCleanly()
        {
            SaveResult r = store.Save(Text("gone soon"), "a.txt", ImportKind.Script);
            File.Delete(Path.Combine(dir, r.File.storedFileName));

            Assert.AreEqual(0, store.List().Count);
            Assert.IsFalse(store.TryReadText(r.File.id, out _, out string error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void DeleteRemovesFileAndEntry()
        {
            SaveResult r = store.Save(Text("bye"), "a.txt", ImportKind.Script);
            Assert.IsTrue(store.Delete(r.File.id));
            Assert.AreEqual(0, store.List().Count);
            Assert.IsFalse(File.Exists(Path.Combine(dir, r.File.storedFileName)));
            Assert.IsFalse(store.Delete(r.File.id));
        }

        [Test]
        public void ReadingAPresentationAsTextFails()
        {
            SaveResult r = store.Save(ImportValidatorTests.MinimalPdf(), "deck.pdf", ImportKind.Presentation);
            Assert.IsFalse(store.TryReadText(r.File.id, out _, out _));
        }

        [Test]
        public void NoActivePresentationByDefault()
        {
            store.Save(ImportValidatorTests.MinimalPdf(), "deck.pdf", ImportKind.Presentation);
            Assert.IsNull(store.GetActivePresentation());
        }

        [Test]
        public void ActivePresentationSurvivesReloadAndLaterImports()
        {
            SaveResult deck = store.Save(ImportValidatorTests.MinimalPdf(), "deck.pdf", ImportKind.Presentation);
            Assert.IsTrue(store.SetActivePresentation(deck.File.id));

            store.Save(Text("later"), "later.txt", ImportKind.Script);

            ImportedFile active = new ImportStore(dir).GetActivePresentation();
            Assert.IsNotNull(active);
            Assert.AreEqual(deck.File.id, active.id);
        }

        [Test]
        public void ScriptCannotBeActivePresentation()
        {
            SaveResult script = store.Save(Text("words"), "talk.txt", ImportKind.Script);
            Assert.IsFalse(store.SetActivePresentation(script.File.id));
            Assert.IsFalse(store.SetActivePresentation("not-an-id"));
            Assert.IsNull(store.GetActivePresentation());
        }

        [Test]
        public void ClearingActivePresentationWorks()
        {
            SaveResult deck = store.Save(ImportValidatorTests.MinimalPdf(), "deck.pdf", ImportKind.Presentation);
            store.SetActivePresentation(deck.File.id);
            Assert.IsTrue(store.SetActivePresentation(null));
            Assert.IsNull(store.GetActivePresentation());
        }

        [Test]
        public void DeletingActivePresentationClearsIt()
        {
            SaveResult deck = store.Save(ImportValidatorTests.MinimalPdf(), "deck.pdf", ImportKind.Presentation);
            store.SetActivePresentation(deck.File.id);
            store.Delete(deck.File.id);
            Assert.IsNull(store.GetActivePresentation());
        }

        [Test]
        public void ActivePresentationWithMissingFileReadsAsNone()
        {
            SaveResult deck = store.Save(ImportValidatorTests.MinimalPdf(), "deck.pdf", ImportKind.Presentation);
            store.SetActivePresentation(deck.File.id);
            File.Delete(Path.Combine(dir, deck.File.storedFileName));
            Assert.IsNull(store.GetActivePresentation());
        }

        [Test]
        public void TamperedActiveIdIsIgnored()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "index.json"),
                "{\"files\":[],\"activePresentationId\":\"../../secret\"}");
            Assert.IsNull(store.GetActivePresentation());
        }
    
        [Test]
        public void IndexCardsAreReadableAsText()
        {
            SaveResult r = store.Save(Text("Hook\n\nPoint"), "cards.txt", ImportKind.IndexCards);
            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(ImportKind.IndexCards, r.File.kind);
            Assert.IsTrue(store.TryReadText(r.File.id, out string text, out _));
            Assert.AreEqual("Hook\n\nPoint", text);
        }

        [Test]
        public void PdfNotesKeepTheirKindAndArePdfBacked()
        {
            SaveResult r = store.Save(ImportValidatorTests.MinimalPdf(), "cards.pdf", ImportKind.IndexCards);
            Assert.IsTrue(r.File.IsPdf);
            StringAssert.EndsWith(".pdf", r.File.storedFileName);
            Assert.IsNotNull(store.GetPdfPath(r.File.id));
            Assert.IsNull(store.GetPresentationPath(r.File.id), "cards are never projected");
            Assert.IsFalse(store.TryReadText(r.File.id, out _, out _));
        }

        [Test]
        public void ActiveNotesSurviveReloadAndAreIndependentOfThePresentation()
        {
            SaveResult deck = store.Save(ImportValidatorTests.MinimalPdf(), "deck.pdf", ImportKind.Presentation);
            SaveResult cards = store.Save(Text("one"), "cards.txt", ImportKind.IndexCards);
            Assert.IsTrue(store.SetActivePresentation(deck.File.id));
            Assert.IsTrue(store.SetActiveNotes(cards.File.id));

            var reloaded = new ImportStore(dir);
            Assert.AreEqual(cards.File.id, reloaded.GetActiveNotes().id);
            Assert.AreEqual(deck.File.id, reloaded.GetActivePresentation().id);
        }

        [Test]
        public void PresentationCannotBeActiveNotes()
        {
            SaveResult deck = store.Save(ImportValidatorTests.MinimalPdf(), "deck.pdf", ImportKind.Presentation);
            Assert.IsFalse(store.SetActiveNotes(deck.File.id));
            Assert.IsFalse(store.SetActiveNotes("not-an-id"));
            Assert.IsNull(store.GetActiveNotes());
        }

        [Test]
        public void ClearingAndDeletingActiveNotesWorks()
        {
            SaveResult script = store.Save(Text("words"), "talk.txt", ImportKind.Script);
            Assert.IsTrue(store.SetActiveNotes(script.File.id));
            Assert.IsTrue(store.SetActiveNotes(null));
            Assert.IsNull(store.GetActiveNotes());

            store.SetActiveNotes(script.File.id);
            store.Delete(script.File.id);
            Assert.IsNull(store.GetActiveNotes());
        }

        [Test]
        public void IndexWrittenBeforeNotesExistedStillLoads()
        {
            SaveResult script = store.Save(Text("words"), "talk.txt", ImportKind.Script);
            string json = File.ReadAllText(Path.Combine(dir, "index.json")).Replace(",\"activeNotesId\":\"\"", string.Empty);
            File.WriteAllText(Path.Combine(dir, "index.json"), json);

            Assert.AreEqual(1, store.List().Count);
            Assert.IsNull(store.GetActiveNotes());
            Assert.AreEqual(script.File.id, store.List()[0].id);
        }

        [Test]
        public void TamperedNotesIdIsIgnored()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "index.json"),
                "{\"files\":[],\"activeNotesId\":\"../../secret\"}");
            Assert.IsNull(store.GetActiveNotes());
        }
    
        [Test]
        public void ScriptDisplayDefaultsToHeldAndPersists()
        {
            Assert.AreEqual(ScriptDisplay.Held, store.GetScriptDisplay());
            Assert.IsTrue(store.SetScriptDisplay(ScriptDisplay.Teleprompter));
            Assert.AreEqual(ScriptDisplay.Teleprompter, new ImportStore(dir).GetScriptDisplay());
        }

        [Test]
        public void ScriptDisplaySurvivesImportsAndNotesChanges()
        {
            store.SetScriptDisplay(ScriptDisplay.Teleprompter);
            SaveResult script = store.Save(Text("words"), "talk.txt", ImportKind.Script);
            store.SetActiveNotes(script.File.id);
            store.Delete(script.File.id);
            Assert.AreEqual(ScriptDisplay.Teleprompter, store.GetScriptDisplay());
        }

        [TestCase("{\"files\":[]}")]
        [TestCase("{\"files\":[],\"scriptDisplay\":7}")]
        [TestCase("{\"files\":[],\"scriptDisplay\":-1}")]
        public void MissingOrUnknownScriptDisplayReadsAsHeld(string json)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "index.json"), json);
            Assert.AreEqual(ScriptDisplay.Held, store.GetScriptDisplay());
        }

        [Test]
        public void UnknownScriptDisplayCannotBeSaved()
        {
            Assert.IsFalse(store.SetScriptDisplay((ScriptDisplay)9));
        }
    }
}
