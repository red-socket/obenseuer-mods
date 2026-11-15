using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace UnofficialPatch
{
    // S-101 restock log (spec §10). Storage.SpawnItems (SRC/Storage.cs:927-955) ignores AddItemToStorageSlot's result
    // (:1100-1128), so a shelf whose filter rejects its own restock stays empty without a trace. While SpawnItems runs,
    // a failed add is classified with the same filter check: a filter rejection is a Warn (once per shelf and item per
    // session), anything else Verbose. No behaviour change.
    internal static class RestockLog
    {
        internal static bool Patched;

        internal static int Depth;
        private static readonly HashSet<string> Logged = new HashSet<string>();

        internal static void Failed(Storage s, ItemReference itemReference, int amount, object[] meta)
        {
            if (!Patched || !Cfg.LogFailedRestock.Value || Depth <= 0 || amount == 0 || s == null) return;
            var item = itemReference?.Item;
            string where = $"\"{s.gameObject.name}\" ({s.GUID}) in {s.gameObject.scene.name}";
            if (item != null && !SlotController.CheckIfInAllowedCategoy(item, s.AllowedCategories, s.ForbiddenCategories, meta))
            {
                if (Logged.Add(s.GUID + "|" + item.ID))
                    Log.Warn($"Shelves: {where} could not restock {item.Title} ({item.ID}): the shelf's item filter refuses it.");
            }
            else
                Log.Verbose($"Shelves: {where} could not restock {(item != null ? item.Title + " (" + item.ID + ")" : "an unknown item")}.");
        }
    }

    // Storage.SpawnItems().
    [HarmonyPatch(typeof(Storage), nameof(Storage.SpawnItems))]
    internal static class SpawnItemsPatch
    {
        private static void Prefix() => RestockLog.Depth++;

        private static void Finalizer() => RestockLog.Depth--;
    }

    // Storage.AddItemToStorageSlot(ItemReference, int, int, object[], List<int>), private.
    [HarmonyPatch]
    internal static class AddItemToStorageSlotPatch
    {
        private static MethodBase TargetMethod() => Vanilla.AddItemToStorageSlot;

        private static void Postfix(Storage __instance, bool __result, ItemReference itemReference, int amount, object[] meta)
        {
            if (__result || RestockLog.Depth <= 0) return;
            try { RestockLog.Failed(__instance, itemReference, amount, meta); }
            catch (Exception e) { Log.Exception("Shelves: restock log", e); }
        }
    }
}
