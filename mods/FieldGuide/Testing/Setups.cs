#if FIELDGUIDE_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace FieldGuide
{
    // Test-only dumps and setters for the in-game test plan (spec §13.1), compiled only with -p:FieldGuideTests=true (the
    // Recipe Book precedent). Never packaged. Set [Testing] Setup to "name arg" while a game is running; it applies once
    // and resets itself to empty. The test build also shows the biogas gas bullets (HintTexts.GasConfirmed) for T-H5.
    internal static class Setups
    {
        private static ConfigEntry<string> _setup;
        private static GameObject _dummy;

        private static readonly Dictionary<string, Action<string[]>> All = new Dictionary<string, Action<string[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["buyers"] = a => Buyers(),
            ["places"] = a => Places(),
            ["hidden-clear"] = a => { Cfg.Popups.Hidden.Value = ""; Log.Warn("Testing: [Popups] Hidden cleared."); },
            ["hint"] = a => Hint(Str(a, 0)),
            ["hint-off"] = a => HintOff(),
        };

        internal static void Bind(ConfigFile c)
        {
            _setup = c.Bind("Testing", "Setup", "", "TEST BUILD ONLY: " + string.Join(", ", All.Keys.ToArray()));
            _setup.SettingChanged += (s, a) => Run();
            Log.Warn("Testing: this is a test build with dumps and setters. Never package it.");
        }

        private static string Str(string[] a, int i) => i < a.Length ? a[i] : throw new ArgumentException("argument " + (i + 1) + " missing");

        private static void Run()
        {
            string line = (_setup.Value ?? "").Trim();
            if (line.Length == 0) return;
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!All.TryGetValue(parts[0], out var act) || GlobalState.instance == null)
                Log.Warn($"Testing: no setup '{parts[0]}' or no game running.");
            else
            {
                try { act(parts.Skip(1).ToArray()); Log.Warn($"Testing: applied '{line}'."); }
                catch (Exception e) { Log.Exception("test setup", e); }
            }
            _setup.Value = "";
        }

        // Every Buyer for the last hovered item: accepts, stolen, price, live, visible (spec §13.1). Uses the hovered
        // ItemData so the live path (CheckIfAllowed with Fair Market's B1) runs as in the tooltip.
        private static void Buyers()
        {
            var d = ItemData.currentHoverItemData;
            if (d == null || d.item == null) { Log.Warn("Testing: hover an item first."); return; }
            var sb = new StringBuilder();
            int owner = d.owner;
            var meta = d.meta;
            sb.Append($"Testing: buyers of {d.item.ID} {d.item.Title} owner {owner} trading {(Game.Trading != null ? Game.Trading.GUID : "-")}\n");
            foreach (var b in TraderData.Buyers)
            {
                var lives = LiveTraders.For(b);
                var live = lives.Count > 0 ? LiveTraders.Main(lives) : null;
                bool accepts = live != null ? live.CheckIfAllowed(d) : SlotController.CheckIfInAllowedCategoy(d.item, b.Allowed, b.Forbidden, meta);
                var slots = live != null ? LiveTraders.SlotsOf(live, b) : new LiveTraders.SlotRule { Allowed = b.SlotAllowed, Forbidden = b.SlotForbidden };
                bool slotOk = SlotController.CheckIfInAllowedCategoy(d.item, slots.Allowed, slots.Forbidden);
                bool stolen = live != null ? live.IsStolen(d) : (owner == b.OwnerId && b.OwnerId > 0) || (owner > 0 && !b.BuysStolen);
                float sell = live != null ? live.baseSellPrice : b.Sell;
                float sf = live != null ? live.stolenPriceFactor : b.StolenFactor;
                var cur = live != null ? (live.sellPriceMode == Trade.PriceMode.OnlyRM ? Money.Currency.RM : Money.Currency.OC) : b.Currency;
                float p = Trade.GetItemPrice(d.item, owner, meta, cur, sell, sf);
                sb.Append($"  {b.Name}: accepts {accepts}, slots {slotOk}, stolen-refused {stolen}, price {p.ToString(CultureInfo.InvariantCulture)} {cur}, " +
                          $"live {(live != null ? lives.Count.ToString(CultureInfo.InvariantCulture) : "no")}, visited {Visited.Any(b.Scenes)}, cert {b.Certificate}\n");
            }
            var r = BoughtBy.Evaluate(d, owner, meta, new BoughtBy.Result(), out _);
            sb.Append($"  => shown {(r != null ? r.Shown.Count : -1)}, hidden {(r != null ? r.Hidden : -1)}, " +
                      $"fair market extra {(r != null && r.FairMarketExtra)}\n");
            sb.Append("  tooltip: ").Append(BoughtBy.TooltipBlock(d, owner, meta)?.Replace("\n", "\\n") ?? "(none)").Append('\n');
            var cands = BoughtBy.PanelCandidates(d);
            sb.Append("  panel: ").Append(cands != null ? string.Join(" | ", cands.ToArray()) : "(none)");
            Log.Warn(sb.ToString());
        }

        private static void Places()
        {
            var u = Visited.Union().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Log.Warn($"Testing: {u.Length} places (version {Visited.Version}): {string.Join(", ", u)}");
        }

        private static void Hint(string kindName)
        {
            HelpKind? kind = null;
            foreach (var k in Kinds.All)
                if (string.Equals(Kinds.Name(k), kindName, StringComparison.OrdinalIgnoreCase)) kind = k;
            if (kind == null) throw new ArgumentException("unknown kind " + kindName);
            HintOff();
            _dummy = new GameObject("FieldGuide test hint");
            HintHost.Open(_dummy, kind.Value, HintTexts.Build(kind.Value, null), station: false);
        }

        private static void HintOff()
        {
            if (_dummy == null) return;
            HintHost.Close(_dummy);
            UnityEngine.Object.Destroy(_dummy);
            _dummy = null;
        }
    }
}
#endif
