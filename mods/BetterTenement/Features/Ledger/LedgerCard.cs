using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BetterTenement
{
    // S-055 on the upgrade cards and floor plan tooltips (spec §3.5.2).
    internal static class LedgerCard
    {
        internal static bool TooltipPatched;

        private const string LineName = "BT_Ledger";

        // C1 postfix: a line in the free strip right of "Required items": value and rent change per payday, and who
        // does the tier. Cards are recreated on every UpgradePanel.OpenAndUpdate, so there is no duplicate risk.
        internal static void CardLine(UpgradeUI card, TenementApartment apartment, TenementUpgrade upgrade, UpgradeButtonUI button)
        {
            if (card == null || upgrade == null || Vanilla.UpgradeUITime == null) return;
            var time = Vanilla.UpgradeUITime(card);
            if (time == null) return;

            var size = apartment != null && apartment.type != null ? apartment.type.size : UpgradeType.ApartmentType.Large;
            var who = Contractors.WhoCanDo(upgrade, size);
            string text;
            if (time.text == "-")   // EmptyUpgrade(): the contractor here cannot do it
            {
                string names = Contractors.Names(who, firstNames: false);
                text = names != null ? "Done by " + names : "No contractor offers this tier yet";
            }
            else
            {
                string by = Contractors.Names(who, firstNames: true);
                string effect = LedgerMath.Describe(LedgerMath.Compute(apartment, upgrade));
                var parts = new List<string>();
                if (effect != null) parts.Add(effect);
                if (by != null) parts.Add("By " + by);
                if (parts.Count == 0) return;
                text = string.Join(" · ", parts);
            }

            var tmp = LineFor(card, time);
            if (tmp != null) tmp.text = text;
        }

        private static TextMeshProUGUI LineFor(UpgradeUI card, TextMeshProUGUI time)
        {
            var existing = card.transform.Find(LineName);
            if (existing != null) return existing.GetComponent<TextMeshProUGUI>();
            var go = Object.Instantiate(time.gameObject, card.transform, false);
            go.name = LineName;
            foreach (Transform child in go.transform) Object.Destroy(child.gameObject);
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Cfg.CardOffset();
            rt.sizeDelta = new Vector2(255f, 28f);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.richText = true;
            tmp.alignment = TextAlignmentOptions.Right;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 11f;
            tmp.fontSizeMax = 18f;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            return tmp;
        }

        // C3: UpgradeButtonUI.UpdateToolTip runs every frame while a renovation ticks (UpgradeButtonUI.cs:280-286) and
        // ends with ToolTipInfo.UpdateToolTipInfo(title, text), which re-activates the tooltip when it is shown. The
        // block is appended to that call's details argument (C4 prefix), so the shown tooltip is built once per frame
        // as in vanilla. Per button, the block is cached on a key of plain ints (Ledger.Version is a one-second tick,
        // so rent changes are at most a second stale), and the composed text is reused while the vanilla text is unchanged.
        private sealed class Tip
        {
            internal int Upgrade, Resident, Rent, Version = int.MinValue;
            internal string Block = "";
            internal string Vanilla, Composed;
        }

        private static readonly Dictionary<int, Tip> Cache = new Dictionary<int, Tip>();
        private static ToolTipInfo _pendingTti;
        private static Tip _pending;

        internal static void OnSceneLoaded()
        {
            Cache.Clear();
            _pendingTti = null;
            _pending = null;
        }

        // C3 prefix: work out the block for this button and arm the C4 prefix for its ToolTipInfo.
        internal static void Begin(UpgradeButtonUI b)
        {
            _pendingTti = null;
            _pending = null;
            if (!TooltipPatched || !Cfg.LedgerCategoryTooltip.Value || Vanilla.TooltipTitle == null || Vanilla.TooltipDetails == null
                || Vanilla.ButtonCurrentUpgrade == null || Vanilla.ButtonApartmentNumber == null) return;
            var tti = b.GetComponent<ToolTipInfo>();
            if (tti == null) return;

            var current = Vanilla.ButtonCurrentUpgrade(b);
            bool general = Vanilla.ButtonTenementUpgrade != null && Vanilla.ButtonTenementUpgrade(b);
            var apartment = general ? null : b.Apartment;
            var info = Game.Difficulty;
            int up = current != null ? current.GetInstanceID() : 0;
            int res = apartment != null && apartment.resident != null ? apartment.resident.GetInstanceID() : 0;
            int rent = info != null ? (int)info.rentPayments : -1;
            int ver = Ledger.Version;

            int id = b.GetInstanceID();
            if (!Cache.TryGetValue(id, out var tip)) Cache[id] = tip = new Tip();
            if (tip.Version != ver || tip.Upgrade != up || tip.Resident != res || tip.Rent != rent)
            {
                string block = BuildBlock(b, current, apartment, Vanilla.ButtonApartmentNumber(b)) ?? "";
                if (block != tip.Block) { tip.Block = block; tip.Vanilla = null; tip.Composed = null; }
                tip.Upgrade = up; tip.Resident = res; tip.Rent = rent; tip.Version = ver;
            }
            if (tip.Block.Length == 0) return;
            _pendingTti = tti;
            _pending = tip;
        }

        // C4 prefix: ToolTipInfo.UpdateToolTipInfo for the armed button only.
        internal static void Append(ToolTipInfo tti, ref string details)
        {
            if (_pending == null || !ReferenceEquals(tti, _pendingTti)) return;
            details = Compose(_pending, details ?? "");
        }

        private static string Compose(Tip tip, string vanilla)
        {
            if (vanilla.EndsWith(tip.Block, StringComparison.Ordinal)) return vanilla;
            if (tip.Composed != null && string.Equals(vanilla, tip.Vanilla, StringComparison.Ordinal)) return tip.Composed;
            tip.Vanilla = vanilla;
            tip.Composed = vanilla + tip.Block;
            return tip.Composed;
        }

        // C3 postfix: disarm; if the C4 prefix did not get to run (a ToolTipInfo subclass that overrides
        // UpdateToolTipInfo), set the text the old way.
        internal static void End()
        {
            var tti = _pendingTti;
            var tip = _pending;
            _pendingTti = null;
            _pending = null;
            if (tti == null || tip == null) return;
            string details = Vanilla.TooltipDetails(tti) ?? "";
            if (details.EndsWith(tip.Block, StringComparison.Ordinal)) return;
            tti.UpdateToolTipInfo(Vanilla.TooltipTitle(tti), Compose(tip, details));
        }

        internal static void Disarm()
        {
            _pendingTti = null;
            _pending = null;
        }

        private static string BuildBlock(UpgradeButtonUI b, TenementUpgrade current, TenementApartment apartment, int aptNo)
        {
            var line = b.Upgrades;
            if (line == null || line.Length == 0) return "";
            TenementUpgrade next = null;
            if (current == null) next = line[0];
            else
            {
                int i = LedgerMath.IndexIn(line, current);
                if (i >= 0 && i + 1 < line.Length) next = line[i + 1];
            }
            if (next == null) return "";   // at max
            var size = apartment != null && apartment.type != null ? apartment.type.size : UpgradeType.ApartmentType.Large;
            string who = Contractors.Names(Contractors.WhoCanDo(next, size), firstNames: false) ?? "no contractor yet";
            string effect = LedgerMath.Describe(LedgerMath.Compute(apartment, next)) ?? "";
            return $"\nNext: {next.GetName(aptNo)} · {who}" + (effect.Length > 0 ? "\n" + effect : "");
        }
    }

    // Ledger refresh tick shared by the tooltip cache.
    internal static class Ledger
    {
        internal static int Version => (int)Time.unscaledTime;
    }

    // C3: UpgradeButtonUI.UpdateToolTip() (private, SRC/UpgradeButtonUI.cs:332).
    [HarmonyPatch(typeof(UpgradeButtonUI), "UpdateToolTip")]
    internal static class UpgradeButtonTooltipPatch
    {
        private static void Prefix(UpgradeButtonUI __instance)
        {
            try { LedgerCard.Begin(__instance); }
            catch (Exception e) { LedgerCard.Disarm(); Log.Exception("Ledger tooltip", e); }
        }

        private static void Postfix()
        {
            try { LedgerCard.End(); }
            catch (Exception e) { Log.Exception("Ledger tooltip", e); }
        }

        private static Exception Finalizer(Exception __exception)
        {
            LedgerCard.Disarm();
            return __exception;
        }
    }

    // C4: ToolTipInfo.UpdateToolTipInfo(string, string, bool) (SRC/ToolTipInfo.cs:25). Does nothing unless C3 armed it
    // for this very ToolTipInfo.
    [HarmonyPatch(typeof(ToolTipInfo), nameof(ToolTipInfo.UpdateToolTipInfo), new[] { typeof(string), typeof(string), typeof(bool) })]
    internal static class TooltipInfoPatch
    {
        private static void Prefix(ToolTipInfo __instance, ref string details)
        {
            try { LedgerCard.Append(__instance, ref details); }
            catch (Exception e) { Log.Exception("Ledger tooltip text", e); }
        }
    }
}
