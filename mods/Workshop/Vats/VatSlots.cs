using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Workshop
{
    // S-086 Bigger Vats (spec §6.2, deviation D5). The vessels are StorageSettings "Custom" with customStorageSlotAmount 9,
    // so Storage.Start calls SetSlotAmount(customStorageSlotAmount) (SRC/Storage.cs:432-439); SetCategorySettings never
    // runs for them. A load calls Start first and then pads the saved slots to that count with ResizeLoadedSlots
    // (:326-394), which only ever removes empty slots, from the end. So raising the count before Start is all it takes,
    // and lowering it later never loses an item (D6).
    internal static class VatSlots
    {
        internal static bool Patched;

        // The authored count of every storage we enlarged (VatAudit compares against it).
        private static readonly ConditionalWeakTable<Storage, StrongBox<int>> Authored = new ConditionalWeakTable<Storage, StrongBox<int>>();

        internal static int AuthoredOf(Storage s)
        {
            if (s != null && Authored.TryGetValue(s, out var box)) return box.Value;
            int custom = s != null && Vanilla.CustomSlotAmount != null ? Vanilla.CustomSlotAmount(s) : 0;
            return custom > 0 ? custom : VatRules.Vanilla9;
        }

        internal static void BeforeStart(Storage s)
        {
            if (!Cfg.BiggerVats.Enabled.Value || s == null || Vanilla.StorageStartDone(s)) return;
            if (s.StorageSettings != "Custom" && !string.IsNullOrEmpty(s.StorageSettings)) return;
            var p = s.GetComponent<manufacturingProcess>();
            if (p == null || (p.OutputStorage != null && p.OutputStorage == s)) return;
            int want = VatRules.SlotsFor(p, out string rule);
            if (want <= VatRules.Vanilla9) return;
            int authored = Vanilla.CustomSlotAmount(s);
            if (authored <= 0) authored = s.storageSlots != null ? s.storageSlots.Length : 0;
            int slots = Math.Max(authored, want);
            if (slots == authored) return;
            Vanilla.CustomSlotAmount(s) = slots;
            Authored.Remove(s);
            Authored.Add(s, new StrongBox<int>(authored));
            Log.Info($"Vats: \"{Stations.Name(p)}\" ({s.gameObject.scene.name}, {rule}): {authored} -> {slots} slots.");
        }
    }

    // W14: Storage.Start() prefix (SRC/Storage.cs:418).
    [HarmonyPatch]
    internal static class StorageStartPatch
    {
        private static MethodBase TargetMethod() => Vanilla.StorageStart;

        private static void Prefix(Storage __instance)
        {
            try { VatSlots.BeforeStart(__instance); }
            catch (Exception e) { Log.Exception("bigger vats", e); }
        }
    }
}
