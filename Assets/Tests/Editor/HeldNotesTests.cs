using System;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using StageReadyVR.Notes;
using UnityEngine;

namespace StageReadyVR.Import.Tests
{
    public class NotesTextTests
    {
        [Test]
        public void BlankLinesSeparateCards()
        {
            var cards = NotesText.SplitCards("Opening\nHook the room\n\nMain Idea\n- the problem\n\n\nThanks");
            Assert.AreEqual(3, cards.Count);
            Assert.AreEqual("Opening\nHook the room", cards[0]);
            Assert.AreEqual("Main Idea\n• the problem", cards[1]);
            Assert.AreEqual("Thanks", cards[2]);
        }

        [Test]
        public void SeparatorLinesWinAndKeepBlankLinesInsideACard()
        {
            var cards = NotesText.SplitCards("Card one\n\nstill one\n---\nCard two\n=====\n\nCard three\n---\n");
            Assert.AreEqual(3, cards.Count);
            Assert.AreEqual("Card one\n\nstill one", cards[0]);
            Assert.AreEqual("Card two", cards[1]);
            Assert.AreEqual("Card three", cards[2]);
        }

        [Test]
        public void WindowsLineEndingsAndStarBulletsAreNormalised()
        {
            var cards = NotesText.SplitCards("Point\r\n* first\r\n•second\r\n\r\nNext");
            Assert.AreEqual(2, cards.Count);
            Assert.AreEqual("Point\n• first\n• second", cards[0]);
        }

        [Test]
        public void DashesInsideTextAreNotBulletsOrSeparators()
        {
            var cards = NotesText.SplitCards("-5 degrees outside\nwell -- maybe");
            Assert.AreEqual(1, cards.Count);
            Assert.AreEqual("-5 degrees outside\nwell -- maybe", cards[0]);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("---\n---")]
        public void EmptyInputStillGivesOneCard(string text)
        {
            Assert.AreEqual(1, NotesText.SplitCards(text).Count);
        }
    }

    public class StickFlickTests
    {
        [Test]
        public void FiresOnceUntilTheStickReturnsToCentre()
        {
            var flick = new StickFlick();
            Assert.AreEqual(0, flick.Step(0.5f));
            Assert.AreEqual(1, flick.Step(0.9f));
            Assert.AreEqual(0, flick.Step(1f), "held stick must not repeat");
            Assert.AreEqual(0, flick.Step(0.5f), "between the thresholds stays disarmed");
            Assert.AreEqual(0, flick.Step(0.1f));
            Assert.AreEqual(-1, flick.Step(-0.8f));
        }

        [Test]
        public void SwingingStraightAcrossDoesNotFireTwice()
        {
            var flick = new StickFlick();
            Assert.AreEqual(1, flick.Step(0.9f));
            Assert.AreEqual(0, flick.Step(-0.9f));
        }

        [Test]
        public void ResetRearms()
        {
            var flick = new StickFlick();
            flick.Step(1f);
            flick.Reset();
            Assert.AreEqual(1, flick.Step(1f));
        }
    }

    public class HeldPoseTests
    {
        const float Eps = 1e-3f;
        static readonly HeldPoseSettings Settings = HeldPoseSettings.Default;

        static Vector3 Forward(float yawDegrees, float pitchDownDegrees = 0f)
        {
            return Quaternion.Euler(pitchDownDegrees, yawDegrees, 0f) * Vector3.forward;
        }

        static Vector3 Up(float yawDegrees, float pitchDownDegrees = 0f)
        {
            return Quaternion.Euler(pitchDownDegrees, yawDegrees, 0f) * Vector3.up;
        }

        [Test]
        public void NotesSitInFrontAndBelowTheEyes()
        {
            HeldPoseState s = HeldPose.Snap(new Vector3(0f, 1.6f, 0f), Vector3.forward, Vector3.up);
            Pose p = HeldPose.ToPose(s, Settings);
            Assert.AreEqual(0f, p.position.x, Eps);
            Assert.AreEqual(1.6f - Settings.drop, p.position.y, Eps);
            Assert.AreEqual(Settings.forward, p.position.z, Eps);
        }

        [Test]
        public void PageFacesTheEyes()
        {
            HeldPoseState s = HeldPose.Snap(new Vector3(0f, 1.6f, 0f), Vector3.forward, Vector3.up);
            Pose p = HeldPose.ToPose(s, Settings);

            // the canvas forward points from the eyes to the page, so its front is seen
            Vector3 toPage = (p.position - s.Head).normalized;
            Assert.Greater(Vector3.Dot(p.rotation * Vector3.forward, toPage), 0.999f);
        }

        [Test]
        public void LookingUpOrDownDoesNotMoveTheNotes()
        {
            HeldPoseState s = HeldPose.Snap(Vector3.zero, Vector3.forward, Vector3.up);
            Pose before = HeldPose.ToPose(s, Settings);

            for (int i = 0; i < 60; i++) s = HeldPose.Step(s, Vector3.zero, Forward(0f, -40f), Up(0f, -40f), Settings, 1f / 60f);
            for (int i = 0; i < 60; i++) s = HeldPose.Step(s, Vector3.zero, Forward(0f, 89f), Up(0f, 89f), Settings, 1f / 60f);

            Assert.AreEqual(0f, Vector3.Distance(before.position, HeldPose.ToPose(s, Settings).position), Eps);
        }

        [Test]
        public void SmallTurnsAreIgnored()
        {
            HeldPoseState s = HeldPose.Snap(Vector3.zero, Vector3.forward, Vector3.up);
            for (int i = 0; i < 120; i++) s = HeldPose.Step(s, Vector3.zero, Forward(25f), Up(25f), Settings, 1f / 60f);
            Assert.AreEqual(0f, s.Yaw, Eps);
        }

        [Test]
        public void LargeTurnsAreFollowedAllTheWay()
        {
            HeldPoseState s = HeldPose.Snap(Vector3.zero, Vector3.forward, Vector3.up);
            for (int i = 0; i < 180; i++) s = HeldPose.Step(s, Vector3.zero, Forward(90f), Up(90f), Settings, 1f / 60f);
            Assert.AreEqual(90f, s.Yaw, 2.5f);
            Assert.IsFalse(s.Recentering);
        }

        [Test]
        public void FollowWrapsTheShortWayAround()
        {
            HeldPoseState s = HeldPose.Snap(Vector3.zero, Forward(170f), Up(170f));
            for (int i = 0; i < 180; i++) s = HeldPose.Step(s, Vector3.zero, Forward(-130f), Up(-130f), Settings, 1f / 60f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(s.Yaw, -130f), 2.5f);
        }

        [Test]
        public void HeadingFallsBackToHeadUpWhenLookingStraightDown()
        {
            Assert.AreEqual(90f, HeldPose.YawOf(Forward(90f, 90f), Up(90f, 90f)), 0.5f);
        }
    }

    /// <summary>Drives the real component in edit mode against a temporary store.</summary>
    public class HeldNotesTests
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        string dir;
        ImportStore store;
        GameObject go;
        GameObject head;
        HeldNotes notes;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Application.temporaryCachePath, "notes-tests-" + Guid.NewGuid().ToString("N"));
            store = new ImportStore(dir);
            go = new GameObject("notes");
            head = new GameObject("head");
            head.transform.position = new Vector3(0f, 1.6f, 0f);
            notes = go.AddComponent<HeldNotes>();
            notes.UseStore(store);
        }

        [TearDown]
        public void TearDown()
        {
            // edit mode does not call OnDestroy, and it stops any render thread
            Invoke("OnDestroy");
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(head);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        void Invoke(string method)
        {
            typeof(HeldNotes).GetMethod(method, Any).Invoke(notes, null);
        }

        static byte[] Text(string s) { return new UTF8Encoding(false).GetBytes(s); }

        string Footer()
        {
            foreach (TMPro.TMP_Text t in go.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (t.name == "Footer") return t.text;
            }
            return null;
        }

        [Test]
        public void NothingChosenMeansNothingShown()
        {
            store.Save(Text("a\n\nb"), "cards.txt", ImportKind.IndexCards);
            Invoke("Start");
            notes.Begin(head.transform);

            Assert.IsFalse(notes.HasNotes);
            Assert.IsFalse(notes.IsShown);
            Assert.AreEqual(0, go.transform.childCount);
            Assert.IsFalse(notes.Next());
        }

        [Test]
        public void CardsStayHiddenUntilBeginThenSitLowInFront()
        {
            SaveResult cards = store.Save(Text("Hook\n\nPoint\n\nClose"), "cards.txt", ImportKind.IndexCards);
            Assert.IsTrue(store.SetActiveNotes(cards.File.id));

            Invoke("Start");
            Assert.IsTrue(notes.HasNotes);
            Assert.IsFalse(notes.IsShown, "notes must stay hidden until the session begins");

            notes.Begin(head.transform);
            Assert.IsTrue(notes.IsShown);

            Transform canvas = go.transform.GetChild(0);
            Assert.Less(canvas.position.y, 1.4f);
            Assert.Greater(canvas.position.z, 0.2f);
            Assert.Less(canvas.position.z, 0.8f);

            notes.Hide();
            Assert.IsFalse(notes.IsShown);
        }

        [Test]
        public void CardsTurnBothWaysAndStopAtTheEnds()
        {
            SaveResult cards = store.Save(Text("Hook\n---\nPoint\n---\nClose"), "cards.txt", ImportKind.IndexCards);
            store.SetActiveNotes(cards.File.id);
            Invoke("Start");

            Assert.AreEqual(ImportKind.IndexCards, notes.Kind);
            Assert.AreEqual(3, notes.PageCount);
            Assert.AreEqual("Card 1 / 3", Footer());
            Assert.IsFalse(notes.Previous());

            Assert.IsTrue(notes.Next());
            Assert.IsTrue(notes.Next());
            Assert.IsFalse(notes.Next());
            Assert.AreEqual(2, notes.CurrentPage);
            Assert.AreEqual("Card 3 / 3", Footer());

            Assert.IsTrue(notes.Previous());
            Assert.AreEqual(1, notes.CurrentPage);
        }

        [Test]
        public void AngleBracketsInNotesAreShownLiterally()
        {
            SaveResult cards = store.Save(Text("<size=500>Intro</size>"), "cards.txt", ImportKind.IndexCards);
            store.SetActiveNotes(cards.File.id);
            Invoke("Start");

            TMPro.TMP_Text body = null;
            foreach (TMPro.TMP_Text t in go.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (t.name == "Body") body = t;
            }
            Assert.IsNotNull(body);
            Assert.IsFalse(body.richText);
            Assert.AreEqual("<size=500>Intro</size>", body.text);
        }

        [Test]
        public void LongScriptIsSplitIntoPages()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 120; i++) sb.Append("This is sentence number ").Append(i).Append(" of a long practice speech. ");
            SaveResult script = store.Save(Text(sb.ToString()), "speech.txt", ImportKind.Script);
            store.SetActiveNotes(script.File.id);
            Invoke("Start");

            Assert.AreEqual(ImportKind.Script, notes.Kind);
            Assert.Greater(notes.PageCount, 1);
            StringAssert.StartsWith("Page 1 / ", Footer());
            Assert.IsTrue(notes.Next());
            StringAssert.StartsWith("Page 2 / ", Footer());
        }

        [Test]
        public void PdfCardsUsePlaceholderPagesInTheEditor()
        {
            byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n<< /Type /Page >>\n<< /Type /Page >>\n%%EOF\n");
            SaveResult cards = store.Save(pdf, "cards.pdf", ImportKind.IndexCards);
            store.SetActiveNotes(cards.File.id);
            Invoke("Start");

            Assert.AreEqual(2, notes.PageCount);
            StringAssert.Contains("preview", Footer());
            Assert.IsTrue(notes.Next());
            Assert.IsFalse(notes.Next());
        }
    }
}
