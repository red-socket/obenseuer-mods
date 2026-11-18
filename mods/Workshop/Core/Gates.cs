using System;
using System.Collections.Generic;
using OS.Items;
using UnityEngine;

namespace Workshop
{
    // Why a start was refused. Transient refusals (power, busy slots, no output room) make a queue wait; every other
    // refusal stops it (spec §5.2).
    internal struct Refusal
    {
        internal string Text;
        internal bool Transient;

        internal static Refusal Lasting(string text) => new Refusal { Text = text };
        internal static Refusal Wait(string text) => new Refusal { Text = text, Transient = true };
    }

    // Every start Workshop makes passes all of the vanilla UI's gates again, in the same frame as the start (spec §5.2).
    // manufacturing.Craft itself checks only power and free slots and removes the inputs without looking (SRC/
    // manufacturing.cs:357-419), so a start without these gates could craft from nothing; this class is why it never does.
    internal static class Gates
    {
        // True while TryCraft is inside manufacturing.Craft: W3 then leaves Last and the repeat count alone.
        internal static bool InOurCall;

        // A manufacturing start (gates 1-12). attended: the player is at the bench (the repeat key with its panel open),
        // so a bench that needs the player may start. checkRoom: the output may arrive with the panel closed, which is
        // true of every start the pump makes (queued or auto-run, whatever the panel state) and of a repeat-key start
        // that arms a queue or is made from the world. autoRun: the pump's auto-run is the caller, the only caller
        // allowed on an AutoRun crusher.
        internal static bool CanCraft(manufacturing m, int id, string name, bool attended, bool checkRoom, bool autoRun, out Recipe copy, out Refusal why)
        {
            copy = null;
            why = default;
            try
            {
                if (!Stations.Usable(m)) { why = Refusal.Lasting(Strings.Unavailable); return false; }                         // 1
                if (Stations.IsLocked(m)) { why = Refusal.Lasting(Strings.Locked); return false; }                             // 2
                if (Stations.IsOwned(m)) { why = Refusal.Lasting(Strings.Owned); return false; }                               // 3
                var kind = Stations.KindOf(m);                                                                                 // 4
                if (kind == Kind.Dishwasher || kind == Kind.VanillaAutocraft || kind == Kind.None || (kind == Kind.AutoRun && !autoRun))
                {
                    why = Refusal.Lasting(Strings.NotSupported);
                    return false;
                }
                copy = RecipeLookup.Copy(m, id, name);                                                                         // 5
                if (copy == null) { why = Refusal.Lasting(Strings.NotMadeHere); return false; }
                if (!RecipeKnown(copy)) { why = Refusal.Lasting(Strings.RecipeUnknown); return false; }                       // 6
                if (!RecipeInfoUI.CheckSkillSoft(copy, m.EquipmentQualityWithUpgrades)) { why = Refusal.Lasting(Strings.SkillTooLow); return false; }   // 7
                if (!RecipeInfoUI.CheckModifiers(copy, m.ModifiersWithUpgrades)) { why = Refusal.Lasting(Strings.NeedsUpgrade); return false; }          // 8
                if (!Vanilla.Compare(m, copy))                                                                                 // 9
                {
                    why = Refusal.Lasting(Strings.Missing(Shortfall.FirstMissing(m, copy)));
                    return false;
                }
                // 10. Pre-checked so Craft never reaches its unguarded FlashPowerIcon (SRC/manufacturing.cs:371-374).
                if (!m.PoweredAndActive) { why = Refusal.Wait(Strings.NoPower); return false; }
                if (!m.HasPowerForCrafting()) { why = Refusal.Wait(Strings.GridTooWeak); return false; }
                if (Vanilla.CraftingInProgress(m) >= m.AllowSimultaneous) { why = Refusal.Wait(Strings.SlotsBusy); return false; }   // 11
                // 11b. One instant craft per machine per frame: a second one in the same frame leaves lastInstantCraftFrame
                // unchanged, so TryCraft could not see that it started and a repeat would make an uncounted extra job.
                if (Vanilla.LastInstantCraftFrame(m) == Time.frameCount) { why = Refusal.Wait(Strings.SlotsBusy); return false; }
                if (!attended && m.RequirePlayer) { why = Refusal.Lasting(Strings.OpenTheBench); return false; }               // 12
                if (checkRoom && !OutputRoom(m, copy)) { why = Refusal.Wait(Strings.NoOutputRoom); return false; }
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("craft gates", e);
                why = Refusal.Lasting(Strings.Unavailable);
                copy = null;
                return false;
            }
        }

        // A vessel start (spec §5.5): gates 1-3 and 5-9 plus power. kNow is the multiplier the slots allow now, the value
        // vanilla SetActiveRecipe would give (SRC/manufacturingProcess.cs:481-499).
        internal static bool CanStartVessel(manufacturingProcess p, int id, string name, out Recipe copy, out int kNow, out Refusal why)
        {
            copy = null;
            kNow = 0;
            why = default;
            try
            {
                if (!Stations.Usable(p)) { why = Refusal.Lasting(Strings.Unavailable); return false; }
                if (Stations.IsLocked(p)) { why = Refusal.Lasting(Strings.Locked); return false; }
                if (Stations.IsOwned(p)) { why = Refusal.Lasting(Strings.Owned); return false; }
                if (p.stage != manufacturingProcess.Stage.LoadingItems)
                {
                    why = Refusal.Lasting(p.stage == manufacturingProcess.Stage.Processing ? Strings.AlreadyRunning : Strings.EmptyItFirst);
                    return false;
                }
                copy = RecipeLookup.Copy(p, id, name);
                if (copy == null) { why = Refusal.Lasting(Strings.NotMadeHere); return false; }
                if (!RecipeKnown(copy)) { why = Refusal.Lasting(Strings.RecipeUnknown); return false; }
                if (!RecipeInfoUI.CheckSkillSoft(copy, p.EquipmentQualityWithUpgrades)) { why = Refusal.Lasting(Strings.SkillTooLow); return false; }
                if (!RecipeInfoUI.CheckModifiers(copy, p.ModifiersWithUpgrades)) { why = Refusal.Lasting(Strings.NeedsUpgrade); return false; }
                kNow = Vanilla.Compare(p, copy) ? p.GetRecipeMultiplierShowValue(copy) : 0;
                if (kNow < 1)
                {
                    why = Refusal.Lasting(Strings.Missing(Shortfall.FirstMissing(p, copy)));
                    return false;
                }
                if (!p.PoweredAndActive) { why = Refusal.Lasting(Strings.NoPower); return false; }
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("vessel gates", e);
                why = Refusal.Lasting(Strings.Unavailable);
                copy = null;
                return false;
            }
        }

        internal static bool RecipeKnown(Recipe r)
        {
            if (!r.RecipeRequired) return true;
            var rc = RecipeController.instance;
            return rc != null && (rc.HasRecipe(r) || rc.HasLearnedRecipe(r));
        }

        // Starts one craft through vanilla Craft with the recipe selected for that one call (spec §5.2 TryCraft). True
        // when a job really started: the job list grew, or an instant craft ran this frame.
        internal static bool TryCraft(manufacturing m, Recipe copy)
        {
            var jobs = Vanilla.JobsOf(m);
            int before = jobs != null ? jobs.Count : 0;
            int lastFrame = Vanilla.LastInstantCraftFrame(m);
            var prev = Vanilla.SelectedRecipe(m);
            m.SetSelectedRecipe(copy);
            InOurCall = true;
            try { m.Craft(); }
            finally { InOurCall = false; }
            jobs = Vanilla.JobsOf(m);
            int frameNow = Vanilla.LastInstantCraftFrame(m);
            bool started = (jobs != null && jobs.Count > before) || (frameNow != lastFrame && frameNow == Time.frameCount);
            // An open panel re-selects its shown recipe in CheckItems; a closed one gets its old selection back.
            if (Vanilla.SelectedRecipe(m) == copy && !Vanilla.PanelOpen(m)) Vanilla.SelectedRecipe(m) = prev;
            return started;
        }

        // Gate 12 (spec §5.2): with the panel closed, Storage.AddToSlots drops overflow on the floor (SRC/Storage.cs:
        // 1277-1294), so a start whose output may arrive unwatched needs room in the output storage for everything every
        // running job and the new one can produce. Each output item needs a stack of it with room for the whole amount
        // (only for items whose stacks always merge), or its own empty slots. Liquids without a container go to the
        // vessel's liquid store and need no slot; returned containers and variant outputs claim an empty slot each.
        // On a machine with one storage, the junk an emptied charge-type input leaves (ItemAmmodata.itemsToSpawn, added
        // by RemoveAmmo during Craft before the slot is freed, SRC/CraftingBase.cs:1293-1299) lands in that same storage
        // first, so it claims an empty slot per junk item too.
        internal static bool OutputRoom(manufacturing m, Recipe add)
        {
            var storage = m.OutputStorage != null ? m.OutputStorage : m.InputOutputStorage;
            if (storage == null || storage.Slots == null) return false;
            var amounts = new Dictionary<int, float>();
            var items = new Dictionary<int, Item>();
            int unknown = 0;

            void Count(Recipe r, List<KeyValuePair<ItemStack, int>> used)
            {
                if (r == null) { unknown++; return; }
                if (r.Output == null) r.SetItems();
                var per = new Dictionary<int, float>();
                void Take(Dictionary<Item, float> outs)
                {
                    if (outs == null) return;
                    foreach (var kv in outs)
                    {
                        var it = kv.Key;
                        if (it == null) continue;
                        if (ItemMetaUtilities.GetMetaOfType<ItemGroupData>(it.Meta) != null) { unknown++; continue; }
                        if (it.Categories != null && Array.IndexOf(it.Categories, "Liquid") >= 0)
                        {
                            var box = r.metaSettings != null && r.metaSettings.useLiquidContainers ? r.metaSettings.putLiquidsIntoContainer : null;
                            if (box != null && box.ID > 1) unknown++;
                            continue;
                        }
                        items[it.ID] = it;
                        per[it.ID] = Math.Max(per.TryGetValue(it.ID, out var a) ? a : 0f, kv.Value);
                    }
                }
                Take(r.Output);
                Take(r.OutputFail);
                foreach (var kv in per) amounts[kv.Key] = (amounts.TryGetValue(kv.Key, out var a) ? a : 0f) + kv.Value;
                if (r.variantItemCategory != VariantCategory.None) unknown++;
                if (used != null)
                    foreach (var u in used)
                        if (u.Key != null && u.Value > 0) unknown++;
            }

            var jobs = Vanilla.JobsOf(m);
            if (jobs != null)
                foreach (var j in jobs) Count(j?.recipe, j?.usedItemReferences);
            Count(add, null);

            int empty = 0;
            foreach (var sc in storage.Slots)
                if (sc?.itemStack != null && (sc.itemStack.itemId < 0 || sc.itemStack.itemAmount <= 0)) empty++;

            int needed = unknown + (m.OutputStorage == null ? JunkSlots(add) : 0);
            foreach (var kv in amounts)
            {
                var it = items[kv.Key];
                int stack = Math.Max(1, it.Stackable);
                int amount = Mathf.CeilToInt(kv.Value);
                if (amount <= 0) continue;
                if (MergesPlainly(it) && HasRoom(storage, it.ID, amount, stack)) continue;
                needed += (amount + stack - 1) / stack;
            }
            return empty >= needed;
        }

        // Junk the new start can spawn into InputOutputStorage: one slot per itemsToSpawn entry of every charge-type input
        // the recipe uses as ammo (ammoInputItemsToIgnore aside). A group input may be met by a charge-type member, so it
        // claims one slot. Over-counts when the source stack does not empty, never under-counts.
        private static int JunkSlots(Recipe r)
        {
            var ms = r?.metaSettings;
            if (r == null || r.disableJunkSpawn || ms == null || !ms.useAmmoAsInput) return 0;
            if (r.Input == null) r.SetItems();
            if (r.Input == null) return 0;
            int n = 0;
            foreach (var kv in r.Input)
            {
                var it = kv.Key;
                if (it == null || (ms.ammoInputItemsToIgnore != null && ms.ammoInputItemsToIgnore.Contains(it.ID))) continue;
                if (ItemMetaUtilities.GetMetaOfType<ItemGroupData>(it.Meta) != null) { n++; continue; }
                var ammo = ItemMetaUtilities.GetMetaOfType<ItemAmmodata>(it.Meta);
                if (ammo != null && ammo.destroyWhenEmpty && ammo.itemsToSpawn != null) n += ammo.itemsToSpawn.Count;
            }
            return n;
        }

        // Items whose stacks never refuse a merge: no quality, freshness, charges or liquid in their meta.
        private static bool MergesPlainly(Item it)
        {
            var meta = it.Meta;
            return ItemMetaUtilities.GetMetaOfType<ItemQualityData>(meta) == null
                && ItemMetaUtilities.GetMetaOfType<ItemPerishabledata>(meta) == null
                && ItemMetaUtilities.GetMetaOfType<ItemAmmodata>(meta) == null
                && ItemMetaUtilities.GetMetaOfType<ItemLiquidData>(meta) == null;
        }

        private static bool HasRoom(Storage s, int id, int amount, int stack)
        {
            foreach (var sc in s.Slots)
            {
                var st = sc?.itemStack;
                if (st != null && st.itemId == id && st.ownerId == -1 && stack - st.itemAmount >= amount) return true;
            }
            return false;
        }
    }
}
