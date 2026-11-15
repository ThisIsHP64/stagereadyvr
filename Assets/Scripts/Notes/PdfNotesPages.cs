using System;
using System.Collections.Generic;
using StageReadyVR.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StageReadyVR.Notes
{
    /// <summary>
    /// Pdf cards or script pages, rendered in the background by the same deck the
    /// projector uses. The previous and next pages are kept ready so turning either
    /// way shows the page at once.
    /// </summary>
    internal sealed class PdfNotesPages : INotesPages
    {
        // notes are read at arm's length, so a smaller render than the projector is plenty
        public const int RenderEdge = 1400;

        readonly SlideDeck deck;
        readonly RawImage image;
        readonly TMP_Text message;
        readonly Action<float> fitAspect;
        readonly bool previewOnly;
        readonly Dictionary<int, Texture2D> textures = new Dictionary<int, Texture2D>();

        PdfNotesPages(SlideDeck deck, RawImage image, TMP_Text message, Action<float> fitAspect, bool previewOnly)
        {
            this.deck = deck;
            this.image = image;
            this.message = message;
            this.fitAspect = fitAspect;
            this.previewOnly = previewOnly;

            // pages arrive with rows top to bottom while textures read bottom up
            image.uvRect = new Rect(0f, 1f, 1f, -1f);
            deck.PageRendered += OnRendered;
            deck.PageFailed += OnFailed;
            deck.Begin();
        }

        /// <summary>Opens the pdf, or returns null with a message that is safe to show.</summary>
        public static PdfNotesPages Open(string path, RawImage image, TMP_Text message, Action<float> fitAspect, out string error)
        {
            IPdfPageSource source = PdfSources.Open(path, out error, out IRenderWorker worker, out bool previewOnly);
            if (source == null) return null;

            if (source.PageCount <= 0)
            {
                worker.Post(source.Dispose);
                worker.Dispose();
                error = "These notes have no pages.";
                return null;
            }

            var deck = new SlideDeck(source, worker, RenderEdge, prefetchBehind: true);
            return new PdfNotesPages(deck, image, message, fitAspect, previewOnly);
        }

        public int Count => deck.PageCount;

        /// <summary>True in the editor, where stand-in pages replace the real pdf.</summary>
        public bool PreviewOnly => previewOnly;

        public void Show(int index)
        {
            deck.GoTo(index);
            DropUnwanted();
            Present();
        }

        public void Pump()
        {
            deck.Pump();
        }

        public void Dispose()
        {
            deck.PageRendered -= OnRendered;
            deck.PageFailed -= OnFailed;
            deck.Dispose();
            image.texture = null;
            foreach (Texture2D t in textures.Values) Release(t);
            textures.Clear();
        }

        void OnRendered(int index, PageImage page)
        {
            Texture2D texture;
            try
            {
                texture = new Texture2D(page.Width, page.Height, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 4,
                    name = "Notes page " + (index + 1)
                };
                texture.LoadRawTextureData(page.Rgba);

                // the cpu copy is not needed once uploaded
                texture.Apply(false, true);
            }
            catch (UnityException e)
            {
                Debug.LogWarning("[HeldNotes] texture upload failed: " + e.Message);
                OnFailed(index, "This page could not be shown.");
                return;
            }

            if (textures.TryGetValue(index, out Texture2D old)) Release(old);
            textures[index] = texture;
            if (index == deck.Current) Present();
        }

        void OnFailed(int index, string error)
        {
            if (index != deck.Current) return;
            image.enabled = false;
            image.texture = null;
            message.text = error ?? "This page could not be shown.";
        }

        void Present()
        {
            if (textures.TryGetValue(deck.Current, out Texture2D texture))
            {
                image.texture = texture;
                image.enabled = true;
                message.text = string.Empty;
                fitAspect((float)texture.width / texture.height);
            }
            else if (image.texture == null)
            {
                message.text = "Loading page " + (deck.Current + 1);
            }
        }

        void DropUnwanted()
        {
            var drop = new List<int>();
            foreach (int index in textures.Keys)
            {
                // the page on the paper keeps its texture until the new one is ready
                if (!deck.IsWanted(index) && image.texture != textures[index]) drop.Add(index);
            }
            foreach (int index in drop)
            {
                Release(textures[index]);
                textures.Remove(index);
            }
        }

        // deferred destroy is not allowed outside play mode, which edit mode tests rely on
        static void Release(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
