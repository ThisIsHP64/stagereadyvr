using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using ISButton = UnityEngine.InputSystem.Controls.ButtonControl;
using ISController = UnityEngine.InputSystem.XR.XRController;
using ISDevice = UnityEngine.InputSystem.InputDevice;

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
    /// Vendor-neutral controller button polling with per-frame "down" edge
    /// detection that is safe to query from several scripts in the same frame.
    /// Each button is read through two independent routes and counts as pressed
    /// if either reports it: the legacy UnityEngine.XR feature usages, and the
    /// Input System controls declared by the active controller layout. Using
    /// both means a naming difference in one route cannot disable a button.
    /// </summary>
    public static class XRControllerInput
    {
        private const InputDeviceCharacteristics LeftHand =
            InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left;
        private const InputDeviceCharacteristics RightHand =
            InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right;

        // retry quickly until every control is found, then only re-check rarely
        private const int ResolveRetryFrames = 60;
        private const int ResolveSettledFrames = 600;

        private static readonly int ButtonCount = Enum.GetValues(typeof(ControllerButton)).Length;
        private static readonly bool[] Held = new bool[ButtonCount];
        private static readonly bool[] Down = new bool[ButtonCount];
        private static readonly List<UnityEngine.XR.InputDevice> Scratch = new List<UnityEngine.XR.InputDevice>(2);

        private static UnityEngine.XR.InputDevice _left;
        private static UnityEngine.XR.InputDevice _right;
        private static int _lastRefreshFrame = -1;

        private static ISButton _isLeftMenu;
        private static ISButton _isLeftPrimary;
        private static ISButton _isRightPrimary;
        private static ISButton _isRightSecondary;
        private static int _nextResolveFrame;

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

            ResolveInputSystemControls(frame);

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
                    return ReadLegacy(ref _left, LeftHand, CommonUsages.menuButton)
                        || IsPressed(_isLeftMenu);
                case ControllerButton.LeftPrimary:
                    return ReadLegacy(ref _left, LeftHand, CommonUsages.primaryButton)
                        || IsPressed(_isLeftPrimary);
                case ControllerButton.RightPrimary:
                    return ReadLegacy(ref _right, RightHand, CommonUsages.primaryButton)
                        || IsPressed(_isRightPrimary);
                case ControllerButton.RightSecondary:
                    return ReadLegacy(ref _right, RightHand, CommonUsages.secondaryButton)
                        || IsPressed(_isRightSecondary);
                case ControllerButton.AnyPrimary:
                    return ReadLegacy(ref _left, LeftHand, CommonUsages.primaryButton)
                        || ReadLegacy(ref _right, RightHand, CommonUsages.primaryButton)
                        || IsPressed(_isLeftPrimary)
                        || IsPressed(_isRightPrimary);
                default:
                    return false;
            }
        }

        private static bool ReadLegacy(ref UnityEngine.XR.InputDevice device, InputDeviceCharacteristics hand,
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

        private static bool IsPressed(ISButton control)
        {
            return control != null && control.device != null && control.device.added && control.isPressed;
        }

        // control names come from the controller layout, so try the layout name first
        // and then its alias; lookups are cached because devices rarely change
        private static void ResolveInputSystemControls(int frame)
        {
            if (frame < _nextResolveFrame) return;

            _isLeftMenu = FindControl("LeftHand", "menu", "menuButton");
            _isLeftPrimary = FindControl("LeftHand", "primaryButton", "X");
            _isRightPrimary = FindControl("RightHand", "primaryButton", "A");
            _isRightSecondary = FindControl("RightHand", "secondaryButton", "B");

            bool allFound = _isLeftMenu != null && _isLeftPrimary != null
                         && _isRightPrimary != null && _isRightSecondary != null;
            _nextResolveFrame = frame + (allFound ? ResolveSettledFrames : ResolveRetryFrames);
        }

        private static ISButton FindControl(string handUsage, string name, string alias)
        {
            var devices = UnityEngine.InputSystem.InputSystem.devices;
            for (int i = 0; i < devices.Count; i++)
            {
                ISDevice device = devices[i];
                if (!(device is ISController) || !HasUsage(device, handUsage)) continue;

                if (device.TryGetChildControl(name) is ISButton byName) return byName;
                if (device.TryGetChildControl(alias) is ISButton byAlias) return byAlias;
            }
            return null;
        }

        private static bool HasUsage(ISDevice device, string usage)
        {
            var usages = device.usages;
            for (int i = 0; i < usages.Count; i++)
            {
                if (string.Equals(usages[i], usage, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
