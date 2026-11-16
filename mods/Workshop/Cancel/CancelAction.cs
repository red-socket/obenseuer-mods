using System;
using System.Collections.Generic;
using System.Linq;
using OS.Items;
using UnityEngine;

namespace Workshop
{
    // U-010 Cancel a running craft (spec §7). Two clicks on a job's progress slot: the first names what comes back, the
    // second runs the game's own CraftDelayed for that job with a forced fail. Nothing is created or refunded by Workshop:
    // the game removes the job, hands out the recipe's failed output, counts the slot down and refreshes the panel. On
    // the dishwasher the failed output is the washed item itself with its original owner (SRC/Dishwasher.cs:76-185).
    internal static class CancelAction
    {
        private const string ConfirmKey = "cancel";

        internal static void OnSlotClick(GameObject slot)
        {
            if (!PlayGate.Open || Game.DragActive) return;
            var panel = ManufacturingPanel.instance;
            var m = panel != null ? panel.ActiveManu : null;
            if (m == null || CraftingBase.currentCraftingBase != m || !Vanilla.IsActive(m)) return;
            var timer = TimerOf(panel, slot);
            if (timer == null) return;
            var job = JobOf(m, timer);
            if (!Eligible(m, job, out string why))
            {
                Confirm.Reset(ConfirmKey);
                if (why != null) Notify.Show(why);
                return;
            }
            if (Confirm.IsArmed(ConfirmKey, timer))
            {
                Run(m, job);
                return;
            }
            Confirm.Arm(ConfirmKey, timer, Cfg.Cancel.ConfirmSeconds.Value);
            var e = MachineStore.Get(m, false);
            Notify.Post(Strings.CancelArmed(m is Dishwasher, Returns(job.recipe), e != null && e.Queued ? e.QLeft : 0));
        }

        // The slot GameObject is a value of ProcessSlotsUI.GetProgressingItemsDictionary (SRC/ProcessSlotsUI.cs:46-49),
        // rebuilt on every UpdateItemsInProgress; the Timer is the stable key.
        private static TimeOfDayAzure.Timer TimerOf(ManufacturingPanel panel, GameObject slot)
        {
            var dict = panel.manufacturingPanelUI?.processSlotsUI?.GetProgressingItemsDictionary();
            if (dict == null) return null;
            foreach (var kv in dict)
                if (kv.Value == slot) return kv.Key;
            return null;
        }

        private static CraftInProgress JobOf(manufacturing m, TimeOfDayAzure.Timer timer)
        {
            var jobs = Vanilla.JobsOf(m);
            if (jobs == null) return null;
            foreach (var j in jobs)
                if (j != null && ReferenceEquals(j.timer, timer)) return j;
            return null;
        }

        // Spec §7.1. why is null when the click should stay silent.
        private static bool Eligible(manufacturing m, CraftInProgress job, out string why)
        {
            why = null;
            bool dishwasher = m is Dishwasher;
            if (!dishwasher && Cfg.Cancel.Scope.Value != CancelScopeMode.AllManufacturing) return false;   // 1: silent outside the scope
            var kind = Stations.KindOf(m);
            if (kind == Kind.VanillaAutocraft || kind == Kind.AutoRun) { why = Strings.CancelAutocraft; return false; }   // 2
            if (job == null || job.recipe == null || job.timer == null) return false;
            var t = TimeOfDayAzure.instance;
            var timers = t != null && Vanilla.Timers != null ? Vanilla.Timers(t) : null;
            if (timers == null || !timers.Contains(job.timer)) { why = Strings.CancelJustLoaded; return false; }       // 3
            if (job.timer.remainingInSeconds <= 0) { why = Strings.CancelAlmostDone; return false; }                    // 4
            var r = job.recipe;
            if (r.Output == null) r.SetItems();
            var ms = r.metaSettings;
            if (dishwasher)
            {
                var owner = r.Meta != null ? ItemMetaUtilities.GetMetaOfType<Recipe.RecipeOwnerMeta>(r.Meta) : null;
                if (!r.keepItemOwnerOnFail || owner == null || owner.ownerID < 1) { why = Strings.CancelNoOwner; return false; }   // 5
                if (ms != null && (ms.useLiquidContainers || ms.putLiquidsIntoContainer != null)) { why = Strings.CancelLiquid; return false; }   // 6
            }
            if ((ms != null && ms.packagedItems != null && ms.packagedItems.Count > 0)                                   // 7
                || (r.OutputFail != null && r.OutputFail.Keys.Any(i => i != null && ItemMetaUtilities.GetMetaOfType<ItemPackageData>(i.Meta) != null)))
            {
                why = Strings.CancelPackage;
                return false;
            }
            // 8: elsewhere a cancel may only cost the player. Where the failed output shares an item with the real
            // output (coffee, tea, press plates, the sawdust of planks), a cancel would hand that item out at once,
            // without the process time. Liquids are keyed by the liquid item, groups by their members.
            if (!dishwasher && SharesOutput(r)) { why = Strings.CancelSameResult; return false; }
            if (!RoomFor(m, job)) { why = Strings.CancelNoRoom; return false; }                                          // 9
            return true;
        }

        private static bool SharesOutput(Recipe r)
        {
            var made = Ids(r.Output);
            foreach (int id in Ids(r.OutputFail))
                if (made.Contains(id)) return true;
            return false;
        }

        private static HashSet<int> Ids(Dictionary<Item, float> outs)
        {
            var set = new HashSet<int>();
            if (outs == null) return set;
            foreach (var kv in outs)
            {
                var it = kv.Key;
                if (it == null) continue;
                set.Add(it.ID);
                var g = ItemMetaUtilities.GetMetaOfType<ItemGroupData>(it.Meta);
                if (g?.itemGroup != null)
                    foreach (var gi in g.itemGroup)
                        if (gi?.item != null) set.Add(gi.item.ID);
            }
            return set;
        }

        // 9: the failed output and any reserved containers fit in the machine's own empty slots, so nothing reaches the
        // player's inventory (where the add path runs crime checks) or the floor.
        private static bool RoomFor(manufacturing m, CraftInProgress job)
        {
            var storage = m.OutputStorage != null ? m.OutputStorage : m.InputOutputStorage;
            if (storage == null || storage.Slots == null) return false;
            int empty = 0;
            foreach (var sc in storage.Slots)
                if (sc?.itemStack != null && (sc.itemStack.itemId < 0 || sc.itemStack.itemAmount <= 0)) empty++;
            int need = 0;
            if (job.recipe.OutputFail != null)
                foreach (var kv in job.recipe.OutputFail)
                {
                    if (kv.Key == null) continue;
                    int stack = Math.Max(1, kv.Key.Stackable);
                    int amount = Math.Max(1, Mathf.CeilToInt(kv.Value));
                    need += (amount + stack - 1) / stack;
                }
            if (job.usedItemReferences != null)
                foreach (var u in job.usedItemReferences)
                    if (u.Key != null && u.Value > 0) need++;
            return empty >= need;
        }

        private static List<string> Returns(Recipe r)
        {
            var list = new List<string>();
            if (r?.OutputFail == null) return list;
            foreach (var kv in r.OutputFail)
                if (kv.Key != null) list.Add(Strings.Amount(kv.Key, kv.Value));
            return list;
        }

        // Spec §7.3: synchronous, main thread. The timer goes first: TimeOfDayAzure.UpdateTimers fires even stopped
        // timers once their time is up, which would run CraftDelayed a second time.
        private static void Run(manufacturing m, CraftInProgress job)
        {
            Confirm.Reset(ConfirmKey);
            MachineStore.ClearQueue(m);
            RepeatButton.RefreshCraftLabel(m);
            var r = job.recipe;
            var ms = r.metaSettings;
            float? keep = null;
            // The dishwasher's recipe is its own per job (GenerateRecipe builds a fresh metaSettings, and a loaded job
            // deserializes its own copy), so this never touches the recipe database. Restored in finally, in the same
            // call, so sibling jobs that share the recipe object see the original value again. KeepFreshness off gives
            // a failed wash's freshness, but never fresher than the item went in: a cancel is no freshness reset.
            if (m is Dishwasher && ms != null)
            {
                keep = ms.setIntegrityFail;
                ms.setIntegrityFail = Cfg.Cancel.KeepFreshness.Value ? ms.setIntegrity : Math.Min(ms.setIntegrity, ms.setIntegrityFail);
            }
            string what = string.Join(", ", Returns(r).ToArray());
            int owner = (r.Meta != null ? ItemMetaUtilities.GetMetaOfType<Recipe.RecipeOwnerMeta>(r.Meta) : null)?.ownerID ?? -1;
            job.timer.Destroy();
            CancelScope.Active = true;
            CancelScope.Machine = m;
            try { m.CraftDelayed(job.timer); }
            finally
            {
                CancelScope.Active = false;
                CancelScope.Machine = null;
                if (keep.HasValue) ms.setIntegrityFail = keep.Value;
            }
            var jobs = Vanilla.JobsOf(m);
            if (jobs != null && jobs.Contains(job))
                Log.Error($"Cancel: {Stations.RecipeName(r)} at {Stations.Name(m)} is still listed after the cancel.");
            if (jobs != null && Vanilla.CraftingInProgress(m) != jobs.Count)
            {
                Log.Warn($"Cancel: {Stations.Name(m)} counted {Vanilla.CraftingInProgress(m)} running jobs for {jobs.Count}; set to {jobs.Count}, as the game does on load.");
                Vanilla.CraftingInProgress(m) = jobs.Count;
            }
            Log.Info($"Cancel: cancelled {Stations.RecipeName(r)} at {Stations.Name(m)}; returned {(what.Length > 0 ? what : "nothing")}, owner {owner}.");
        }
    }
}
