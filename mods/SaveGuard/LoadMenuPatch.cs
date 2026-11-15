using System;
using HarmonyLib;

namespace SaveGuard
{
    // P3: LoadMenu.LoadGameSlot() (SRC/LoadMenu.cs:516). Stops a load of a structurally broken slot before any fade
    // starts and offers Restore / Load anyway / Cancel (§6.3). This is Save Guard's only `return false`.
    [HarmonyPatch(typeof(LoadMenu), "LoadGameSlot")]
    internal static class LoadMenuPatch
    {
        internal static bool BypassOnce;

        private static bool Prefix(LoadMenu __instance)
        {
            try
            {
                if (!Cfg.BlockLoadingDamagedSaves.Value || BypassOnce) { BypassOnce = false; return true; }
                if (SaveController.Loading) return true;
                // The second call after vanilla's version warning: the slot was checked on the first one.
                var warning = Vanilla.GetVersionWarningPanel(__instance);
                if (warning != null && warning.activeInHierarchy) return true;
                // Vanilla shows its folder-name mismatch panel and does not load; let it.
                if (!string.IsNullOrEmpty(Vanilla.GetFolderNameMismatchText(__instance))) return true;

                var info = Vanilla.GetCurrentSaveInfo(__instance);
                int n = Vanilla.GetCurrentCharacterNumber(__instance);
                if (info == null || string.IsNullOrEmpty(info.SaveName) || __instance.Characters == null || n < 0 || n >= __instance.Characters.Count) return true;
                // Exactly the folder LoadCoroutine (:551) will load.
                string slotDir = __instance.Characters[n].FolderName + "/" + info.SaveName;
                var r = SlotCheck.Structural(slotDir);
                if (r.Status == SlotStatus.Ok) r = SlotCheck.FullInfoGlobals(slotDir);
                if (r.Status == SlotStatus.Ok || r.Status == SlotStatus.Empty) return true;

                Log.Warn($"Load of {slotDir} stopped: {r}");
                GuardWindow.ShowDamagedPrompt(__instance, slotDir, r);
                return false;
            }
            catch (Exception e)
            {
                Log.Exception("LoadGameSlot prefix", e);
                return true;
            }
        }
    }
}
