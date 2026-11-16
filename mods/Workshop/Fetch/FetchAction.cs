using System;
using System.Collections.Generic;
using System.Linq;

namespace Workshop
{
    internal enum FetchState { NothingMissing, VesselFull, Full, Partial, NotFound }

    // The Fetch button's label and click (spec §4.1, §4.5). The label comes from a cached plan, keyed by station, recipe,
    // source snapshot and the station's contents, so hovering recipes stays cheap and the label does not jump.
    internal static class FetchAction
    {
        private static bool _busy;
        private static CraftingBase _cacheStation;
        private static int _cacheRecipe = int.MinValue, _cacheVersion = -1, _cacheContents;
        private static FetchState _cacheState;
        private static string _cacheLabel;

        internal static void ClearCache() => _cacheStation = null;

        internal static FetchState Label(CraftingBase station, Recipe r, out string label)
        {
            label = Strings.FetchNotFound;
            if (station == null || r == null) return FetchState.NotFound;
            try
            {
                var sources = Sources.Snapshot(station, fresh: false);
                int contents = Contents(station);
                if (station == _cacheStation && r.ID == _cacheRecipe && Sources.Version == _cacheVersion && contents == _cacheContents)
                {
                    label = _cacheLabel;
                    return _cacheState;
                }
                var plan = Planner.Best(station, r, sources, out _, out _);
                var state = StateOf(station, plan);
                label = LabelOf(state, plan, station is manufacturingProcess);
                _cacheStation = station;
                _cacheRecipe = r.ID;
                _cacheVersion = Sources.Version;
                _cacheContents = contents;
                _cacheState = state;
                _cacheLabel = label;
                return state;
            }
            catch (Exception e)
            {
                Log.Exception("fetch label", e);
                return FetchState.NotFound;
            }
        }

        private static FetchState StateOf(CraftingBase station, Plan plan)
        {
            if (plan.NothingMissing) return station is manufacturingProcess && plan.Moves.Count == 0 && plan.Needs.Count == 0 ? FetchState.VesselFull : FetchState.NothingMissing;
            if (plan.Complete && !plan.VariantMissing && plan.Moves.Count > 0) return FetchState.Full;
            return plan.Moves.Count > 0 ? FetchState.Partial : FetchState.NotFound;
        }

        private static string LabelOf(FetchState s, Plan plan, bool vessel)
        {
            switch (s)
            {
                case FetchState.NothingMissing: return Strings.FetchNothingMissing;
                case FetchState.VesselFull: return Strings.FetchVesselFull;
                case FetchState.Full: return vessel ? Strings.FetchForBatch(plan.K) : Strings.FetchAll;
                case FetchState.Partial: return Strings.FetchPartial;
                default: return Strings.FetchNotFound;
            }
        }

        // A cheap fingerprint of the station's slots.
        private static int Contents(CraftingBase station)
        {
            int h = 17;
            var slots = station.InputSlots;
            if (slots != null)
                foreach (var st in slots)
                    h = unchecked(h * 31 + (st == null ? 0 : st.itemId * 7919 + st.itemAmount));
            if (station is manufacturingProcess p) h = unchecked(h * 31 + (int)p.stage);
            return h;
        }

        // The button's click (mouse, or A with a controller). Main thread, one frame, never re-entered.
        internal static void Click(CraftingBase station, RecipeInfoUI info)
        {
            if (_busy) return;
            _busy = true;
            try { Run(station, info); }
            catch (Exception e) { Log.Exception("fetch", e); }
            finally { _busy = false; }
        }

        private static void Run(CraftingBase station, RecipeInfoUI info)
        {
            if (!Cfg.Fetch.Enabled.Value) return;
            if (!PlayGate.Open || SaveController.Loading) return;
            if (Game.DragActive || Game.SliderOpen || Game.TradeActive)
            {
                Notify.Show(Strings.FinishFirst);
                return;
            }
            if (station == null || !Vanilla.PanelOpen(station)) return;
            if (station is manufacturingProcess p && p.stage != manufacturingProcess.Stage.LoadingItems) return;
            var r = info != null ? info.CurrentRecipe() : null;
            if (r == null) return;

            var sources = Sources.Snapshot(station, fresh: true);
            var plan = Planner.Best(station, r, sources, out _, out _);
            Log.Verbose($"Fetch: {Stations.Name(station)}, {Stations.RecipeName(r)} ×{plan.K}: {plan.Moves.Count} moves, " +
                        $"{plan.Uncovered.Count} inputs not found, variant missing {plan.VariantMissing}, room short {plan.NoRoom}.");
            if (plan.NothingMissing) return;
            if (plan.Moves.Count == 0)
            {
                // Always shown: it answers a click on "Not found nearby". It names only the inputs that can't be covered
                // (with AllowPartial off, the ones that could be are not named).
                var notFound = new List<string>();
                foreach (var n in plan.Uncovered) notFound.Add(Strings.Amount(n.Item, n.Missing));
                Notify.Show(Strings.NotFoundNote(notFound, plan.VariantMissing ? Shortfall.VariantName(r) : null, plan.NoRoom));
                return;
            }

            var res = Mover.Run(station, plan);
            Sources.Clear();
            ClearCache();
            try { station.CheckItems(); }
            catch (Exception e) { Log.Exception("fetch refresh", e); }

            var still = MissingList(station, r, plan.K);
            bool variant = false;
            try { Shortfall.Of(station, r, plan.K, out variant); }
            catch { }
            Log.Verbose($"Fetch: moved {string.Join(", ", res.Moved.Select(kv => kv.Value + " × " + kv.Key).ToArray())}; still missing {still.Count}.");
            try { if (InventoryNavigationHandler.Instance != null) InventoryNavigationHandler.Instance.ExitRecipeOpenedPanel(); }
            catch (Exception e) { Log.Exception("fetch nav", e); }

            if ((still.Count > 0 || variant || res.StationFull) && Cfg.Fetch.ShowSummary.Value)
                Notify.Show(Strings.FetchSummary(Fetched(res), still, variant ? Shortfall.VariantName(r) : null, res.StationFull));
        }

        private static List<string> MissingList(CraftingBase station, Recipe r, int k)
        {
            var list = new List<string>();
            try
            {
                foreach (var n in Shortfall.Of(station, r, k, out _))
                    if (n.Missing > Shortfall.Epsilon) list.Add(Strings.Amount(n.Item, n.Missing));
            }
            catch (Exception e) { Log.Exception("fetch missing", e); }
            return list;
        }

        private static List<string> Fetched(Mover.Result res)
        {
            var list = new List<string>();
            foreach (var kv in res.Moved) list.Add(Strings.Amount(Game.ItemById(kv.Key), kv.Value));
            return list;
        }
    }
}
