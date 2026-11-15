using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace BetterTenement
{
    // Content of the Landlord Ledger panel (spec §3.5.3), rebuilt at most once per second while the panel is shown.
    // Read-only: everything comes from vanilla getters and the controller's own lists.
    internal static class LedgerData
    {
        internal sealed class Line
        {
            internal string Text;
            internal bool Heading, Small;
        }

        private static List<Line> _lines = new List<Line>();
        private static float _nextBuild;

        internal static void Invalidate() => _nextBuild = 0f;

        internal static List<Line> Lines()
        {
            if (Time.unscaledTime < _nextBuild) return _lines;
            _nextBuild = Time.unscaledTime + 1f;
            try { _lines = Build(); }
            catch (Exception e)
            {
                Log.Exception("Ledger", e);
                _lines = new List<Line> { new Line { Text = "Ledger unavailable here (see the log).", Small = true } };
            }
            return _lines;
        }

        private static void H(List<Line> l, string t) => l.Add(new Line { Text = t, Heading = true });
        private static void T(List<Line> l, string t) => l.Add(new Line { Text = t });
        private static void S(List<Line> l, string t) => l.Add(new Line { Text = t, Small = true });

        private static string Rm(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static List<Line> Build()
        {
            var l = new List<Line>();
            var tc = TenementController.instance;
            var info = Game.Difficulty;
            if (tc == null || info == null || tc.apartments == null) { S(l, "No tenement data in this area."); return l; }
            string unit = info.GetRentPaymentTimeUnit();

            // 1. Rent per payday
            var income = tc.GetTotalIncome();
            H(l, "Rent per payday");
            T(l, $"{income.OC} OC + {Rm(income.RM)} RM / {unit}");

            // 2. Occupancy
            var flats = tc.apartments.Where(a => a != null).OrderBy(a => a.apartmentNumber).ToList();
            int rented = 0, ready = 0;
            var rows = new List<string>();
            foreach (var a in flats)
            {
                string state;
                if (a.evictionStarted) state = "being evicted";
                else if (a.moveStarted) state = "moving in";
                else if (a.onlyFurniture) state = "furniture left";
                else if (a.resident != null) { state = "rented"; rented++; }
                else if (a.IsApartmentAvailable()) { state = Skin.Color("vacant, ready", Skin.Amber); ready++; }
                else if (!a.examined) state = "not ready: not examined";
                else state = $"not ready: Group 1 {a.FurnitureClearPercentPrimary()}% / Group 2 {a.FurnitureClearPercentSecondary()}%";
                rows.Add($"Apt {a.apartmentNumber}: {state}");
            }
            H(l, "Occupancy");
            T(l, $"{rented} of {flats.Count} rented, {ready} vacant and ready");
            foreach (var r in rows) S(l, r);

            // 3. Renovations: the per-category progress fields, never currentUpgradeProgresses (it drops jobs awaiting payment).
            H(l, "Renovations");
            int jobs = 0;
            foreach (TenementController.GeneralUpgradeCategory cat in Enum.GetValues(typeof(TenementController.GeneralUpgradeCategory)))
                if (AddJob(l, tc.GetGeneralUpgradeProgress(cat), 0)) jobs++;
            foreach (var a in flats)
                foreach (var p in a.GetApartmentProgresses())
                    if (AddJob(l, p, a.apartmentNumber)) jobs++;
            if (jobs == 0) S(l, "None running.");
            S(l, "Renovations progress only in areas with tenement logic.");

            // 4. Shopping list for starred upgrades
            if (Cfg.LedgerShoppingList.Value) ShoppingList(l, tc, info);
            return l;
        }

        private static bool AddJob(List<Line> l, TenementUpgradeProgress p, int aptNo)
        {
            if (p == null || p.upgrade == null) return false;
            string name = (aptNo > 0 ? $"Apt {aptNo} " : "") + p.upgrade.GetName(aptNo, targetName: true) + ": " + p.upgrade.GetName(aptNo);
            var c = p.GetContractor();
            string who = c != null ? c.contractorName : "?";
            if (p.IsUpgradeReady()) T(l, $"{name} · {Skin.Color($"Ready: pay {p.upgrade.GetPriceFinalRM()} RM at {who}", Skin.Amber)}");
            else T(l, $"{name} · {Game.FirstName(who)} · {p.GetCurrentTime(false)}");
            return true;
        }

        private sealed class Need
        {
            internal int Id, Amount;
            internal string Name;
        }

        private static void ShoppingList(List<Line> l, TenementController tc, DifficultyInfo info)
        {
            H(l, "Shopping list (starred upgrades)");
            var marks = Vanilla.MarkedUpgrades != null ? Vanilla.MarkedUpgrades(tc) : null;
            var valid = new List<TenementUpgradeMarked>();
            if (marks != null)
            {
                foreach (var m in marks)
                {
                    TenementUpgrade u = null;
                    TenementContractor c = null;
                    try { u = m?.Upgrade; c = m?.Contractor; }
                    catch (Exception e) { Log.Exception("marked upgrade", e); }
                    if (u == null || c == null) { Log.WarnOnce("mark-null", "A starred upgrade has no upgrade or contractor; the shopping list skips it."); continue; }
                    valid.Add(m);
                }
            }
            if (valid.Count == 0) { S(l, "Star upgrades at a contractor to build a list."); return; }

            var live = Vanilla.CurrentSceneContractor != null ? Vanilla.CurrentSceneContractor(tc) : null;
            bool anyRemote = false;
            foreach (var group in valid.GroupBy(m => m.Contractor.Id))
            {
                var c = group.First().Contractor;
                bool remote = c != live;   // vanilla's own test (TenementController.cs:1110)
                anyRemote |= remote;
                T(l, $"<b>{c.contractorName}</b>");
                int oc = 0, rm = 0;
                var needs = new Dictionary<int, Need>();
                var unmet = new List<string>();
                foreach (var m in group)
                {
                    var u = m.Upgrade;
                    var apt = m.apartmentNumber > 0 ? tc.GetApartmentFromNumber(m.apartmentNumber) : null;
                    S(l, (m.apartmentNumber > 0 ? $"Apt {m.apartmentNumber} " : "") + u.GetName(m.apartmentNumber, targetName: true) + ": " + u.GetName(m.apartmentNumber));
                    oc += u.GetPriceStartOC();
                    rm += u.GetPriceFinalRM();
                    if (u.requiredUpgrades != null)
                        foreach (var r in u.requiredUpgrades)
                            if (r != null && !r.IsUpgraded(apt)) unmet.Add(r.GetName(m.apartmentNumber, targetName: true) + ": " + r.GetName(m.apartmentNumber));
                    if (u.requiredItems == null) continue;
                    foreach (var ri in u.requiredItems)
                    {
                        if (ri?.Item == null) continue;
                        int id = ri.Item.ID;
                        if (!needs.TryGetValue(id, out var n))
                            needs[id] = n = new Need { Id = id, Name = ri.Item.Item != null ? ri.Item.Item.Title : id.ToString() };
                        n.Amount += info.GetRenovationResourceCost(ri.itemAmount);   // scaled, unlike vanilla's summary above 5 marks
                    }
                }
                foreach (var n in needs.Values.OrderBy(x => x.Name))
                {
                    int have = (int)tc.GetItemAmount(n.Id, c);
                    int carried = Inventory.instance != null ? Inventory.instance.GetItemAmountInInventory(n.Id) : 0;
                    int chest = have - carried;
                    int stolenCarried = StolenCarried(n.Id);
                    string stolen = stolenCarried > 0 ? $"{stolenCarried} carried" : null;
                    if (!remote)
                    {
                        int sc = StolenInChests(tc, n.Id);
                        if (sc > 0) stolen = (stolen != null ? stolen + ", " : "") + $"{sc} in chest";
                    }
                    int missing = Math.Max(0, n.Amount - have);
                    string line = $"{n.Name}: need {n.Amount} · have {have} (chest {(remote ? "~" : "")}{chest}, carried {carried}" +
                                  (stolen != null ? $"; stolen {stolen}" : "") + ")";
                    if (missing > 0) line += " · " + Skin.Color($"missing {missing}", Skin.Amber);
                    T(l, line);
                }
                T(l, $"First payments {oc} OC · final payments {rm} RM");
                foreach (var u in unmet.Distinct()) T(l, Skin.Color("Needs first: " + u, Skin.Amber));
            }
            if (anyRemote) S(l, "~ = count from that contractor's chest at your last visit.");
        }

        private static int StolenCarried(int id)
        {
            int n = 0;
            if (Inventory.instance == null) return 0;
            foreach (var s in Inventory.instance.AllSlots())
                if (s?.itemStack != null && s.itemStack.itemId == id && s.itemStack.itemAmount > 0 && Materials.IsStolen(s.itemStack)) n += s.itemStack.itemAmount;
            return n;
        }

        private static int StolenInChests(TenementController tc, int id)
        {
            int n = 0;
            foreach (var chest in Game.ResourceChests(tc))
            {
                if (chest.Slots == null) continue;
                foreach (var s in chest.Slots)
                    if (s?.itemStack != null && s.itemStack.itemId == id && s.itemStack.itemAmount > 0 && Materials.IsStolen(s.itemStack)) n += s.itemStack.itemAmount;
            }
            return n;
        }
    }
}
