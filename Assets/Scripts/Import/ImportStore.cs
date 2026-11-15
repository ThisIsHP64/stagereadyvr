using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace StageReadyVR.Import
{
    public sealed class SaveResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }
        public ImportedFile File { get; private set; }

        public static SaveResult Fail(string message)
        {
            return new SaveResult { Success = false, Message = message };
        }

        public static SaveResult Ok(ImportedFile file)
        {
            return new SaveResult { Success = true, Message = "Imported " + file.displayName + ".", File = file };
        }
    }

    [Serializable]
    internal sealed class ImportIndex
    {
        public List<ImportedFile> files = new List<ImportedFile>();

        // empty means no deck is projected; JsonUtility writes null strings as empty
        public string activePresentationId = string.Empty;

        // empty means nothing is held during sessions; older indexes simply lack the field
        public string activeNotesId = string.Empty;

        // a ScriptDisplay value; zero keeps older indexes on held pages
        public int scriptDisplay;
    }

    /// <summary>
    /// Keeps imported files in one app-private folder with a json index.
    /// Files are stored under a generated name, so nothing the client sends
    /// can influence a path on disk. Safe to call from any thread.
    /// </summary>
    public sealed class ImportStore
    {
        const string IndexFileName = "index.json";

        // the index is plain json on disk, so entries are re-checked on load
        static readonly Regex StoredNamePattern = new Regex("^[0-9a-f]{32}\\.(txt|pdf)$", RegexOptions.Compiled);

        readonly string root;
        readonly object gate = new object();

        public ImportStore(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("A storage directory is required.", nameof(rootDirectory));
            root = Path.GetFullPath(rootDirectory);
        }

        /// <summary>Must be called on the main thread because persistentDataPath is a Unity API.</summary>
        public static ImportStore CreateDefault()
        {
            return new ImportStore(Path.Combine(Application.persistentDataPath, "Imports"));
        }

        public string RootDirectory => root;

        public SaveResult Save(byte[] data, string originalName, ImportKind kind)
        {
            ValidationResult check = ImportValidator.Validate(data, originalName, kind);
            if (!check.IsValid) return SaveResult.Fail(check.Message);

            lock (gate)
            {
                string storedPath = null;
                try
                {
                    Directory.CreateDirectory(root);

                    string id = Guid.NewGuid().ToString("N");
                    string storedName = id + (check.IsPdf ? ".pdf" : ".txt");
                    storedPath = Path.Combine(root, storedName);
                    WriteReplacing(storedPath, check.Data);

                    var entry = new ImportedFile
                    {
                        id = id,
                        displayName = check.DisplayName,
                        kind = check.Kind,
                        storedFileName = storedName,
                        sizeBytes = check.Data.LongLength,
                        importedAtUtc = DateTime.UtcNow.ToString("o")
                    };

                    ImportIndex index = LoadIndex();
                    index.files.Add(entry);
                    WriteReplacing(IndexPath, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(index)));

                    return SaveResult.Ok(entry);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Debug.LogWarning("[ImportStore] save failed: " + e.Message);
                    TryDelete(storedPath);
                    return SaveResult.Fail("The file could not be saved on this device. Check that there is free storage space.");
                }
            }
        }

        /// <summary>All imports whose file still exists, newest first.</summary>
        public IReadOnlyList<ImportedFile> List()
        {
            lock (gate)
            {
                var result = new List<ImportedFile>();
                foreach (ImportedFile entry in LoadIndex().files)
                {
                    if (File.Exists(Path.Combine(root, entry.storedFileName))) result.Add(entry);
                }
                result.Reverse();
                return result;
            }
        }

        public ImportedFile Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (ImportedFile entry in List())
            {
                if (entry.id == id) return entry;
            }
            return null;
        }

        /// <summary>Reads a stored text script or text index cards. On failure, error holds a message that is safe to show the user.</summary>
        public bool TryReadText(string id, out string text, out string error)
        {
            text = null;
            error = null;

            ImportedFile entry = Get(id);
            if (entry == null || !entry.IsNotes || entry.IsPdf)
            {
                error = "Those notes could not be found. Import them again.";
                return false;
            }

            try
            {
                byte[] bytes;
                lock (gate) bytes = File.ReadAllBytes(Path.Combine(root, entry.storedFileName));

                if (ImportValidator.TryDecodeText(bytes, out text) && text.Trim().Length > 0) return true;
                error = "Those notes are empty or damaged. Import them again.";
                return false;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[ImportStore] read failed: " + e.Message);
                error = "Those notes could not be read. Import them again.";
                return false;
            }
        }

        /// <summary>Full path of a stored presentation for a later renderer, or null if it is missing.</summary>
        public string GetPresentationPath(string id)
        {
            ImportedFile entry = Get(id);
            if (entry == null || entry.kind != ImportKind.Presentation) return null;
            return Path.Combine(root, entry.storedFileName);
        }

        /// <summary>Full path of any stored pdf, such as pdf index cards, or null if it is missing or text.</summary>
        public string GetPdfPath(string id)
        {
            ImportedFile entry = Get(id);
            if (entry == null || !entry.IsPdf) return null;
            return Path.Combine(root, entry.storedFileName);
        }

        /// <summary>
        /// Chooses the deck projected during sessions. Null or empty clears it.
        /// Returns false when the id is not an existing presentation or the index cannot be written.
        /// </summary>
        public bool SetActivePresentation(string id)
        {
            lock (gate)
            {
                ImportIndex index = LoadIndex();
                string next = string.Empty;

                if (!string.IsNullOrEmpty(id))
                {
                    ImportedFile entry = index.files.Find(f => f.id == id);
                    if (entry == null || entry.kind != ImportKind.Presentation) return false;
                    if (!File.Exists(Path.Combine(root, entry.storedFileName))) return false;
                    next = id;
                }

                index.activePresentationId = next;
                try
                {
                    Directory.CreateDirectory(root);
                    WriteReplacing(IndexPath, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(index)));
                    return true;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Debug.LogWarning("[ImportStore] selecting presentation failed: " + e.Message);
                    return false;
                }
            }
        }

        /// <summary>The deck chosen for sessions, or null when none is chosen or its file is gone.</summary>
        public ImportedFile GetActivePresentation()
        {
            string id;
            lock (gate) id = LoadIndex().activePresentationId;
            if (string.IsNullOrEmpty(id)) return null;

            // the lookup only returns entries whose file exists, so a stale id reads as none
            ImportedFile entry = Get(id);
            return entry != null && entry.kind == ImportKind.Presentation ? entry : null;
        }

        /// <summary>
        /// Chooses the script or index cards held during sessions. Null or empty clears it.
        /// Returns false when the id is not existing notes or the index cannot be written.
        /// </summary>
        public bool SetActiveNotes(string id)
        {
            lock (gate)
            {
                ImportIndex index = LoadIndex();
                string next = string.Empty;

                if (!string.IsNullOrEmpty(id))
                {
                    ImportedFile entry = index.files.Find(f => f.id == id);
                    if (entry == null || !entry.IsNotes) return false;
                    if (!File.Exists(Path.Combine(root, entry.storedFileName))) return false;
                    next = id;
                }

                index.activeNotesId = next;
                try
                {
                    Directory.CreateDirectory(root);
                    WriteReplacing(IndexPath, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(index)));
                    return true;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Debug.LogWarning("[ImportStore] selecting notes failed: " + e.Message);
                    return false;
                }
            }
        }

        /// <summary>The notes chosen for sessions, or null when none are chosen or the file is gone.</summary>
        public ImportedFile GetActiveNotes()
        {
            string id;
            lock (gate) id = LoadIndex().activeNotesId;
            if (string.IsNullOrEmpty(id)) return null;

            ImportedFile entry = Get(id);
            return entry != null && entry.IsNotes ? entry : null;
        }

        /// <summary>How text scripts are shown. Unknown stored values read as held pages.</summary>
        public ScriptDisplay GetScriptDisplay()
        {
            int raw;
            lock (gate) raw = LoadIndex().scriptDisplay;
            return Enum.IsDefined(typeof(ScriptDisplay), raw) ? (ScriptDisplay)raw : ScriptDisplay.Held;
        }

        /// <summary>Saves how text scripts are shown. Returns false when the index cannot be written.</summary>
        public bool SetScriptDisplay(ScriptDisplay display)
        {
            if (!Enum.IsDefined(typeof(ScriptDisplay), display)) return false;

            lock (gate)
            {
                ImportIndex index = LoadIndex();
                index.scriptDisplay = (int)display;
                try
                {
                    Directory.CreateDirectory(root);
                    WriteReplacing(IndexPath, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(index)));
                    return true;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Debug.LogWarning("[ImportStore] saving script display failed: " + e.Message);
                    return false;
                }
            }
        }

        public bool Delete(string id)
        {
            lock (gate)
            {
                ImportIndex index = LoadIndex();
                int at = index.files.FindIndex(f => f.id == id);
                if (at < 0) return false;

                string path = Path.Combine(root, index.files[at].storedFileName);
                index.files.RemoveAt(at);
                if (index.activePresentationId == id) index.activePresentationId = string.Empty;
                if (index.activeNotesId == id) index.activeNotesId = string.Empty;

                try
                {
                    WriteReplacing(IndexPath, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(index)));
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Debug.LogWarning("[ImportStore] delete failed: " + e.Message);
                    return false;
                }

                TryDelete(path);
                return true;
            }
        }

        string IndexPath => Path.Combine(root, IndexFileName);

        // a missing or damaged index must never block importing, so it reads as empty
        ImportIndex LoadIndex()
        {
            var index = new ImportIndex();
            if (!File.Exists(IndexPath)) return index;

            try
            {
                ImportIndex loaded = JsonUtility.FromJson<ImportIndex>(File.ReadAllText(IndexPath));
                if (loaded?.files == null) return index;

                foreach (ImportedFile entry in loaded.files)
                {
                    bool sane = entry != null
                        && !string.IsNullOrEmpty(entry.id)
                        && !string.IsNullOrEmpty(entry.storedFileName)
                        && StoredNamePattern.IsMatch(entry.storedFileName);
                    if (sane) index.files.Add(entry);
                }

                // only keep an id that still names a surviving entry
                string active = loaded.activePresentationId;
                if (!string.IsNullOrEmpty(active) && index.files.Exists(f => f.id == active))
                    index.activePresentationId = active;

                index.scriptDisplay = loaded.scriptDisplay;

                string notes = loaded.activeNotesId;
                if (!string.IsNullOrEmpty(notes) && index.files.Exists(f => f.id == notes))
                    index.activeNotesId = notes;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ImportStore] index unreadable, starting empty: " + e.Message);
            }
            return index;
        }

        // write to a temp file first so a crash never leaves a half written target
        static void WriteReplacing(string path, byte[] bytes)
        {
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[ImportStore] cleanup failed: " + e.Message);
            }
        }
    }
}
