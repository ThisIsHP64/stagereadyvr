using StageReadyVR.Import;
using StageReadyVR.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StageReadyVR.Notes
{
    /// <summary>
    /// The index cards or script chosen in the Import screen, held at chest height
    /// in front of the speaker during a session. It stays hidden until Begin, so it
    /// never flashes at the world origin, and does nothing when no notes are chosen.
    /// </summary>
    public class HeldNotes : MonoBehaviour
    {
        // the canvas is built in millimetres so ui sizes read as physical sizes
        const float UnitsPerMetre = 1000f;
        const float FooterHeight = 14f;
        const float Margin = 10f;

        [Header("Placement")]
        [SerializeField] HeldPoseSettings placement = HeldPoseSettings.Default;

        [Header("Size in metres")]
        [Tooltip("Index cards, a little larger than a real 5 by 8 card so text stays readable in the headset")]
        [SerializeField] Vector2 cardSize = new Vector2(0.20f, 0.13f);
        [Tooltip("Script pages, roughly a letter page scaled to arm's length")]
        [SerializeField] Vector2 pageSize = new Vector2(0.18f, 0.24f);

        [Header("Text in millimetres")]
        [SerializeField] float cardFontSize = 9f;
        [SerializeField] float cardMinFontSize = 5f;
        [SerializeField] float scriptFontSize = 6.5f;

        [Header("Turning")]
        [SerializeField] float flipSeconds = 0.18f;

        [Header("Appearance")]
        [SerializeField] Color paperColor = new Color(0.98f, 0.97f, 0.93f, 1f);
        [SerializeField] Color inkColor = new Color(0.1f, 0.11f, 0.14f, 1f);
        [SerializeField] Color footerColor = new Color(0.42f, 0.44f, 0.5f, 1f);

        ImportStore storeOverride;
        INotesPages pages;
        ImportKind kind;
        bool previewOnly;
        bool loaded;

        Canvas canvas;
        RectTransform flipper;
        RectTransform paper;
        TMP_Text body;
        RawImage pageImage;
        TMP_Text footer;
        TMP_Text message;

        Transform head;
        HeldPoseState pose;

        // flip progress runs from 0 to 1; the new page is drawn at the halfway point
        float flipProgress = 1f;
        int flipDirection;
        int shownPage = -1;

        /// <summary>True once notes are loaded; they show when Begin is called.</summary>
        public bool HasNotes { get; private set; }

        public bool IsShown => canvas != null && canvas.gameObject.activeSelf;
        public int CurrentPage { get; private set; }
        public int PageCount => pages != null ? pages.Count : 0;
        public ImportKind Kind => kind;

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
            pages?.Pump();
            AnimateFlip(Time.deltaTime);
        }

        // placement runs after head tracking has updated for the frame
        void LateUpdate()
        {
            if (!IsShown || head == null) return;
            pose = HeldPose.Step(pose, head.position, head.forward, head.up, placement, Time.deltaTime);
            ApplyPose();
        }

        void OnDestroy()
        {
            pages?.Dispose();
            pages = null;
        }

        /// <summary>Shows the notes in front of the given head, starting from where it faces now.</summary>
        public void Begin(Transform headTransform)
        {
            Load();
            if (!HasNotes || canvas == null || headTransform == null) return;

            head = headTransform;
            pose = HeldPose.Snap(head.position, head.forward, head.up);
            ApplyPose();
            canvas.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        /// <summary>Turns to the next page. Returns false on the last page or when nothing is loaded.</summary>
        public bool Next()
        {
            return TurnTo(CurrentPage + 1);
        }

        /// <summary>Turns back one page. Returns false on the first page or when nothing is loaded.</summary>
        public bool Previous()
        {
            return TurnTo(CurrentPage - 1);
        }

        bool TurnTo(int index)
        {
            if (pages == null || index < 0 || index >= pages.Count || index == CurrentPage) return false;

            flipDirection = index > CurrentPage ? 1 : -1;
            CurrentPage = index;
            UpdateFooter();

            // edit mode and hidden notes have no frames to animate, so the page changes at once
            if (!Application.isPlaying || !IsShown || flipSeconds <= 0f)
            {
                flipProgress = 1f;
                ShowPage();
                SetFlipAngle(0f);
            }
            else if (flipProgress >= 1f)
            {
                flipProgress = 0f;
            }
            else if (flipProgress >= 0.5f)
            {
                // the swap point of the running flip has passed, so a fast second flick draws its page now
                ShowPage();
            }
            return true;
        }

        void AnimateFlip(float deltaTime)
        {
            if (flipProgress >= 1f || flipper == null) return;

            bool beforeHalf = flipProgress < 0.5f;
            flipProgress = Mathf.Min(1f, flipProgress + deltaTime / flipSeconds);

            if (beforeHalf && flipProgress >= 0.5f) ShowPage();

            // the page turns edge on, swaps content, then turns back in from the other side
            float angle = flipProgress < 0.5f
                ? Mathf.Lerp(0f, 90f, flipProgress * 2f)
                : Mathf.Lerp(-90f, 0f, (flipProgress - 0.5f) * 2f);
            SetFlipAngle(angle * flipDirection);
        }

        void SetFlipAngle(float degrees)
        {
            if (flipper != null) flipper.localRotation = Quaternion.Euler(0f, degrees, 0f);
        }

        void ShowPage()
        {
            if (pages == null || shownPage == CurrentPage) return;
            shownPage = CurrentPage;
            pages.Show(CurrentPage);
        }

        void ApplyPose()
        {
            Pose p = HeldPose.ToPose(pose, placement);
            canvas.transform.SetPositionAndRotation(p.position, p.rotation);
        }

        void Load()
        {
            if (loaded) return;
            loaded = true;

            ImportStore store = storeOverride ?? ImportStore.CreateDefault();
            ImportedFile active = store.GetActiveNotes();
            if (active == null) return;

            // a text script set to the teleprompter is shown there instead
            if (Teleprompter.ShowsOnTeleprompter(active, store.GetScriptDisplay())) return;

            kind = active.kind;
            HasNotes = true;
            Vector2 size = kind == ImportKind.IndexCards ? cardSize : pageSize;
            BuildCanvas(size);

            // text that has never been active skips layout, so pages are laid out first and hidden in the same frame
            try
            {
                pages = OpenPages(store, active, out string error);
                if (pages == null)
                {
                    ShowMessage(error);
                    return;
                }

                CurrentPage = 0;
                ShowPage();
                UpdateFooter();
            }
            finally
            {
                canvas.gameObject.SetActive(false);
            }
        }

        INotesPages OpenPages(ImportStore store, ImportedFile active, out string error)
        {
            if (active.IsPdf)
            {
                string path = store.GetPdfPath(active.id);
                if (string.IsNullOrEmpty(path))
                {
                    error = "Those notes could not be found. Import them again.";
                    return null;
                }

                PdfNotesPages pdf = PdfNotesPages.Open(path, pageImage, message, FitToAspect, out error);
                previewOnly = pdf != null && pdf.PreviewOnly;
                return pdf;
            }

            if (!store.TryReadText(active.id, out string text, out error)) return null;

            body.gameObject.SetActive(true);
            if (kind == ImportKind.IndexCards)
            {
                body.fontSize = cardFontSize;
                body.fontSizeMax = cardFontSize;
                body.fontSizeMin = cardMinFontSize;
                return new TextCardPages(text, body);
            }

            body.fontSize = scriptFontSize;
            return new TextScriptPages(text, body);
        }

        void ShowMessage(string text)
        {
            if (body != null) body.gameObject.SetActive(false);
            if (pageImage != null) pageImage.enabled = false;
            if (message != null) message.text = text ?? string.Empty;
            if (footer != null) footer.text = string.Empty;
        }

        void UpdateFooter()
        {
            if (footer == null || pages == null) return;
            footer.text = (kind == ImportKind.IndexCards ? "Card " : "Page ") + (CurrentPage + 1) + " / " + pages.Count
                + (previewOnly ? "   preview" : string.Empty);
        }

        // a pdf page keeps its own shape inside the size limit for its kind
        void FitToAspect(float aspect)
        {
            Vector2 bounds = kind == ImportKind.IndexCards ? cardSize : pageSize;
            Vector2 fitted = ScreenPlacement.FitSize(aspect, bounds.x, bounds.y) * UnitsPerMetre;
            paper.sizeDelta = fitted + new Vector2(0f, FooterHeight);
        }

        void BuildCanvas(Vector2 sizeMetres)
        {
            var root = new GameObject("HeldNotesCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);

            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            root.transform.localScale = Vector3.one / UnitsPerMetre;
            Vector2 size = sizeMetres * UnitsPerMetre;
            ((RectTransform)root.transform).sizeDelta = size + new Vector2(0f, FooterHeight);

            // the flipper turns around the page's vertical centre line, like a card being flipped over
            var flipObject = new GameObject("Flipper", typeof(RectTransform));
            flipObject.transform.SetParent(root.transform, false);
            flipper = (RectTransform)flipObject.transform;

            var paperObject = new GameObject("Paper", typeof(RectTransform), typeof(Image));
            paperObject.transform.SetParent(flipper, false);
            paper = (RectTransform)paperObject.transform;
            paper.sizeDelta = size + new Vector2(0f, FooterHeight);
            var paperImage = paperObject.GetComponent<Image>();
            paperImage.color = paperColor;
            paperImage.raycastTarget = false;

            body = NewText(paper, "Body", inkColor, TextAlignmentOptions.TopLeft);

            // imported text is shown exactly as written, so angle brackets cannot restyle or hide it
            body.richText = false;
            Inset(body.rectTransform, Margin, Margin + FooterHeight, Margin, Margin);
            body.gameObject.SetActive(false);

            var imageObject = new GameObject("Page", typeof(RectTransform), typeof(RawImage));
            imageObject.transform.SetParent(paper, false);
            pageImage = imageObject.GetComponent<RawImage>();
            pageImage.raycastTarget = false;
            pageImage.enabled = false;
            Inset(pageImage.rectTransform, 0f, FooterHeight, 0f, 0f);

            message = NewText(paper, "Message", inkColor, TextAlignmentOptions.Center);
            message.fontSize = 7f;
            Inset(message.rectTransform, Margin, Margin + FooterHeight, Margin, Margin);

            footer = NewText(paper, "Footer", footerColor, TextAlignmentOptions.Right);
            footer.fontSize = 5f;
            RectTransform f = footer.rectTransform;
            f.anchorMin = new Vector2(0f, 0f);
            f.anchorMax = new Vector2(1f, 0f);
            f.pivot = new Vector2(0.5f, 0f);
            f.offsetMin = new Vector2(Margin, 2f);
            f.offsetMax = new Vector2(-Margin, FooterHeight);
        }

        TMP_Text NewText(Transform parent, string name, Color color, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        static void Inset(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
