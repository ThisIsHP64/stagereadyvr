using System;

namespace StageReadyVR.Notes
{
    /// <summary>
    /// The pages of one set of held notes. HeldNotes owns the page number and asks
    /// a source to draw it; sources differ only in how a page reaches the paper.
    /// </summary>
    internal interface INotesPages : IDisposable
    {
        int Count { get; }

        /// <summary>Draws a page on the paper. Index is always within Count.</summary>
        void Show(int index);

        /// <summary>Called every frame for sources that finish work in the background.</summary>
        void Pump();
    }
}
