using System;
using System.Collections.Generic;
using OS.Items;
using UnityEngine;

namespace Workshop
{
    // One planned move: units of one source stack towards one recipe input.
    internal sealed class Move
    {
        internal Source Src;
        internal int ItemId;
        internal int Units;
        internal bool Whole;   // liquid containers and charged items always move as whole stacks
    }

    internal sealed class Plan
    {
        internal int K = 1;
        internal readonly List<Move> Moves = new List<Move>();
        internal List<Need> Needs = new List<Need>();
        internal readonly List<Need> Uncovered = new List<Need>();
        internal bool VariantMissing, NoRoom, NothingMissing;

        internal bool Complete => Uncovered.Count == 0 && !NoRoom;
    }

    // Spec §4.5: which stacks to move, and whether the station has room for them. The station's slots are simulated as
    // (item, amount) pairs: a move merges into an existing stack of the same item only where the game's own
    // SlotHasSpaceForItem accepts it, and otherwise takes an empty slot. A stack placed by an earlier move is never
    // merged into, so the plan can only need more slots than the real move uses, never fewer.
    internal static class Planner
    {
        private sealed class SimSlot
        {
            internal int Id = -1;
            internal int Amount;
            internal SlotController Real;
            internal bool Placed;
        }

        // The best plan for a station and recipe: ordinary stations k = 1; vessels the largest feasible k in
        // [max(1, cur + 1), kTarget] (binary search). Without a complete plan, the partial plan of whole inputs, or
        // (AllowPartial off) a plan that moves nothing.
        internal static Plan Best(CraftingBase station, Recipe r, List<Source> sources, out int kTarget, out int cur)
        {
            kTarget = 1;
            cur = 0;
            if (station is manufacturingProcess p)
            {
                VesselRange(p, r, out cur, out kTarget);
                if (cur >= kTarget) return new Plan { K = cur, NothingMissing = true };
                int lo = Math.Max(1, cur + 1), hi = kTarget;
                Plan best = null;
                var first = Make(station, r, lo, sources, wholeOnly: false);
                if (first.Complete)
                {
                    best = first;
                    int a = lo + 1, b = hi;
                    while (a <= b)
                    {
                        int mid = a + (b - a) / 2;
                        var plan = Make(station, r, mid, sources, wholeOnly: false);
                        if (plan.Complete) { best = plan; a = mid + 1; }
                        else b = mid - 1;
                    }
                    return best;
                }
                return Cfg.Fetch.AllowPartial.Value ? Make(station, r, lo, sources, wholeOnly: true) : Nothing(first);
            }
            var one = Make(station, r, 1, sources, wholeOnly: false);
            if (one.Complete || one.NothingMissing) return one;
            return Cfg.Fetch.AllowPartial.Value ? Make(station, r, 1, sources, wholeOnly: true) : Nothing(one);
        }

        // AllowPartial off: an incomplete plan moves nothing. Its needs stay for the "not found" note.
        private static Plan Nothing(Plan plan)
        {
            plan.Moves.Clear();
            return plan;
        }

        // The vessel's batch range (spec §4.2), with vanilla's clamp from GetRecipeMultiplierShowValue
        // (SRC/manufacturingProcess.cs:493-499). cur never uses GetRecipeMultiplier alone: it says 1 when an input is
        // missing entirely.
        internal static void VesselRange(manufacturingProcess p, Recipe r, out int cur, out int kTarget)
        {
            if (r.Input == null) r.SetItems();
            float inL = p.GetRecipeInputLiquidAmount(r), outL = p.GetRecipeOutputLiquidAmount(r);
            float num = inL > outL ? inL : outL;
            int cap = Math.Max(1, (int)(p.capacity / 10f / (num == 0f ? 1f : num)));
            cur = Vanilla.Compare(p, r) ? p.GetRecipeMultiplierShowValue(r) : 0;
            kTarget = Cfg.Fetch.VesselTarget.Value == VesselTarget.FullBatch ? cap : Math.Min(cap, cur + 1);
        }

        internal static Plan Make(CraftingBase station, Recipe r, int k, List<Source> sources, bool wholeOnly)
        {
            var plan = new Plan { K = k };
            plan.Needs = Shortfall.Of(station, r, k, out plan.VariantMissing);
            plan.NothingMissing = !Shortfall.AnyMissing(plan.Needs) && !plan.VariantMissing;
            var sim = Simulate(station);
            var used = new Dictionary<Source, int>();
            var stationMembers = StationMembers(station, r);

            foreach (var need in plan.Needs)
            {
                if (need.Missing <= Shortfall.Epsilon) continue;
                var simBefore = Clone(sim);
                var usedBefore = new Dictionary<Source, int>(used);
                int movesBefore = plan.Moves.Count;
                bool noRoom;
                float left = Cover(station, r, need, sources, sim, used, plan.Moves, stationMembers, out noRoom);
                if (left > Shortfall.Epsilon || noRoom)
                {
                    plan.Uncovered.Add(need);
                    if (noRoom) plan.NoRoom = true;
                    if (wholeOnly)
                    {
                        // Partial plans bring whole inputs only: drop what this input planned.
                        sim = simBefore;
                        used = usedBefore;
                        plan.Moves.RemoveRange(movesBefore, plan.Moves.Count - movesBefore);
                    }
                }
            }
            if (wholeOnly) plan.NoRoom = false;   // a partial plan is judged by what it moves
            return plan;
        }

        private sealed class Cand
        {
            internal Source Src;
            internal int Avail;
            internal float PerUnit, Total;
            internal int Member;
            internal bool Whole, Liquid;
            internal float Integrity;
        }

        private static float Cover(CraftingBase station, Recipe r, Need need, List<Source> sources, List<SimSlot> sim,
            Dictionary<Source, int> used, List<Move> moves, HashSet<int> stationMembers, out bool noRoom)
        {
            noRoom = false;
            float left = need.Missing;
            var cands = new List<Cand>();
            foreach (var s in sources)
            {
                var st = s.Sc.itemStack;
                if (st == null || st.itemAmount < 1) continue;
                used.TryGetValue(s, out int taken);
                int avail = st.itemAmount - taken;
                if (avail < 1) continue;
                float total = Shortfall.Contribution(need.Req, st, r);
                if (total <= 0f) continue;
                var liquid = ItemMetaUtilities.GetMetaOfType<ItemLiquidData>(st.Meta);
                bool isLiquid = liquid != null && liquid.liquidItem != null && liquid.GetLiquidAmount(st.itemAmount) > 0f && liquid.liquidItem.ID != st.itemId
                                && need.Item != null && st.itemId != need.Item.ID;
                bool charged = ItemMetaUtilities.GetMetaOfType<ItemAmmodata>(st.Meta) != null && r.metaSettings != null && r.metaSettings.useAmmoAsInput;
                bool whole = isLiquid || charged;
                if (whole && taken > 0) continue;
                var per = ItemMetaUtilities.GetMetaOfType<ItemPerishabledata>(st.Meta);
                cands.Add(new Cand
                {
                    Src = s, Avail = avail, Total = total, PerUnit = total / st.itemAmount, Whole = whole, Liquid = isLiquid,
                    Member = isLiquid ? liquid.liquidItem.ID : st.itemId,
                    Integrity = per != null ? per.integrity : 2f,
                });
            }
            if (cands.Count == 0) return left;
            var rank = new Dictionary<int, int>();

            // Groups (yeasts, grapes, cans of any water): the member already in the station first, then the member with
            // the most in one source. One member per input, unless no single member is enough.
            if (need.Req.isGroup)
            {
                var members = new List<int>();
                var best = new Dictionary<int, float>();
                var sum = new Dictionary<int, float>();
                foreach (var c in cands)
                {
                    if (!members.Contains(c.Member)) members.Add(c.Member);
                    float give = c.Whole ? c.Total : c.PerUnit * c.Avail;
                    best[c.Member] = Math.Max(best.TryGetValue(c.Member, out var b) ? b : 0f, give);
                    sum[c.Member] = (sum.TryGetValue(c.Member, out var t) ? t : 0f) + give;
                }
                members.Sort((a, b) =>
                {
                    bool ia = stationMembers.Contains(a), ib = stationMembers.Contains(b);
                    if (ia != ib) return ia ? -1 : 1;
                    return best[b].CompareTo(best[a]);
                });
                int pick = members.FindIndex(m => sum[m] + Shortfall.Epsilon >= left);
                if (pick >= 0)
                {
                    int member = members[pick];
                    cands.RemoveAll(c => c.Member != member);
                }
                else
                    for (int i = 0; i < members.Count; i++) rank[members[i]] = i;
            }

            // Members in that order; within a member, source order (inventory, bag, nearest storage), or least fresh first.
            bool oldest = Cfg.Fetch.OldestFirst.Value;
            StableSort(cands, (a, b) =>
            {
                int c = (rank.TryGetValue(a.Member, out var ra) ? ra : 0).CompareTo(rank.TryGetValue(b.Member, out var rb) ? rb : 0);
                return c != 0 || !oldest ? c : a.Integrity.CompareTo(b.Integrity);
            });

            var item = need.Item;
            while (left > Shortfall.Epsilon && cands.Count > 0)
            {
                Cand c;
                if (cands[0].Liquid)
                {
                    // The smallest single container that covers what is left, else the largest; whole containers move
                    // and vanilla RemoveLiquids takes only what the recipe needs.
                    c = null;
                    foreach (var x in cands)
                        if (x.Liquid && x.Total + Shortfall.Epsilon >= left && (c == null || x.Total < c.Total)) c = x;
                    if (c == null)
                        foreach (var x in cands)
                            if (c == null || x.Total > c.Total) c = x;
                }
                else c = cands[0];
                cands.Remove(c);

                int units = c.Whole ? c.Avail : Math.Min(c.Avail, Math.Max(1, Mathf.CeilToInt(left / c.PerUnit - 1e-3f)));
                var srcItem = c.Src.Sc.itemStack.itemReference?.Item ?? Game.ItemById(c.Src.Sc.itemStack.itemId);
                if (srcItem == null) continue;
                if (!Place(sim, srcItem, units, c.Src.Sc.itemStack))
                {
                    noRoom = true;
                    break;
                }
                moves.Add(new Move { Src = c.Src, ItemId = c.Src.Sc.itemStack.itemId, Units = units, Whole = c.Whole });
                used[c.Src] = (used.TryGetValue(c.Src, out var u) ? u : 0) + units;
                left -= c.Whole ? c.Total : units * c.PerUnit;
            }
            return Math.Max(0f, left);
        }

        // Member items of the recipe's groups already in the station (spec §4.5 "groups").
        private static HashSet<int> StationMembers(CraftingBase station, Recipe r)
        {
            var set = new HashSet<int>();
            try
            {
                if (r.Input == null) return set;
                var reqs = CraftingBaseHelperFunctions.CreateRequirements(r, onlyExecutable: true);
                foreach (var st in station.InputSlots)
                {
                    if (st == null || st.itemId < 0 || st.itemAmount < 1) continue;
                    foreach (var q in reqs)
                    {
                        if (!q.isGroup || Shortfall.Contribution(q, st, r) <= 0f) continue;
                        var liquid = ItemMetaUtilities.GetMetaOfType<ItemLiquidData>(st.Meta);
                        set.Add(liquid?.liquidItem != null && liquid.liquidItem.ID != st.itemId ? liquid.liquidItem.ID : st.itemId);
                    }
                }
            }
            catch (Exception e) { Log.Exception("station members", e); }
            return set;
        }

        private static List<SimSlot> Simulate(CraftingBase station)
        {
            var list = new List<SimSlot>();
            var slots = station.InputOutputStorage?.Slots;
            if (slots == null) return list;
            foreach (var sc in slots)
            {
                var st = sc?.itemStack;
                bool empty = st == null || st.itemId < 0 || st.itemAmount <= 0;
                list.Add(new SimSlot { Id = empty ? -1 : st.itemId, Amount = empty ? 0 : st.itemAmount, Real = sc });
            }
            return list;
        }

        private static List<SimSlot> Clone(List<SimSlot> sim)
        {
            var copy = new List<SimSlot>(sim.Count);
            foreach (var s in sim) copy.Add(new SimSlot { Id = s.Id, Amount = s.Amount, Real = s.Real, Placed = s.Placed });
            return copy;
        }

        private static bool Place(List<SimSlot> sim, Item item, int units, ItemStack src)
        {
            int stack = Math.Max(1, item.Stackable);
            foreach (var s in sim)
            {
                if (units <= 0) break;
                if (s.Placed || s.Id != item.ID || s.Real == null) continue;
                int room = stack - s.Amount;
                if (room <= 0) continue;
                int want = Math.Min(units, room);
                int fits;
                try
                {
                    s.Real.SlotHasSpaceForItem(item, out int rem, src.ownerId, want, 0, src.Meta, true);
                    fits = Mathf.Clamp(want - rem, 0, want);
                }
                catch { fits = 0; }
                s.Amount += fits;
                units -= fits;
            }
            foreach (var s in sim)
            {
                if (units <= 0) break;
                if (s.Id >= 0) continue;
                int put = Math.Min(units, stack);
                s.Id = item.ID;
                s.Amount = put;
                s.Placed = true;
                units -= put;
            }
            return units <= 0;
        }

        private static void StableSort<T>(List<T> list, Comparison<T> cmp)
        {
            var indexed = new List<(T v, int i)>(list.Count);
            for (int i = 0; i < list.Count; i++) indexed.Add((list[i], i));
            indexed.Sort((a, b) => { int c = cmp(a.v, b.v); return c != 0 ? c : a.i.CompareTo(b.i); });
            for (int i = 0; i < list.Count; i++) list[i] = indexed[i].v;
        }
    }
}
