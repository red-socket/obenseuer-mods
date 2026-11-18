using System;
using System.Reflection;
using HarmonyLib;

namespace GridSense
{
    // P2 (spec §4.5, D3): BuildingSystemMenu.ChangeSelection(int, int) (SRC/BuildingSystemMenu.cs:472-565), private. For a
    // Place slot vanilla sets currentSelectionName from SlotName() and copies it to currentSelectionText (:537-552), calls
    // ChangeCurrentObject (:555), then sets currentVisibleSlot = newSlot (:561). The suffix goes on the backing string too,
    // because the per-frame UpdateCurrentSelectionText (:777-790) rebuilds planter text from it ("Flowerbox With Lamp -
    // 50 W (Sun)"). The name is reassigned on every Place selection, so the suffix never stacks; the EndsWith check covers
    // the empty-slots early return (:495-502). Take, Move and Category keep vanilla's literals (compared at :519, :527);
    // item previews and ShowMovingItem are untouched. currentSelectionName is a UI string and is never saved.
    //
    // Alt-prefab cycling (BuildingSystem.ChangeAlternativePrefab, SRC/BuildingSystem.cs:1438) does not come through here and
    // does not touch the text; every alt pair has the same watts in 0.5.19 (test plan T0b).
    [HarmonyPatch]
    internal static class BuildBarPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ChangeSelection;

        private static void Postfix(BuildingSystemMenu __instance, int prefabAltIndex)
        {
            if (!Cfg.Hot.BuildBar) return;
            try
            {
                var slots = Vanilla.Slots(__instance);
                int i = Vanilla.CurrentVisibleSlot(__instance);
                if (slots == null || i < 0 || i >= slots.Count) return;
                var s = slots[i];
                if (s == null || s.slotType != BuildingSystemSlot.SlotType.Place) return;
                if (s.itemStack != null && s.itemStack.itemId != -1) return;   // item previews
                var f = s.buildObject?.furniture;
                if (f == null) return;
                var info = PowerInfo.Get(f, prefabAltIndex);
                if (info.IsEmpty) return;
                string suffix = " - " + info.ShortText;
                ref string name = ref Vanilla.CurrentSelectionName(__instance);
                if (name == null || name.EndsWith(suffix, StringComparison.Ordinal)) return;
                name += suffix;
                var tmp = Vanilla.CurrentSelectionText(__instance);
                if (tmp != null) tmp.text = name;
            }
            catch (Exception e) { Log.Exception("Build: suffix", e); }
        }
    }
}
