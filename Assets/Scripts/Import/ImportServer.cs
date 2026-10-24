using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace StageReadyVR.Import
{
    /// <summary>
    /// A tiny upload server for getting files onto the headset over Wi-Fi.
    /// It only listens between Start and Stop. Every upload needs the PIN shown
    /// in VR, and requests are checked against the expected Host and Origin so a
    /// web page on another site cannot drive it from the user's browser.
    /// Events fire on background threads.
    /// </summary>
    public sealed class ImportServer : IDisposable
    {
        public const int DefaultPort = 8080;

        const int PortAttempts = 10;
        const int MaxPinFailures = 5;
        const int UploadTimeoutMs = 60000;
        const string PinHeader = "X-Import-Pin";

        readonly ImportStore store;
        readonly object pinGate = new object();
        readonly SemaphoreSlim uploadSlot = new SemaphoreSlim(1, 1);

        HttpListener listener;
        HashSet<string> allowedHosts;
        string pin;
        int pinFailures;

        public ImportServer(ImportStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public event Action<ImportedFile> FileImported;
        public event Action<string> UploadFailed;

        public bool IsRunning { get; private set; }
        public int Port { get; private set; }
        public string Pin => pin;
        public string StartError { get; private set; }

        /// <param name="lanAddress">The address shown to the user, accepted as a Host header value.</param>
        public bool Start(string lanAddress, int preferredPort = DefaultPort)
        {
            if (IsRunning) return true;
            StartError = null;

            for (int i = 0; i < PortAttempts; i++)
            {
                int port = preferredPort + i;
                var candidate = new HttpListener();
                try
                {
                    candidate.Prefixes.Add("http://+:" + port + "/");
                    candidate.Start();
                }
                catch (Exception e)
                {
                    candidate.Close();
                    Debug.LogWarning("[ImportServer] port " + port + " unavailable: " + e.Message);
                    continue;
                }

                listener = candidate;
                Port = port;
                pin = GeneratePin();
                pinFailures = 0;
                allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "127.0.0.1:" + port,
                    "localhost:" + port
                };
                if (!string.IsNullOrEmpty(lanAddress)) allowedHosts.Add(lanAddress + ":" + port);

                IsRunning = true;
                Task.Run(() => AcceptLoop(candidate));
                return true;
            }

            StartError = "The headset could not open a port for receiving files. Close other apps and try again.";
            return false;
        }

        public void Stop()
        {
            HttpListener current = listener;
            listener = null;
            IsRunning = false;
            Port = 0;
            if (current == null) return;

            try
            {
                current.Stop();
                current.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ImportServer] stop: " + e.Message);
            }
        }

        public void Dispose()
        {
            Stop();
            uploadSlot.Dispose();
        }

        // ── request loop ──────────────────────────────────────────────

        async Task AcceptLoop(HttpListener active)
        {
            while (active.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await active.GetContextAsync();
                }
                catch (Exception)
                {
                    break;      // listener was stopped
                }

                _ = Task.Run(() => Handle(context));
            }
        }

        void Handle(HttpListenerContext context)
        {
            try
            {
                HttpListenerRequest request = context.Request;
                AddSecurityHeaders(context.Response);

                // a mismatched host is how dns rebinding reaches a lan service
                if (!allowedHosts.Contains(request.Headers["Host"] ?? string.Empty))
                {
                    Reply(context, 403, false, "Request rejected.");
                    return;
                }

                string path = request.Url != null ? request.Url.AbsolutePath : string.Empty;

                if (request.HttpMethod == "GET" && path == "/")
                {
                    ReplyHtml(context, UploadPage.Html);
                    return;
                }

                if (request.HttpMethod == "POST" && path == "/upload")
                {
                    HandleUpload(context);
                    return;
                }

                Reply(context, 404, false, "Not found.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ImportServer] request failed: " + e.Message);
                TryReply(context, 500, "Something went wrong on the headset. Try again.");
            }
        }

        void HandleUpload(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;

            // browsers always send origin on cross site posts
            string origin = request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin) &&
                !string.Equals(origin, "http://" + request.Headers["Host"], StringComparison.OrdinalIgnoreCase))
            {
                Reply(context, 403, false, "Request rejected.");
                return;
            }

            switch (CheckPin(request.Headers[PinHeader] ?? string.Empty))
            {
                case PinState.Locked:
                    Fail(context, 429, "Too many wrong PIN attempts. Stop and restart the import on the headset to try again.");
                    return;
                case PinState.Wrong:
                    Fail(context, 403, "Wrong PIN. Check the code shown in VR.");
                    return;
            }

            string name = request.QueryString["name"];
            if (!ImportValidator.TryGetKind(name, out ImportKind kind))
            {
                Fail(context, 415, "Only .txt and .pdf files can be imported.");
                return;
            }

            long length = request.ContentLength64;
            if (length < 0)
            {
                Fail(context, 411, "The upload was not sized correctly. Try again.");
                return;
            }
            if (length == 0)
            {
                Fail(context, 400, "The file is empty.");
                return;
            }
            if (length > ImportValidator.MaxBytesFor(kind))
            {
                Fail(context, 413, "The file is too large. The limit is " +
                    (ImportValidator.MaxBytesFor(kind) / (1024 * 1024)) + " MB for this file type.");
                return;
            }

            if (!uploadSlot.Wait(0))
            {
                Reply(context, 503, false, "Another upload is in progress. Try again in a moment.");
                return;
            }

            try
            {
                byte[] body = ReadBody(context, (int)length);
                if (body == null)
                {
                    Fail(context, 400, "The upload was interrupted. Try again.");
                    return;
                }

                SaveResult saved = store.Save(body, name);
                if (!saved.Success)
                {
                    Fail(context, 422, saved.Message);
                    return;
                }

                // raised before the reply so a client that sees success can rely on the event being queued
                RaiseImported(saved.File);
                Reply(context, 200, true, saved.Message);
            }
            finally
            {
                uploadSlot.Release();
            }
        }

        // the watchdog aborts the connection so a stalled client cannot hold the upload slot
        static byte[] ReadBody(HttpListenerContext context, int length)
        {
            var buffer = new byte[length];
            int read = 0;

            using (new Timer(_ => TryAbort(context), null, UploadTimeoutMs, Timeout.Infinite))
            {
                try
                {
                    while (read < length)
                    {
                        int n = context.Request.InputStream.Read(buffer, read, length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return read == length ? buffer : null;
        }

        // ── pin handling ──────────────────────────────────────────────

        enum PinState { Ok, Wrong, Locked }

        PinState CheckPin(string supplied)
        {
            lock (pinGate)
            {
                // once locked even the right pin is refused, which stops guessing
                if (pinFailures >= MaxPinFailures) return PinState.Locked;
                if (ConstantTimeEquals(supplied, pin)) return PinState.Ok;
                pinFailures++;
                return PinState.Wrong;
            }
        }

        static bool ConstantTimeEquals(string a, string b)
        {
            int diff = a.Length ^ b.Length;
            int longest = Math.Max(a.Length, b.Length);
            for (int i = 0; i < longest; i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;
                diff |= x ^ y;
            }
            return diff == 0;
        }

        static string GeneratePin()
        {
            const uint Range = 1000000;
            const uint Limit = uint.MaxValue - (uint.MaxValue % Range);     // rejection sampling removes modulo bias

            using (var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[4];
                uint value;
                do
                {
                    rng.GetBytes(bytes);
                    value = BitConverter.ToUInt32(bytes, 0);
                } while (value >= Limit);
                return (value % Range).ToString("D6");
            }
        }

        // ── responses ─────────────────────────────────────────────────

        void Fail(HttpListenerContext context, int status, string message)
        {
            RaiseFailed(message);
            Reply(context, status, false, message);
        }

        void RaiseImported(ImportedFile file)
        {
            try { FileImported?.Invoke(file); }
            catch (Exception e) { Debug.LogWarning("[ImportServer] handler: " + e.Message); }
        }

        void RaiseFailed(string message)
        {
            try { UploadFailed?.Invoke(message); }
            catch (Exception e) { Debug.LogWarning("[ImportServer] handler: " + e.Message); }
        }

        static void AddSecurityHeaders(HttpListenerResponse response)
        {
            response.AddHeader("X-Content-Type-Options", "nosniff");
            response.AddHeader("X-Frame-Options", "DENY");
            response.AddHeader("Referrer-Policy", "no-referrer");
            response.AddHeader("Cache-Control", "no-store");
            response.AddHeader("Content-Security-Policy",
                "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'");
        }

        static void ReplyHtml(HttpListenerContext context, string html)
        {
            Write(context, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
        }

        static void Reply(HttpListenerContext context, int status, bool ok, string message)
        {
            string json = "{\"ok\":" + (ok ? "true" : "false") + ",\"message\":\"" + EscapeJson(message) + "\"}";
            Write(context, status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
        }

        static void TryReply(HttpListenerContext context, int status, string message)
        {
            try { Reply(context, status, false, message); }
            catch (Exception) { TryAbort(context); }
        }

        static void Write(HttpListenerContext context, int status, string contentType, byte[] bytes)
        {
            HttpListenerResponse response = context.Response;
            response.StatusCode = status;
            response.ContentType = contentType;
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Close();
        }

        static void TryAbort(HttpListenerContext context)
        {
            try { context.Response.Abort(); }
            catch (Exception) { }
        }

        static string EscapeJson(string text)
        {
            var sb = new StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
