using System.Collections.Generic;
using UnityEngine;

namespace GreenThumb
{
    // D16 (spec §9.3). Vanilla never checks inventory space for a whole plant or a dead uproot (grow.Harvest passes a
    // hasSpace that always says yes, SRC/grow.cs:829), and ItemOperations.AddItems fills only the 40 main slots, so a
    // sweep over a potato field could drop dozens of stacks at the player's feet. Harvest-all skips such a plant unless
    // its worst-case yield fits the main slots. A single manual press is unchanged.
    //
    // Worst case, per grow.HarvestExternal / UprootItems (:880-933, :1026-1041), each owned by the plant's owner with the
    // item's template meta: a dead plant gives each uproot item at ceil(max · scale); otherwise the seed item at its max
    // plus each grown item at ceil(max · scale) (animals at max), with scale = max(1, Age / FullGrownAge). A dead plant
    // that never grew up gives nothing (Harvest's Age < FullGrownAge branch, :811-826).
    // Fill order is vanilla's: grow.Harvest adds each stack with ItemOperations.AddItems, which starts at Slots[0]
    // (Inventory.GetFirstSlot) and then walks Siblings (= Slots, Inventory.cs:241) in index order; the first slot that is
    // empty or holds a stack that accepts the merge takes the units (ItemStack.AddItemToPanel starts a new stack in an empty
    // slot, SRC/ItemStack.cs:229-257). The grants are walked in HarvestExternal's list order (seed first, then grown
    // items; uproot items for a dead plant), and a slot an earlier grant has started is booked to that item. The instance
    // SlotHasSpaceForItem answers for each slot, so Tidy Inventory's pin veto and our bands count. Tidy Inventory's pin
    // redirect (an add starting at a pinned slot of that item) can only place units into an existing stack earlier than
    // this walk does, so the walk is the stricter answer.
    // A skill-quality roll can still keep a grant from joining the expected stack; that stack then follows vanilla
    // overflow (dropped at the feet), as the brief allows.
    internal static class SpaceCheck
    {
        private struct Grant
        {
            internal Item Item;
            internal int Amount;
        }

        private static readonly List<Grant> Grants = new List<Grant>();
        private static int[] _bookedId = new int[0];    // item an earlier grant started in an empty slot (0 = none)
        private static int[] _booked = new int[0];      // units promised to that slot by earlier grants

        internal static bool Fits(grow t)
        {
            var inv = Inventory.instance;
            if (inv == null || inv.Slots == null) return false;
            Grants.Clear();
            bool dead = Vanilla.GrowIsDead(t);
            if (dead && t.Age < t.FullGrownAge) return true;
            float scale = Mathf.Max(1f, t.Age / (float)Mathf.Max(1, t.FullGrownAge));
            if (dead)
            {
                if (t.uprootItems != null)
                    foreach (var a in t.uprootItems) Add(a, scale, false);
            }
            else
            {
                if (t.seedItem != null) Add(t.seedItem, 1f, false);
                if (t.grownItems != null)
                    foreach (var a in t.grownItems) Add(a, scale, true);
            }
            if (Grants.Count == 0) return true;

            var slots = inv.Slots;
            if (_booked.Length != slots.Length)
            {
                _booked = new int[slots.Length];
                _bookedId = new int[slots.Length];
            }
            else
            {
                System.Array.Clear(_booked, 0, _booked.Length);
                System.Array.Clear(_bookedId, 0, _bookedId.Length);
            }
            foreach (var g in Grants)
            {
                int left = g.Amount, cap = Mathf.Max(1, g.Item.Stackable), id = g.Item.ID;
                object[] meta = g.Item.CloneMeta();
                // Slots[0] first, then the rest in index order, as AddItems and Siblings do.
                for (int i = 0; i < slots.Length && left > 0; i++)
                {
                    var sc = slots[i];
                    if (sc == null || sc.itemStack == null) continue;
                    int room;
                    if (sc.itemStack.IsEmpty())
                    {
                        if (_bookedId[i] != 0 && _bookedId[i] != id) continue;   // started by an earlier grant of another item
                        room = cap - _booked[i];
                    }
                    else if (sc.itemStack.itemId == id) room = cap - sc.itemStack.itemAmount - _booked[i];
                    else continue;
                    if (room <= 0 || !sc.SlotHasSpaceForItem(g.Item, out _, t.ownerId, 1, 0, meta)) continue;
                    int take = Mathf.Min(room, left);
                    if (sc.itemStack.IsEmpty()) _bookedId[i] = id;
                    _booked[i] += take;
                    left -= take;
                }
                if (left > 0) return false;
            }
            return true;
        }

        private static void Add(grow.ItemAmount a, float scale, bool animalsUnscaled)
        {
            var item = a?.item?.Item;
            if (item == null || item.ID <= 0) return;
            int max = a.amount.y;
            if (!(animalsUnscaled && Item.GetMetaOfType<OS.Items.ItemAnimalData>(item.Meta) != null))
                max = Mathf.CeilToInt(max * scale);
            if (max > 0) Grants.Add(new Grant { Item = item, Amount = max });
        }
    }
}
