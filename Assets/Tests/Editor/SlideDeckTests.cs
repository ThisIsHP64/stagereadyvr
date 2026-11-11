using System;
using System.Collections.Generic;
using NUnit.Framework;
using StageReadyVR.Presentation;

namespace StageReadyVR.Import.Tests
{
    public class SlideDeckTests
    {
        // runs jobs on the calling thread so tests are deterministic
        sealed class ImmediateWorker : IRenderWorker
        {
            public bool Disposed;
            public void Post(Action work) { if (!Disposed) work(); }
            public void Dispose() { Disposed = true; }
        }

        sealed class FakeSource : IPdfPageSource
        {
            public readonly List<int> Rendered = new List<int>();
            public readonly HashSet<int> Broken = new HashSet<int>();
            public bool Disposed;

            public FakeSource(int pages) { PageCount = pages; }
            public int PageCount { get; }

            public bool TryRenderPage(int index, int maxEdge, out PageImage image, out string error)
            {
                Rendered.Add(index);
                image = default;
                error = null;
                if (Broken.Contains(index)) { error = "broken page"; return false; }
                image = new PageImage(2, 1, new byte[8]);
                return true;
            }

            public void Dispose() { Disposed = true; }
        }

        FakeSource source;
        ImmediateWorker worker;
        SlideDeck deck;
        List<int> shown;
        List<string> failures;

        void Make(int pages)
        {
            source = new FakeSource(pages);
            worker = new ImmediateWorker();
            deck = new SlideDeck(source, worker, 64);
            shown = new List<int>();
            failures = new List<string>();
            deck.PageRendered += (i, _) => shown.Add(i);
            deck.PageFailed += (i, e) => failures.Add(i + ":" + e);
        }

        [Test]
        public void BeginRendersCurrentAndPrefetchesNext()
        {
            Make(3);
            deck.Begin();
            deck.Pump();
            CollectionAssert.AreEqual(new[] { 0, 1 }, source.Rendered);
            CollectionAssert.AreEqual(new[] { 0, 1 }, shown);
        }

        [Test]
        public void AdvanceMovesOneAndRequestsOnlyTheNewNextPage()
        {
            Make(4);
            deck.Begin();
            Assert.IsTrue(deck.Advance());
            Assert.AreEqual(1, deck.Current);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, source.Rendered);
        }

        [Test]
        public void AdvanceStopsOnLastSlide()
        {
            Make(2);
            deck.Begin();
            Assert.IsTrue(deck.Advance());
            Assert.IsTrue(deck.IsLast);
            Assert.IsFalse(deck.Advance());
            Assert.AreEqual(1, deck.Current);
        }

        [Test]
        public void SinglePageDeckNeverAdvances()
        {
            Make(1);
            deck.Begin();
            Assert.IsFalse(deck.Advance());
            CollectionAssert.AreEqual(new[] { 0 }, source.Rendered);
        }

        [Test]
        public void EmptyDeckIsHarmless()
        {
            Make(0);
            deck.Begin();
            deck.Pump();
            Assert.IsFalse(deck.Advance());
            Assert.IsEmpty(source.Rendered);
            Assert.IsFalse(deck.IsWanted(0));
        }

        [Test]
        public void OnlyCurrentAndNextAreWanted()
        {
            Make(5);
            deck.Begin();
            deck.Advance();
            Assert.IsFalse(deck.IsWanted(0));
            Assert.IsTrue(deck.IsWanted(1));
            Assert.IsTrue(deck.IsWanted(2));
            Assert.IsFalse(deck.IsWanted(3));
        }

        [Test]
        public void ResultsForPagesNoLongerWantedAreDropped()
        {
            Make(5);
            deck.Begin();
            deck.Advance();
            deck.Advance();
            deck.Pump();
            CollectionAssert.DoesNotContain(shown, 0);
            CollectionAssert.DoesNotContain(shown, 1);
            CollectionAssert.Contains(shown, 2);
            CollectionAssert.Contains(shown, 3);
        }

        [Test]
        public void RenderFailureIsReportedWithMessage()
        {
            Make(2);
            source.Broken.Add(0);
            deck.Begin();
            deck.Pump();
            CollectionAssert.AreEqual(new[] { "0:broken page" }, failures);
            CollectionAssert.AreEqual(new[] { 1 }, shown);
        }

        [Test]
        public void DisposeReleasesSourceAndWorker()
        {
            Make(2);
            deck.Dispose();
            Assert.IsTrue(source.Disposed);
            Assert.IsTrue(worker.Disposed);
            Assert.IsFalse(deck.Advance());
            Assert.DoesNotThrow(() => deck.Dispose());
        }

        [Test]
        public void ThreadWorkerRendersInBackgroundAndPumpDelivers()
        {
            var src = new FakeSource(2);
            var threaded = new SlideDeck(src, new ThreadRenderWorker(), 64);
            var got = new List<int>();
            threaded.PageRendered += (i, _) => got.Add(i);
            threaded.Begin();

            DateTime until = DateTime.UtcNow.AddSeconds(5);
            while (got.Count < 2 && DateTime.UtcNow < until)
            {
                threaded.Pump();
                System.Threading.Thread.Sleep(5);
            }
            threaded.Dispose();

            CollectionAssert.AreEquivalent(new[] { 0, 1 }, got);
            Assert.IsTrue(src.Disposed);
        }

        [Test]
        public void PageImageRejectsMismatchedPixels()
        {
            Assert.Throws<ArgumentException>(() => new PageImage(2, 2, new byte[4]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PageImage(0, 2, new byte[0]));
        }
    }
}
