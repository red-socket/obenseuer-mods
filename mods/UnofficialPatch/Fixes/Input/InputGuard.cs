using System;
using System.Reflection;
using HarmonyLib;

namespace UnofficialPatch
{
    // S-041 keyboard/mouse only (spec §5a; OFF by default). In 0.5.19 any joystick axis past 0.5 switches the game to
    // controller prompts and navigation (InventoryNavigationHandler.SwitchToControllerOnAxisInput, :988-1004), and
    // moving the mouse switches back, so a device that reports a stray axis makes prompts and the inventory selection
    // flicker (#5811). This option refuses InputManager.ChangeInputType(Controller) and covers the four places that
    // write InputManager.inputType directly (InventoryNavigationHandler.cs:1607/:1628, VirtualKeyboardUI.cs:437,
    // InteractableList.cs:94). Axis values are filtered by AxisFilter. Stateless.
    internal static class InputGuard
    {
        internal static bool Patched;

        internal static bool Live => Patched && Cfg.KeyboardMouseOnly.Value;

        private static MethodBase _change;
        internal static MethodBase ChangeTarget => _change ?? (_change = AccessTools.Method(typeof(InputManager), nameof(InputManager.ChangeInputType),
            new[] { typeof(InputManager.InputType) }));

        internal static void ForceKeyboard(InputManager im)
        {
            if (im != null && im.inputType == InputManager.InputType.Controller) im.ChangeInputType(InputManager.InputType.Keyboard);
        }
    }

    // InputManager.ChangeInputType(InputType). Only rejects Controller, so it applies next to other mods' patches.
    [HarmonyPatch]
    internal static class ChangeInputTypePatch
    {
        private static MethodBase TargetMethod() => InputGuard.ChangeTarget;

        private static bool Prefix(InputManager.InputType inputType)
        {
            try
            {
                if (!InputGuard.Live || inputType != InputManager.InputType.Controller) return true;
                Compat.Overlap(InputGuard.ChangeTarget, "keyboard/mouse only");
                return false;
            }
            catch (Exception e)
            {
                Log.Exception("Input Guard: ChangeInputType", e);
                return true;
            }
        }
    }

    // InputManager.LateUpdate(), private: the safety net for direct field writes.
    [HarmonyPatch]
    internal static class InputLateUpdatePatch
    {
        private static MethodBase TargetMethod() => Vanilla.InputLateUpdate;

        private static void Postfix(InputManager __instance)
        {
            if (!InputGuard.Live) return;
            try { InputGuard.ForceKeyboard(__instance); }
            catch (Exception e) { Log.Exception("Input Guard: LateUpdate", e); }
        }
    }

    // InventoryNavigationHandler.ChangeControlsInfo(InputType, int, CurrentInventoryWindow) writes the field directly.
    [HarmonyPatch(typeof(InventoryNavigationHandler), nameof(InventoryNavigationHandler.ChangeControlsInfo))]
    internal static class ChangeControlsInfoPatch
    {
        private static void Prefix(ref InputManager.InputType inputType)
        {
            try { if (InputGuard.Live) inputType = InputManager.InputType.Keyboard; }
            catch (Exception e) { Log.Exception("Input Guard: controls info", e); }
        }
    }

    // InventoryNavigationHandler.HandleItemInteraction(InputType), private; writes the field directly.
    [HarmonyPatch]
    internal static class HandleItemInteractionPatch
    {
        private static MethodBase TargetMethod() => Vanilla.HandleItemInteraction;

        private static void Prefix(ref InputManager.InputType inputType)
        {
            try { if (InputGuard.Live) inputType = InputManager.InputType.Keyboard; }
            catch (Exception e) { Log.Exception("Input Guard: controls info", e); }
        }
    }

    // VirtualKeyboardUI.ChangeControlsInfoIfNecessary(InputType), private; writes the field directly.
    [HarmonyPatch]
    internal static class VkChangeControlsInfoPatch
    {
        private static MethodBase TargetMethod() => Vanilla.VkChangeControlsInfo;

        private static void Prefix(ref InputManager.InputType inputType)
        {
            try { if (InputGuard.Live) inputType = InputManager.InputType.Keyboard; }
            catch (Exception e) { Log.Exception("Input Guard: controls info", e); }
        }
    }

    // InventoryNavigationHandler.RightTriggerPressed(), private: the trigger "use" shortcut in the inventory.
    [HarmonyPatch]
    internal static class RightTriggerPressedPatch
    {
        private static MethodBase TargetMethod() => Vanilla.RightTriggerPressed;

        private static bool Prefix(ref bool __result)
        {
            try
            {
                if (!InputGuard.Live) return true;
                __result = false;
                return false;
            }
            catch (Exception e)
            {
                Log.Exception("Input Guard: right trigger", e);
                return true;
            }
        }
    }

    // InteractableList.HandleMovementInput(), private; sets the field to Controller on any D-pad value (:92-95).
    [HarmonyPatch]
    internal static class ListMovementInputPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ListMovementInput;

        private static void Postfix()
        {
            if (!InputGuard.Live) return;
            try { InputGuard.ForceKeyboard(InputManager.instance); }
            catch (Exception e) { Log.Exception("Input Guard: InteractableList", e); }
        }
    }
}
