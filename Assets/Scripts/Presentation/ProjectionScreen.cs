using System.Collections.Generic;
using StageReadyVR.Import;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StageReadyVR.Presentation
{
    /// <summary>
    /// A projector style screen that shows the presentation chosen in the
    /// Import screen. It stays hidden until Place is called, so it never flashes
    /// at the world origin, and does nothing at all when no deck is chosen.
    /// </summary>
    public class ProjectionScreen : MonoBehaviour
    {
        // the canvas is built in millimetres so ui sizes read as physical sizes
        const float UnitsPerMetre = 1000f;

        [Header("Placement")]
        [Tooltip("Optional fixed spot such as the room's projector wall; overrides placing behind the player")]
        [SerializeField] Transform anchor;
        [Tooltip("Metres behind the player's head")]
        [SerializeField] float distanceBehind = 2.5f;
        [Tooltip("Metres the screen centre sits above the player's head, so it shows over them")]
        [SerializeField] float heightAboveHead = 0.35f;

        [Header("Size")]
        [SerializeField] float maxSlideWidth = 3.2f;
        [SerializeField] float maxSlideHeight = 1.8f;
        [Tooltip("Metres of dark border around the slide")]
        [SerializeField] float bezel = 0.06f;
        [Tooltip("Longest edge in pixels a page is rendered at")]
        [SerializeField] int renderEdge = SlideDeck.DefaultMaxEdge;

        [Header("Appearance")]
        [SerializeField] Color bezelColor = new Color(0.03f, 0.03f, 0.04f, 1f);
        [SerializeField] Color textColor = new Color(0.92f, 0.94f, 0.98f, 1f);

        readonly Dictionary<int, Texture2D> textures = new Dictionary<int, Texture2D>();
        ImportStore storeOverride;
        SlideDeck deck;
        Canvas canvas;
        RectTransform frame;
        RawImage slide;
        TMP_Text caption;
        TMP_Text message;
        bool previewOnly;

        /// <summary>True once a deck is loaded and the screen will show it when placed.</summary>
        public bool HasPresentation { get; private set; }

        public int CurrentSlide => deck != null ? deck.Current : 0;
        public int SlideCount => deck != null ? deck.PageCount : 0;

        /// <summary>Lets tests read from a temporary store instead of the app storage.</summary>
        public void UseStore(ImportStore store)
        {
            storeOverride = store;
        }

        void Start()
        {
            Load();
        }

        void Update()
        {
            deck?.Pump();
        }

        void OnDestroy()
        {
            if (deck != null)
            {
                deck.PageRendered -= OnPageRendered;
                deck.PageFailed -= OnPageFailed;
                deck.Dispose();
                deck = null;
            }
            foreach (Texture2D t in textures.Values) Release(t);
            textures.Clear();
        }

        /// <summary>Shows the screen behind the given head, or at the anchor when one is set.</summary>
        public void Place(Transform head)
        {
            if (!HasPresentation || canvas == null) return;

            if (anchor != null)
            {
                canvas.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            }
            else if (head != null)
            {
                Pose pose = ScreenPlacement.Behind(head.position, head.forward, distanceBehind, heightAboveHead);
                canvas.transform.SetPositionAndRotation(pose.position, pose.rotation);
            }
            else
            {
                return;
            }

            canvas.gameObject.SetActive(true);
        }

        /// <summary>Moves one slide forward. Returns false on the last slide or when nothing is loaded.</summary>
        public bool Advance()
        {
            if (deck == null || !deck.Advance()) return false;
            DropUnwantedTextures();
            ShowCurrent();
            return true;
        }

        void Load()
        {
            if (deck != null || HasPresentation) return;

            ImportStore store = storeOverride ?? ImportStore.CreateDefault();
            ImportedFile active = store.GetActivePresentation();
            if (active == null) return;

            string path = store.GetPresentationPath(active.id);
            if (string.IsNullOrEmpty(path)) return;

            HasPresentation = true;
            BuildCanvas();

            IPdfPageSource source = OpenSource(path, out string error, out IRenderWorker worker);
            if (source == null)
            {
                ShowMessage(error);
                return;
            }
            if (source.PageCount <= 0)
            {
                worker.Post(source.Dispose);
                worker.Dispose();
                ShowMessage("This presentation has no pages.");
                return;
            }

            deck = new SlideDeck(source, worker, Mathf.Max(256, renderEdge));
            deck.PageRendered += OnPageRendered;
            deck.PageFailed += OnPageFailed;
            deck.Begin();
            ShowCurrent();
        }

        IPdfPageSource OpenSource(string path, out string error, out IRenderWorker worker)
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

        void OnPageRendered(int index, PageImage image)
        {
            Texture2D texture;
            try
            {
                texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 4,
                    name = "Slide " + (index + 1)
                };
                texture.LoadRawTextureData(image.Rgba);

                // the cpu copy is no longer needed once uploaded, which halves memory per slide
                texture.Apply(false, true);
            }
            catch (UnityException e)
            {
                Debug.LogWarning("[ProjectionScreen] texture upload failed: " + e.Message);
                OnPageFailed(index, "This slide could not be shown.");
                return;
            }

            if (textures.TryGetValue(index, out Texture2D old)) Release(old);
            textures[index] = texture;
            if (index == deck.Current) ShowCurrent();
        }

        void OnPageFailed(int index, string error)
        {
            if (deck != null && index == deck.Current) ShowMessage(error);
        }

        void ShowCurrent()
        {
            if (deck == null) return;

            caption.text = "Slide " + (deck.Current + 1) + " / " + deck.PageCount
                + (previewOnly ? "    Preview: real pages render on the headset" : string.Empty);

            if (textures.TryGetValue(deck.Current, out Texture2D texture))
            {
                slide.texture = texture;
                slide.enabled = true;
                message.text = string.Empty;
                FitTo((float)texture.width / texture.height);
            }
            else
            {
                // keep the previous slide up so a slow render never blanks the screen
                if (slide.texture == null) message.text = "Loading slide " + (deck.Current + 1);
            }
        }

        void ShowMessage(string text)
        {
            if (slide != null)
            {
                slide.enabled = false;
                slide.texture = null;
            }
            if (message != null) message.text = text ?? string.Empty;
        }

        void DropUnwantedTextures()
        {
            var drop = new List<int>();
            foreach (int index in textures.Keys)
            {
                if (!deck.IsWanted(index)) drop.Add(index);
            }
            foreach (int index in drop)
            {
                // the slide on screen keeps its texture until the next one is ready
                if (slide.texture == textures[index]) continue;
                Release(textures[index]);
                textures.Remove(index);
            }
        }

        void FitTo(float aspect)
        {
            Vector2 size = ScreenPlacement.FitSize(aspect, maxSlideWidth, maxSlideHeight) * UnitsPerMetre;
            slide.rectTransform.sizeDelta = size;
            frame.sizeDelta = size + Vector2.one * (bezel * 2f * UnitsPerMetre);
        }

        void BuildCanvas()
        {
            var root = new GameObject("ProjectionScreenCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            root.SetActive(false);

            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            root.transform.localScale = Vector3.one / UnitsPerMetre;
            ((RectTransform)root.transform).sizeDelta = new Vector2(maxSlideWidth, maxSlideHeight) * UnitsPerMetre;

            var frameObject = new GameObject("Bezel", typeof(RectTransform), typeof(Image));
            frameObject.transform.SetParent(root.transform, false);
            frame = (RectTransform)frameObject.transform;
            var frameImage = frameObject.GetComponent<Image>();
            frameImage.color = bezelColor;
            frameImage.raycastTarget = false;

            var slideObject = new GameObject("Slide", typeof(RectTransform), typeof(RawImage));
            slideObject.transform.SetParent(frame, false);
            slide = slideObject.GetComponent<RawImage>();
            slide.raycastTarget = false;
            slide.enabled = false;

            // pages arrive with rows top to bottom while textures read bottom up
            slide.uvRect = new Rect(0f, 1f, 1f, -1f);

            message = NewText(frame, "Message", 60f, TextAlignmentOptions.Center);
            Stretch(message.rectTransform, 80f);

            // under the bezel so it follows the frame as slides of other shapes resize it
            caption = NewText(frame, "Caption", 36f, TextAlignmentOptions.Right);
            RectTransform captionRect = caption.rectTransform;
            captionRect.anchorMin = new Vector2(0f, 0f);
            captionRect.anchorMax = new Vector2(1f, 0f);
            captionRect.pivot = new Vector2(0.5f, 1f);
            captionRect.anchoredPosition = new Vector2(0f, -10f);
            captionRect.sizeDelta = new Vector2(0f, 60f);

            FitTo(16f / 9f);
        }

        TMP_Text NewText(Transform parent, string name, float size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = textColor;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        // Destroy is not allowed outside play mode, which edit mode tests rely on
        static void Release(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        static void Stretch(RectTransform rect, float margin)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }
    }
}
