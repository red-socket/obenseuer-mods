using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FairMarket
{
    // Resolves and caches each Trade's extra goods (spec §6.2). Resolved lazily on the first B1 call for that Trade, so
    // traders that start hidden are covered. The cache holds Trades of the current scene only: cleared on every scene
    // load and when a [Buyers] setting changes. A null entry means "no extras".
    internal static class BuyerRules
    {
        private static readonly Dictionary<Trade, Rules> Cache = new Dictionary<Trade, Rules>();
        private static List<CustomEntry> _custom;
        private static bool _validated;

        internal static void Clear()
        {
            Cache.Clear();
            _custom = null;
            _validated = false;
        }

        private static List<CustomEntry> Custom => _custom ?? (_custom = TraderKeys.ParseCustom(Cfg.Buyers.Custom.Value));

        internal static Rules For(Trade trade)
        {
            if (trade == null) return null;
            if (Cache.TryGetValue(trade, out var cached)) return cached;
            Rules r = null;
            try { r = Resolve(trade); }
            catch (Exception e) { Log.Exception("buyers resolve", e); r = null; }
            Cache[trade] = r;
            return r;
        }

        private static Rules Resolve(Trade trade)
        {
            ValidateOnce();
            string scene = TraderKeys.SceneOf(trade);
            Rules merged = null;
            foreach (var slot in Cfg.BuyerSlots)
            {
                if (string.IsNullOrEmpty(slot.Entry.Value) || !TraderKeys.MatchesSlot(trade, scene, slot)) continue;
                Add(ref merged, Tokens.Parse(slot.Entry.Value, slot.Key));
            }
            string sceneFold = Names.Fold(scene);
            foreach (var c in Custom)
            {
                if (c.Scene != sceneFold || !TraderKeys.MatchesWho(trade, c.Who)) continue;
                Add(ref merged, c.Rules);
            }
            if (merged == null || merged.Empty) return null;
            if (trade is TradeSpecial)
            {
                Log.WarnOnce("buyers special|" + merged.Source, $"Buyers: {merged.Source} names a certificate trader; refused (they need certificates).");
                return null;
            }
            // A new array per trade: the Trade's own forbiddenCategories is never mutated.
            merged.ForbiddenUnion = Rules.Union(trade.forbiddenCategories ?? new string[0], merged.ExcludeTokens);
            Log.Verbose($"Buyers: {merged.Source} -> '{trade.title}' in {scene} ({merged.Allow.Length} tokens, {merged.AllowIds.Count} items).");
            return merged;
        }

        // A copy, so one Rules object never carries two traders' ForbiddenUnion.
        private static void Add(ref Rules merged, Rules r)
        {
            if (r == null) return;
            if (merged == null) merged = new Rules();
            merged.Merge(r);
        }

        private static void ValidateOnce()
        {
            if (_validated || ItemDatabase.database == null || ItemDatabase.database.Count == 0) return;
            _validated = true;
            foreach (var slot in Cfg.BuyerSlots)
                if (!string.IsNullOrEmpty(slot.Entry.Value)) Tokens.Validate(Tokens.Parse(slot.Entry.Value, slot.Key));
            foreach (var c in Custom) Tokens.Validate(c.Rules);
        }

        internal static int ConfiguredCount()
        {
            int n = 0;
            foreach (var slot in Cfg.BuyerSlots) if (!string.IsNullOrEmpty(slot.Entry.Value?.Trim())) n++;
            return n + Custom.Count;
        }

        // On each scene load: a configured trader of this scene that matches no Trade, or a Custom entry that matches
        // several, is logged once (spec §6.2).
        internal static void CheckScene(Scene scene)
        {
            if (!Cfg.Hot.Buyers) return;
            Trade[] trades = null;
            string sceneFold = Names.Fold(scene.name);
            foreach (var slot in Cfg.BuyerSlots)
            {
                if (string.IsNullOrEmpty(slot.Entry.Value?.Trim()) || slot.Scene != scene.name) continue;
                trades = trades ?? AllTrades();
                if (!Array.Exists(trades, t => TraderKeys.MatchesSlot(t, scene.name, slot)))
                    Log.WarnOnce("buyers unresolved|" + slot.Key, $"Buyers: '{slot.Key}' found no trader in {scene.name}; that entry does nothing.");
            }
            foreach (var c in Custom)
            {
                if (c.Scene != sceneFold) continue;
                trades = trades ?? AllTrades();
                int n = Array.FindAll(trades, t => TraderKeys.MatchesWho(t, c.Who)).Length;
                if (n == 0) Log.WarnOnce("buyers unresolved|" + c.Text, $"Buyers: Custom '{c.Text}' found no trader in {scene.name}.");
                else if (n > 1) Log.WarnOnce("buyers several|" + c.Text, $"Buyers: Custom '{c.Text}' matches {n} traders in {scene.name}; all of them get the extras.");
            }
        }

        private static Trade[] AllTrades()
        {
            try { return UnityEngine.Object.FindObjectsByType<Trade>(FindObjectsInactive.Include, FindObjectsSortMode.None); }
            catch (Exception e) { Log.Exception("buyers scan", e); return new Trade[0]; }
        }
    }
}
