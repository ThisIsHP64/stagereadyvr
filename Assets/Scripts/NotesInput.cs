using StageReadyVR.Controllers;
using StageReadyVR.Notes;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Routes the left thumbstick to whichever speaker notes are showing. For held
/// notes a sideways flick turns the page. For the teleprompter a sideways flick
/// jumps a few lines, an up or down flick changes the speed, and a stick click
/// pauses. Desktop keys mirror the stick. Lives outside the notes assembly
/// because XRControllerInput is in Assembly-CSharp, which an assembly
/// definition cannot reference.
/// </summary>
public class NotesInput : MonoBehaviour
{
    public HeldNotes heldNotes;
    public Teleprompter teleprompter;
    public SessionManager sessionManager;
    [Tooltip("Optional; notes do not move while paused")]
    public PauseManager pauseManager;

    readonly StickFlick sideways = new StickFlick();
    readonly StickFlick upDown = new StickFlick();

    void Update()
    {
        // sampled every frame, even when ignored, so a stick held through a pause does not fire on resume
        Vector2 stick = XRControllerInput.GetLeftStick();
        int across = sideways.Step(stick.x) + KeyAcross();
        int vertical = upDown.Step(stick.y) + KeyVertical();
        bool click = XRControllerInput.GetDown(ControllerButton.LeftStickClick) || KeyPause();

        bool running = sessionManager != null && sessionManager.IsSessionActive
                       && (pauseManager == null || !pauseManager.IsPaused);

        // the teleprompter scrolls on its own, so it must be told every frame whether it may
        if (teleprompter != null) teleprompter.SetHalted(!running);
        if (!running) return;

        if (teleprompter != null && teleprompter.HasScript)
        {
            if (across != 0) teleprompter.Nudge(across);
            if (vertical > 0) teleprompter.Faster();
            else if (vertical < 0) teleprompter.Slower();
            if (click) teleprompter.TogglePause();
        }
        else if (heldNotes != null && heldNotes.HasNotes)
        {
            if (across > 0) heldNotes.Next();
            else if (across < 0) heldNotes.Previous();
        }
    }

    static int KeyAcross()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return 0;
        if (keyboard.periodKey.wasPressedThisFrame) return 1;
        if (keyboard.commaKey.wasPressedThisFrame) return -1;
        return 0;
    }

    static int KeyVertical()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return 0;
        if (keyboard.upArrowKey.wasPressedThisFrame) return 1;
        if (keyboard.downArrowKey.wasPressedThisFrame) return -1;
        return 0;
    }

    static bool KeyPause()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
    }
}
