using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace StageReadyVR.Controllers
{
    /// <summary>
    /// Logical controller buttons used by the app. Names follow the physical
    /// Touch/PICO layout: A and B on the right controller, X and the menu
    /// button on the left.
    /// </summary>
    public enum ControllerButton
    {
        LeftMenu,
        LeftPrimary,      // X
        RightPrimary,     // A
        RightSecondary,   // B
        AnyPrimary        // A or X
    }

    /// <summary>
    /// Vendor-neutral controller button polling built on UnityEngine.XR.InputDevices,
    /// so it works with whichever XR loader is active (PICO, OpenXR, ...). Provides
    /// per-frame "down" edge detection that is safe to query from several scripts
    /// in the same frame.
    /// </summary>
    public static class XRControllerInput
    {
        private const InputDeviceCharacteristics LeftHand =
            InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left;
        private const InputDeviceCharacteristics RightHand =
            InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right;

        private static readonly int ButtonCount = System.Enum.GetValues(typeof(ControllerButton)).Length;
        private static readonly bool[] Held = new bool[ButtonCount];
        private static readonly bool[] Down = new bool[ButtonCount];
        private static readonly List<InputDevice> Scratch = new List<InputDevice>(2);

        private static InputDevice _left;
        private static InputDevice _right;
        private static int _lastRefreshFrame = -1;

        /// <summary>True only on the first frame the button is pressed.</summary>
        public static bool GetDown(ControllerButton button)
        {
            Refresh();
            return Down[(int)button];
        }

        /// <summary>True for as long as the button is held.</summary>
        public static bool Get(ControllerButton button)
        {
            Refresh();
            return Held[(int)button];
        }

        // samples every button once per frame so repeated queries agree
        private static void Refresh()
        {
            int frame = Time.frameCount;
            if (frame == _lastRefreshFrame) return;

            // after a gap in polling the stored state is stale, and a button that is
            // already held would register as a fresh press, so suppress edges then
            bool contiguous = _lastRefreshFrame == frame - 1;
            _lastRefreshFrame = frame;

            for (int i = 0; i < ButtonCount; i++)
            {
                bool now = Sample((ControllerButton)i);
                Down[i] = contiguous && now && !Held[i];
                Held[i] = now;
            }
        }

        private static bool Sample(ControllerButton button)
        {
            switch (button)
            {
                case ControllerButton.LeftMenu:
                    return Read(ref _left, LeftHand, CommonUsages.menuButton);
                case ControllerButton.LeftPrimary:
                    return Read(ref _left, LeftHand, CommonUsages.primaryButton);
                case ControllerButton.RightPrimary:
                    return Read(ref _right, RightHand, CommonUsages.primaryButton);
                case ControllerButton.RightSecondary:
                    return Read(ref _right, RightHand, CommonUsages.secondaryButton);
                case ControllerButton.AnyPrimary:
                    return Read(ref _left, LeftHand, CommonUsages.primaryButton)
                        || Read(ref _right, RightHand, CommonUsages.primaryButton);
                default:
                    return false;
            }
        }

        private static bool Read(ref InputDevice device, InputDeviceCharacteristics hand,
                                 InputFeatureUsage<bool> usage)
        {
            // controllers can connect or drop at any time, so re-acquire when invalid
            if (!device.isValid)
            {
                Scratch.Clear();
                InputDevices.GetDevicesWithCharacteristics(hand, Scratch);
                if (Scratch.Count == 0) return false;
                device = Scratch[0];
            }
            return device.TryGetFeatureValue(usage, out bool value) && value;
        }
    }
}
