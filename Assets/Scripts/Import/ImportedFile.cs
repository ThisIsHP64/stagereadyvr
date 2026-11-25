using System;

namespace StageReadyVR.Import
{
    public enum ImportKind
    {
        // values are stored as integers in the index, so new kinds are only ever appended
        Script,         // full speech text, shown as pages
        Presentation,   // pdf exported slides, projected behind the speaker
        IndexCards      // short cue cards, shown one card at a time
    }

    /// <summary>How a text script is shown during sessions. Stored as an integer, so values are only appended.</summary>
    public enum ScriptDisplay
    {
        Held,           // pages held at chest height
        Teleprompter    // scrolling text at eye level toward the audience
    }

    /// <summary>Named teleprompter speeds exposed by the setup menu.</summary>
    public enum TeleprompterPace
    {
        Slow,
        Medium,
        Fast
    }

    /// <summary>
    /// One imported file as recorded in the store index. Field names are public
    /// and lowercase because JsonUtility serializes them directly.
    /// </summary>
    [Serializable]
    public class ImportedFile
    {
        public string id;
        public string displayName;
        public ImportKind kind;
        public string storedFileName;
        public long sizeBytes;
        public string importedAtUtc;    // iso 8601, JsonUtility cannot store DateTime

        /// <summary>True when the stored file is a pdf rather than utf-8 text.</summary>
        public bool IsPdf => storedFileName != null && storedFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

        /// <summary>Scripts and index cards are the notes a speaker can hold during a session.</summary>
        public bool IsNotes => kind == ImportKind.Script || kind == ImportKind.IndexCards;
    }
}
