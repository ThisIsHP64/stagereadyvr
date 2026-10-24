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

        public SaveResult Save(byte[] data, string originalName)
        {
            ValidationResult check = ImportValidator.Validate(data, originalName);
            if (!check.IsValid) return SaveResult.Fail(check.Message);

            lock (gate)
            {
                string storedPath = null;
                try
                {
                    Directory.CreateDirectory(root);

                    string id = Guid.NewGuid().ToString("N");
                    string storedName = id + (check.Kind == ImportKind.Script ? ".txt" : ".pdf");
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

        /// <summary>Reads a stored script. On failure, error holds a message that is safe to show the user.</summary>
        public bool TryReadText(string id, out string text, out string error)
        {
            text = null;
            error = null;

            ImportedFile entry = Get(id);
            if (entry == null || entry.kind != ImportKind.Script)
            {
                error = "That script could not be found. Import it again.";
                return false;
            }

            try
            {
                byte[] bytes;
                lock (gate) bytes = File.ReadAllBytes(Path.Combine(root, entry.storedFileName));

                if (ImportValidator.TryDecodeText(bytes, out text) && text.Trim().Length > 0) return true;
                error = "That script is empty or damaged. Import it again.";
                return false;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[ImportStore] read failed: " + e.Message);
                error = "That script could not be read. Import it again.";
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

        public bool Delete(string id)
        {
            lock (gate)
            {
                ImportIndex index = LoadIndex();
                int at = index.files.FindIndex(f => f.id == id);
                if (at < 0) return false;

                string path = Path.Combine(root, index.files[at].storedFileName);
                index.files.RemoveAt(at);

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
