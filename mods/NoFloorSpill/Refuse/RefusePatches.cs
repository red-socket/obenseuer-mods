using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NoFloorSpill
{
    // U-009 / S-088 refuse half (spec §2.2). Fuel.SpawnRefuse and SpawnRefuseAll put ash into the stove's refuse slot
    // through SlotController.AddItem, which drops whatever does not fit next to the stove (SRC/SlotController.cs:54-61,
    // :113-117). The transpilers swap only that call (1 in SpawnRefuse, 2 in SpawnRefuseAll) for RefuseSink.Add; the
    // queue, grouping, RefuseToSpawn.Clear() and the peak resets stay vanilla. Refuse is never requeued: DeltaSeconds'
    // `while (Count > 0 && peak - Count < num)` would loop forever.
    [HarmonyPatch]
    internal static class SpawnRefusePatch
    {
        private static MethodBase TargetMethod() => Vanilla.SpawnRefuse;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            IlSwap.Swap(instructions, original, Vanilla.SlotAddItem, RefuseSink.AddMethod, 1, false, RefuseSink.Group);
    }

    [HarmonyPatch]
    internal static class SpawnRefuseAllPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SpawnRefuseAll;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            IlSwap.Swap(instructions, original, Vanilla.SlotAddItem, RefuseSink.AddMethod, 2, false, RefuseSink.Group);
    }

    internal static class RefuseSink
    {
        internal static readonly SwapGroup Group = new SwapGroup("Refuse overflow");
        internal static readonly MethodInfo AddMethod = AccessTools.Method(typeof(RefuseSink), nameof(Add));

        private static bool _compatChecked;

        // Called from Fuel in place of RefuseSlot.AddItem(item, amount, -1, null, Parent). Returns what vanilla returns
        // (true only when everything fit); the caller pops it.
        public static bool Add(SlotController slot, Item item, int amount, int owner, object[] meta,
            GameObject dropper, bool notify, int stackAmount, ItemOperations.DropItemValues values)
        {
            var mode = Cfg.RefuseModeEntry.Value;
            // dropper == null also covers a Unity-null Fuel.Parent (not initialised): exact vanilla then.
            if (!Group.Active || mode == RefuseMode.Vanilla || item == null || amount <= 0
                || !Cfg.RefuseIds.Contains(item.ID) || dropper == null)
                return slot.AddItem(item, amount, owner, meta, dropper, notify, stackAmount, values); // a null slot throws as in vanilla

            if (!_compatChecked)
            {
                _compatChecked = true;
                Compat.Overlap(Vanilla.SpawnRefuse, "the refuse overflow fix");
                Compat.Overlap(Vanilla.SpawnRefuseAll, "the refuse overflow fix");
            }

            // The same fill as vanilla, without the drop. Deliberately outside the try: it is vanilla's own code path.
            int rem = slot.AddItemAndReturnRemaining(item, amount, owner, meta, false, dropper, notify, stackAmount, values);
            if (rem <= 0) return true;

            string src = "?";
            bool fullSameStack = false;
            bool inserting = false; // see JunkSink.Route: after a throwing insert, never drop the stale rem
            try
            {
                var st = slot.itemStack;
                // Never hard-code 99: Custom difficulty changes stack sizes, and ItemStack treats Stackable 0 as 1.
                int cap = item.Stackable == 0 ? 1 : item.Stackable;
                fullSameStack = st != null && st.itemId == item.ID && st.itemAmount >= cap;
                src = Tally.StationName(dropper);

                if (mode == RefuseMode.Delete && fullSameStack)
                {
                    Tally.Add(Kind.Refuse, Outcome.Deleted, src, item, rem);
                    rem = 0;
                }
                else if (mode == RefuseMode.NearbyStorage)
                {
                    inserting = true;
                    bool searched = NearbyStorage.Insert(dropper, item, ref rem, owner, meta, dropper, src, Kind.Refuse);
                    inserting = false;
                    // While the game is still loading no storage was tried, so WhenNoRoom does not apply: the rest drops.
                    if (searched && rem > 0 && fullSameStack && Cfg.RefuseWhenNoRoom.Value == NoRoom.Delete)
                    {
                        Tally.Add(Kind.Refuse, Outcome.Deleted, src, item, rem);
                        rem = 0;
                    }
                }
                // A foreign item in the slot (E-waste from an Odd Teddy Bear while ash arrives) blocks it: never deleted.
                if (rem > 0 && !fullSameStack)
                    Log.InfoOnce("blocked|" + item.ID, $"Refuse: {Blocked(st, src, item)}; it is dropped as in the game, never deleted.");
            }
            catch (Exception e)
            {
                Log.Exception("refuse routing", e);
                if (inserting && rem > 0)
                {
                    Log.WarnOnce("refuse-gaveup|" + item.ID, $"Refuse: an error interrupted putting {rem} {Tally.ItemName(item.ID)} " +
                        $"from {src} into nearby storage; the rest was not dropped, so it cannot be duplicated.");
                    rem = 0;
                }
                // Otherwise nothing was stored yet: fall through to the drop with the current rem.
            }

            if (rem > 0)
            {
                ItemOperations.DropItem(item, rem, owner, meta, dropper, values); // = SlotController.cs:113-117
                Tally.Add(Kind.Refuse, Outcome.Dropped, src, item, rem);
            }
            return false; // vanilla returns false whenever anything was left over
        }

        private static string Blocked(ItemStack st, string src, Item item)
        {
            string name = Tally.ItemName(item.ID);
            if (st == null || st.itemId < 0 || st.itemAmount <= 0) return $"the ash slot of {src} does not accept {name}";
            return $"the ash slot of {src} holds {Tally.ItemName(st.itemId)}, so {name} cannot stack there";
        }
    }
}
