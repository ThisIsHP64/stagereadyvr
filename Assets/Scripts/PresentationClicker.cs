using StageReadyVR.Controllers;
using StageReadyVR.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Advances the projected presentation by one slide on a right trigger pull,
/// or on Page Down, which is also what usb presentation clickers send.
/// Lives outside the presentation assembly because XRControllerInput is in
/// Assembly-CSharp, which an assembly definition cannot reference.
/// </summary>
public class PresentationClicker : MonoBehaviour
{
    public ProjectionScreen projectionScreen;
    public SessionManager sessionManager;
    [Tooltip("Optional; slides do not advance while paused")]
    public PauseManager pauseManager;

    void Update()
    {
        // sampled every frame, even when ignored, so the trigger edge stays accurate
        bool pressed = XRControllerInput.GetDown(ControllerButton.RightTrigger) || PageDownPressed();
        if (!pressed || projectionScreen == null || !projectionScreen.HasPresentation) return;

        // only during the talk, never in the countdown, the pause menu or the feedback dashboard
        if (sessionManager == null || !sessionManager.IsSessionActive) return;
        if (pauseManager != null && pauseManager.IsPaused) return;

        projectionScreen.Advance();
    }

    static bool PageDownPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.pageDownKey.wasPressedThisFrame;
    }
}
