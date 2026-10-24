using System;

namespace StageReadyVR.Import
{
    public enum ImportKind
    {
        Script,         // .txt speech text
        Presentation    // .pdf exported slides
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
    }
}
