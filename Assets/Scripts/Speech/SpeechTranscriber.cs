using System;
using UnityEngine;

/// <summary>
/// Provider-agnostic live transcription source consumed by SpeechAnalyzer.
/// A concrete subclass wraps one speech-to-text service and raises the events
/// below, so the analyzer never depends on a specific vendor SDK.
/// </summary>
public abstract class SpeechTranscriber : MonoBehaviour
{
    /// <summary>Interim text for the utterance in progress.</summary>
    public event Action<string> OnPartialTranscription;

    /// <summary>Finalized text for a completed utterance.</summary>
    public event Action<string> OnFullTranscription;

    /// <summary>Raised when the service stops listening (silence or deactivation).</summary>
    public event Action OnStoppedListening;

    /// <summary>Begin listening. Implementations must tolerate repeated calls.</summary>
    public abstract void Activate();

    /// <summary>Stop listening. Implementations must tolerate repeated calls.</summary>
    public abstract void Deactivate();

    protected void RaisePartial(string text) => OnPartialTranscription?.Invoke(text);
    protected void RaiseFull(string text) => OnFullTranscription?.Invoke(text);
    protected void RaiseStopped() => OnStoppedListening?.Invoke();
}
