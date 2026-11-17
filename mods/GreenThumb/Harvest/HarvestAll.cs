using System;
using UnityEngine;

namespace GreenThumb
{
    // S-070 harvest-all (spec §9.1-9.2, §9.6, D13-D17). Vanilla already sweeps while Use is held: 0.4 s after the press it
    // calls Growing.RepeatHarvest(hit point) every frame, which harvests the one ready plant within 0.5 m of the crosshair
    // hit, at most every 0.2 s (SRC/GameController.cs:252-283, InteractObjects.cs:436-446, Growing.cs:782-813). Held for
    // HoldSeconds in total, H1 extends that one step: when vanilla's own choice harvested nothing, Step harvests the
    // nearest ready plant of the same grower that is within hand reach, through vanilla grow.Harvest. No input is read:
    // the press is identified by GameController.repeatHarvestReadyTime, so it works on keyboard E and pad A, first and
    // third person, wherever vanilla's hold does. One plant per >= 0.2 s, so witness odds stay vanilla. Writes nothing of
    // its own.
    internal static class HarvestAll
    {
        internal static bool Patched;
        // Another mod can skip RepeatHarvest (Compat.Foreign at the first level load): harvest-all stands down.
        internal static bool ForeignSkip;

        // H3 scope: set only inside Step (try/finally), and never for someone else's plants.
        internal static Growing ScopeGrowing;

        private static float _pressId = float.PositiveInfinity, _tainted = float.NaN, _nextScan;
        private static int _count;
        private static string _from, _stop;
        private static bool _warned;

        // Plugin.Update. Tracks the press: a new repeatHarvestReadyTime value is a new press. A press that started the
        // uproot ring (HoldToUseController) never becomes harvest-all, even after the ring finishes (D13, T-H9).
        internal static void TrackPress()
        {
            if (!Patched) return;
            var gc = GameController.instance;
            float ready = gc != null ? Vanilla.RepeatHarvestReadyTime(gc) : float.PositiveInfinity;
            if (ready != _pressId)
            {
                EndPress(float.IsPositiveInfinity(ready) ? "released" : null);
                _pressId = ready;
            }
            if (float.IsPositiveInfinity(ready)) return;
            var h = HoldToUseController.instance;
            if (h != null && h.IsUpdating) _tainted = ready;
        }

        private static void EndPress(string reason)
        {
            if (_count > 0 || _stop != null)
                Log.Verbose($"Harvest all: {_count} plants from {_from ?? "?"}; stopped: {_stop ?? reason ?? "released"}");
            _count = 0;
            _from = _stop = null;
            _warned = false;
            _nextScan = 0f;
        }

        internal static bool Armed
        {
            get
            {
                if (!Cfg.Hot.HarvestAll) return false;
                var gc = GameController.instance;
                if (gc == null) return false;
                float ready = Vanilla.RepeatHarvestReadyTime(gc);
                if (float.IsPositiveInfinity(ready) || ready == _tainted) return false;
                var h = HoldToUseController.instance;
                if (h != null && h.IsUpdating) return false;
                if (Time.unscaledTime < ready - Vanilla.RepeatHarvestHoldDelay + Cfg.Hot.HoldSeconds) return false;
                if (!PlayGate.Open) return false;
                // InteractControls also runs in the build menu's RemoveOnly mode.
                var menu = BuildingSystemMenu.instance;
                return menu == null || !menu.menuIsOpen;
            }
        }

        // H1 postfix body, after vanilla's own crosshair choice harvested nothing this call.
        internal static void AfterVanilla(Growing g)
        {
            if (!g.ManualHarvest || !g.isActiveAndEnabled || g.IsInteractDisabled()) return;
            if (Time.time - Vanilla.LastManualHarvestTime() < 0.2f) return;   // vanilla pace: one plant per step
            if (Time.unscaledTime < _nextScan) return;
            if (!Step(g)) _nextScan = Time.unscaledTime + 0.2f;   // nothing to take: don't rescan every frame
        }

        // Harvests at most one plant of g: the nearest ready one the player could harvest by hand. True if one was taken.
        internal static bool Step(Growing g)
        {
            var player = PlayerLocator.instance;
            var spots = g.GrowingSlots;
            if (player == null || spots == null) return false;
            Vector3 me = player.transform.position;
            grow best = null;
            Growing bestScope = null;
            bool bestOwned = false;
            float bestD = float.PositiveInfinity;
            bool skippedForSpace = false;
            for (int i = 0; i < spots.Count; i++)
            {
                var spot = spots[i];
                if (spot == null || !spot.HasPlant()) continue;
                var t = spot.Plant.GetComponentInChildren<grow>();   // vanilla's lookup in RepeatHarvest
                if (t == null) continue;
                bool owned = t.ownerId > 1 && !t.PermissionToTake;
                if (owned && !Cfg.Hot.IncludeOwned) continue;                       // D15
                bool dead = Vanilla.GrowIsDead(t);
                if (dead && !Cfg.Hot.IncludeDead) continue;                         // D17
                var scope = Cfg.Hot.Reach == HarvestReach.WholeGrower && !owned ? g : null;
                // Vanilla's "ready, in reach, fruit fits": never an immature plant, never a fruit plant with no ripe fruit
                // (Harvest would uproot it).
                if (!CanRepeat(t, scope)) continue;
                var items = t.visibleGrownItems;
                if (items == null || items.Length == 0 || dead && !AnyRipeVisible(items))
                {
                    // A whole plant or a dead uproot: vanilla never checks space for these (D16).
                    if (!SpaceCheck.Fits(t))
                    {
                        skippedForSpace = true;
                        continue;
                    }
                }
                float d = (t.transform.position - me).sqrMagnitude;   // nearest to the player first
                if (d < bestD)
                {
                    best = t;
                    bestD = d;
                    bestScope = scope;
                    bestOwned = owned;
                }
            }
            if (best != null)
            {
                // A press makes noise before the interaction (InteractObjects.UseInteractable, SRC/InteractObjects.cs:417).
                // The stolen add makes noise as well (Inventory.CheckForAddedStolenItems :412), but only when the stack
                // fits; this keeps one press's worth of noise per stolen plant in every case. HearPlayer only restarts
                // a look timer, so the two calls in one frame count once, as by hand.
                if (bestOwned) CharacterHeadController.PlayerMadeNoise();
                ScopeGrowing = bestScope;
                try { best.Harvest(); }   // the vanilla manual harvest
                finally { ScopeGrowing = null; }
                _count++;
                _from = g.ManuName;
                _stop = null;
                return true;
            }
            if (skippedForSpace)
            {
                _stop = "space";
                if (!_warned && UseText.instance != null)
                {
                    UseText.instance.WarningUseEffect();
                    _warned = true;
                }
            }
            else if (_count > 0) _stop = "none ready";
            return false;
        }

        private static bool CanRepeat(grow t, Growing scope)
        {
            ScopeGrowing = scope;
            try { return t.CanRepeatHarvest(); }
            finally { ScopeGrowing = null; }
        }

        private static bool AnyRipeVisible(GrowItem[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                var f = items[i];
                if (f != null && f.gameObject.activeInHierarchy && f.readyToPick) return true;
            }
            return false;
        }

        // H3 body: an aimed-at grow or GrowItem of the scoped grower, within MaxDistance horizontally.
        internal static bool InScope(Interactable target)
        {
            var scope = ScopeGrowing;
            if (scope == null || target == null || !(target is grow || target is GrowItem)) return false;
            if (target.GetComponentInParent<Growing>() != scope) return false;
            var player = PlayerLocator.instance;
            if (player == null) return false;
            Vector3 d = target.transform.position - player.transform.position;
            d.y = 0f;
            float r = Cfg.Hot.MaxDistance;
            return d.sqrMagnitude <= r * r;
        }

        internal static void OnSceneLoaded()
        {
            ScopeGrowing = null;
            _pressId = float.PositiveInfinity;
            _tainted = float.NaN;
            _count = 0;
            _from = _stop = null;
            _warned = false;
            _nextScan = 0f;
        }
    }
}
