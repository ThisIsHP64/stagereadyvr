using System;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using StageReadyVR.Notes;
using UnityEngine;

namespace StageReadyVR.Import.Tests
{
    public class TeleprompterScrollTests
    {
        const float Eps = 1e-3f;

        [Test]
        public void ScrollsAtTheChosenWordsPerMinute()
        {
            // 600 units over 300 words is 2 units a word, so 120 wpm is 2 words or 4 units a second
            var scroll = new TeleprompterScroll(600f, 300, 120f);
            scroll.Step(1f);
            Assert.AreEqual(4f, scroll.Offset, Eps);
        }

        [Test]
        public void PauseStopsScrollingAndResumes()
        {
            var scroll = new TeleprompterScroll(600f, 300, 120f);
            scroll.TogglePause();
            scroll.Step(5f);
            Assert.AreEqual(0f, scroll.Offset, Eps);
            Assert.IsTrue(scroll.Paused);

            scroll.TogglePause();
            scroll.Step(1f);
            Assert.AreEqual(4f, scroll.Offset, Eps);
        }

        [Test]
        public void StopsAtTheEnd()
        {
            var scroll = new TeleprompterScroll(10f, 5, 220f);
            scroll.Step(100f);
            Assert.AreEqual(10f, scroll.Offset, Eps);
            Assert.IsTrue(scroll.AtEnd);
        }

        [Test]
        public void SpeedIsClampedAtBothEnds()
        {
            var scroll = new TeleprompterScroll(100f, 10, 500f);
            Assert.AreEqual(TeleprompterScroll.MaxWpm, scroll.Wpm);
            for (int i = 0; i < 50; i++) scroll.ChangeSpeed(-10f);
            Assert.AreEqual(TeleprompterScroll.MinWpm, scroll.Wpm);
            scroll.ChangeSpeed(10f);
            Assert.AreEqual(TeleprompterScroll.MinWpm + 10f, scroll.Wpm);
        }

        [Test]
        public void NudgeMovesWholeLinesWithinRange()
        {
            var scroll = new TeleprompterScroll(100f, 10);
            scroll.Nudge(-3, 40f);
            Assert.AreEqual(0f, scroll.Offset, Eps);
            scroll.Nudge(2, 40f);
            Assert.AreEqual(80f, scroll.Offset, Eps);
            scroll.Nudge(3, 40f);
            Assert.AreEqual(100f, scroll.Offset, Eps);
        }

        [Test]
        public void EmptyScriptNeverMoves()
        {
            var scroll = new TeleprompterScroll(0f, 0);
            scroll.Step(10f);
            Assert.AreEqual(0f, scroll.Offset);
            Assert.IsTrue(scroll.AtEnd);
        }

        [TestCase("", 0)]
        [TestCase("one", 1)]
        [TestCase("  two\twords \n", 2)]
        [TestCase("Hello, everyone.\n\nThank you!", 4)]
        public void CountsWords(string text, int expected)
        {
            Assert.AreEqual(expected, TeleprompterScroll.CountWords(text));
        }

        [Test]
        public void AheadIsLevelAndIgnoresPitch()
        {
            Vector3 lookingDown = Quaternion.Euler(50f, 90f, 0f) * Vector3.forward;
            Vector3 upWhenDown = Quaternion.Euler(50f, 90f, 0f) * Vector3.up;
            Pose p = HeldPose.Ahead(new Vector3(0f, 1.6f, 0f), lookingDown, upWhenDown, 1.8f, -0.05f);

            Assert.AreEqual(1.8f, p.position.x, Eps);
            Assert.AreEqual(1.55f, p.position.y, Eps);
            Assert.AreEqual(0f, p.position.z, Eps);
            Assert.AreEqual(0f, (p.rotation * Vector3.forward).y, Eps);
        }
    }

    /// <summary>Drives the real teleprompter and held notes in edit mode against a temporary store.</summary>
    public class TeleprompterTests
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        string dir;
        ImportStore store;
        GameObject go;
        GameObject head;
        Teleprompter prompter;
        HeldNotes held;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Application.temporaryCachePath, "prompter-tests-" + Guid.NewGuid().ToString("N"));
            store = new ImportStore(dir);
            go = new GameObject("notes");
            head = new GameObject("head");
            head.transform.position = new Vector3(0f, 1.6f, 0f);
            prompter = go.AddComponent<Teleprompter>();
            prompter.UseStore(store);
            prompter.UseSpeedMemory(false);
            held = go.AddComponent<HeldNotes>();
            held.UseStore(store);
        }

        [TearDown]
        public void TearDown()
        {
            Call(held, "OnDestroy");
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(head);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        static void Call(object target, string method, params object[] args)
        {
            target.GetType().GetMethod(method, Any).Invoke(target, args.Length == 0 ? null : args);
        }

        static byte[] Text(string s) { return new UTF8Encoding(false).GetBytes(s); }

        static string LongSpeech()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 80; i++) sb.Append("Sentence ").Append(i).Append(" of the practice speech goes here. ");
            return sb.ToString();
        }

        void Choose(byte[] data, string name, ImportKind kind, ScriptDisplay display)
        {
            SaveResult r = store.Save(data, name, kind);
            Assert.IsTrue(r.Success, r.Message);
            Assert.IsTrue(store.SetActiveNotes(r.File.id));
            Assert.IsTrue(store.SetScriptDisplay(display));
            Call(prompter, "Start");
            Call(held, "Start");
        }

        [Test]
        public void TextScriptInTeleprompterStyleGoesToTheTeleprompterOnly()
        {
            Choose(Text(LongSpeech()), "talk.txt", ImportKind.Script, ScriptDisplay.Teleprompter);
            Assert.IsTrue(prompter.HasScript);
            Assert.IsFalse(held.HasNotes, "held notes must step aside for the teleprompter");
        }

        [Test]
        public void HeldStyleLeavesTheTeleprompterInert()
        {
            Choose(Text(LongSpeech()), "talk.txt", ImportKind.Script, ScriptDisplay.Held);
            Assert.IsFalse(prompter.HasScript);
            Assert.IsTrue(held.HasNotes);
        }

        [Test]
        public void CardsAndPdfScriptsStayHeldEvenInTeleprompterStyle()
        {
            Choose(Text("a\n\nb"), "cards.txt", ImportKind.IndexCards, ScriptDisplay.Teleprompter);
            Assert.IsFalse(prompter.HasScript);
            Assert.IsTrue(held.HasNotes);
        }

        [Test]
        public void PdfScriptFallsBackToHeldPages()
        {
            byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n<< /Type /Page >>\n%%EOF\n");
            Choose(pdf, "talk.pdf", ImportKind.Script, ScriptDisplay.Teleprompter);
            Assert.IsFalse(prompter.HasScript);
            Assert.IsTrue(held.HasNotes);
        }

        [Test]
        public void HiddenUntilBeginThenFixedAheadAtEyeLevel()
        {
            Choose(Text(LongSpeech()), "talk.txt", ImportKind.Script, ScriptDisplay.Teleprompter);
            Assert.IsFalse(prompter.IsShown);

            prompter.Begin(head.transform);
            Assert.IsTrue(prompter.IsShown);

            Transform canvas = go.transform.Find("TeleprompterCanvas");
            Assert.AreEqual(1.8f, canvas.position.z, 0.01f);
            Assert.AreEqual(1.55f, canvas.position.y, 0.01f);

            // turning away must not drag it along
            head.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Call(prompter, "Update");
            Assert.AreEqual(1.8f, canvas.position.z, 0.01f);

            prompter.Hide();
            Assert.IsFalse(prompter.IsShown);
        }

        [Test]
        public void ScrollsOnlyWhileNotHaltedOrPaused()
        {
            Choose(Text(LongSpeech()), "talk.txt", ImportKind.Script, ScriptDisplay.Teleprompter);
            prompter.Begin(head.transform);

            Call(prompter, "Step", 2f);
            Assert.AreEqual(0f, prompter.Offset, "starts halted until the session says otherwise");

            prompter.SetHalted(false);
            Call(prompter, "Step", 2f);
            float moved = prompter.Offset;
            Assert.Greater(moved, 0f);

            prompter.TogglePause();
            Call(prompter, "Step", 2f);
            Assert.AreEqual(moved, prompter.Offset);

            prompter.TogglePause();
            prompter.SetHalted(true);
            Call(prompter, "Step", 2f);
            Assert.AreEqual(moved, prompter.Offset);
        }

        [Test]
        public void SpeedAndNudgeControlsWork()
        {
            Choose(Text(LongSpeech()), "talk.txt", ImportKind.Script, ScriptDisplay.Teleprompter);
            Assert.AreEqual(TeleprompterScroll.DefaultWpm, prompter.Wpm);

            prompter.Faster();
            Assert.AreEqual(TeleprompterScroll.DefaultWpm + 10f, prompter.Wpm);
            prompter.Slower();
            prompter.Slower();
            Assert.AreEqual(TeleprompterScroll.DefaultWpm - 10f, prompter.Wpm);

            prompter.Nudge(1);
            Assert.Greater(prompter.Offset, 0f);
            prompter.Nudge(-1);
            Assert.AreEqual(0f, prompter.Offset, 0.01f);
        }

        [Test]
        public void ScriptTextIsShownLiterally()
        {
            Choose(Text("<b>Opening</b> line"), "talk.txt", ImportKind.Script, ScriptDisplay.Teleprompter);
            TMPro.TMP_Text script = go.transform.Find("TeleprompterCanvas/Panel/Viewport/Script").GetComponent<TMPro.TMP_Text>();
            Assert.IsFalse(script.richText);
            Assert.AreEqual("<b>Opening</b> line", script.text);
        }
    }
}
