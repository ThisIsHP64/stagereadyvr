using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace StageReadyVR.Presentation
{
    /// <summary>
    /// Stand-in pages for platforms without a pdf renderer, mainly the Unity
    /// Editor. Each page gets its own tint and a progress bar, so placement and
    /// advancing can be checked without a headset.
    /// </summary>
    public sealed class PlaceholderPageSource : IPdfPageSource
    {
        public const int FallbackPageCount = 5;
        const int Width = 1280;
        const int Height = 720;

        // matches page objects but not the /Pages tree node
        static readonly Regex PageObject = new Regex(@"/Type\s*/Page(?![A-Za-z])", RegexOptions.Compiled);

        public PlaceholderPageSource(int pageCount)
        {
            PageCount = Math.Max(1, pageCount);
        }

        public int PageCount { get; }

        /// <summary>
        /// Uses the page count visible in the file, or a fixed count when pages
        /// sit inside compressed object streams where plain text search cannot see them.
        /// </summary>
        public static PlaceholderPageSource ForFile(string path)
        {
            return new PlaceholderPageSource(CountPages(path));
        }

        public static int CountPages(string path)
        {
            try
            {
                // one char per byte, so binary streams cannot break the search and no code page is needed
                byte[] bytes = File.ReadAllBytes(path);
                var chars = new char[bytes.Length];
                for (int i = 0; i < bytes.Length; i++) chars[i] = (char)bytes[i];

                int count = PageObject.Matches(new string(chars)).Count;
                return count > 0 ? count : FallbackPageCount;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return FallbackPageCount;
            }
        }

        public bool TryRenderPage(int index, int maxEdge, out PageImage image, out string error)
        {
            image = default;
            error = null;
            if (index < 0 || index >= PageCount)
            {
                error = "That slide does not exist.";
                return false;
            }

            float scale = Mathf.Min(1f, (float)maxEdge / Mathf.Max(Width, Height));
            int w = Mathf.Max(16, Mathf.RoundToInt(Width * scale));
            int h = Mathf.Max(9, Mathf.RoundToInt(Height * scale));

            Color32 tint = Color.HSVToRGB((index * 0.17f) % 1f, 0.45f, 0.85f);
            Color32 paper = new Color32(245, 246, 250, 255);
            Color32 track = new Color32(60, 64, 76, 255);

            int margin = h / 12;
            int barTop = h - margin * 2;          // rows run top to bottom
            int barBottom = barTop + margin / 2;
            int filled = margin + (int)((w - margin * 2) * ((index + 1f) / PageCount));

            var rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool inPaper = x >= margin && x < w - margin && y >= margin && y < h - margin;
                    bool inBar = y >= barTop && y < barBottom && x >= margin * 2 && x < w - margin * 2;

                    Color32 c = tint;
                    if (inPaper) c = paper;
                    if (inBar) c = x < filled ? tint : track;

                    int o = (y * w + x) * 4;
                    rgba[o] = c.r;
                    rgba[o + 1] = c.g;
                    rgba[o + 2] = c.b;
                    rgba[o + 3] = 255;
                }
            }

            image = new PageImage(w, h, rgba);
            return true;
        }

        public void Dispose() { }
    }
}
