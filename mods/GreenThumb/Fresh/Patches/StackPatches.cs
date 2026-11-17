using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OS.Items;

namespace GreenThumb
{
    // B1: the static SlotController.SlotHasSpaceForItem(Item, SlotController, ItemStack, out int, int, int, int, object[],
    // bool, bool, ItemOperationSettings) (SRC/SlotController.cs:798). Every merge check goes through it: ItemStack.
    // AddItemToPanel (:229 of ItemStack.cs, right before the merge) and every capacity check (InventoryIsNotFull,
    // Growing.HasSpaceFor, Fewer Clicks' Top up, Workshop's Fetch planner, Tidy Inventory's pins), so "inventory full"
    // agrees with the real merge. Records the target stack only; never changes a result. TidyInventory's P29a records
    // its own context on the same method; First/Last keeps ours around everything.
    [HarmonyPatch]
    internal static class SlotSpacePatch
    {
        private static MethodBase TargetMethod() => Vanilla.SlotHasSpaceForItemStatic;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(Item item, ItemStack itemStack, object[] meta)
        {
            try
            {
                bool eligible = Cfg.Hot.Bands && item != null && itemStack != null && itemStack.itemId == item.ID
                    && Eligible.Item(item) && Eligible.PlainMeta(meta) && Eligible.PlainMeta(itemStack.Meta);
                MergeCtx.Current = new MergeCtx.Ctx { Set = true, Stack = itemStack, Eligible = eligible };
            }
            catch (Exception e)
            {
                MergeCtx.Current = default;
                Log.Exception("Stacks: merge check", e);
            }
        }

        // The method does not nest (MetaMergeAllowed and CompareItemMetas never call back into it), so clearing is enough.
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(Exception __exception)
        {
            MergeCtx.Current = default;
            return __exception;
        }
    }

    // B2: ItemMetaUtilities.CompareItemMetas(object[], object[], out MetaAction, int, bool, bool) (SRC :174) transpiler.
    // The perishable test (:211) compiles to
    //   ldfld integrity; ldloc(.s) X; ldfld integrity; sub; call Mathf.Abs(float32); ldc.r4 0.025; bgt.un.s
    // with meta1's perishable already on the stack (IL_0075-IL_008c in 0.5.19). The first five become
    //   ldloc X; call Bands.Distance(ItemPerishabledata, ItemPerishabledata)
    // and the 0.025 compare stays. Matched by opcode and operand, not by local index. Anything but exactly one match
    // throws, so the whole group is rolled back and the game keeps its own tolerance.
    [HarmonyPatch]
    internal static class CompareMetasPatch
    {
        private static readonly FieldInfo Integrity = AccessTools.Field(typeof(ItemPerishabledata), nameof(ItemPerishabledata.integrity));
        private static readonly MethodInfo Abs = AccessTools.Method(typeof(UnityEngine.Mathf), nameof(UnityEngine.Mathf.Abs), new[] { typeof(float) });
        private static readonly MethodInfo Distance = AccessTools.Method(typeof(Bands), nameof(Bands.Distance));

        private static MethodBase TargetMethod() => Vanilla.CompareItemMetas;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int match = -1, count = 0;
            for (int i = 0; i + 5 < code.Count; i++)
            {
                if (!code[i].LoadsField(Integrity) || !code[i + 1].IsLdloc() || !code[i + 2].LoadsField(Integrity)) continue;
                if (code[i + 3].opcode != OpCodes.Sub || !code[i + 4].Calls(Abs)) continue;
                if (code[i + 5].opcode != OpCodes.Ldc_R4 || !(code[i + 5].operand is float f) || f != 0.025f) continue;
                match = i;
                count++;
            }
            if (count != 1 || Integrity == null || Abs == null || Distance == null)
                throw new InvalidOperationException($"Stacks: CompareItemMetas pattern found {count} times; freshness bands off this session");
            var first = code[match];
            var load = new CodeInstruction(code[match + 1].opcode, code[match + 1].operand) { labels = first.labels, blocks = first.blocks };
            var call = new CodeInstruction(OpCodes.Call, Distance);
            code.RemoveRange(match, 5);
            code.InsertRange(match, new[] { load, call });
            Log.Info("Stacks: freshness bands applied to CompareItemMetas (1 match).");
            return code;
        }
    }

    // B3: ItemStack.MixStackQuality(object[] addedMeta, int addedAmount) prefix (private, SRC/ItemStack.cs:262; called
    // at :252/:257 right before itemAmount changes). The target's unpaid decay is settled first, exactly as drawing its
    // slot would (vanilla RemoveMinutesFromIntegrity, which also carries UnofficialPatch's prefix), then the two are
    // mixed by amount with vanilla PerishableMix. Amounts, remaining and the stack cap are never touched, so a merge never
    // changes the total amount, and the result always lies between the two inputs (inside their band). The incoming value
    // is used as stored: UI moves come from slots just drawn, harvest metas have lastChecked = 0, and Workshop's Fetch
    // settles its source first. This also replaces vanilla's "the target keeps its value" for the merges vanilla allowed.
    [HarmonyPatch]
    internal static class MixQualityPatch
    {
        private static MethodBase TargetMethod() => Vanilla.MixStackQuality;

        private static void Prefix(ItemStack __instance, object[] addedMeta, int addedAmount)
        {
            if (!Cfg.Hot.Weight || addedAmount <= 0 || __instance == null || __instance.itemAmount <= 0) return;
            try
            {
                if (!Eligible.Item(__instance)) return;
                var meta = __instance.Meta;
                if (!Eligible.PlainMeta(meta) || !Eligible.PlainMeta(addedMeta)) return;
                var t = Eligible.FirstPerishable(meta);
                var n = Eligible.FirstPerishable(addedMeta);
                // Same reference: a stack dropped back onto its own slot.
                if (t == null || n == null || ReferenceEquals(t, n) || !Eligible.SameParams(t, n)) return;
                if (!Settle(t, __instance)) return;
                ItemPerishabledata.PerishableMix(t, n, __instance.itemAmount, addedAmount);
            }
            catch (Exception e) { Log.Exception("Stacks: weighted merge", e); }
        }

        // Slot.CheckItemMetas -> CountPerishingTime (SRC/OS/Items/ItemPerishabledata.cs:47-91) for the target only:
        // charge the minutes since its last check at the stack's refrigeration, then stamp the check. If that would spoil
        // it, nothing is changed or mixed (vanilla doesn't stamp on that branch either) and the next draw spoils it as vanilla.
        // False when the target would spoil: then nothing is mixed either.
        private static bool Settle(ItemPerishabledata t, ItemStack s)
        {
            var clock = TimeOfDayAzure.instance;
            var now = clock != null ? clock.currentTimeAndDay : null;
            if (now == null || t.lastChecked == null || t.perishingDisabled || s.spoilDisabled) return true;
            ulong last = t.lastChecked.TotalMinutes, cur = now.TotalMinutes;
            if (last == 0 || cur <= last) return true;
            float before = t.integrity;
            t.RemoveMinutesFromIntegrity(cur - last, s.refigerated, 1f);
            if (t.integrity <= 0f)
            {
                t.integrity = before;
                return false;
            }
            t.lastChecked.CopyValues(now);
            return true;
        }
    }
}
