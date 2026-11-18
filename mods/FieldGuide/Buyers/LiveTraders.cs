using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FieldGuide
{
    // The Trade components of the loaded scenes, matched to Buyers (spec §9.1, D24). Traders here are checked through
    // the live instance: the game's own Trade.CheckIfAllowed (with Fair Market's B1 postfix, D18) and Trade.IsStolen, and
    // their sell factor, currency and stolen rules are read at hover time, because the save restores them after the
    // scene loads. Matching is by (scene, GUID), or by sharedId (a shared trader's live GUID is "SharedTrader_<id>",
    // SRC/Trade.cs:211-220). Unmatched Trades are never shown; they are logged once per scene, so stale data shows up in
    // user logs. A destroyed instance fails Unity's null check and its Buyer falls back to the bundled data.
    // Each matched Trade's storage-slot rule (the part of Trade.CheckIfFull that refuses an item whatever the space,
    // design.md §9.3) is read once here from its Storage and destroyPurchasedItems: both are scene settings that the save
    // doesn't carry.
    internal static class LiveTraders
    {
        internal struct SlotRule
        {
            internal string[] Allowed, Forbidden;
        }

        private static readonly Dictionary<Buyer, List<Trade>> Live = new Dictionary<Buyer, List<Trade>>();
        private static readonly Dictionary<Trade, SlotRule> Slots = new Dictionary<Trade, SlotRule>();
        private static readonly List<Trade> Empty = new List<Trade>();

        internal static int Version;

        internal static void OnScene()
        {
            Live.Clear();
            Slots.Clear();
            Version++;
            if (!TraderData.Ok) return;
            try
            {
                int matched = 0;
                var unmatched = new List<string>();
                foreach (var t in UnityEngine.Object.FindObjectsByType<Trade>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t == null) continue;
                    string scene = t.gameObject.scene.name ?? "";
                    var b = Match(t, scene);
                    if (b == null)
                    {
                        if (TraderData.Excluded.Contains(scene + "|" + (t.GUID ?? ""))) continue;
                        unmatched.Add(scene + "|" + (t.GUID ?? "") + " (" + Classify.Path(t.transform) + ")");
                        continue;
                    }
                    if (!Live.TryGetValue(b, out var list)) Live[b] = list = new List<Trade>(2);
                    list.Add(t);
                    var st = t.GetComponent<Storage>();
                    Slots[t] = st != null
                        ? new SlotRule { Allowed = st.AllowedCategories ?? new string[0], Forbidden = TraderData.SlotForbidden(st.ForbiddenCategories, t.destroyPurchasedItems) }
                        : new SlotRule { Allowed = b.SlotAllowed, Forbidden = b.SlotForbidden };
                    matched++;
                }
                Log.Verbose($"Traders: {matched} live traders matched in {SceneManager.GetActiveScene().name}.");
                if (unmatched.Count > 0)
                    Log.InfoOnce("unmatched|" + SceneManager.GetActiveScene().name, $"Traders:{unmatched.Count} trader(s) here are not in the bundled data and are not listed: " +
                        string.Join("; ", unmatched.ToArray()));
            }
            catch (Exception e)
            {
                Live.Clear();
                Slots.Clear();
                Log.Exception("Traders: scene scan", e);
            }
        }

        // The save restores trader fields at load; the Bought-by caches key on Version.
        internal static void Touch() => Version++;

        private static Buyer Match(Trade t, string scene)
        {
            if (TraderData.ByKey.TryGetValue(scene + "|" + (t.GUID ?? ""), out var b)) return b;
            if (!string.IsNullOrEmpty(t.sharedId) && TraderData.BySharedId.TryGetValue(t.sharedId, out b)) return b;
            return null;
        }

        // The live Trades of a Buyer in the loaded scenes (destroyed ones dropped), or an empty list.
        internal static List<Trade> For(Buyer b)
        {
            if (!Live.TryGetValue(b, out var list)) return Empty;
            list.RemoveAll(x => x == null);
            return list;
        }

        // The storage-slot rule of a live Trade (its Buyer's bundled rule if it wasn't in the snapshot).
        internal static SlotRule SlotsOf(Trade t, Buyer b) =>
            t != null && Slots.TryGetValue(t, out var r) ? r : new SlotRule { Allowed = b.SlotAllowed, Forbidden = b.SlotForbidden };

        // The instance whose fields stand for the Buyer: an active one first.
        internal static Trade Main(List<Trade> list)
        {
            Trade first = null;
            foreach (var t in list)
            {
                if (t == null) continue;
                if (t.isActiveAndEnabled) return t;
                if (first == null) first = t;
            }
            return first;
        }
    }
}
