using System;
using OS.Items;

namespace FairJustice
{
    // Which slots are the player's own, and the stolen mark (spec §2.2). Tag is the only place the mod writes stack metas.
    internal static class Owned
    {
        // Main inventory, character/equipment slots, or the worn backpack's storage (read now: the bag can change).
        // Slot.playerSlot is not used: it is false for backpack slots.
        internal static bool IsPlayerOwned(SlotController s)
        {
            if (s == null) return false;
            var inv = Inventory.instance;
            if (inv == null) return false;
            if (inv.Slots != null && Array.IndexOf(inv.Slots, s) >= 0) return true;
            if (inv.CharacterSlots != null && Array.IndexOf(inv.CharacterSlots, s) >= 0) return true;
            var bp = WornBackpack();
            return bp != null && bp.Slots != null && Array.IndexOf(bp.Slots, s) >= 0;
        }

        // Same test as vanilla Inventory.InPlayerSlot (SRC/Inventory.cs:1418).
        internal static bool InMainSlots(SlotController s)
        {
            var inv = Inventory.instance;
            return s != null && inv != null && inv.Slots != null && Array.IndexOf(inv.Slots, s) >= 0;
        }

        internal static Storage WornBackpack()
        {
            var bs = BackpackStorage.instance;
            var inv = Inventory.instance;
            if (bs == null || inv == null || inv.CharacterSlots == null || inv.CharacterSlots.Length <= 5) return null;
            return bs.GetStorage();
        }

        // Bought or given: vanilla's permission test (SRC/Inventory.cs:434-444).
        internal static bool HasPermission(object[] meta)
        {
            if (meta == null) return false;
            foreach (var o in meta)
                if (o is ItemOwnerPermission p && p.ownerPermission) return true;
            return false;
        }

        // Someone else's stack the player was allowed to have (D-4): the game's crime counts skip it (Crime.cs:402-410,
        // :453-470, Inventory.cs:434-444) and the item panel says "Owned", but GetStolenItems and ConfiscateItems ignore
        // the permission. This is all KeepPermittedItems protects. Owner-1 stacks without permission are left to vanilla:
        // the item panel labels them "Stolen" and traders refuse them (design.md, D-4 research).
        internal static bool IsPermitted(ItemStack st) => st != null && st.ownerId > 0 && HasPermission(st.Meta);

        // Exact type, like ItemMetaUtilities.GetMetaOfType (which logs a warning on a null array; this does not).
        internal static ItemStolenData FindStolen(object[] meta)
        {
            if (meta == null) return null;
            foreach (var o in meta)
                if (o != null && o.GetType() == typeof(ItemStolenData)) return (ItemStolenData)o;
            return null;
        }

        internal static bool IsStolenTagged(object[] meta) => FindStolen(meta)?.playerStolen == true;

        // Adds a fresh ItemStolenData (playerStolen = true) to the stack. A stack holding one with playerStolen = false
        // gets a new array with a fresh object in its place, so a meta object shared with other stacks is never changed.
        // Returns true when the stack was not marked before.
        internal static bool Tag(ItemStack st)
        {
            if (st == null || st.itemAmount <= 0) return false;
            var meta = st.Meta;
            var sd = FindStolen(meta);
            if (sd != null && sd.playerStolen) return false;
            if (sd == null)
            {
                st.AddMeta(new ItemStolenData());
                return true;
            }
            var copy = (object[])meta.Clone();
            for (int i = 0; i < copy.Length; i++)
                if (ReferenceEquals(copy[i], sd)) copy[i] = new ItemStolenData();
            st.Meta = copy;
            return true;
        }
    }
}
