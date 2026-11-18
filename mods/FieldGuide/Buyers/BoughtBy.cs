using System;
using System.Collections.Generic;
using System.Text;
using OS.Items;

namespace FieldGuide
{
    // Who buys an item, and for how much (spec §9.3, §9.4). Read-only: the game's own static checks and price formula are
    // called, never re-implemented (SlotController.CheckIfInAllowedCategoy, Trade.GetItemPrice), and traders in the loaded
    // scenes go through their live Trade.CheckIfAllowed / IsStolen (D18, D24). Scope carries the tooltip block from
    // ToolTip.ConstructDataString (T1) to the TooltipUI.UpdateTooltip calls it makes (T2); outside that call it is null,
    // so HoverTip, ToolTipInfo, Vital Signs, Better Tenement, Quest Helper and the task items are never touched.
    //
    // Cost (§9.5): which traders take an item doesn't depend on its sale price, so each surface keeps its last
    // evaluation under a key without the price (item, owner, liquid, meta types, open trader, and the config, places,
    // live-trader and Fair Market stamps). A hit re-prices only the k rows shown, which is all that changes while
    // LiquidStoragePanelUI fills a container and calls UpdateInfo every frame.
    internal static class BoughtBy
    {
        internal static string Scope;

        internal struct Row
        {
            internal Buyer B;
            internal float Eff, Sell, Sf;
            internal Money.Currency Cur;
        }

        internal struct Key : IEquatable<Key>
        {
            internal int Item, Owner, Liquid, Meta, Trading, Cfg, Places, Live, FairMarket;
            internal bool Also;

            public bool Equals(Key o) => Item == o.Item && Owner == o.Owner && Liquid == o.Liquid && Meta == o.Meta &&
                Trading == o.Trading && Cfg == o.Cfg && Places == o.Places && Live == o.Live && FairMarket == o.FairMarket &&
                Also == o.Also;

            public override bool Equals(object o) => o is Key k && Equals(k);
            public override int GetHashCode() => Item * 397 ^ Owner * 31 ^ Liquid ^ Meta;
        }

        // One evaluation, cached per surface. Shown is sorted by Eff descending, then name; Hidden counts accepting
        // traders in places the player hasn't been (Traders = Visited). Labels are priced for Sale and reset when it
        // changes; Text and Cands are the formatted surface strings for Sale.
        internal sealed class Result
        {
            internal readonly List<Row> Shown = new List<Row>();
            internal int Hidden;
            internal bool Also, FairMarketExtra;
            internal Item Item;
            internal int Owner;
            internal object[] Meta;
            internal readonly List<string> Labels = new List<string>();
            internal int LabelSale;

            internal Key Key;
            internal bool Valid;
            internal int TextSale;
            internal bool TextValid;
            internal string Text;
            internal List<string> Cands;

            internal void Reset()
            {
                Valid = TextValid = false;
                Text = null;
                Cands = null;
                Labels.Clear();
                Shown.Clear();
            }
        }

        private static readonly StringBuilder Sb = new StringBuilder(256);
        private static readonly Comparison<Row> ByEff = (a, b) =>
        {
            int c = b.Eff.CompareTo(a.Eff);
            return c != 0 ? c : string.CompareOrdinal(a.B.Name, b.B.Name);
        };

        // One per surface, so the hover tooltip and the item panel (which may show different items) don't evict each other.
        private static readonly Result Tip = new Result(), Panel = new Result();

        internal static void Clear()
        {
            Tip.Reset();
            Panel.Reset();
            Scope = null;
        }

        // ---- Tooltip (T1, T2) ----

        internal static string TooltipBlock(ItemData d, int owner, object[] meta)
        {
            if (!Cfg.Hot.TooltipOn || !TraderData.Ok) return null;
            var r = Evaluate(d, owner, meta, Tip, out int sale);
            if (r == null) return null;
            if (r.TextValid && r.TextSale == sale) return r.Text;
            r.Text = FormatTooltip(r);
            r.TextSale = sale;
            r.TextValid = true;
            return r.Text;
        }

        // \n\n<b>Bought by:</b> <noparse>A (8 OC) · B (6 OC) · C (1 RM)</noparse> (+2 more; +1 in places you haven't been)
        private static string FormatTooltip(Result r)
        {
            int hidden = !Cfg.Hot.AllTraders && Cfg.Hot.CountUnvisited ? r.Hidden : 0;
            if (r.Shown.Count == 0 && hidden == 0) return null;
            Sb.Length = 0;
            Sb.Append("\n\n<b>").Append(r.Also ? Strings.AlsoBoughtBy : Strings.BoughtBy).Append("</b> ");
            if (r.Shown.Count == 0) Sb.Append(Strings.UnvisitedOnly(hidden));
            else
            {
                int k = Math.Min(r.Shown.Count, Cfg.Hot.TooltipNames);
                Labels(r, k);
                Sb.Append("<noparse>");
                for (int i = 0; i < k; i++)
                {
                    if (i > 0) Sb.Append(Strings.Separator);
                    Sb.Append(r.Labels[i]);
                }
                Sb.Append("</noparse>");
                int more = r.Shown.Count - k;
                if (more > 0 && hidden > 0) Sb.Append(" (").Append(Strings.More(more)).Append("; ").Append(Strings.Unvisited(hidden)).Append(')');
                else if (more > 0) Sb.Append(" (").Append(Strings.More(more)).Append(')');
                else if (hidden > 0) Sb.Append(" (").Append(Strings.Unvisited(hidden)).Append(')');
            }
            // Only when a Fair Market extra buyer is in this answer (D18): elsewhere its extras aren't known.
            if (r.FairMarketExtra) Sb.Append("\n<noparse>").Append(Strings.FairMarketNote).Append("</noparse>");
            return Sb.ToString();
        }

        // ---- Item panel (P1) ----

        // The candidate lines, fullest first (spec §9.4): k names with prices for k = PanelNames..1, then the count.
        // Plain text: the panel has rich text off. Null when there is no buyer to show (hidden counts are tooltip only).
        internal static List<string> PanelCandidates(ItemData d)
        {
            if (!Cfg.Hot.PanelOn || !TraderData.Ok || d == null) return null;
            var r = Evaluate(d, d.owner, d.meta, Panel, out int sale);
            if (r == null || r.Shown.Count == 0) return null;
            if (r.TextValid && r.TextSale == sale) return r.Cands;
            var list = new List<string>(4);
            int max = Math.Min(r.Shown.Count, Cfg.Hot.PanelNames);
            Labels(r, max);
            string header = r.Also ? Strings.AlsoBoughtBy : Strings.BoughtBy;
            for (int k = max; k >= 1; k--)
            {
                Sb.Length = 0;
                Sb.Append(header).Append(' ');
                for (int i = 0; i < k; i++)
                {
                    if (i > 0) Sb.Append(Strings.Separator);
                    Sb.Append(r.Labels[i]);
                }
                int more = r.Shown.Count - k;
                if (more > 0) Sb.Append(" (").Append(Strings.More(more)).Append(')');
                list.Add(Sb.ToString());
            }
            list.Add(Strings.Count(r.Shown.Count, r.Also));
            r.Cands = list;
            r.TextSale = sale;
            r.TextValid = true;
            return list;
        }

        // ---- Evaluation (spec §9.3) ----

        // Null when the item gets no line at all (money, quest items, trader-side items in the trade window, no game).
        // `into` is the surface's cache; on a key hit only the price stamp is refreshed.
        internal static Result Evaluate(ItemData d, int owner, object[] meta, Result into, out int sale)
        {
            sale = 0;
            var item = d != null ? d.item : null;
            if (item == null || item.ID == 0 || item.ID == 1) return null;                        // money (D21)
            if (item.Categories != null && Array.IndexOf(item.Categories, "QuestItem") >= 0) return null; // quest items (D21)
            if (Money.instance == null) return null;

            Trade trading = Game.Trading;
            if (trading != null)
            {
                // D20: only items on the player's side, and only when InTradeWindow is on.
                if (!Cfg.Hot.InTradeWindow) return null;
                var slot = d.slotController != null ? d.slotController.slot : null;
                if (slot == null || !slot.playerSlot) return null;
            }

            var liq = Item.GetMetaOfType<ItemLiquidData>(meta);
            var key = new Key
            {
                Item = item.ID, Owner = owner,
                Liquid = liq != null && liq.liquidItem != null ? liq.liquidItem.ID : -1,
                Meta = MetaTypes(meta),
                Trading = trading != null ? trading.GetInstanceID() : 0,
                Also = trading != null,
                Cfg = Cfg.Hot.Version, Places = Visited.Version, Live = LiveTraders.Version, FairMarket = Compat.FairMarketVersion,
            };
            sale = item.GetItemSalePrice(meta);

            var r = into;
            if (!(r.Valid && r.Key.Equals(key)))
            {
                r.Reset();
                Accept(r, d, item, owner, meta, trading);
                r.Key = key;
                r.Valid = true;
            }
            r.Item = item;
            r.Owner = owner;
            r.Meta = meta;
            if (r.LabelSale != sale || r.Labels.Count == 0)
            {
                r.Labels.Clear();
                r.LabelSale = sale;
            }
            return r;
        }

        // Which traders take the item, at what factor. Mirrors Trade.CanSell (SRC/Trade.cs:519-541) without the money and
        // free-space parts: IsStolen, CheckIfAllowed, and the part of CheckIfFull that refuses whatever the space, the
        // storage slots' categories (SlotController.SlotHasSpaceForItem → CheckIfAllowed(item), meta null,
        // SRC/SlotController.cs:821-826).
        private static void Accept(Result r, ItemData d, Item item, int owner, object[] meta, Trade trading)
        {
            r.Hidden = 0;
            r.Also = trading != null;
            r.FairMarketExtra = false;
            bool fm = Compat.FairMarketBuyers;
            foreach (var b in TraderData.Buyers)
            {
                if (b.Certificate && !Cfg.Hot.CertShops) continue;
                var lives = LiveTraders.For(b);
                var live = lives.Count > 0 ? LiveTraders.Main(lives) : null;
                float sell, sf;
                Money.Currency cur;
                bool extra = false;
                if (live != null)
                {
                    if (trading != null && lives.Contains(trading)) continue; // the open trader (D20)
                    if (!live.CheckIfAllowed(d) || Stolen(live, d, owner)) continue;
                    var slots = LiveTraders.SlotsOf(live, b);
                    if (!SlotController.CheckIfInAllowedCategoy(item, slots.Allowed, slots.Forbidden)) continue;
                    // Accepted by the live check but not by the scene's rules: one of Fair Market's extra buyers (D18).
                    extra = fm && !SlotController.CheckIfInAllowedCategoy(item, live.allowedCategories, live.forbiddenCategories, meta);
                    sell = live.baseSellPrice;
                    cur = live.sellPriceMode == Trade.PriceMode.OnlyRM ? Money.Currency.RM : Money.Currency.OC;
                    sf = live.stolenPriceFactor;
                }
                else
                {
                    if (!SlotController.CheckIfInAllowedCategoy(item, b.Allowed, b.Forbidden, meta)) continue;
                    // Trade.IsStolen (SRC/Trade.cs:544-555): never the item's own owner; owner > 0 needs buysStolen.
                    if ((owner == b.OwnerId && b.OwnerId > 0) || (owner > 0 && !b.BuysStolen)) continue;
                    if (!SlotController.CheckIfInAllowedCategoy(item, b.SlotAllowed, b.SlotForbidden)) continue;
                    sell = b.Sell;
                    cur = b.Currency;
                    sf = b.StolenFactor;
                }
                // A factor means the same in either currency (1 RM = 10 OC, SRC/Money.cs:163), so no currency factor (§0).
                var row = new Row { B = b, Sell = sell, Sf = sf, Cur = cur, Eff = sell * (owner > 0 ? sf : 1f) };
                if (Cfg.Hot.AllTraders || live != null || Visited.Any(b.Scenes))
                {
                    r.Shown.Add(row);
                    if (extra) r.FairMarketExtra = true;
                }
                else r.Hidden++;
            }
            r.Shown.Sort(ByEff);
        }

        // The meta types an item carries: the category check reads them through `_meta_` tokens (HasMetaOfType).
        private static int MetaTypes(object[] meta)
        {
            if (meta == null) return 0;
            int h = 17;
            for (int i = 0; i < meta.Length; i++)
                h = h * 31 + (meta[i] != null ? meta[i].GetType().GetHashCode() : 0);
            return h;
        }

        // The game's Trade.IsStolen (it reads d.owner, the same value as `owner` on both surfaces). A Trade with no owner
        // reference would throw there, so that case runs the same rule with owner 0.
        private static bool Stolen(Trade t, ItemData d, int owner)
        {
            if (t.owner != null && d.owner == owner) return t.IsStolen(d);
            int traderOwner = t.owner != null ? t.owner.ID : 0;
            if (owner == traderOwner && traderOwner > 0) return true;
            return owner > 0 && !t.buysStolen;
        }

        // "Name (price)" for the first k rows, priced by the game's own formula for one item (D12).
        private static void Labels(Result r, int k)
        {
            for (int i = r.Labels.Count; i < k && i < r.Shown.Count; i++)
            {
                var row = r.Shown[i];
                float p = Trade.GetItemPrice(r.Item, r.Owner, r.Meta, row.Cur, row.Sell, row.Sf);
                string price = row.Cur == Money.Currency.RM ? Strings.Rm(Money.DisplayRm(p)) : Strings.Oc(p);
                r.Labels.Add(Strings.Name(row.B.Name) + " (" + Strings.Price(price, row.B.Certificate) + ")");
            }
        }
    }
}
