using System;

namespace StageReadyVR.Presentation
{
    /// <summary>
    /// One rendered page as tightly packed RGBA32 pixels. Rows run top to bottom,
    /// the way Android bitmaps store them, so the display flips it vertically.
    /// </summary>
    public readonly struct PageImage
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Rgba;

        public PageImage(int width, int height, byte[] rgba)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Page size must be positive.");
            if (rgba == null || rgba.Length != width * height * 4)
                throw new ArgumentException("Pixel data does not match the page size.", nameof(rgba));
            Width = width;
            Height = height;
            Rgba = rgba;
        }

        public float Aspect => (float)Width / Height;
    }

    /// <summary>
    /// Turns pages of one opened pdf into pixels. Implementations are not
    /// thread safe; SlideDeck calls them from a single worker thread only.
    /// </summary>
    public interface IPdfPageSource : IDisposable
    {
        int PageCount { get; }

        /// <summary>Renders a page with its longest edge at most maxEdge pixels. On failure, error is safe to show the user.</summary>
        bool TryRenderPage(int index, int maxEdge, out PageImage image, out string error);
    }

    /// <summary>Runs render work off the main thread, one job at a time in the order posted.</summary>
    public interface IRenderWorker : IDisposable
    {
        void Post(Action work);
    }
}
