using System;
using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TidyInventory
{
    // Optional daily emptying (spec §4.5, off by default) of the Trash Bins and Green Dumpsters the player placed in the
    // chosen building areas (the bin prefabs' own spawnItemAreas are Stairs and Outside). Live on
    // TimeOfDayAzure.DayChanged; on arrival in a level, bins whose save is from an earlier day are emptied once, timed like
    // Storage.LoadDelay (wait while the clock is held, then two frames). Per stack: SlotController.RemoveItem with the
    // same protections as Empty. Never Storage.Empty, SpawnItems or RemoveRandom, so spawn state and Unofficial Patch's
    // restock log are not touched.
    internal static class AutoEmpty
    {
        private static Coroutine _catchUp;

        internal static void OnSceneLoaded()
        {
            if (_catchUp != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(_catchUp);
            _catchUp = null;
        }

        // TimeOfDayAzure.DayChanged. The game logs and swallows a throw here (SRC/TimeOfDayAzure.cs:549-556); ours is
        // caught anyway.
        internal static void OnDayChanged(int day)
        {
            try
            {
                if (!Cfg.BinAutoEmpty.Enabled.Value || SaveController.Loading) return;
                Run(catchUp: false);
            }
            catch (Exception e) { Log.Exception("daily emptying", e); }
        }

        internal static void OnLoadingDone()
        {
            if (!Cfg.BinAutoEmpty.Enabled.Value || Plugin.Instance == null) return;
            OnSceneLoaded();
            _catchUp = Plugin.Instance.StartCoroutine(CatchUp());
        }

        private static IEnumerator CatchUp()
        {
            yield return null;
            float waited = 0f;
            while ((TimeOfDayAzure.instance == null || TimeOfDayAzure.instance.updateTimeDisabled) && waited < 60f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _catchUp = null;
            if (TimeOfDayAzure.instance == null || TimeOfDayAzure.instance.updateTimeDisabled || SaveController.Loading) yield break;
            try { Run(catchUp: true); }
            catch (Exception e) { Log.Exception("daily emptying catch-up", e); }
        }

        private static void Run(bool catchUp)
        {
            var tod = TimeOfDayAzure.instance;
            if (tod == null) return;
            var areas = Cfg.List(Cfg.BinAutoEmpty.Areas);
            int bins = 0, items = 0, kept = 0;
            foreach (var s in Object.FindObjectsByType<Storage>(FindObjectsSortMode.None))
            {
                if (s == null || s.Slots == null || !Bins.IsBin(s, ignorePlayerPlacedOnly: true) || !Bins.IsPlayerPlaced(s)) continue;
                var fp = s.GetComponentInParent<FurniturePlaceable>();
                if (fp == null || fp.manager == null || !areas.Contains(fp.manager.area.ToString())) continue;
                if (catchUp && !(s.savedTimeAndDay != null && s.savedTimeAndDay.currentDay < tod.dayCounter)) continue;

                int removedHere = 0;
                foreach (var sc in s.Slots)
                {
                    if (!Game.NonEmpty(sc)) continue;
                    if (Bins.Protected(sc, out _)) { kept++; continue; }
                    items += sc.itemStack.itemAmount;
                    sc.RemoveItem();
                    removedHere++;
                }
                if (removedHere == 0) continue;
                bins++;
                if (Storage.active == s) Game.ClearHoverFor(x => Array.IndexOf(s.Slots, x) >= 0);
            }
            if (bins == 0) return;
            Log.Info($"Bins: emptied {bins} placed bins ({(catchUp ? "on arrival" : "at the day change")}): {items} items thrown away, {kept} stacks kept.");
            if (Cfg.BinAutoEmpty.Notify.Value) Hints.Notify(Strings.EmptiedOvernight(bins));
        }
    }
}
