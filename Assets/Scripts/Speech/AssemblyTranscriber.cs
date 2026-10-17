using UnityEngine;

/// <summary>
/// Placeholder for the Assembly speech-to-text integration. It never produces
/// a transcript, so sessions run end to end with zero words until a real client
/// is implemented here (raise the events from SpeechTranscriber as results
/// arrive). Keeping the stub as the only Assembly touchpoint means no other
/// script has to change when the real service is wired in.
/// </summary>
public class AssemblyTranscriber : SpeechTranscriber
{
    private bool _active;
    private bool _warned;

    public override void Activate()
    {
        if (_active) return;
        _active = true;

        // warn once per session rather than on every analyzer re-activation
        if (!_warned)
        {
            _warned = true;
            Debug.LogWarning("[AssemblyTranscriber] Assembly is not integrated yet; " +
                             "no transcript will be produced.");
        }
    }

    public override void Deactivate()
    {
        if (!_active) return;
        _active = false;
        RaiseStopped();
    }
}
