using System;
using UnityEngine;

namespace TimeKeeper
{
    // U-004 / S-064: the weekday on every worn watch (spec §4.2 P1, P2, §8). Visibility is never changed: with the
    // inventory closed only a worn clock shows the HUD clock (SRC/ShowTime.cs:74), with it open a carried "Clock" item
    // already does (:49). So no scope ever shows a clock without a watch or clock; it only adds the weekday.
    internal static class Weekday
    {
        internal static bool Patched;

        private static int _frame = -1;
        private static bool _qualifies;
        private static string _lastWorn;

        internal static bool Live => Patched && Cfg.Weekday.Enabled.Value;

        // A 'Watch' item is equipped in the watch slot and the slot reports a clock. ClockEquipped is not patched, so P1
        // calling it never recurses. Manual loop: Categories is a small string[] read every frame.
        internal static bool WornWatch(WatchSlotController w)
        {
            if (w == null || !Vanilla.Equipped(w)) return false;
            var item = Vanilla.CurrentItem(w);
            if (item == null || !HasCategory(item.Categories, "Watch")) return false;
            return w.ClockEquipped;
        }

        // Whether the screen-space clocks may show the weekday this frame, by Scope. Cached per frame: three HUD
        // ShowTimes call P2 every frame while they show the time.
        internal static bool Qualifies()
        {
            int f = Time.frameCount;
            if (f == _frame) return _qualifies;
            _frame = f;
            _qualifies = Compute();
            return _qualifies;
        }

        private static bool Compute()
        {
            var w = WatchSlotController.instance;
            bool worn = WornWatch(w);
            if (Log.VerboseOn) TrackWorn(w, worn);
            if (worn) return true;

            var scope = Cfg.Weekday.Scope.Value;
            if (scope == WatchScope.WornWatchOnly) return false;
            var ui = GameUIController.instance;
            var inv = Inventory.instance;
            if (ui == null || inv == null || !ui.InventoryIsVisible()) return false;
            if (inv.CheckForCategoryItem("Watch", ignoreBackpack: true)) return true;
            return scope == WatchScope.AnyClock && inv.CheckForCategoryItem("Clock", ignoreBackpack: true);
        }

        // Verbose: one line whenever the worn watch that shows the weekday changes.
        private static void TrackWorn(WatchSlotController w, bool worn)
        {
            string name = null;
            if (worn)
            {
                var item = Vanilla.CurrentItem(w);
                name = item != null ? item.Title : "?";
            }
            if (name == _lastWorn) return;
            _lastWorn = name;
            Log.Verbose(name != null ? $"Weekday: worn {name} → weekday on" : "Weekday: no watch worn → weekday from carried items only");
        }

        internal static bool HasCategory(string[] categories, string category)
        {
            if (categories == null) return false;
            for (int i = 0; i < categories.Length; i++)
                if (string.Equals(categories[i], category, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
