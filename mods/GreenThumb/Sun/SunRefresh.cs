using UnityEngine;

namespace GreenThumb
{
    // S-044b (spec §5, D4). NaturalLightSourceChecker only re-checks on OnEnable, LoadingDone (+1 s) and
    // WindowNaturalLight.SourcesChanged (SRC/NaturalLightSourceChecker.cs:36-72), and BuildingSystem.Place only refreshes
    // the moved object's own checker, so a planter keeps Sun after a cupboard is put over it until the next load. Every
    // IntervalSeconds of real time (and once 1 s after the build menu closes) every checker in the scene is asked to
    // re-check, ChecksPerFrame per frame. RefreshModifiers is vanilla's own entry: a no-op while a check is pending, and
    // the check itself (CheckCO -> HasSun -> ApplyModifiers) runs at the end of the frame. Sun is pure geometry and
    // ignores the time of day, so TimeKeeper's long waits need nothing special. Writes nothing to saves
    // (CraftingBase.Modifiers is [fsIgnore]). No patches.
    internal static class SunRefresh
    {
        internal static bool On;

        private static NaturalLightSourceChecker[] _queue;
        private static int _index, _refreshed;
        private static float _nextPass = float.PositiveInfinity, _menuPassAt = float.PositiveInfinity;
        private static bool _menuWasOpen;

        // sceneLoaded: forget the pass in progress; the next one is due a full interval later (vanilla re-checks 1 s
        // after LoadingDone itself).
        internal static void Reset()
        {
            _queue = null;
            _index = _refreshed = 0;
            _nextPass = Time.unscaledTime + Cfg.Hot.IntervalSeconds;
            _menuPassAt = float.PositiveInfinity;
            _menuWasOpen = false;
        }

        internal static void Tick()
        {
            if (!On || !Cfg.Sunlight.Refresh.Value) return;
            if (!PlayGate.Open || SaveController.Loading || PowerFix.Holding) return;
            var t = TimeOfDayAzure.instance;
            if (t == null || t.updateTimeDisabled) return;

            float now = Time.unscaledTime;
            var menu = BuildingSystemMenu.instance;
            bool open = menu != null && menu.menuIsOpen;
            if (_menuWasOpen && !open) _menuPassAt = now + 1f;
            _menuWasOpen = open;

            if (_queue == null)
            {
                if (float.IsPositiveInfinity(_nextPass)) _nextPass = now + Cfg.Hot.IntervalSeconds;
                if (now < _nextPass && now < _menuPassAt) return;
                _queue = Object.FindObjectsByType<NaturalLightSourceChecker>(FindObjectsSortMode.None);
                _index = _refreshed = 0;
                _nextPass = now + Cfg.Hot.IntervalSeconds;
                _menuPassAt = float.PositiveInfinity;
            }

            int budget = Cfg.Hot.ChecksPerFrame;
            while (budget > 0 && _index < _queue.Length)
            {
                var c = _queue[_index++];
                budget--;
                if (c == null || !c.isActiveAndEnabled) continue;
                var g = Vanilla.NlscGrowing(c);
                if (g == null) continue;   // BuildingSystem preview copies have no grower
                // The open panel: vanilla wouldn't redraw its recipe list, so leave it for the next pass.
                if (CraftingBase.currentCraftingBase == g && Vanilla.ManuActive(g)) continue;
                c.RefreshModifiers();
                _refreshed++;
            }
            if (_index < _queue.Length) return;
            _queue = null;
            Log.Verbose($"Sun: re-checked {_refreshed} planters");
        }
    }
}
