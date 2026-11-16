using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using OS.Items;

namespace TidyInventory
{
    // S-062 stolen stacks (spec §5.1). ItemMetaUtilities.CompareItemMetas refuses to merge a stack marked stolen
    // (ItemStolenData { playerStolen = true }) with an unmarked or differently marked one of the same owner
    // (SRC/ItemMetaUtilities.cs:429-447, and its trailing "return flag"), so stolen goods of one owner split into stacks
    // that never join. When the existing stack (meta1) is marked and neither side carries an owner permission, both
    // arrays are compared without the stolen mark: every other mismatch still refuses (each returns false at once,
    // :201-240), the owner check stays with the caller (SRC/SlotController.cs:803), and the target keeps its marked meta,
    // so the merged stack stays stolen (Fair Justice's invariant). The permission guard matters: the permission check is
    // one-way (:320-335), and without it a permitted stack could merge into a stolen one and become stolen.
    // Only when the target stack is in the player's own slots (main, character, worn backpack): an add there still runs
    // CheckForAddedStolenItems on the unmarked incoming meta (noise, witness roll, crime). A merge inside a storage would
    // hand the owner's unmarked goods the mark, and taking a marked stack skips that check (SRC/Inventory.cs:450-454),
    // so storage merges, storage-to-storage moves and storage Sort stay vanilla.
    // Callers' arrays are never changed; we pass new ones.
    internal static class StolenMerge
    {
        internal static bool Patched;

        // The target of the merge check running now, set by P29a around the static SlotHasSpaceForItem (main thread
        // only). Unset outside it, so ForceItemToSlot and every other path stay vanilla.
        internal struct Target
        {
            internal SlotController Controller;
            internal ItemStack Stack;
            internal bool Set;
        }

        internal static Target Current;

        internal static bool ShouldStrip(object[] meta1, object[] meta2)
        {
            bool marked = false;
            foreach (var m in meta1)
            {
                if (m is ItemStolenData s && s.playerStolen) marked = true;
                else if (m is ItemOwnerPermission) return false;
            }
            if (!marked) return false;
            if (meta2 != null)
                foreach (var m in meta2)
                    if (m is ItemOwnerPermission) return false;
            return true;
        }

        internal static object[] Strip(object[] meta) => meta?.Where(m => !(m is ItemStolenData)).ToArray();

        // The current target stack sits in a main, character or worn backpack slot. Only asked once ShouldStrip agreed.
        internal static bool TargetIsPlayers()
        {
            var t = Current;
            if (!t.Set) return false;
            var inv = Inventory.instance;
            if (inv == null) return false;
            SlotController[] pack = null;
            try { pack = BackpackStorage.instance != null ? BackpackStorage.instance.GetStorage()?.Slots : null; }
            catch (Exception) { pack = null; }
            return In(inv.Slots, t) || In(inv.CharacterSlots, t) || In(pack, t);
        }

        private static bool In(SlotController[] slots, Target t)
        {
            if (slots == null) return false;
            foreach (var sc in slots)
            {
                if (sc == null) continue;
                if (t.Controller != null ? sc == t.Controller : ReferenceEquals(sc.itemStack, t.Stack)) return true;
            }
            return false;
        }
    }

    // P29a: the static SlotController.SlotHasSpaceForItem(Item, SlotController, ItemStack, out int, int, int, int, object[],
    // bool, bool, ItemOperationSettings) (SRC/SlotController.cs:796). Records the target only; never changes a result.
    // ItemStack.AddItemToPanel passes a null controller (SRC/ItemStack.cs:229), so the stack itself is kept too.
    [HarmonyPatch]
    internal static class SlotSpaceTargetPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SlotHasSpaceForItemStatic;

        private static void Prefix(SlotController controller, ItemStack itemStack)
        {
            StolenMerge.Current = new StolenMerge.Target { Controller = controller, Stack = itemStack, Set = true };
        }

        // The method does not nest (MetaMergeAllowed and CompareItemMetas never call back into it), so clearing is enough.
        private static Exception Finalizer(Exception __exception)
        {
            StolenMerge.Current = default;
            return __exception;
        }
    }

    // P29: ItemMetaUtilities.CompareItemMetas(object[], object[], out MetaAction, int, bool, bool) (SRC :174). A void
    // prefix: it only replaces the two local arrays. Liquids (LiquidStorage) are left alone.
    [HarmonyPatch]
    internal static class CompareItemMetasPatch
    {
        private static MethodBase TargetMethod() => Vanilla.CompareItemMetas;

        private static void Prefix(ref object[] meta1, ref object[] meta2, int owner, bool liquids)
        {
            if (owner <= 1 || liquids || meta1 == null || !StolenMerge.Current.Set) return;   // hot path: four compares
            try
            {
                if (!Cfg.StolenStacks.MergeSameOwner.Value || !StolenMerge.ShouldStrip(meta1, meta2) || !StolenMerge.TargetIsPlayers()) return;
                var a = StolenMerge.Strip(meta1);
                var b = StolenMerge.Strip(meta2);
                meta1 = a;
                meta2 = b;
            }
            catch (Exception e) { Log.Exception("stolen stacks", e); }
        }
    }
}
