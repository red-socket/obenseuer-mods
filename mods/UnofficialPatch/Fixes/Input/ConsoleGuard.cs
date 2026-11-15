using System;
using System.Reflection;
using HarmonyLib;

namespace UnofficialPatch
{
    // S-068 (spec §6). The F1 console only disables player controls (DeveloperConsole.ToggleDevConsole, :394-406), and
    // InputManager.GetKeyDown & co. (InputManager.cs:202-306) never check it, so typed letters open menus, use items and
    // so on (#5942). While the console is open these prefixes answer false. Exceptions:
    // - DeveloperConsole.ShouldToggleDevConsole (:356) reads "Pause Menu" to close the console with Esc;
    // - VirtualKeyboardUI.Update types into the console with a controller when useDevConsole is set.
    // GetKeyUp is deliberately not blocked: its callers release hold-sprint, hold-crouch and similar
    // (OpenSewerCharacterController.cs:381-385, :402-405, :421-432), so blocking it would leave the player stuck.
    // Vanilla clears _consoleOpened one frame after Esc (Closelate), so that Esc never reaches other systems either.
    // Raw Input reads (DialogueController and ShopController Return, InteractableList X/C, ...) are not covered,
    // except the pause menu (GuardPauseMenu), mirroring MainMenuNavigationHandler.cs:95. Stateless.
    internal static class ConsoleGuard
    {
        internal static bool Patched, PausePatched;

        private static bool _allow;
        private static bool _lastOpen;

        internal static bool ConsoleOpen
        {
            get
            {
                var c = DeveloperConsole.Instance;
                return !ReferenceEquals(c, null) && c.consoleOpened;
            }
        }

        // Hot path (every key query): static reads only, no allocation.
        internal static bool Blocking
        {
            get
            {
                if (!Patched || _allow || !Cfg.BlockGameKeysWhileOpen.Value) return false;
                bool open = ConsoleOpen;
                if (open != _lastOpen)
                {
                    _lastOpen = open;
                    if (Log.VerboseOn) Log.Verbose(open ? "Console: open, game keys blocked." : "Console: closed, game keys back.");
                }
                return open;
            }
        }

        internal static void Allow(bool allow) => _allow = allow;

        // The five guarded InputManager methods, by index (see the patch classes below). Other plugins may patch them
        // after our Awake, so each one's overlap is checked once, at its first blocked call; after that the prefix
        // only reads statics.
        internal const int Down = 0, Key = 1, WithHold = 2, DownKeyboardOnly = 3, AuxReload = 4;
        private static readonly bool[] OverlapChecked = new bool[5];

        private static MethodBase TargetOf(int i)
        {
            switch (i)
            {
                case Down: return AccessTools.Method(typeof(InputManager), nameof(InputManager.GetKeyDown), new[] { typeof(string) });
                case Key: return AccessTools.Method(typeof(InputManager), nameof(InputManager.GetKey), new[] { typeof(string) });
                case WithHold: return AccessTools.Method(typeof(InputManager), nameof(InputManager.GetKeyWithHold), new[] { typeof(string), typeof(int) });
                case DownKeyboardOnly: return AccessTools.Method(typeof(InputManager), nameof(InputManager.GetKeyDownKeyboardOnly), new[] { typeof(string) });
                default: return AccessTools.Method(typeof(InputManager), nameof(InputManager.GetAuxiliaryReloadDown));
            }
        }

        internal static bool Prefix(int target, ref bool result)
        {
            if (!Blocking) return true;
            if (!OverlapChecked[target])
            {
                OverlapChecked[target] = true;
                Compat.Overlap(TargetOf(target), "the console key guard");
            }
            result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetKeyDown), new[] { typeof(string) })]
    internal static class GetKeyDownPatch
    {
        private static bool Prefix(ref bool __result)
        {
            try { return ConsoleGuard.Prefix(ConsoleGuard.Down, ref __result); }
            catch (Exception e) { Log.Exception("Console: GetKeyDown", e); return true; }
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetKey), new[] { typeof(string) })]
    internal static class GetKeyPatch
    {
        private static bool Prefix(ref bool __result)
        {
            try { return ConsoleGuard.Prefix(ConsoleGuard.Key, ref __result); }
            catch (Exception e) { Log.Exception("Console: GetKey", e); return true; }
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetKeyWithHold), new[] { typeof(string), typeof(int) })]
    internal static class GetKeyWithHoldPatch
    {
        private static bool Prefix(ref bool __result)
        {
            try { return ConsoleGuard.Prefix(ConsoleGuard.WithHold, ref __result); }
            catch (Exception e) { Log.Exception("Console: GetKeyWithHold", e); return true; }
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetKeyDownKeyboardOnly), new[] { typeof(string) })]
    internal static class GetKeyDownKeyboardOnlyPatch
    {
        private static bool Prefix(ref bool __result)
        {
            try { return ConsoleGuard.Prefix(ConsoleGuard.DownKeyboardOnly, ref __result); }
            catch (Exception e) { Log.Exception("Console: GetKeyDownKeyboardOnly", e); return true; }
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.GetAuxiliaryReloadDown))]
    internal static class GetAuxiliaryReloadDownPatch
    {
        private static bool Prefix(ref bool __result)
        {
            try { return ConsoleGuard.Prefix(ConsoleGuard.AuxReload, ref __result); }
            catch (Exception e) { Log.Exception("Console: GetAuxiliaryReloadDown", e); return true; }
        }
    }

    // DeveloperConsole.ShouldToggleDevConsole(), private: the console's own Esc check sees "Pause Menu".
    [HarmonyPatch]
    internal static class ShouldToggleDevConsolePatch
    {
        private static MethodBase TargetMethod() => Vanilla.ShouldToggleDevConsole;

        private static void Prefix() => ConsoleGuard.Allow(true);

        private static void Finalizer() => ConsoleGuard.Allow(false);
    }

    // VirtualKeyboardUI.Update(), private: controller typing into the console keeps working.
    [HarmonyPatch]
    internal static class VirtualKeyboardUpdatePatch
    {
        private static MethodBase TargetMethod() => Vanilla.VkUpdate;

        private static void Prefix(VirtualKeyboardUI __instance)
        {
            try { ConsoleGuard.Allow(__instance != null && Vanilla.UseDevConsole(__instance)); }
            catch (Exception e) { Log.Exception("Console: virtual keyboard", e); }
        }

        private static void Finalizer() => ConsoleGuard.Allow(false);
    }

    // PauseNavigationHandler.Update(), private: raw X/Return would click pause menu buttons while typing.
    [HarmonyPatch]
    internal static class PauseNavigationUpdatePatch
    {
        private static MethodBase TargetMethod() => Vanilla.PauseNavUpdate;

        private static bool Prefix()
        {
            try { return !(ConsoleGuard.PausePatched && Cfg.GuardPauseMenu.Value && ConsoleGuard.ConsoleOpen); }
            catch (Exception e) { Log.Exception("Console: pause menu", e); return true; }
        }
    }
}
