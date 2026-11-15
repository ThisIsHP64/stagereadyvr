using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace StageReadyVR.Import
{
    /// <summary>
    /// The menu panel for importing files. Open and Close are the two methods
    /// menu buttons call. The upload server runs only while this panel is
    /// visible, because it starts in OnEnable and the controller stops it in
    /// its own OnDisable.
    /// </summary>
    public class ImportScreen : MonoBehaviour
    {
        const int MaxListed = 5;

        [SerializeField] ImportController controller;
        [Tooltip("The panel to hide while this one is open and to restore on close")]
        [SerializeField] GameObject menuPanel;

        [Header("Text")]
        [SerializeField] TMP_Text instructionText;
        [SerializeField] TMP_Text urlText;
        [SerializeField] TMP_Text pinText;
        [SerializeField] TMP_Text statusText;
        [SerializeField] TMP_Text filesText;
        [Tooltip("Shows which imported pdf is projected during sessions")]
        [SerializeField] TMP_Text presentationText;
        [Tooltip("Shows which script or index cards are held during sessions")]
        [SerializeField] TMP_Text notesText;

        /// <summary>Steps the projected deck through none and each imported pdf, for a menu button.</summary>
        public void CyclePresentation()
        {
            if (controller == null) return;

            ImportStore store = controller.Store;
            ImportedFile current = store.GetActivePresentation();
            string next = NextPresentationId(store.List(), current != null ? current.id : null);

            if (!store.SetActivePresentation(next))
                SetText(statusText, "The presentation choice could not be saved. Try again.");
            Refresh();
        }

        /// <summary>
        /// The id after currentId in the order none, then each presentation as listed,
        /// then none again. Scripts are skipped. Null means none.
        /// </summary>
        public static string NextPresentationId(IReadOnlyList<ImportedFile> files, string currentId)
        {
            var decks = new List<string>();
            if (files != null)
            {
                foreach (ImportedFile f in files)
                {
                    if (f != null && f.kind == ImportKind.Presentation) decks.Add(f.id);
                }
            }
            if (decks.Count == 0) return null;

            // an unknown current id behaves like none so the cycle restarts at the first deck
            int at = string.IsNullOrEmpty(currentId) ? -1 : decks.IndexOf(currentId);
            return at + 1 < decks.Count ? decks[at + 1] : null;
        }

        /// <summary>Steps the held notes through none and each imported script or card set, for a menu button.</summary>
        public void CycleNotes()
        {
            if (controller == null) return;

            ImportStore store = controller.Store;
            ImportedFile current = store.GetActiveNotes();
            string next = NextNotesId(store.List(), current != null ? current.id : null);

            if (!store.SetActiveNotes(next))
                SetText(statusText, "The notes choice could not be saved. Try again.");
            Refresh();
        }

        /// <summary>
        /// The id after currentId in the order none, then each script or card set as listed,
        /// then none again. Presentations are skipped. Null means none.
        /// </summary>
        public static string NextNotesId(IReadOnlyList<ImportedFile> files, string currentId)
        {
            var notes = new List<string>();
            if (files != null)
            {
                foreach (ImportedFile f in files)
                {
                    if (f != null && f.IsNotes) notes.Add(f.id);
                }
            }
            if (notes.Count == 0) return null;

            int at = string.IsNullOrEmpty(currentId) ? -1 : notes.IndexOf(currentId);
            return at + 1 < notes.Count ? notes[at + 1] : null;
        }

        /// <summary>Switches text scripts between held pages and the teleprompter, for a menu button.</summary>
        public void CycleScriptDisplay()
        {
            if (controller == null) return;

            ImportStore store = controller.Store;
            ScriptDisplay next = store.GetScriptDisplay() == ScriptDisplay.Held ? ScriptDisplay.Teleprompter : ScriptDisplay.Held;
            if (!store.SetScriptDisplay(next))
                SetText(statusText, "The script style could not be saved. Try again.");
            Refresh();
        }

        public static string FormatNotes(ImportedFile active, ScriptDisplay display = ScriptDisplay.Held)
        {
            if (active == null) return "Notes: none";

            string detail = KindLabel(active.kind);
            if (active.kind == ImportKind.Script)
            {
                // a pdf page is an image, so it cannot reflow into a scrolling teleprompter
                if (display == ScriptDisplay.Teleprompter && active.IsPdf) detail += ", held: pdf scripts cannot scroll";
                else detail += display == ScriptDisplay.Teleprompter ? ", teleprompter" : ", held";
            }
            return "Notes: " + active.displayName + " (" + detail + ")";
        }

        /// <summary>Short user facing name for a kind, used in lists and labels.</summary>
        public static string KindLabel(ImportKind kind)
        {
            switch (kind)
            {
                case ImportKind.Presentation: return "slides";
                case ImportKind.IndexCards: return "cards";
                default: return "script";
            }
        }

        public static string FormatPresentation(ImportedFile active)
        {
            return "Presentation: " + (active != null ? active.displayName : "none");
        }

        public void Open()
        {
            if (menuPanel != null) menuPanel.SetActive(false);
            gameObject.SetActive(true);
        }

        public void Close()
        {
            if (controller != null) controller.EndImport();
            gameObject.SetActive(false);
            if (menuPanel != null) menuPanel.SetActive(true);
        }

        void OnEnable()
        {
            SetText(statusText, string.Empty);
            if (controller == null)
            {
                SetText(statusText, "Import is not available right now.");
                return;
            }

            // subscribe first so a startup failure such as no Wi-Fi is not missed
            controller.Imported += OnImported;
            controller.Failed += OnFailed;
            controller.BeginImport();
            Refresh();
        }

        void OnDisable()
        {
            if (controller == null) return;
            controller.Imported -= OnImported;
            controller.Failed -= OnFailed;
        }

        void OnImported(ImportedFile file)
        {
            SetText(statusText, "Imported " + file.displayName + ".");
            Refresh();
        }

        void OnFailed(string message)
        {
            SetText(statusText, message);
            Refresh();
        }

        void Refresh()
        {
            bool running = controller != null && controller.IsImporting;

            if (instructionText != null) instructionText.gameObject.SetActive(running);
            SetText(urlText, running ? controller.Url : string.Empty);
            SetText(pinText, running ? "PIN " + controller.Pin : string.Empty);
            SetText(filesText, controller != null ? FormatList(controller.Store.List(), MaxListed) : string.Empty);
            SetText(presentationText, controller != null ? FormatPresentation(controller.Store.GetActivePresentation()) : string.Empty);
            SetText(notesText, controller != null
                ? FormatNotes(controller.Store.GetActiveNotes(), controller.Store.GetScriptDisplay())
                : string.Empty);
        }

        /// <summary>Lists imports as one line each, newest first, with a note when more exist.</summary>
        public static string FormatList(IReadOnlyList<ImportedFile> files, int max)
        {
            if (files == null || files.Count == 0) return "No files imported yet.";

            var sb = new StringBuilder();
            int shown = Mathf.Min(files.Count, max);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append('\n');
                ImportedFile f = files[i];
                sb.Append(f.displayName).Append(" (")
                  .Append(KindLabel(f.kind)).Append(", ")
                  .Append(f.IsPdf ? "pdf" : "txt").Append(", ")
                  .Append(FormatSize(f.sizeBytes)).Append(')');
            }
            if (files.Count > shown) sb.Append("\n+").Append(files.Count - shown).Append(" more");
            return sb.ToString();
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return ((bytes + 1023) / 1024) + " KB";
            return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
        }

        static void SetText(TMP_Text target, string value)
        {
            if (target != null) target.text = value;
        }
    }
}
