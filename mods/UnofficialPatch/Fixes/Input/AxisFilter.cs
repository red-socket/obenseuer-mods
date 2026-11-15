using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace UnofficialPatch
{
    // S-041 axis filter (spec §5b; patched when KeyboardMouseOnly or AxisDeadzone is on). Input.GetAxis is an icall,
    // so it is never patched; instead, calls to Input.GetAxis/GetAxisRaw inside the listed methods are swapped for
    // Get/GetRaw below. Only joystick-named axes are filtered; every other name (Horizontal/Vertical, which mix arrow
    // keys with the stick, Mouse X/Y, InventoryHorizontal/Vertical, ...) passes through unchanged. In
    // SwitchToControllerOnAxisInput the single 0.5 switch threshold becomes SwitchThreshold(). Each method is patched
    // on its own; a method that is missing or does not match keeps vanilla input and logs one warning.
    // Menu spots get both KeyboardMouseOnly (axis -> 0) and AxisDeadzone; the gameplay spots (ladder climbing, the
    // quick-slot bar, the card game cursor) get only KeyboardMouseOnly, so the deadzone never changes gameplay.
    internal static class AxisFilter
    {
        internal static bool Patched;

        private static readonly MethodInfo GetAxisM = AccessTools.Method(typeof(Input), nameof(Input.GetAxis), new[] { typeof(string) });
        private static readonly MethodInfo GetAxisRawM = AccessTools.Method(typeof(Input), nameof(Input.GetAxisRaw), new[] { typeof(string) });
        private static readonly MethodInfo GetM = AccessTools.Method(typeof(AxisFilter), nameof(Get));
        private static readonly MethodInfo GetRawM = AccessTools.Method(typeof(AxisFilter), nameof(GetRaw));
        private static readonly MethodInfo GetKbmM = AccessTools.Method(typeof(AxisFilter), nameof(GetKbmOnly));
        private static readonly MethodInfo GetRawKbmM = AccessTools.Method(typeof(AxisFilter), nameof(GetRawKbmOnly));
        private static readonly MethodInfo ThresholdM = AccessTools.Method(typeof(AxisFilter), nameof(SwitchThreshold));

        private const string SwitchMethodName = "SwitchToControllerOnAxisInput";
        private static bool _matched;

        // Spec list, plus [new] the other InventoryNavigationHandler/UINavigation methods that read joystick axes
        // (zoom slider, map drag, scrolling, blueprint movement, trigger zoom, stats label, controller detection) and
        // InteractableList.HandleVerticalInput (deviation recorded in .internal/design.md).
        private static readonly (Type type, string name, Type[] args)[] Targets =
        {
            (typeof(InventoryNavigationHandler), SwitchMethodName, Type.EmptyTypes),
            (typeof(InventoryNavigationHandler), "TrySelectingNearbySlot", Type.EmptyTypes),
            (typeof(InventoryNavigationHandler), "HandleHorizontalInput", Type.EmptyTypes),
            (typeof(InventoryNavigationHandler), "HandleVerticalInput", Type.EmptyTypes),
            (typeof(InventoryNavigationHandler), "HandleNonMovementInput", Type.EmptyTypes),
            (typeof(InventoryNavigationHandler), "HasRightStickInput", Type.EmptyTypes),
            (typeof(InventoryNavigationHandler), "HasHorizontalInput", Type.EmptyTypes),
            (typeof(InventoryNavigationHandler), "HasVerticalInput", Type.EmptyTypes),
            (typeof(InventoryNavigationHandler), "TryScrolling", Type.EmptyTypes),
            (typeof(InventoryNavigationHandler), "HandleZoomingWithTriggerButtons", Type.EmptyTypes),
            (typeof(InventoryNavigationHandler), "TryAdjustingZoomSlider", new[] { typeof(bool) }),
            (typeof(InventoryNavigationHandler), "HandleMapDragging", Type.EmptyTypes),
            (typeof(InventoryNavigationHandler), "HandleTenementBlueprintMovement", Type.EmptyTypes),
            (typeof(InventoryNavigationHandler), "RightTriggerPressed", Type.EmptyTypes),
            (typeof(InventoryNavigationHandler), "Update", Type.EmptyTypes),
            (typeof(InputManager), "GetHorizontalInput", Type.EmptyTypes),
            (typeof(InputManager), "GetVerticalInput", Type.EmptyTypes),
            (typeof(InputManager), "GetHorizontalInputRightStick", Type.EmptyTypes),
            (typeof(InputManager), "GetVerticalInputRightStick", Type.EmptyTypes),
            (typeof(UINavigationCore), "HasHorizontalInput", Type.EmptyTypes),
            (typeof(UINavigationCore), "HasVerticalInput", Type.EmptyTypes),
            (typeof(UINavigationBase), "HandleHorizontalInput", null),
            (typeof(UINavigationBase), "HandleVerticalInput", null),
            (typeof(UINavigationBase), "ControllerInputDetected", Type.EmptyTypes),
            (typeof(InteractableList), "HandleMovementInput", Type.EmptyTypes),
            (typeof(InteractableList), "HandleVerticalInput", Type.EmptyTypes),
            // Menu screens that read DPadHorizontal, LeftStickInventoryHorizontal or RightTrigger directly: the sleep
            // and wait time picker, the rotary lock, option sliders and +/- buttons, the virtual keyboard's shift.
            (typeof(WaitingUI), "Update", Type.EmptyTypes),
            (typeof(RotaryLockUI), "Controls", Type.EmptyTypes),
            (typeof(UINavigationHelperBase), "PressPlusOrMinusButton", new[] { typeof(UnityEngine.Object), typeof(SliderMovementDirection) }),
            (typeof(UINavigationHelperBase), "MoveSlider", new[] { typeof(UnityEngine.Object), typeof(SliderMovementDirection) }),
            (typeof(VirtualKeyboardUI), "HandleNonMovementInput", Type.EmptyTypes),
            // Gameplay spots: KeyboardMouseOnly only (see IsGameplay).
            (typeof(InteractableLadder), "FixedUpdate", Type.EmptyTypes),
            (typeof(CardInputHandler), "GetControllerDirection", Type.EmptyTypes),
            (typeof(QuickSlotBar), "GetMoveStep", Type.EmptyTypes),
        };

        // Decided from the method itself, because Harmony re-runs this transpiler whenever another mod patches the
        // same method later.
        private static bool IsGameplay(MethodBase m)
        {
            var t = m?.DeclaringType;
            return t == typeof(InteractableLadder) || t == typeof(CardInputHandler) || t == typeof(QuickSlotBar);
        }

        private static bool KbmOnly => InputGuard.Live || (Patched && Cfg.KeyboardMouseOnly.Value);
        private static bool Deadzone => Patched && Cfg.AxisDeadzone.Value;

        // Called from game code in place of Input.GetAxis / GetAxisRaw. Allocation-free.
        public static float Get(string axisName) => Filter(axisName, Input.GetAxis(axisName));
        public static float GetRaw(string axisName) => Filter(axisName, Input.GetAxisRaw(axisName));

        // Gameplay spots: KeyboardMouseOnly only, never the deadzone.
        public static float GetKbmOnly(string axisName) => FilterKbm(axisName, Input.GetAxis(axisName));
        public static float GetRawKbmOnly(string axisName) => FilterKbm(axisName, Input.GetAxisRaw(axisName));

        // Replaces the 0.5 constant in SwitchToControllerOnAxisInput.
        public static float SwitchThreshold() => Deadzone ? Cfg.ControllerSwitchThreshold.Value : 0.5f;

        private static float Filter(string name, float v)
        {
            if (v == 0f || !IsJoystickAxis(name)) return v;
            if (KbmOnly) return 0f;
            if (Deadzone && Math.Abs(v) < Cfg.NavigationDeadzone.Value) return 0f;
            return v;
        }

        private static float FilterKbm(string name, float v) => v != 0f && KbmOnly && IsJoystickAxis(name) ? 0f : v;

        // Joystick axes from ProjectSettings/InputManager.asset (type 2, joyNum 0 = any joystick).
        private static bool IsJoystickAxis(string name)
        {
            switch (name)
            {
                case "LeftStickInventoryHorizontal":
                case "LeftStickInventoryVertical":
                case "RightStickHorizontal":
                case "RightStickVertical":
                case "DPadHorizontal":
                case "DPadVertical":
                case "LeftTrigger":
                case "RightTrigger":
                case "JoystickAxis1":
                case "JoystickAxis2":
                case "JoystickAxis3":
                case "JoystickAxis4":
                case "JoystickAxis5":
                case "JoystickAxis6":
                case "JoystickAxis7":
                    return true;
                default:
                    return false;
            }
        }

        // Patches every target; returns "n/m spots" for the startup line, or null when nothing could be patched.
        internal static string PatchAll(Harmony harmony)
        {
            if (GetAxisM == null || GetAxisRawM == null || GetM == null || GetRawM == null || GetKbmM == null || GetRawKbmM == null || ThresholdM == null)
            {
                Log.Error("Input Guard: Input.GetAxis or the filter methods were not found; the axis filter is off.");
                return null;
            }
            Patched = true;
            var transpiler = new HarmonyMethod(AccessTools.Method(typeof(AxisFilter), nameof(Transpiler)));
            int ok = 0;
            foreach (var (type, name, args) in Targets)
            {
                string label = type.Name + "." + name;
                try
                {
                    var m = args == null ? AccessTools.DeclaredMethod(type, name) : AccessTools.DeclaredMethod(type, name, args);
                    if (m == null)
                    {
                        Log.Warn($"Input Guard: {label} not found; that spot keeps vanilla input.");
                        continue;
                    }
                    _matched = false;
                    harmony.Patch(m, transpiler: transpiler);
                    if (_matched) ok++;
                    else Log.Warn($"Input Guard: {label} did not match; that spot keeps vanilla input.");
                }
                catch (Exception e)
                {
                    Log.Warn($"Input Guard: patching {label} failed ({e.Message}); that spot keeps vanilla input.");
                }
            }
            if (ok == 0)
            {
                Patched = false;
                return null;
            }
            return $"{ok}/{Targets.Length} spots";
        }

        // Swaps the axis calls. On no match (or a 0.5 constant count other than 1 in the switch method) the original
        // instructions are returned unchanged.
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var src = instructions.ToList();
            var outList = new List<CodeInstruction>(src.Count);
            int swaps = 0, thresholds = 0;
            bool isSwitch = original != null && original.Name == SwitchMethodName && original.DeclaringType == typeof(InventoryNavigationHandler);
            bool gameplay = IsGameplay(original);
            foreach (var ins in src)
            {
                var c = new CodeInstruction(ins);
                if (c.opcode == OpCodes.Call && c.operand is MethodInfo mi)
                {
                    if (mi == GetAxisM) { c.operand = gameplay ? GetKbmM : GetM; swaps++; }
                    else if (mi == GetAxisRawM) { c.operand = gameplay ? GetRawKbmM : GetRawM; swaps++; }
                }
                else if (isSwitch && c.opcode == OpCodes.Ldc_R4 && c.operand is float f && f == 0.5f)
                {
                    c.opcode = OpCodes.Call;
                    c.operand = ThresholdM;
                    thresholds++;
                }
                outList.Add(c);
            }
            if (swaps == 0 || (isSwitch && thresholds != 1))
            {
                _matched = false;
                return src;
            }
            _matched = true;
            return outList;
        }
    }
}
