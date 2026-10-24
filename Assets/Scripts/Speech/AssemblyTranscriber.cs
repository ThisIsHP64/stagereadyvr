using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AssemblyAI realtime transcription source.
///
/// Audio is captured from Unity's microphone as mono 16-bit PCM at 16 kHz and
/// streamed to AssemblyAI's v3 WebSocket API. Only final turns are forwarded
/// to SpeechAnalyzer as full transcript events; interim turns are forwarded as
/// partial events. Network and microphone failures are logged and leave the
/// session running instead of throwing into the Unity frame loop.
///
/// Development setup:
///   Create Assets/Resources/assemblyai_key.txt and put only the AssemblyAI
///   API key in it. The file is ignored by git. Do not ship this key in a
///   public APK; use a relay service for a real deployment.
/// </summary>
public class AssemblyTranscriber : SpeechTranscriber
{
    private const string Endpoint =
        "wss://streaming.assemblyai.com/v3/ws?sample_rate=16000&speech_model=u3-rt-pro";
    private const string KeyResourceName = "assemblyai_key";
    private const int SampleRate = 16000;
    private const int FramesPerChunk = 800; // 50 ms at 16 kHz
    private const int MicrophoneBufferSeconds = 2;

    private sealed class TranscriptResult
    {
        public string Text;
        public bool IsFinal;
    }

    [Serializable]
    private sealed class AssemblyMessage
    {
        public string type;
        public string transcript;
        public bool end_of_turn;
    }

    private readonly ConcurrentQueue<byte[]> _audioQueue = new ConcurrentQueue<byte[]>();
    private readonly ConcurrentQueue<TranscriptResult> _transcriptQueue =
        new ConcurrentQueue<TranscriptResult>();
    private readonly ConcurrentQueue<string> _statusQueue = new ConcurrentQueue<string>();

    private ClientWebSocket _socket;
    private CancellationTokenSource _cancellation;
    private AudioClip _microphoneClip;
    private float[] _sampleBuffer;
    private int _lastMicrophoneFrame;
    private bool _active;
    private bool _microphoneStarted;
    private volatile bool _socketOpen;
    private int _activationVersion;

    public bool IsConnected => _socketOpen;

    public override void Activate()
    {
        if (_active) return;
        _active = true;
        _activationVersion++;

        string apiKey = LoadApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            QueueStatus("[AssemblyTranscriber] Missing Assets/Resources/assemblyai_key.txt; " +
                        "transcription is disabled for this session.");
            return;
        }

        int version = _activationVersion;
        _ = ConnectAsync(apiKey, version);
    }

    public override void Deactivate()
    {
        if (!_active) return;
        _active = false;
        _activationVersion++;
        StopMicrophone();

        ClientWebSocket socket = _socket;
        CancellationTokenSource cancellation = _cancellation;
        _socket = null;
        _cancellation = null;
        _socketOpen = false;
        _ = CloseSocketAsync(socket, cancellation);

        RaiseStopped();
    }

    private void Update()
    {
        DrainStatusMessages();
        DrainTranscriptMessages();

        if (_active && _socketOpen)
        {
            if (!_microphoneStarted) StartMicrophone();
            PumpMicrophone();
        }
    }

    private async Task ConnectAsync(string apiKey, int version)
    {
        var socket = new ClientWebSocket();
        var cancellation = new CancellationTokenSource();

        try
        {
            socket.Options.SetRequestHeader("Authorization", apiKey);
            await socket.ConnectAsync(new Uri(Endpoint), cancellation.Token);

            if (!_active || version != _activationVersion)
            {
                await CloseSocketAsync(socket, cancellation);
                return;
            }

            _socket = socket;
            _cancellation = cancellation;
            _socketOpen = true;
            QueueStatus("[AssemblyTranscriber] AssemblyAI connection established.");

            _ = ReceiveLoopAsync(socket, cancellation.Token);
            _ = SendLoopAsync(socket, cancellation.Token);
        }
        catch (Exception ex)
        {
            cancellation.Dispose();
            socket.Dispose();
            QueueStatus("[AssemblyTranscriber] Connection failed; continuing without " +
                        "transcription. " + ex.Message);
        }
    }

    private async Task SendLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                if (_audioQueue.TryDequeue(out byte[] audio))
                {
                    await socket.SendAsync(new ArraySegment<byte>(audio),
                        WebSocketMessageType.Binary, true, token);
                }
                else
                {
                    await Task.Delay(10, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal during Deactivate.
        }
        catch (Exception ex)
        {
            QueueStatus("[AssemblyTranscriber] Audio stream failed; continuing without " +
                        "transcription. " + ex.Message);
            _socketOpen = false;
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        byte[] buffer = new byte[64 * 1024];

        try
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using (var message = new MemoryStream())
                {
                    WebSocketReceiveResult received;
                    do
                    {
                        received = await socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer), token);

                        if (received.MessageType == WebSocketMessageType.Close)
                        {
                            _socketOpen = false;
                            QueueStatus("[AssemblyTranscriber] AssemblyAI closed the connection.");
                            return;
                        }

                        message.Write(buffer, 0, received.Count);
                    }
                    while (!received.EndOfMessage);

                    HandleServerMessage(Encoding.UTF8.GetString(message.ToArray()));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal during Deactivate.
        }
        catch (Exception ex)
        {
            _socketOpen = false;
            QueueStatus("[AssemblyTranscriber] Transcript stream failed; continuing " +
                        "without transcription. " + ex.Message);
        }
    }

    private void HandleServerMessage(string json)
    {
        AssemblyMessage message;
        try
        {
            message = JsonUtility.FromJson<AssemblyMessage>(json);
        }
        catch (Exception ex)
        {
            QueueStatus("[AssemblyTranscriber] Ignored malformed server message: " + ex.Message);
            return;
        }

        if (message == null) return;

        if (message.type == "Turn" && !string.IsNullOrWhiteSpace(message.transcript))
        {
            _transcriptQueue.Enqueue(new TranscriptResult
            {
                Text = message.transcript,
                IsFinal = message.end_of_turn
            });
        }
        else if (message.type == "Termination")
        {
            QueueStatus("[AssemblyTranscriber] AssemblyAI session terminated.");
        }
    }

    private void StartMicrophone()
    {
        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            QueueStatus("[AssemblyTranscriber] No microphone was detected; " +
                        "transcription is disabled for this session.");
            return;
        }

        try
        {
            _microphoneClip = Microphone.Start(null, true, MicrophoneBufferSeconds, SampleRate);
            _sampleBuffer = new float[FramesPerChunk * Math.Max(1, _microphoneClip.channels)];
            _lastMicrophoneFrame = 0;
            _microphoneStarted = _microphoneClip != null;
        }
        catch (Exception ex)
        {
            QueueStatus("[AssemblyTranscriber] Microphone could not start; " + ex.Message);
        }
    }

    private void PumpMicrophone()
    {
        if (!_microphoneStarted || _microphoneClip == null) return;

        int currentFrame = Microphone.GetPosition(null);
        if (currentFrame < 0) return;

        int availableFrames = currentFrame - _lastMicrophoneFrame;
        if (availableFrames < 0) availableFrames += _microphoneClip.samples;

        while (availableFrames >= FramesPerChunk)
        {
            ReadMicrophoneFrames(_lastMicrophoneFrame, FramesPerChunk, _sampleBuffer);
            _audioQueue.Enqueue(ToPcm16(_sampleBuffer, _microphoneClip.channels));
            _lastMicrophoneFrame = (_lastMicrophoneFrame + FramesPerChunk) % _microphoneClip.samples;
            availableFrames -= FramesPerChunk;
        }
    }

    private void ReadMicrophoneFrames(int startFrame, int frameCount, float[] destination)
    {
        int channels = Math.Max(1, _microphoneClip.channels);
        int sampleStart = startFrame * channels;
        int sampleCount = frameCount * channels;
        int totalSamples = _microphoneClip.samples * channels;

        if (sampleStart + sampleCount <= totalSamples)
        {
            _microphoneClip.GetData(destination, sampleStart);
            return;
        }

        int firstSamples = totalSamples - sampleStart;
        var first = new float[firstSamples];
        var second = new float[sampleCount - firstSamples];
        _microphoneClip.GetData(first, sampleStart);
        _microphoneClip.GetData(second, 0);
        Array.Copy(first, 0, destination, 0, first.Length);
        Array.Copy(second, 0, destination, first.Length, second.Length);
    }

    private static byte[] ToPcm16(float[] samples, int channels)
    {
        channels = Math.Max(1, channels);
        int frames = samples.Length / channels;
        byte[] pcm = new byte[frames * 2];

        for (int frame = 0; frame < frames; frame++)
        {
            float value = 0f;
            for (int channel = 0; channel < channels; channel++)
                value += samples[frame * channels + channel];
            value = Mathf.Clamp(value / channels, -1f, 1f);
            short sample = (short)(value * short.MaxValue);
            pcm[frame * 2] = (byte)(sample & 0xff);
            pcm[frame * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }

        return pcm;
    }

    private void StopMicrophone()
    {
        if (_microphoneStarted)
        {
            try { Microphone.End(null); }
            catch (Exception ex) { QueueStatus("[AssemblyTranscriber] Microphone stop failed: " + ex.Message); }
        }

        _microphoneStarted = false;
        _microphoneClip = null;
        _sampleBuffer = null;
        while (_audioQueue.TryDequeue(out _)) { }
    }

    private static async Task CloseSocketAsync(ClientWebSocket socket,
        CancellationTokenSource cancellation)
    {
        if (socket == null) return;

        try
        {
            if (socket.State == WebSocketState.Open)
            {
                byte[] terminate = Encoding.UTF8.GetBytes("{\"type\":\"Terminate\"}");
                await socket.SendAsync(new ArraySegment<byte>(terminate),
                    WebSocketMessageType.Text, true, CancellationToken.None);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                    "Session complete", CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // The connection may already be gone; shutdown must stay best-effort.
        }
        finally
        {
            if (cancellation != null) cancellation.Cancel();
            socket.Dispose();
            if (cancellation != null) cancellation.Dispose();
        }
    }

    private string LoadApiKey()
    {
        TextAsset keyAsset = Resources.Load<TextAsset>(KeyResourceName);
        return keyAsset == null ? string.Empty : keyAsset.text.Trim();
    }

    private void QueueTranscript(TranscriptResult result)
    {
        _transcriptQueue.Enqueue(result);
    }

    private void QueueStatus(string message)
    {
        _statusQueue.Enqueue(message);
    }

    private void DrainTranscriptMessages()
    {
        while (_transcriptQueue.TryDequeue(out TranscriptResult result))
        {
            if (result.IsFinal) RaiseFull(result.Text);
            else RaisePartial(result.Text);
        }
    }

    private void DrainStatusMessages()
    {
        while (_statusQueue.TryDequeue(out string message))
            Debug.LogWarning(message);
    }

    private void OnDestroy()
    {
        Deactivate();
    }
}
