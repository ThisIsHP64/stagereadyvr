using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace StageReadyVR.Presentation
{
    /// <summary>
    /// A single dedicated background thread. One thread keeps pdf renderer
    /// calls serialized and lets jni attach once instead of per job.
    /// </summary>
    public sealed class ThreadRenderWorker : IRenderWorker
    {
        const int JoinTimeoutMs = 2000;

        readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        readonly Thread thread;
        readonly Action onThreadStart;
        readonly Action onThreadExit;
        int disposed;

        /// <param name="onThreadStart">Runs first on the worker thread, for example to attach it to the jvm.</param>
        /// <param name="onThreadExit">Runs last on the worker thread, for example to detach it.</param>
        public ThreadRenderWorker(Action onThreadStart = null, Action onThreadExit = null)
        {
            this.onThreadStart = onThreadStart;
            this.onThreadExit = onThreadExit;
            thread = new Thread(Run) { IsBackground = true, Name = "SlideRender" };
            thread.Start();
        }

        public void Post(Action work)
        {
            if (work == null || Volatile.Read(ref disposed) != 0) return;
            try { queue.Add(work); }
            catch (InvalidOperationException) { }   // disposal raced this post, the job is simply dropped
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            queue.CompleteAdding();

            // queued jobs, including source cleanup, still run before the thread exits
            if (!thread.Join(JoinTimeoutMs))
                Debug.LogWarning("[ThreadRenderWorker] render thread did not stop in time.");
        }

        void Run()
        {
            try
            {
                onThreadStart?.Invoke();
                foreach (Action work in queue.GetConsumingEnumerable())
                {
                    try { work(); }
                    catch (Exception e) { Debug.LogWarning("[ThreadRenderWorker] job failed: " + e.Message); }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ThreadRenderWorker] thread failed: " + e.Message);
            }
            finally
            {
                try { onThreadExit?.Invoke(); }
                catch (Exception e) { Debug.LogWarning("[ThreadRenderWorker] thread exit failed: " + e.Message); }
            }
        }
    }
}
