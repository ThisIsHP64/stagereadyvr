using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using StageReadyVR.HybridScoring;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

// One microphone and one socket per presentation. Never run PicoMicProbe in this scene.
public class AssemblyTranscriber : SpeechTranscriber
{
    private const int Rate = 16000, Frames = 800, BufferSeconds = 4;
    private const string Endpoint = "wss://streaming.assemblyai.com/v3/ws?sample_rate=16000&speech_model=u3-rt-pro";
    private sealed class Run
    {
        public readonly ClientWebSocket Socket = new ClientWebSocket();
        public readonly CancellationTokenSource Cancel = new CancellationTokenSource();
        public readonly ConcurrentQueue<byte[]> Audio = new ConcurrentQueue<byte[]>();
        public readonly ConcurrentQueue<Turn> Text = new ConcurrentQueue<Turn>();
        public volatile bool Connected, Finish, Terminated, Failed;
        public volatile string Error;
        public Task Work;
    }
    [Serializable] private sealed class Turn { public string type, transcript; public int turn_order; public bool end_of_turn; }
    [Serializable] private sealed class TokenReply { public string token; }
    private Run run;
    private AudioClip clip;
    private int cursor, nativeChunk, channels;
    private float lastPump, lastMotion;
    private Coroutine startup;
    private bool ending;
    private int lastFinal = -1;
    public bool IsConnected => run != null && run.Connected && !run.Failed && !run.Terminated;
    public bool CompletedSuccessfully { get; private set; }
    public bool StartupFinished => run != null && startup == null;
    public bool CaptureReady => StartupFinished && clip != null && IsConnected;
    public string Status { get; private set; } = "Not started";
    public event Action<float> OnVolumeDb;

    public override void Activate()
    {
        if (run != null || startup != null || ending) return;
        CompletedSuccessfully = false;
        lastFinal = -1;
        Status = "Starting microphone";
        run = new Run();
        startup = StartCoroutine(StartCapture(run));
    }
    private IEnumerator StartCapture(Run r)
    {
        yield return null; // let Activate store the coroutine handle first
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Status = "Allow microphone access";
            Permission.RequestUserPermission(Permission.Microphone);
            float until = Time.realtimeSinceStartup + 20f;
            while (run == r && !Permission.HasUserAuthorizedPermission(Permission.Microphone) && Time.realtimeSinceStartup < until)
                yield return null;
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            { Fail(r, "Microphone permission not granted"); startup = null; yield break; }
        }
#endif
        string key = null, token = null;
        Status = "Connecting to AssemblyAI";
        if (LiveSessionRelay.TryLoad(out var config, out var configError))
        {
            using (var req = LiveSessionRelay.Request(config, "/aai-token", "GET", null))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                { Fail(r, "Speech token request failed (HTTP " + req.responseCode + ")"); startup = null; yield break; }
                TokenReply reply = null;
                try { reply = JsonUtility.FromJson<TokenReply>(req.downloadHandler.text); } catch (Exception) { }
                token = reply == null ? null : reply.token;
                if (string.IsNullOrWhiteSpace(token)) { Fail(r, "Speech token missing"); startup = null; yield break; }
            }
        }
        else
        {
#if UNITY_EDITOR
            if (LiveSessionRelay.HasConfig) { Fail(r, configError); startup = null; yield break; }
            key = Environment.GetEnvironmentVariable("ASSEMBLYAI_API_KEY");
            if (string.IsNullOrWhiteSpace(key))
            {
                var asset = Resources.Load<TextAsset>("assemblyai_key");
                key = asset == null ? null : asset.text.Trim();
            }
            if (string.IsNullOrWhiteSpace(key)) { Fail(r, "AssemblyAI key not configured"); startup = null; yield break; }
#else
            Fail(r, configError); startup = null; yield break;
#endif
        }
        r.Work = Network(r, key, token);
        float deadline = Time.realtimeSinceStartup + 15f;
        while (run == r && !r.Connected && !r.Failed && Time.realtimeSinceStartup < deadline) yield return null;
        if (run != r) yield break;
        if (!r.Connected || r.Failed) { Fail(r, r.Error ?? "AssemblyAI connection timed out"); startup = null; yield break; }
        // The working PICO probe uses the default device and waits for a moving position.
        // An empty device list on Android is diagnostic, not a reason to refuse the default mic.
        try
        {
            Debug.Log("[LiveSpeech] Microphone devices: " + (Microphone.devices?.Length ?? 0));
            clip = Microphone.Start(null, true, BufferSeconds, Rate);
        }
        catch (Exception) { Fail(r, "Microphone.Start failed"); }
        if (clip == null) { Fail(r, "Microphone returned no audio clip"); startup = null; yield break; }
        channels = Math.Max(1, clip.channels);
        nativeChunk = Math.Max(1, Mathf.RoundToInt(clip.frequency / 20f));
        deadline = Time.realtimeSinceStartup + 5f;
        while (run == r && !r.Failed && Microphone.GetPosition(null) <= 0 && Time.realtimeSinceStartup < deadline) yield return null;
        if (run != r) yield break;
        if (r.Failed || Microphone.GetPosition(null) <= 0) { Fail(r, "Microphone position stayed at zero"); ReleaseMic(); startup = null; yield break; }
        cursor = 0; lastPump = lastMotion = Time.realtimeSinceStartup;
        Status = "Listening";
        Debug.Log("[LiveSpeech] CAPTURE STARTED; rate=" + clip.frequency + " channels=" + channels);
        startup = null;
    }
    private static async Task Network(Run r, string key, string token)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(key)) r.Socket.Options.SetRequestHeader("Authorization", key.Trim());
            string url = Endpoint + (string.IsNullOrEmpty(token) ? "" : "&token=" + Uri.EscapeDataString(token));
            await r.Socket.ConnectAsync(new Uri(url), r.Cancel.Token);
            r.Connected = true;
            var send = Send(r); var receive = Receive(r);
            await Task.WhenAny(send, receive);
            // Normal send completion means Terminate was sent; keep receiving until the server confirms.
            if (send.IsFaulted) await send;
            if (receive.IsFaulted) await receive;
            if (send.IsCompleted && r.Finish && !r.Failed && !r.Terminated) await receive;
            r.Cancel.Cancel();
            try { await Task.WhenAll(send, receive); } catch (OperationCanceledException) { }
            if (!r.Terminated && !r.Failed) Fail(r, "Speech connection closed before final confirmation");
        }
        catch (OperationCanceledException) { }
        catch (Exception) { Fail(r, "AssemblyAI network connection failed"); }
        finally { r.Connected = false; try { r.Cancel.Cancel(); } catch (ObjectDisposedException) { } r.Socket.Dispose(); r.Cancel.Dispose(); }
    }
    private static async Task Send(Run r)
    {
        while (!r.Cancel.IsCancellationRequested)
        {
            if (r.Audio.TryDequeue(out var packet))
                await r.Socket.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, r.Cancel.Token);
            else if (r.Finish)
            {
                var end = Encoding.UTF8.GetBytes("{\"type\":\"Terminate\"}");
                await r.Socket.SendAsync(new ArraySegment<byte>(end), WebSocketMessageType.Text, true, r.Cancel.Token);
                return;
            }
            else await Task.Delay(10, r.Cancel.Token);
        }
    }
    private static async Task Receive(Run r)
    {
        var buffer = new byte[16384];
        while (!r.Cancel.IsCancellationRequested)
        {
            using (var message = new MemoryStream())
            {
                WebSocketReceiveResult part;
                do
                {
                    part = await r.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), r.Cancel.Token);
                    if (part.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, part.Count);
                    if (message.Length > 1024 * 1024) throw new InvalidDataException();
                } while (!part.EndOfMessage);
                Turn t = JsonUtility.FromJson<Turn>(Encoding.UTF8.GetString(message.ToArray()));
                if (t == null) continue;
                if (t.type == "Turn") r.Text.Enqueue(t);
                if (t.type == "Termination") { r.Terminated = true; return; }
                if (t.type == "Error") { Fail(r, "AssemblyAI returned a speech error"); return; }
            }
        }
    }
    private static void Fail(Run r, string reason) { r.Error = reason; r.Failed = true; try { r.Cancel.Cancel(); } catch (ObjectDisposedException) { } }
    private void Update()
    {
        if (run == null) return;
        Drain(run);
        if (run.Failed)
        {
            if (Status != run.Error) Debug.LogWarning("[LiveSpeech] " + run.Error);
            Status = run.Error;
            ReleaseMic();
            return;
        }
        if (startup == null && !ending && clip != null) Pump(run, false);
    }
    private void Drain(Run r)
    {
        while (r.Text.TryDequeue(out var t))
        {
            if (t.turn_order <= lastFinal) continue;
            if (t.end_of_turn)
            {
                lastFinal = t.turn_order;
                if (!string.IsNullOrWhiteSpace(t.transcript)) RaiseFull(t.transcript);
                else RaisePartial("");
            }
            else RaisePartial(t.transcript ?? "");
        }
    }
    private void Pump(Run r, bool flush)
    {
        if (clip == null || r.Failed) return;
        try
        {
            float now = Time.realtimeSinceStartup;
            int pos = Microphone.GetPosition(null);
            if (pos < 0 || !Microphone.IsRecording(null)) { Fail(r, "Microphone stopped unexpectedly"); return; }
            if (now - lastPump >= BufferSeconds - 0.25f) { Fail(r, "Audio buffer overrun"); return; }
            lastPump = now;
            int available = (pos - cursor + clip.samples) % clip.samples;
            if (available > 0) lastMotion = now;
            if (!flush && now - lastMotion > 3f) { Fail(r, "Microphone stopped delivering audio"); return; }
            while (available >= nativeChunk || (flush && available > 0))
            {
                int n = Math.Min(nativeChunk, available);
                var raw = new float[n * channels];
                if (!clip.GetData(raw, cursor)) { Fail(r, "Microphone audio could not be read"); return; }
                var pcm = new byte[Frames * 2];
                double power = 0;
                for (int f = 0; f < n; f++) { float sample = Mono(raw, f, channels); power += sample * sample; }
                float db = Mathf.Max(-80f, 10f * Mathf.Log10(Mathf.Max(1e-8f, (float)(power / n))));
                OnVolumeDb?.Invoke(db);
                int output = Math.Min(Frames, Mathf.RoundToInt(n * (float)Frames / nativeChunk));
                for (int i = 0; i < output; i++)
                {
                    float p = i * (float)nativeChunk / Frames;
                    int a = Math.Min(n - 1, (int)p), b = Math.Min(n - 1, a + 1);
                    float v = Mathf.Lerp(Mono(raw, a, channels), Mono(raw, b, channels), p - a);
                    int s = Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 32767f);
                    pcm[2*i] = (byte)(s & 255); pcm[2*i+1] = (byte)((s >> 8) & 255);
                }
                if (r.Audio.Count >= 100) { Fail(r, "Speech network audio queue overflow"); return; }
                r.Audio.Enqueue(pcm);
                cursor = (cursor + n) % clip.samples; available -= n;
            }
        }
        catch (Exception) { Fail(r, "Microphone capture failed"); }
    }
    private static float Mono(float[] raw, int frame, int ch)
    { float sum = 0; for (int i = 0; i < ch; i++) sum += raw[frame*ch+i]; return sum / ch; }
    private void ReleaseMic()
    {
        if (clip == null) return;
        try { Microphone.End(null); } catch (Exception) { }
        Destroy(clip); clip = null;
    }
    public IEnumerator FinishGracefully()
    {
        Run r = run;
        if (r == null) yield break;
        ending = true;
        bool captured = startup == null && clip != null && !r.Failed;
        if (startup != null) { StopCoroutine(startup); startup = null; }
        if (captured) Pump(r, true);
        ReleaseMic();
        if (captured && !r.Failed && r.Connected)
        {
            Status = "Finalizing speech";
            r.Finish = true;
            float deadline = Time.realtimeSinceStartup + 12f;
            while (!r.Terminated && !r.Failed && Time.realtimeSinceStartup < deadline) { Drain(r); yield return null; }
        }
        Drain(r);
        CompletedSuccessfully = captured && r.Terminated && !r.Failed;
        Status = CompletedSuccessfully ? "Transcript finalized" : (r.Error ?? "Transcript incomplete or timed out");
        Debug.Log("[LiveSpeech] " + Status);
        Cleanup(r); RaiseStopped();
    }
    private void Cleanup(Run r)
    {
        if (startup != null) { StopCoroutine(startup); startup = null; }
        ReleaseMic();
        if (r.Work == null) { r.Socket.Dispose(); r.Cancel.Dispose(); }
        else { try { r.Cancel.Cancel(); } catch (ObjectDisposedException) { } }
        if (run == r) run = null;
        ending = false;
    }
    public override void Deactivate()
    {
        if (run == null) return;
        CompletedSuccessfully = false;
        string status = run.Error ?? "Stopped"; Cleanup(run); Status = status; RaiseStopped();
    }
    private void OnDestroy() { if (run != null) Cleanup(run); }
}
