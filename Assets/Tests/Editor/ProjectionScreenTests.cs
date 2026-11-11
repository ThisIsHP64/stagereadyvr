using System;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using StageReadyVR.Presentation;
using UnityEngine;

namespace StageReadyVR.Import.Tests
{
    public class ScreenPlacementTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void ScreenSitsBehindAndAboveTheHead()
        {
            Pose p = ScreenPlacement.Behind(new Vector3(1f, 1.6f, 2f), Vector3.forward, 2.5f, 0.35f);
            Assert.AreEqual(1f, p.position.x, Eps);
            Assert.AreEqual(1.95f, p.position.y, Eps);
            Assert.AreEqual(-0.5f, p.position.z, Eps);
        }

        [Test]
        public void CanvasForwardPointsBackSoTheFrontFacesTheAudience()
        {
            Pose p = ScreenPlacement.Behind(Vector3.zero, Vector3.right, 2f, 0f);
            Vector3 forward = p.rotation * Vector3.forward;
            Assert.AreEqual(-1f, forward.x, Eps);
            Assert.AreEqual(0f, forward.y, Eps);
        }

        [Test]
        public void HeadPitchIsIgnored()
        {
            Vector3 lookingDown = new Vector3(0f, -0.8f, 0.6f);
            Pose p = ScreenPlacement.Behind(Vector3.zero, lookingDown, 2f, 0f);
            Assert.AreEqual(0f, p.position.y, Eps);
            Assert.AreEqual(-2f, p.position.z, Eps);
            Assert.AreEqual(0f, (p.rotation * Vector3.up - Vector3.up).magnitude, Eps);
        }

        [Test]
        public void LookingStraightDownFallsBackToWorldForward()
        {
            Pose p = ScreenPlacement.Behind(Vector3.zero, Vector3.down, 2f, 0f);
            Assert.AreEqual(-2f, p.position.z, Eps);
        }

        [Test]
        public void FitKeepsAspectInsideBounds()
        {
            Vector2 wide = ScreenPlacement.FitSize(16f / 9f, 3.2f, 1.8f);
            Assert.AreEqual(3.2f, wide.x, Eps);
            Assert.AreEqual(1.8f, wide.y, Eps);

            Vector2 square = ScreenPlacement.FitSize(1f, 3.2f, 1.8f);
            Assert.AreEqual(1.8f, square.x, Eps);
            Assert.AreEqual(1.8f, square.y, Eps);

            Vector2 bad = ScreenPlacement.FitSize(0f, 3.2f, 1.8f);
            Assert.AreEqual(16f / 9f, bad.x / bad.y, 1e-3f);
        }
    }

    public class PlaceholderPageSourceTests
    {
        string dir;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Application.temporaryCachePath, "placeholder-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        [Test]
        public void CountsPlainPageObjectsButNotThePageTree()
        {
            string path = Path.Combine(dir, "three.pdf");
            File.WriteAllText(path, "%PDF-1.4\n<< /Type /Pages /Count 3 >>\n<< /Type /Page >>\n<</Type/Page>>\n<< /Type /Page /Parent 2 0 R >>\n%%EOF");
            Assert.AreEqual(3, PlaceholderPageSource.CountPages(path));
        }

        [Test]
        public void FallsBackWhenPagesAreNotVisible()
        {
            string path = Path.Combine(dir, "packed.pdf");
            File.WriteAllBytes(path, ImportValidatorTests.MinimalPdf());
            Assert.AreEqual(PlaceholderPageSource.FallbackPageCount, PlaceholderPageSource.CountPages(path));
            Assert.AreEqual(PlaceholderPageSource.FallbackPageCount, PlaceholderPageSource.CountPages(Path.Combine(dir, "missing.pdf")));
        }

        [Test]
        public void RendersOpaquePagesWithinTheEdgeLimit()
        {
            var source = new PlaceholderPageSource(2);
            Assert.IsTrue(source.TryRenderPage(1, 640, out PageImage image, out _));
            Assert.LessOrEqual(Mathf.Max(image.Width, image.Height), 640);
            Assert.AreEqual(255, image.Rgba[3]);
            Assert.IsFalse(source.TryRenderPage(2, 640, out _, out string error));
            Assert.IsNotEmpty(error);
        }
    }

    /// <summary>Drives the real component in edit mode against a temporary store.</summary>
    public class ProjectionScreenTests
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        string dir;
        ImportStore store;
        GameObject go;
        GameObject head;
        ProjectionScreen screen;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Application.temporaryCachePath, "screen-tests-" + Guid.NewGuid().ToString("N"));
            store = new ImportStore(dir);
            go = new GameObject("screen");
            head = new GameObject("head");
            head.transform.position = new Vector3(0f, 1.6f, 0f);
            screen = go.AddComponent<ProjectionScreen>();
            screen.UseStore(store);
        }

        [TearDown]
        public void TearDown()
        {
            // edit mode does not call OnDestroy, and it stops the render thread
            Invoke("OnDestroy");
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(head);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        void Invoke(string method)
        {
            typeof(ProjectionScreen).GetMethod(method, Any).Invoke(screen, null);
        }

        static byte[] Pdf(int pages)
        {
            var sb = new StringBuilder("%PDF-1.4\n");
            for (int i = 0; i < pages; i++) sb.Append("<< /Type /Page >>\n");
            sb.Append("%%EOF\n");
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        Texture SlideTexture()
        {
            return go.GetComponentInChildren<UnityEngine.UI.RawImage>(true).texture;
        }

        void PumpUntilSlideShown(int index)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < until)
            {
                Invoke("Update");
                Texture t = SlideTexture();
                if (t != null && t.name == "Slide " + (index + 1)) return;
                System.Threading.Thread.Sleep(5);
            }
            Assert.Fail("Slide " + (index + 1) + " was never shown.");
        }

        [Test]
        public void NoChosenDeckMeansNoScreen()
        {
            store.Save(Pdf(3), "deck.pdf");
            Invoke("Start");
            screen.Place(head.transform);

            Assert.IsFalse(screen.HasPresentation);
            Assert.AreEqual(0, go.transform.childCount);
            Assert.IsFalse(screen.Advance());
        }

        [Test]
        public void ChosenDeckIsPlacedBehindAndAdvancesOneSlideAtATime()
        {
            SaveResult deck = store.Save(Pdf(3), "deck.pdf");
            Assert.IsTrue(store.SetActivePresentation(deck.File.id));

            Invoke("Start");
            Assert.IsTrue(screen.HasPresentation);
            Assert.AreEqual(3, screen.SlideCount);

            Transform canvas = go.transform.GetChild(0);
            Assert.IsFalse(canvas.gameObject.activeSelf, "screen must stay hidden until placed");

            screen.Place(head.transform);
            Assert.IsTrue(canvas.gameObject.activeSelf);
            Assert.Less(canvas.position.z, -2f);
            Assert.Greater(canvas.position.y, 1.6f);

            PumpUntilSlideShown(0);
            Assert.IsTrue(screen.Advance());
            Assert.AreEqual(1, screen.CurrentSlide);
            PumpUntilSlideShown(1);

            Assert.IsTrue(screen.Advance());
            Assert.IsFalse(screen.Advance());
            Assert.AreEqual(2, screen.CurrentSlide);
        }
    }
}
