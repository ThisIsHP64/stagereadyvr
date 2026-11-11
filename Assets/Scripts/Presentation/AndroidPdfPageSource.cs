#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace StageReadyVR.Presentation
{
    /// <summary>
    /// Renders pdf pages with the platform android.graphics.pdf.PdfRenderer,
    /// which ships with Android and needs no plugin. Open on the main thread,
    /// then call everything else from one worker thread attached to the jvm.
    /// </summary>
    public sealed class AndroidPdfPageSource : IPdfPageSource
    {
        const int ModeReadOnly = 0x10000000;    // ParcelFileDescriptor.MODE_READ_ONLY
        const int RenderModeForDisplay = 1;     // PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY
        const int OpaqueWhite = -1;             // 0xffffffff as a java int

        const string OpenFailedMessage =
            "This presentation could not be opened. Export it again as a PDF without a password and re-import it.";
        const string RenderFailedMessage = "This slide could not be shown.";

        AndroidJavaObject descriptor;
        AndroidJavaObject renderer;
        bool disposed;

        AndroidPdfPageSource(AndroidJavaObject descriptor, AndroidJavaObject renderer, int pageCount)
        {
            this.descriptor = descriptor;
            this.renderer = renderer;
            PageCount = pageCount;
        }

        public int PageCount { get; }

        /// <summary>Returns null and a user safe error when the file is unreadable, damaged or encrypted.</summary>
        public static AndroidPdfPageSource Open(string path, out string error)
        {
            error = null;
            AndroidJavaObject descriptor = null;
            AndroidJavaObject renderer = null;
            try
            {
                using (var file = new AndroidJavaObject("java.io.File", path))
                using (var pfd = new AndroidJavaClass("android.os.ParcelFileDescriptor"))
                {
                    descriptor = pfd.CallStatic<AndroidJavaObject>("open", file, ModeReadOnly);
                }
                renderer = new AndroidJavaObject("android.graphics.pdf.PdfRenderer", descriptor);
                int count = renderer.Call<int>("getPageCount");
                return new AndroidPdfPageSource(descriptor, renderer, count);
            }
            catch (Exception e)
            {
                // the java exception text can include paths, so it goes to the log only
                Debug.LogWarning("[AndroidPdfPageSource] open failed: " + e.Message);
                Close(renderer);
                Close(descriptor);
                error = OpenFailedMessage;
                return null;
            }
        }

        public static void AttachThread()
        {
            AndroidJNI.AttachCurrentThread();
        }

        public static void DetachThread()
        {
            AndroidJNI.DetachCurrentThread();
        }

        public bool TryRenderPage(int index, int maxEdge, out PageImage image, out string error)
        {
            image = default;
            error = null;
            if (disposed || index < 0 || index >= PageCount || maxEdge <= 0)
            {
                error = RenderFailedMessage;
                return false;
            }

            AndroidJavaObject page = null;
            AndroidJavaObject bitmap = null;
            AndroidJavaObject buffer = null;
            try
            {
                // only one page may be open at a time, which the single worker thread guarantees
                page = renderer.Call<AndroidJavaObject>("openPage", index);
                int pageWidth = page.Call<int>("getWidth");
                int pageHeight = page.Call<int>("getHeight");
                if (pageWidth <= 0 || pageHeight <= 0) throw new InvalidOperationException("page has no size");

                // pages are measured in points, so scale to the pixel budget keeping the aspect
                float scale = (float)maxEdge / Math.Max(pageWidth, pageHeight);
                int width = Math.Max(1, (int)Math.Round(pageWidth * scale));
                int height = Math.Max(1, (int)Math.Round(pageHeight * scale));

                using (var configClass = new AndroidJavaClass("android.graphics.Bitmap$Config"))
                using (var argb = configClass.GetStatic<AndroidJavaObject>("ARGB_8888"))
                using (var bitmapClass = new AndroidJavaClass("android.graphics.Bitmap"))
                {
                    bitmap = bitmapClass.CallStatic<AndroidJavaObject>("createBitmap", width, height, argb);
                }

                // pdf pages may be transparent, and a projected slide should sit on white paper
                bitmap.Call("eraseColor", OpaqueWhite);

                CallVoid(page, "render", "(Landroid/graphics/Bitmap;Landroid/graphics/Rect;Landroid/graphics/Matrix;I)V",
                    new jvalue { l = bitmap.GetRawObject() },
                    new jvalue { l = IntPtr.Zero },
                    new jvalue { l = IntPtr.Zero },
                    new jvalue { i = RenderModeForDisplay });

                using (var byteBufferClass = new AndroidJavaClass("java.nio.ByteBuffer"))
                {
                    buffer = byteBufferClass.CallStatic<AndroidJavaObject>("allocate", width * height * 4);
                }

                // argb_8888 is laid out in memory as r, g, b, a bytes, which matches rgba32
                CallVoid(bitmap, "copyPixelsToBuffer", "(Ljava/nio/Buffer;)V",
                    new jvalue { l = buffer.GetRawObject() });

                // free the native bitmap before copying into managed memory to lower the peak
                bitmap.Call("recycle");

                sbyte[] signed = buffer.Call<sbyte[]>("array");
                var rgba = new byte[signed.Length];
                Buffer.BlockCopy(signed, 0, rgba, 0, signed.Length);

                image = new PageImage(width, height, rgba);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AndroidPdfPageSource] page " + index + " failed: " + e.Message);
                error = RenderFailedMessage;
                return false;
            }
            finally
            {
                Dispose(buffer);
                Dispose(bitmap);
                Close(page);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Close(renderer);
            Close(descriptor);
            renderer = null;
            descriptor = null;
        }

        // calls with an exact signature, because inference from runtime argument types
        // cannot express null arguments or a parameter declared as a supertype
        static void CallVoid(AndroidJavaObject target, string name, string signature, params jvalue[] args)
        {
            IntPtr method = AndroidJNIHelper.GetMethodID(target.GetRawClass(), name, signature, false);
            ThrowIfJavaException(name);
            if (method == IntPtr.Zero) throw new InvalidOperationException(name + " was not found");

            AndroidJNI.CallVoidMethod(target.GetRawObject(), method, args);
            ThrowIfJavaException(name);
        }

        // raw jni calls leave java exceptions pending instead of throwing, which would poison later calls
        static void ThrowIfJavaException(string what)
        {
            IntPtr thrown = AndroidJNI.ExceptionOccurred();
            if (thrown == IntPtr.Zero) return;
            AndroidJNI.ExceptionClear();
            AndroidJNI.DeleteLocalRef(thrown);
            throw new InvalidOperationException(what + " threw in java");
        }

        static void Close(AndroidJavaObject closeable)
        {
            if (closeable == null) return;
            try { closeable.Call("close"); }
            catch (Exception e) { Debug.LogWarning("[AndroidPdfPageSource] close failed: " + e.Message); }
            closeable.Dispose();
        }

        static void Dispose(AndroidJavaObject obj)
        {
            obj?.Dispose();
        }
    }
}
#endif
