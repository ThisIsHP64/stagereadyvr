using System.Collections.Generic;
using TMPro;

namespace StageReadyVR.Notes
{
    /// <summary>One index card per page, shrunk to fit when a card holds a lot of text.</summary>
    internal sealed class TextCardPages : INotesPages
    {
        readonly List<string> cards;
        readonly TMP_Text body;

        public TextCardPages(string text, TMP_Text body)
        {
            cards = NotesText.SplitCards(text);
            this.body = body;
            body.overflowMode = TextOverflowModes.Ellipsis;
            body.enableAutoSizing = true;
        }

        public int Count => cards.Count;

        public void Show(int index)
        {
            body.text = cards[index];
        }

        public void Pump() { }
        public void Dispose() { }
    }

    /// <summary>
    /// A full script laid out as pages. TextMeshPro paginates with the real font
    /// metrics, so a page break never cuts a line and font changes need no extra code.
    /// </summary>
    internal sealed class TextScriptPages : INotesPages
    {
        readonly TMP_Text body;

        public TextScriptPages(string text, TMP_Text body)
        {
            this.body = body;
            body.enableAutoSizing = false;
            body.overflowMode = TextOverflowModes.Page;
            body.text = NotesText.Normalize(text).Trim();
            body.pageToDisplay = 1;
            body.ForceMeshUpdate(true, true);

            // a font that failed to load reports no pages, which still has to show something
            Count = body.textInfo != null && body.textInfo.pageCount > 0 ? body.textInfo.pageCount : 1;
        }

        public int Count { get; }

        public void Show(int index)
        {
            // tmp pages are numbered from one
            body.pageToDisplay = index + 1;
        }

        public void Pump() { }
        public void Dispose() { }
    }
}
