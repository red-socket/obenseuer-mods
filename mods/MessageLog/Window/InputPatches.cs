using System;
using System.Reflection;
using HarmonyLib;

namespace MessageLog
{
    // T1-T5: the five InputManager key queries (SRC/InputManager.cs:202-306) answer false while the search field has the
    // keyboard (SearchGuard.Blocking), bool prefixes at Priority.First that only ever skip. Recipe Book's TypingGuard and
    // Unofficial Patch's ConsoleGuard prefix the same methods and also only answer false, so their order doesn't matter.
    // GetKeyUp is not guarded: held actions must still release.
    [HarmonyPatch]
    internal static class GuardGetKeyDown
    {
        private static MethodBase TargetMethod() => Vanilla.GetKeyDown;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result)
        {
            try
            {
                if (!SearchGuard.Blocking) return true;
                __result = false;
                return false;
            }
            catch (Exception e) { SearchGuard.Fail("Typing: GetKeyDown", e); return true; }
        }
    }

    [HarmonyPatch]
    internal static class GuardGetAuxiliaryReloadDown
    {
        private static MethodBase TargetMethod() => Vanilla.GetAuxiliaryReloadDown;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result)
        {
            try
            {
                if (!SearchGuard.Blocking) return true;
                __result = false;
                return false;
            }
            catch (Exception e) { SearchGuard.Fail("Typing: GetAuxiliaryReloadDown", e); return true; }
        }
    }

    [HarmonyPatch]
    internal static class GuardGetKeyDownKeyboardOnly
    {
        private static MethodBase TargetMethod() => Vanilla.GetKeyDownKeyboardOnly;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result)
        {
            try
            {
                if (!SearchGuard.Blocking) return true;
                __result = false;
                return false;
            }
            catch (Exception e) { SearchGuard.Fail("Typing: GetKeyDownKeyboardOnly", e); return true; }
        }
    }

    [HarmonyPatch]
    internal static class GuardGetKey
    {
        private static MethodBase TargetMethod() => Vanilla.GetKey;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result)
        {
            try
            {
                if (!SearchGuard.Blocking) return true;
                __result = false;
                return false;
            }
            catch (Exception e) { SearchGuard.Fail("Typing: GetKey", e); return true; }
        }
    }

    [HarmonyPatch]
    internal static class GuardGetKeyWithHold
    {
        private static MethodBase TargetMethod() => Vanilla.GetKeyWithHold;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result)
        {
            try
            {
                if (!SearchGuard.Blocking) return true;
                __result = false;
                return false;
            }
            catch (Exception e) { SearchGuard.Fail("Typing: GetKeyWithHold", e); return true; }
        }
    }
}
