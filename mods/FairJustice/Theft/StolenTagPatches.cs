using System;
using System.Reflection;
using HarmonyLib;
using OS.Items;

namespace FairJustice
{
    // U-002 / S-036 (spec §3.1 A and C). Vanilla CheckForAddedStolenItems (SRC/Inventory.cs:406-472) writes the new
    // stolen mark to the slot it is given, which ItemOperations.AddItemAndReturnRemaining/AddItems pass as
    // Inventory slot 0 whatever actually received the items, so the receivers stay unmarked and every later move of
    // them is a fresh theft. P2/P3 record the candidate receivers before the add, P1 decides, and the add's postfix
    // marks the stacks that grew. During an own move (Theft/OwnMovePatches.cs) P1 skips the theft path entirely.
    internal static class StolenTags
    {
        internal static bool Patched;

        private static MethodBase _check;
        internal static MethodBase CheckTarget => _check ?? (_check = AccessTools.Method(typeof(Inventory), nameof(Inventory.CheckForAddedStolenItems),
            new[] { typeof(Item), typeof(object[]).MakeByRefType(), typeof(SlotController), typeof(int), typeof(int), typeof(bool) }));

        internal static MethodBase AddReturnRemainingTarget => AccessTools.Method(typeof(ItemOperations), nameof(ItemOperations.AddItemAndReturnRemaining),
            new[] { typeof(Item), typeof(int), typeof(SlotController), typeof(object[]), typeof(int), typeof(int), typeof(ItemOperations.ItemOperationSettings) });

        internal static MethodBase AddItemsTarget => AccessTools.Method(typeof(ItemOperations), nameof(ItemOperations.AddItems),
            new[] { typeof(Item), typeof(int), typeof(int), typeof(object[]), typeof(SlotController), typeof(bool), typeof(bool) });

        // Own-move exemption is on and nobody else changes the check (else vanilla runs: spec §2.4).
        internal static bool OwnMovesLive(MoveFrame mf) =>
            mf != null && OwnMoves.Patched && Cfg.FreeInternalMoves.Value && !Compat.Foreign(CheckTarget, "the free own-move exemption");

        // P1 prefix. Returns false to skip vanilla (own move, own use, listed in-place use); otherwise may null the slot so
        // vanilla writes no mark.
        internal static bool CheckPrefix(Item item, ref object[] meta, ref SlotController slot, int owner, out AddFrame state)
        {
            state = null;
            if (owner < 2 || item == null || !Patched) return true;
            var af = AddFrames.Top;
            bool afMatch = af != null && af.target == slot && af.item == item && af.owner == owner;
            // A use frame is only opened when its feature is live and nobody else changes the check (FreeUse.Open*).
            if (UseFrames.Count > 0 && FreeUse.HandleCheck(item, ref meta, slot, owner, af, afMatch)) return false;   // no noise, no witness roll, no crime
            var mf = MoveFrames.Match(item, owner);
            if (OwnMovesLive(mf))
            {
                string what = MarkLikeVanilla(item, ref meta, slot, owner, af, afMatch);
                mf.skipped = true;
                Log.Verbose($"Own move ({mf.kind}): {item.Title} (id {item.ID}, owner {owner}): theft check skipped{what}");
                return false;   // no noise, no witness roll, no crime
            }
            if (afMatch && Cfg.FixStolenTagging.Value)
            {
                slot = null;     // gate-neutral (null counts as a player slot) and vanilla writes no mark to slot 0
                state = af;
            }
            return true;
        }

        // The marking half of vanilla's check (SRC/Inventory.cs:413-466) for a check we skip: same slot gate, same
        // permission test, the mark on the meta, and the receivers (or the passed stack) marked. Returns a log suffix.
        internal static string MarkLikeVanilla(Item item, ref object[] meta, SlotController slot, int owner, AddFrame af, bool afMatch)
        {
            // Mirror vanilla's slot gate (:413-433): a move into somebody else's storage gets no mark.
            bool reachable = slot == null || Owned.IsPlayerOwned(slot);
            if (!reachable || Owned.HasPermission(meta)) return ", no mark";
            // Mirror vanilla's meta tagging (:445-466) without changing the caller's meta objects, except the one in-place
            // flag write vanilla itself makes at :453.
            if (meta == null) meta = item.CloneMeta();
            var sd = Owned.FindStolen(meta);
            if (sd != null) sd.playerStolen = true;
            else ItemMetaUtilities.AddMeta(ref meta, new ItemStolenData());   // copies the array
            if (afMatch)
            {
                af.tagStolen = true;                                            // receivers are marked in P2/P3's postfix
                return ", receivers will be marked";
            }
            if (slot != null && slot.itemStack != null && slot.itemStack.ownerId == owner && !Owned.HasPermission(slot.itemStack.Meta))
                Owned.Tag(slot.itemStack);                                      // DropItem and Invoke pass the real stack
            return ", stack marked";
        }

        // P1 postfix: vanilla marked the meta (or it came in marked), so the receivers need the mark as well.
        internal static void CheckPostfix(object[] meta, AddFrame state)
        {
            if (state == null || !AddFrames.IsOpen(state) || !Cfg.FixStolenTagging.Value) return;
            if (!Owned.HasPermission(meta) && Owned.IsStolenTagged(meta)) state.tagStolen = true;
        }

        // P2 prefix. Opens a frame when the add lands in the player's slots and someone may need to mark receivers.
        internal static AddFrame OpenForReturnRemaining(Item item, int owner, SlotController slot, object[] meta, ItemOperations.ItemOperationSettings settings)
        {
            if (!Patched || item == null || item.ID <= 1) return null;
            var inv = Inventory.instance;
            if (inv == null) return null;
            var t = slot ?? inv.GetFirstSlot();
            if (t == null) return null;
            var mf = MoveFrames.Match(item, owner);
            bool own = OwnMovesLive(mf);
            bool main = Owned.InMainSlots(t);
            // An own move into the worn backpack or a character slot never reaches the check in vanilla; the scope
            // still marks its receivers.
            bool open = (main && (Cfg.FixStolenTagging.Value || own)) || (own && Owned.IsPlayerOwned(t));
            if (!open) return null;

            var f = AddFrames.Push(item, owner, t);
            f.Snapshot(t);
            f.Snapshot(t.Siblings, t);
            if (main)
            {
                f.Snapshot(inv.CharacterSlots);
                if (settings == null || !settings.ignoreBackpack) f.Snapshot(Owned.WornBackpack()?.Slots);
            }
            if (own && !Owned.HasPermission(meta))
            {
                f.tagStolen = true;
                // We take over marking for this move, so the source remainder gets the mark too (D-3), but only once the
                // add has really moved something: Close sets mf.skipped then. A failed move (target full) changes nothing.
                f.ownMove = mf;
            }
            return f;
        }

        // P3 prefix. Main-slot targets reach the check; ignoreCrime adds (SortSlots) never do. A leftover of an own use
        // (FreeUse) into the worn backpack or a character slot never reaches the check either, so its receivers are marked
        // up front, as P2 does for own moves.
        internal static AddFrame OpenForAddItems(Item item, int owner, SlotController slot, object[] meta, bool ignoreCrime)
        {
            if (!Patched || ignoreCrime || item == null || item.ID <= 1) return null;
            var inv = Inventory.instance;
            if (inv == null) return null;
            var t = slot ?? inv.GetFirstSlot();
            if (t == null) return null;
            bool main = Owned.InMainSlots(t);
            var uf = UseFrames.Match(owner);
            bool use = uf != null && uf.kind == UseKind.Own && Owned.IsPlayerOwned(t);
            if (!main && !use) return null;
            if (!use && !Cfg.FixStolenTagging.Value && !OwnMovesLive(MoveFrames.Match(item, owner))) return null;
            var f = AddFrames.Push(item, owner, t);
            f.Snapshot(t);
            f.Snapshot(t.Siblings, t);
            if (use && !main && !Owned.HasPermission(meta)) f.tagStolen = true;
            return f;
        }

        // P2/P3 postfix. moved: how many items the add placed (P2 only; P3 never sets ownMove).
        internal static void Close(AddFrame f, int moved = 0)
        {
            if (f == null || !AddFrames.IsOpen(f)) return;
            try
            {
                if (f.tagStolen)
                {
                    int n = f.TagReceivers();
                    if (n > 0) Log.Verbose($"Stolen mark put on {n} receiving stack(s) of {f.item?.Title} (owner {f.owner}).");
                }
                if (f.ownMove != null && moved > 0 && MoveFrames.IsOpen(f.ownMove)) f.ownMove.skipped = true;
            }
            finally { AddFrames.Pop(f); }
        }
    }

    // P1: Inventory.CheckForAddedStolenItems(Item, ref object[], SlotController, int, int, bool). The slot is by value in
    // the original; declaring it ref here lets the prefix change what vanilla sees.
    [HarmonyPatch]
    internal static class CheckStolenPatch
    {
        private static MethodBase TargetMethod() => StolenTags.CheckTarget;

        private static bool Prefix(Item item, ref object[] meta, ref SlotController slot, int owner, out AddFrame __state)
        {
            __state = null;
            if (owner < 2) return true;
            try { return StolenTags.CheckPrefix(item, ref meta, ref slot, owner, out __state); }
            catch (Exception e)
            {
                Log.Exception("CheckForAddedStolenItems prefix", e);
                return true;
            }
        }

        private static void Postfix(ref object[] meta, AddFrame __state)
        {
            if (__state == null) return;
            try { StolenTags.CheckPostfix(meta, __state); }
            catch (Exception e) { Log.Exception("CheckForAddedStolenItems postfix", e); }
        }
    }

    // P2: ItemOperations.AddItemAndReturnRemaining(Item, int, SlotController, object[], int, int, ItemOperationSettings).
    [HarmonyPatch]
    internal static class AddReturnRemainingPatch
    {
        private static MethodBase TargetMethod() => StolenTags.AddReturnRemainingTarget;

        private static void Prefix(Item item, int owner, SlotController slot, object[] meta, ItemOperations.ItemOperationSettings settings, out AddFrame __state)
        {
            __state = null;
            if (owner < 2) return;   // hot path: one int compare
            try { __state = StolenTags.OpenForReturnRemaining(item, owner, slot, meta, settings); }
            catch (Exception e) { Log.Exception("AddItemAndReturnRemaining prefix", e); }
        }

        private static void Postfix(AddFrame __state, int amount, int __result)
        {
            if (__state == null) return;
            try { StolenTags.Close(__state, amount - __result); }
            catch (Exception e) { Log.Exception("AddItemAndReturnRemaining postfix", e); }
        }

        private static Exception Finalizer(Exception __exception, AddFrame __state)
        {
            if (__state != null) AddFrames.Pop(__state);
            return __exception;
        }
    }

    // P3: ItemOperations.AddItems(Item, int, int, object[], SlotController, bool, bool). The ItemStack and int overloads
    // forward here and are not patched. Every use leftover (ItemsToSpawn, ammo empties, Unpack, the empty bottle) comes
    // through here via ItemOperations.AddItem(WithPermission).
    [HarmonyPatch]
    internal static class AddItemsPatch
    {
        private static MethodBase TargetMethod() => StolenTags.AddItemsTarget;

        // owner and meta are by value in the original; ref here lets a use frame change what vanilla adds (FreeUse.BeforeAdd).
        private static void Prefix(Item item, ref int owner, ref object[] meta, SlotController slot, bool ignoreCrime, out AddFrame __state)
        {
            __state = null;
            if (owner < 2) return;   // hot path: one int compare
            try
            {
                if (UseFrames.Count > 0) FreeUse.BeforeAdd(item, ref owner, ref meta);
                if (owner < 2) return;
                __state = StolenTags.OpenForAddItems(item, owner, slot, meta, ignoreCrime);
            }
            catch (Exception e) { Log.Exception("AddItems prefix", e); }
        }

        private static void Postfix(AddFrame __state)
        {
            if (__state == null) return;
            try { StolenTags.Close(__state); }
            catch (Exception e) { Log.Exception("AddItems postfix", e); }
        }

        private static Exception Finalizer(Exception __exception, AddFrame __state)
        {
            if (__state != null) AddFrames.Pop(__state);
            return __exception;
        }
    }
}
