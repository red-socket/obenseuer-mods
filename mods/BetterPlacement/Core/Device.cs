using System;
using UnityEngine;

namespace BetterPlacement
{
    internal enum InputDevice { KeyboardMouse, Pad }

    // Which device the player last used, and where each trigger rests (spec §3.3). The game's InputManager.inputType is
    // no help here: the drifting trigger read itself flips it to Controller (SRC/BuildingSystem.cs:1253-1256) and mouse
    // movement never flips it back. Tick runs once per frame, from Plugin.Update or the trigger filter, whichever comes
    // first, in every scene (not only in building mode).
    //
    // Keyboard/mouse activity: mouse movement, a mouse button or a key press (with no controller button down). Pad
    // activity: a controller button, the D-pad, a stick past half way, or a trigger read the filter let through
    // (MarkPad). While the mouse or keyboard is in use, each trigger's resting value is sampled as its baseline, but only
    // while that trigger is still (moved less than 0.05 since the last frame): a pull made while the mouse moves is never
    // taken for the resting value.
    internal static class Device
    {
        private static readonly string[] TriggerAxes = { "RightTrigger", "LeftTrigger" };
        private static readonly string[] PadAxes = { "LeftStickInventoryHorizontal", "LeftStickInventoryVertical", "RightStickHorizontal", "RightStickVertical" };
        private static readonly float[] Prev = new float[2];
        private static readonly float[] Baselines = new float[2];

        private static int _frame = -1;
        private static bool _init;
        private static bool _triggersMissing, _dpadMissing, _sticksMissing;

        internal static InputDevice Last { get; private set; } = InputDevice.KeyboardMouse;

        internal static float Baseline(string axis) => axis == TriggerAxes[0] ? Baselines[0] : axis == TriggerAxes[1] ? Baselines[1] : 0f;

        internal static void MarkPad() => Last = InputDevice.Pad;

        internal static void Tick()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            if (!_init)
            {
                if (InputManager.instance == null) return;
                Last = InputManager.instance.inputType == InputManager.InputType.Keyboard ? InputDevice.KeyboardMouse : InputDevice.Pad;
                _init = true;
            }

            var cur = new Vector2(Trigger(0), Trigger(1));
            if (KeyboardMouseActivity())
            {
                for (int i = 0; i < 2; i++)
                    if (Mathf.Abs(cur[i] - Prev[i]) < 0.05f) Baselines[i] = cur[i];
                Last = InputDevice.KeyboardMouse;
            }
            if (PadActivity()) Last = InputDevice.Pad;
            Prev[0] = cur[0];
            Prev[1] = cur[1];
        }

        private static float Trigger(int i)
        {
            if (_triggersMissing) return 0f;
            try { return Mathf.Abs(Input.GetAxis(TriggerAxes[i])); }
            catch (Exception)
            {
                _triggersMissing = true;
                Log.WarnOnce("trigger axes", "Rotation: the game has no trigger axes; the trigger baseline is off.");
                return 0f;
            }
        }

        private static bool KeyboardMouseActivity()
        {
            if (Mathf.Abs(Input.GetAxisRaw("Mouse X")) + Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0f) return true;
            for (var k = KeyCode.Mouse0; k <= KeyCode.Mouse6; k++)
                if (Input.GetKeyDown(k)) return true;
            if (!Input.anyKeyDown) return false;
            return !AnyPadButtonDown();
        }

        private static bool PadActivity()
        {
            if (AnyPadButtonDown()) return true;
            if (!_dpadMissing)
            {
                try
                {
                    if (Mathf.Abs(Input.GetAxisRaw("DPadHorizontal")) > 0.5f || Mathf.Abs(Input.GetAxisRaw("DPadVertical")) > 0.5f) return true;
                }
                catch (Exception) { _dpadMissing = true; }
            }
            if (!_sticksMissing)
            {
                try
                {
                    var left = new Vector2(Input.GetAxisRaw(PadAxes[0]), Input.GetAxisRaw(PadAxes[1]));
                    var right = new Vector2(Input.GetAxisRaw(PadAxes[2]), Input.GetAxisRaw(PadAxes[3]));
                    if (left.magnitude > 0.5f || right.magnitude > 0.5f) return true;
                }
                catch (Exception) { _sticksMissing = true; }
            }
            return false;
        }

        private static bool AnyPadButtonDown()
        {
            for (var k = KeyCode.JoystickButton0; k <= KeyCode.JoystickButton19; k++)
                if (Input.GetKeyDown(k)) return true;
            return false;
        }
    }
}
