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
            SaveResult r = store.Save(Text("Hello team."), "talk.txt");
            Assert.IsTrue(r.Success, r.Message);

            Assert.IsTrue(store.TryReadText(r.File.id, out string text, out string error), error);
            Assert.AreEqual("Hello team.", text);
        }

        [Test]
        public void SavesPdfAndExposesPath()
        {
            SaveResult r = store.Save(ImportValidatorTests.MinimalPdf(), "deck.pdf");
            Assert.IsTrue(r.Success, r.Message);

            string path = store.GetPresentationPath(r.File.id);
            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(path.StartsWith(Path.GetFullPath(dir)));
        }

        [Test]
        public void StoredNameIsGeneratedNotClientControlled()
        {
            SaveResult r = store.Save(Text("x"), "../../escape.txt");
            Assert.IsTrue(r.Success);
            Assert.AreEqual("escape.txt", r.File.displayName);
            StringAssert.IsMatch("^[0-9a-f]{32}\\.txt$", r.File.storedFileName);
            Assert.IsFalse(File.Exists(Path.Combine(dir, "..", "..", "escape.txt")));
        }

        [Test]
        public void ListSurvivesReloadNewestFirst()
        {
            store.Save(Text("one"), "one.txt");
            store.Save(Text("two"), "two.txt");

            var reloaded = new ImportStore(dir);
            var list = reloaded.List();
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("two.txt", list[0].displayName);
        }

        [Test]
        public void InvalidFileIsNotStored()
        {
            SaveResult r = store.Save(Text("   "), "blank.txt");
            Assert.IsFalse(r.Success);
            Assert.AreEqual(0, store.List().Count);
        }

        [Test]
        public void CorruptIndexRecoversToEmptyAndStillSaves()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "index.json"), "{ not json at all");

            Assert.AreEqual(0, store.List().Count);
            Assert.IsTrue(store.Save(Text("fresh"), "a.txt").Success);
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
            SaveResult r = store.Save(Text("gone soon"), "a.txt");
            File.Delete(Path.Combine(dir, r.File.storedFileName));

            Assert.AreEqual(0, store.List().Count);
            Assert.IsFalse(store.TryReadText(r.File.id, out _, out string error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void DeleteRemovesFileAndEntry()
        {
            SaveResult r = store.Save(Text("bye"), "a.txt");
            Assert.IsTrue(store.Delete(r.File.id));
            Assert.AreEqual(0, store.List().Count);
            Assert.IsFalse(File.Exists(Path.Combine(dir, r.File.storedFileName)));
            Assert.IsFalse(store.Delete(r.File.id));
        }

        [Test]
        public void ReadingAPresentationAsTextFails()
        {
            SaveResult r = store.Save(ImportValidatorTests.MinimalPdf(), "deck.pdf");
            Assert.IsFalse(store.TryReadText(r.File.id, out _, out _));
        }
    }
}
