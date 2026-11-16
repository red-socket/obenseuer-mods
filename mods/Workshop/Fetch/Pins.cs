using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Bootstrap;

namespace Workshop
{
    // Tidy Inventory's pinned main-inventory slots (spec §4.4 source 1). Fetch never takes from a pinned slot, whatever
    // Tidy's BlockManualMoves says. Read live from TidyInventory.PinStore.Map by reflection (an internal static
    // SortedDictionary<int, Pin> keyed by slot index); if that fails, from Tidy's saved record in GlobalState
    // ("tidyinventory.pins", "v1|idx:id;..."). With Tidy loaded and neither readable, every main slot counts as pinned.
    internal static class Pins
    {
        private const string TidyGuid = "redsocket.obenseuer.tidyinventory";
        private const string Key = "tidyinventory.pins";

        private static bool _looked;
        private static IDictionary _map;

        // null: skip every main slot. Empty: nothing pinned (or Tidy not loaded).
        internal static HashSet<int> Indices()
        {
            var set = new HashSet<int>();
            if (!TidyLoaded()) return set;
            var map = Map();
            if (map != null)
            {
                try
                {
                    foreach (var k in map.Keys)
                        if (k is int i) set.Add(i);
                    return set;
                }
                catch (Exception e) { Log.Exception("pin read", e); }
            }
            if (FromRecord(set)) return set;
            Log.WarnOnce("pins unreadable", "Fetch: Tidy Inventory's pinned slots could not be read, so Fetch leaves your main inventory alone.");
            return null;
        }

        private static bool TidyLoaded()
        {
            try { return Chainloader.PluginInfos.TryGetValue(TidyGuid, out var info) && info?.Instance != null; }
            catch { return false; }
        }

        private static IDictionary Map()
        {
            if (_looked) return _map;
            _looked = true;
            try
            {
                var asm = Chainloader.PluginInfos[TidyGuid].Instance.GetType().Assembly;
                var t = asm.GetType("TidyInventory.PinStore");
                var f = t?.GetField("Map", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                _map = f?.GetValue(null) as IDictionary;
            }
            catch (Exception e) { Log.Exception("pin map", e); _map = null; }
            return _map;
        }

        // Tidy's saved record: "v1|idx:id;idx:id". Only v1 is understood.
        private static bool FromRecord(HashSet<int> set)
        {
            try
            {
                var raw = GlobalState.instance?.GetState(Key)?.data as string;
                if (raw == null) return true;   // no record: no pins saved
                if (!raw.StartsWith("v1|", StringComparison.Ordinal)) return false;
                foreach (var part in raw.Substring(3).Split(';'))
                {
                    int colon = part.IndexOf(':');
                    if (colon <= 0) continue;
                    if (int.TryParse(part.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx)) set.Add(idx);
                }
                return true;
            }
            catch (Exception e) { Log.Exception("pin record", e); return false; }
        }
    }
}
