using System;
using System.Collections.Generic;
using HarmonyLib;

namespace UnofficialPatch
{
    // S-101 shelf filters (spec §10). SlotController.CheckIfInAllowedCategoy (SRC/SlotController.cs:726-760) matches
    // "_item_<title>" filters against the lower-cased item title. The "Tablets" shelf in Interior Bazaar Electronics
    // still lists "_item_a Sales Package (Rexus)" while Items.json titles the item "Sales Package (Rexus)", so its
    // restock (Storage.SpawnItems, :927) fails silently and the shelf stays empty. Before Storage.Start, each
    // "_item_a X" entry (allowed or forbidden) with no item titled "a X" but one titled "X" is rewritten to "_item_X"
    // in a new array. These arrays are [fsIgnore] scene data, so nothing reaches the save. Idempotent. "_item_Tablesalt"
    // is left alone (D-5); RestockLog reports those shelves. Better Tenement's Storage.Start prefix (resource chests)
    // reads and writes other fields, so the order does not matter.
    internal static class ShelfFilters
    {
        internal static bool Patched;

        private const string Prefix = "_item_a ";
        private static HashSet<string> _titles;
        private static readonly HashSet<string> Logged = new HashSet<string>();

        internal static void BeforeStart(Storage s)
        {
            if (!Patched || !Cfg.FixShelfFilters.Value || s == null) return;
            if (!HasStale(s.AllowedCategories) && !HasStale(s.ForbiddenCategories)) return;
            var titles = Titles();
            if (titles == null) return;
            s.AllowedCategories = Fix(s, s.AllowedCategories, titles);
            s.ForbiddenCategories = Fix(s, s.ForbiddenCategories, titles);
        }

        private static bool HasStale(string[] a)
        {
            if (a == null) return false;
            foreach (var x in a)
                if (x != null && x.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Lower-case titles of every item, built once the database has loaded.
        private static HashSet<string> Titles()
        {
            if (_titles != null) return _titles;
            var db = ItemDatabase.database;
            if (db == null || db.Count == 0) return null;
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in db)
                if (item != null && item.Title != null) set.Add(item.Title.ToLower());
            return _titles = set;
        }

        private static string[] Fix(Storage s, string[] a, HashSet<string> titles)
        {
            if (!HasStale(a)) return a;
            string[] copy = null;
            for (int i = 0; i < a.Length; i++)
            {
                var x = a[i];
                if (x == null || !x.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string withA = x.Substring("_item_".Length);   // "a X"
                string bare = x.Substring(Prefix.Length);       // "X"
                if (titles.Contains(withA.ToLower()) || !titles.Contains(bare.ToLower())) continue;
                if (copy == null) copy = (string[])a.Clone();
                copy[i] = "_item_" + bare;
                string key = (s.GUID ?? s.gameObject.name) + "|" + x;
                if (Logged.Add(key))
                    Log.Info($"Shelves: \"{s.gameObject.name}\" in {s.gameObject.scene.name} listed \"{withA}\"; matched it to \"{bare}\" so the shelf restocks.");
            }
            return copy ?? a;
        }
    }

    // Storage.Start() (:418).
    [HarmonyPatch(typeof(Storage), nameof(Storage.Start))]
    internal static class ShelfStartPatch
    {
        private static void Prefix(Storage __instance)
        {
            try { ShelfFilters.BeforeStart(__instance); }
            catch (Exception e) { Log.Exception("Shelves: filter", e); }
        }
    }
}
