using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace StageReadyVR.Presentation
{
    /// <summary>
    /// Tracks the current slide and keeps the current and next pages rendering
    /// in the background so an advance can show the next page immediately.
    /// Every member except the worker jobs must be used from the main thread;
    /// results only surface through Pump, so handlers can touch Unity objects.
    /// </summary>
    public sealed class SlideDeck : IDisposable
    {
        public const int DefaultMaxEdge = 1920;

        readonly struct Outcome
        {
            public readonly int Index;
            public readonly bool Ok;
            public readonly PageImage Image;
            public readonly string Error;

            public Outcome(int index, bool ok, PageImage image, string error)
            {
                Index = index;
                Ok = ok;
                Image = image;
                Error = error;
            }
        }

        readonly IPdfPageSource source;
        readonly IRenderWorker worker;
        readonly int maxEdge;
        readonly ConcurrentQueue<Outcome> finished = new ConcurrentQueue<Outcome>();
        readonly HashSet<int> requested = new HashSet<int>();
        bool disposed;

        /// <summary>A wanted page finished rendering.</summary>
        public event Action<int, PageImage> PageRendered;

        /// <summary>A wanted page could not be rendered, with a message that is safe to show the user.</summary>
        public event Action<int, string> PageFailed;

        public SlideDeck(IPdfPageSource source, IRenderWorker worker, int maxEdge = DefaultMaxEdge)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.worker = worker ?? throw new ArgumentNullException(nameof(worker));
            if (maxEdge <= 0) throw new ArgumentOutOfRangeException(nameof(maxEdge));
            this.maxEdge = maxEdge;
            PageCount = Math.Max(0, source.PageCount);
        }

        public int PageCount { get; }
        public int Current { get; private set; }
        public bool IsLast => Current >= PageCount - 1;

        /// <summary>Starts rendering the first pages.</summary>
        public void Begin()
        {
            RequestWindow();
        }

        /// <summary>Moves one slide forward. Returns false on the last slide, which stays shown.</summary>
        public bool Advance()
        {
            if (disposed || Current + 1 >= PageCount) return false;
            Current++;
            RequestWindow();
            return true;
        }

        /// <summary>Pages worth keeping in memory: the one shown and the one prefetched for the next press.</summary>
        public bool IsWanted(int index)
        {
            return index >= 0 && index < PageCount && (index == Current || index == Current + 1);
        }

        /// <summary>Raises events for finished renders. Call once per frame on the main thread.</summary>
        public void Pump()
        {
            while (finished.TryDequeue(out Outcome o))
            {
                if (disposed) continue;

                if (!IsWanted(o.Index))
                {
                    // allow a fresh request should the page become wanted again
                    requested.Remove(o.Index);
                    continue;
                }

                if (o.Ok) PageRendered?.Invoke(o.Index, o.Image);
                else PageFailed?.Invoke(o.Index, o.Error);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            // posted last so it runs after any render already queued on the same thread
            IPdfPageSource owned = source;
            worker.Post(owned.Dispose);
            worker.Dispose();
        }

        void RequestWindow()
        {
            requested.RemoveWhere(i => !IsWanted(i));
            Request(Current);
            Request(Current + 1);
        }

        void Request(int index)
        {
            if (disposed || index < 0 || index >= PageCount || !requested.Add(index)) return;

            IPdfPageSource src = source;
            int edge = maxEdge;
            worker.Post(() =>
            {
                Outcome outcome;
                try
                {
                    bool ok = src.TryRenderPage(index, edge, out PageImage image, out string error);
                    outcome = new Outcome(index, ok, image, ok ? null : (error ?? "This slide could not be shown."));
                }
                catch (Exception)
                {
                    outcome = new Outcome(index, false, default, "This slide could not be shown.");
                }
                finished.Enqueue(outcome);
            });
        }
    }
}
