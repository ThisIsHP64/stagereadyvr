using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace StageReadyVR.Import
{
    /// <summary>
    /// Drop-in component for any screen that offers file import. A UI calls
    /// BeginImport, shows Url and Pin to the user, and reacts to the events.
    /// The server runs on background threads, so results are queued and raised
    /// from Update, which keeps every event safe to use with Unity UI.
    /// </summary>
    public class ImportController : MonoBehaviour
    {
        [SerializeField] int preferredPort = ImportServer.DefaultPort;

        readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();
        ImportStore store;
        ImportServer server;

        /// <summary>A file passed validation and was saved.</summary>
        public event Action<ImportedFile> Imported;

        /// <summary>An upload or startup problem, as a message that is safe to show the user.</summary>
        public event Action<string> Failed;

        /// <summary>Instructions or progress text suitable for a label.</summary>
        public event Action<string> StatusChanged;

        public ImportStore Store => store ?? (store = ImportStore.CreateDefault());
        public bool IsImporting => server != null && server.IsRunning;
        public string Url { get; private set; }
        public string Pin { get; private set; }
        public string StatusMessage { get; private set; } = string.Empty;

        public void BeginImport()
        {
            if (IsImporting) return;

            string address = NetworkAddress.GetLanIPv4();
            if (address == null)
            {
                Fail("The headset is not connected to Wi-Fi. Connect to a network and try again.");
                return;
            }

            var candidate = new ImportServer(Store);
            candidate.FileImported += file => Enqueue(() => OnFileImported(file));
            candidate.UploadFailed += message => Enqueue(() => Fail(message));

            if (!candidate.Start(address, preferredPort))
            {
                string reason = candidate.StartError;
                candidate.Dispose();
                Fail(reason);
                return;
            }

            server = candidate;
            Url = "http://" + address + ":" + server.Port;
            Pin = server.Pin;
            SetStatus("On a laptop or phone using the same Wi-Fi, open " + Url + " and enter the PIN " + Pin + ".");
        }

        public void EndImport()
        {
            if (server == null) return;
            server.Dispose();
            server = null;
            Url = null;
            Pin = null;
        }

        void Update()
        {
            while (mainThreadQueue.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogWarning("[ImportController] handler: " + e.Message); }
            }
        }

        // the server must not keep listening while the headset sleeps or the screen closes
        void OnApplicationPause(bool paused)
        {
            if (paused) EndImport();
        }

        void OnDisable()
        {
            EndImport();
        }

        void Enqueue(Action action)
        {
            mainThreadQueue.Enqueue(action);
        }

        void OnFileImported(ImportedFile file)
        {
            SetStatus("Imported " + file.displayName + ".");
            Imported?.Invoke(file);
        }

        void Fail(string message)
        {
            SetStatus(message);
            Failed?.Invoke(message);
        }

        void SetStatus(string message)
        {
            StatusMessage = message;
            StatusChanged?.Invoke(message);
        }
    }
}
