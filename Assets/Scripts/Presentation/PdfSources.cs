namespace StageReadyVR.Presentation
{
    /// <summary>
    /// Picks the pdf page source for the current platform. The projector and the
    /// held notes both go through here so they always render the same way.
    /// </summary>
    public static class PdfSources
    {
        /// <summary>
        /// Opens a pdf with its own render worker. On failure returns null, worker is null
        /// and error is safe to show. previewOnly is true where real pages cannot be drawn.
        /// </summary>
        public static IPdfPageSource Open(string path, out string error, out IRenderWorker worker, out bool previewOnly)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            previewOnly = false;
            AndroidPdfPageSource android = AndroidPdfPageSource.Open(path, out error);
            worker = android != null
                ? new ThreadRenderWorker(AndroidPdfPageSource.AttachThread, AndroidPdfPageSource.DetachThread)
                : null;
            return android;
#else
            // the editor has no pdf renderer, so stand-in pages let the flow be tested on a desktop
            previewOnly = true;
            error = null;
            worker = new ThreadRenderWorker();
            return PlaceholderPageSource.ForFile(path);
#endif
        }
    }
}
