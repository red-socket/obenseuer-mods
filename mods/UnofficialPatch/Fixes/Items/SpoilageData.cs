using System;
using System.Collections.Generic;
using HarmonyLib;
using OS.Items;

namespace UnofficialPatch
{
    // S-093 spoilage data (spec §9). ItemPerishabledata.RemoveMinutesFromIntegrity (SRC/OS/Items/ItemPerishabledata.cs:96-133)
    // stops decay when storage cold >= refigerationFactor and ignores cold altogether when the factor is 0.
    // Items.json gives Vanilla Ice Cream (15860) and the Chocolate Ice Cream Cone (18480) 1.2, more than a freezer's
    // 1.0, so they rot in the freezer (#5905), and all four coffees 0, so a fridge never helps. The item templates are
    // corrected when ItemDatabase loads (only while a value still equals vanilla's; assign, never scale); stacks that
    // already exist carry their own copy and are corrected the next time they decay, matched by signature:
    //   factor 1.2 and spoils into Paper Trash (11530)   -> ice cream (only 15860 and 18480 have this);
    //   factor 0   and spoils into Stale Coffee (19210)  -> coffee (only the four coffees; Plörö has 0.5).
    // Template edits are runtime only; corrected stacks keep their vanilla-typed float after removal.
    internal static class SpoilageData
    {
        internal static bool Patched, HealPatched;

        internal const int IceCreamId = 15860, ConeId = 18480, PaperTrashId = 11530, StaleCoffeeId = 19210;
        private const float VanillaIceCream = 1.2f, VanillaCoffee = 0f;

        private static int _healedIce, _healedCoffee, _healedTotal;

        internal static void ApplyItemFixes()
        {
            if (!Patched) return;
            var db = ItemDatabase.database;
            if (db == null || db.Count == 0) return;
            var changed = new List<string>();
            foreach (var item in db)
            {
                if (item == null || item.Meta == null) continue;
                bool ice = Cfg.IceCreamFix.Value && (item.ID == IceCreamId || item.ID == ConeId);
                bool coffee = Cfg.CoffeeFix.Value && Cfg.CoffeeIds.Contains(item.ID);
                if (!ice && !coffee) continue;
                var p = ItemMetaUtilities.GetMetaOfType<ItemPerishabledata>(item.Meta);
                if (p == null) continue;
                if (ice && p.refigerationFactor == VanillaIceCream)
                {
                    p.refigerationFactor = Cfg.IceCreamRefrigerationFactor.Value;
                    changed.Add($"{item.Title} {VanillaIceCream} -> {p.refigerationFactor}");
                }
                else if (coffee && p.refigerationFactor == VanillaCoffee)
                {
                    p.refigerationFactor = Cfg.CoffeeRefrigerationFactor.Value;
                    changed.Add($"{item.Title} {VanillaCoffee} -> {p.refigerationFactor}");
                }
            }
            if (changed.Count > 0)
                Log.Info($"Spoilage: corrected the cold needed to keep {changed.Count} items: {string.Join(", ", changed)}.");
        }

        // Hot path (every perishable stack's decay tick): two float compares, no allocation.
        internal static void Heal(ItemPerishabledata p)
        {
            if (p == null) return;
            float f = p.refigerationFactor;
            if (f == VanillaIceCream)
            {
                if (!Cfg.IceCreamFix.Value || p.spoiledItem == null || p.spoiledItem.ID != PaperTrashId) return;
                p.refigerationFactor = Cfg.IceCreamRefrigerationFactor.Value;
                _healedIce++;
            }
            else if (f == VanillaCoffee)
            {
                if (!Cfg.CoffeeFix.Value || p.spoiledItem == null || p.spoiledItem.ID != StaleCoffeeId) return;
                p.refigerationFactor = Cfg.CoffeeRefrigerationFactor.Value;
                _healedCoffee++;
            }
            else return;
            _healedTotal++;
            if (Log.VerboseOn) Log.Verbose($"Spoilage: corrected {_healedTotal} existing stacks this session.");
        }

        // One summary per load.
        internal static void OnLoadingDone()
        {
            if (_healedIce == 0 && _healedCoffee == 0) return;
            Log.Info($"Spoilage: corrected {_healedIce} existing ice cream and {_healedCoffee} coffee stacks; spoilage that already happened stays.");
            _healedIce = _healedCoffee = 0;
        }
    }

    // ItemDatabase.Awake() / OnEnable() (private) and Reload().
    [HarmonyPatch]
    internal static class ItemDatabaseAwakePatch
    {
        private static System.Reflection.MethodBase TargetMethod() => Vanilla.ItemDbAwake;

        private static void Postfix()
        {
            try { SpoilageData.ApplyItemFixes(); }
            catch (Exception e) { Log.Exception("Spoilage: item data (Awake)", e); }
        }
    }

    [HarmonyPatch]
    internal static class ItemDatabaseOnEnablePatch
    {
        private static System.Reflection.MethodBase TargetMethod() => Vanilla.ItemDbOnEnable;

        private static void Postfix()
        {
            try { SpoilageData.ApplyItemFixes(); }
            catch (Exception e) { Log.Exception("Spoilage: item data (OnEnable)", e); }
        }
    }

    [HarmonyPatch(typeof(ItemDatabase), nameof(ItemDatabase.Reload))]
    internal static class ItemDatabaseReloadPatch
    {
        private static void Postfix()
        {
            try { SpoilageData.ApplyItemFixes(); }
            catch (Exception e) { Log.Exception("Spoilage: item data (Reload)", e); }
        }
    }

    // ItemPerishabledata.RemoveMinutesFromIntegrity(float, float, float).
    [HarmonyPatch(typeof(ItemPerishabledata), nameof(ItemPerishabledata.RemoveMinutesFromIntegrity),
        new[] { typeof(float), typeof(float), typeof(float) })]
    internal static class RemoveMinutesFromIntegrityPatch
    {
        private static void Prefix(ItemPerishabledata __instance)
        {
            if (!SpoilageData.HealPatched || !Cfg.HealExistingItems.Value) return;
            try { SpoilageData.Heal(__instance); }
            catch (Exception e) { Log.Exception("Spoilage: existing stack", e); }
        }
    }
}
