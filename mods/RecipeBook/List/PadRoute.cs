using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Window = InventoryNavigationHandler.CurrentInventoryWindow;

namespace RecipeBook
{
    // The controller route to the bar (spec §8.1, D6). No new binding: Up on the top recipe selects the bar, Left/Right
    // cycle Show, A resets text and chip, Down returns to the list, and Up again does the game's own Up-from-top (the
    // power panel when the station has one). On a keyboard the same route works with the movement keys, and Use Item on
    // the bar focuses the text field. Acts only in the OtherPanelRecipe window on the inventory tab; Workshop's route acts
    // only in OtherPanelRecipeOpened, Tidy Inventory, Fewer Clicks and Better Tenement in storage and sort windows.
    internal static class PadRoute
    {
        internal static bool Patched;
        internal static bool OnBar;
        internal static FilterBar Bar;

        // The route may be used right now: patched, switched on and the bar has a chip to drive.
        internal static bool Usable => Patched && Cfg.Hot.Pad && Cfg.Hot.Chip;

        internal static bool OnBarOf(FilterBar bar) => OnBar && bar != null && ReferenceEquals(Bar, bar);

        internal static void EnterBar(FilterBar bar, InventoryNavigationData data)
        {
            var mover = InventoryNavigationMovement.Instance;
            if (mover != null) Vanilla.Clear(mover, data);
            data.CurrentCoordinates = Vector2.zero;
            OnBar = true;
            Bar = bar;
            bar.SetHighlight(true);
            bar.ScrollToTop();
            Log.Verbose("Pad: on the bar.");
        }

        internal static void Reset()
        {
            if (Bar != null) Bar.SetHighlight(false);
            bool was = OnBar;
            OnBar = false;
            Bar = null;
            if (was) Log.Verbose("Pad: left the bar.");
        }

        internal static void ResetIf(FilterBar bar)
        {
            if (ReferenceEquals(Bar, bar)) Reset();
        }

        // Drops the bar selection once the pad is elsewhere (spec §8.1): another window or tab, the bar gone, or a mouse
        // click (the game's own click handler moves the window and coordinates, SRC/InventoryNavigationHandler.cs:1336-1353).
        // While on the bar, the coordinates stay at (0,0): EnterBar puts them there and no decision on the bar moves them.
        // data.RecipeOpened is not a reason: it stays true long after a recipe closes (only a craft start or pad A on an
        // open recipe clears it), and Left/Right/Down/A on the bar are ours whether a recipe is open or not.
        internal static void Tick(FilterBar active)
        {
            if (!OnBar) return;
            var data = InventoryNavigationData.Instance;
            if (active == null || !ReferenceEquals(active, Bar) || !active.Shown || !active.HasChip || !Usable ||
                data.CurrentTab != 0 || data.CurrentWindow != Window.OtherPanelRecipe || data.CurrentCoordinates != Vector2.zero ||
                Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                Reset();
                var h = InventoryNavigationHandler.Instance;
                if (h != null && InputManager.instance != null) h.ChangeControlsInfo(InputManager.instance.inputType, data.CurrentTab, data.CurrentWindow);
            }
        }

        // The game's Up from the top row needs a recipe panel or a power panel: with neither (the filter hid every
        // recipe on a station without power), vanilla Move reaches CanJumpToProgressPanelFromOtherPanel, which reads
        // GetActiveRecipePanel(...).ColumnCount unchecked (SRC/InventoryNavigationMovement.cs:359-360). Vanilla never has
        // OtherPanelRecipe with no recipe panel; only our filter makes that state.
        internal static bool VanillaUpSafe()
        {
            var h = InventoryNavigationHandler.Instance;
            if (h == null) return false;
            return InventoryNavigationMovement.GetActiveRecipePanel(h.OtherPanelsOfTypeRecipe) != null ||
                   InventoryNavigationMovement.GetActivePowerPanel(h.OtherPanelsOfTypePower) != null;
        }
    }

    // P7: InventoryNavigationMovement.Move(MovementDirection, InventoryNavigationData, InputType) (SRC :428). Decide in
    // the prefix (Priority.First, skipped when another prefix already cancelled the original), apply in the postfix: the
    // Fewer Clicks / Tidy Inventory pattern. When the original was skipped the postfix refreshes the controls line,
    // which the game does first in Move (:430). The game's refresh comes before the window changes, so on arrival in
    // the recipe list the line still describes the window you came from; the postfix refreshes it once more then, so
    // FILTER shows on the first arrival (spec §21 item 17).
    [HarmonyPatch]
    internal static class NavMovePatch
    {
        internal enum Decision { None, EnterBar, Prev, Next, BackToList, LeaveUp, Stay }

        // Per call (Move calls itself from the backpack, SRC :502): the decision and the window before the move.
        internal struct State
        {
            internal Decision Decision;
            internal Window Before;
        }

        private static MethodBase TargetMethod() => Vanilla.NavMove;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(MovementDirection direction, InventoryNavigationData data, bool __runOriginal, out State __state)
        {
            __state = new State { Decision = Decision.None, Before = data != null ? data.CurrentWindow : default(Window) };
            if (!__runOriginal) return true;
            try
            {
                if (!PadRoute.Usable || data == null) return true;
                var bar = FilterBar.Active;
                if (bar == null || !bar.Shown || !bar.HasChip) return true;
                if (data.CurrentTab != 0 || data.CurrentWindow != Window.OtherPanelRecipe) return true;
                bool onBar = PadRoute.OnBarOf(bar);
                if (!onBar)
                {
                    if (direction != MovementDirection.Up || data.CurrentCoordinates.y != 0f) return true;
                    __state.Decision = Decision.EnterBar;
                    return false;
                }
                switch (direction)
                {
                    case MovementDirection.Left: __state.Decision = Decision.Prev; return false;
                    case MovementDirection.Right: __state.Decision = Decision.Next; return false;
                    case MovementDirection.Down: __state.Decision = Decision.BackToList; return false;
                    case MovementDirection.Up:
                        // The game's Up from row 0 (EnterBar left the coordinates at 0,0): the power panel when there
                        // is one (SRC/InventoryNavigationMovement.cs:241-246), otherwise nothing. With no visible recipe
                        // and no power panel, vanilla would throw, so we stay on the bar.
                        if (!PadRoute.VanillaUpSafe()) { __state.Decision = Decision.Stay; return false; }
                        __state.Decision = Decision.LeaveUp;
                        return true;
                    default: return true;
                }
            }
            catch (Exception e)
            {
                Log.Exception("Pad: route", e);
                __state.Decision = Decision.None;
                PadRoute.Reset();
                return true;
            }
        }

        private static void Postfix(InventoryNavigationMovement __instance, InventoryNavigationData data, InputManager.InputType inputType, State __state)
        {
            if (data == null) return;
            if (__state.Decision == Decision.None)
            {
                if (__state.Before != Window.OtherPanelRecipe) Arrived(data, inputType);
                return;
            }
            var decision = __state.Decision;
            try
            {
                var bar = FilterBar.Active;
                if (bar == null) { PadRoute.Reset(); return; }
                switch (decision)
                {
                    case Decision.EnterBar:
                        PadRoute.EnterBar(bar, data);
                        break;
                    case Decision.Prev:
                        Filter.Cycle(bar, -1);
                        break;
                    case Decision.Next:
                        Filter.Cycle(bar, +1);
                        break;
                    case Decision.BackToList:
                    {
                        var h = InventoryNavigationHandler.Instance;
                        var panel = h != null ? InventoryNavigationMovement.GetActiveRecipePanel(h.OtherPanelsOfTypeRecipe) : null;
                        if (panel == null) break;   // nothing visible: stay on the bar
                        PadRoute.Reset();
                        Vanilla.SwitchTo(__instance, data, Vector2.zero, panel.SlotsTwoDimensional);
                        break;
                    }
                    case Decision.LeaveUp:
                        if (data.CurrentWindow != Window.OtherPanelRecipe) PadRoute.Reset();
                        else bar.SetHighlight(true);   // no power panel: still on the bar
                        break;
                    case Decision.Stay:
                        bar.SetHighlight(true);
                        break;
                }
                if (decision != Decision.LeaveUp && InventoryNavigationHandler.Instance != null)
                    InventoryNavigationHandler.Instance.ChangeControlsInfo(inputType, data.CurrentTab, data.CurrentWindow);
                if (Log.VerboseOn) Log.Verbose($"Pad: {decision} -> {data.CurrentWindow}, on bar {PadRoute.OnBar}");
            }
            catch (Exception e)
            {
                Log.Exception("Pad: route apply", e);
                PadRoute.Reset();
            }
        }

        // A vanilla move just reached the recipe list: rebuild the controls line for it, but only when P9 would add the
        // filter hint there; otherwise the game's line is left as the game made it.
        private static void Arrived(InventoryNavigationData data, InputManager.InputType inputType)
        {
            if (data.CurrentTab != 0 || data.CurrentWindow != Window.OtherPanelRecipe || !Cfg.Hot.Hints || !PadRoute.Usable) return;
            try
            {
                var bar = FilterBar.Active;
                var h = InventoryNavigationHandler.Instance;
                if (bar == null || !bar.Shown || !bar.HasChip || h == null) return;
                h.ChangeControlsInfo(inputType, data.CurrentTab, data.CurrentWindow);
            }
            catch (Exception e) { Log.Exception("Pad: hint on arrival", e); }
        }
    }

    // P8: InventoryNavigationHandler.HandleRecipeInteraction() (SRC :1786), private: Use Item / A while the bar is
    // selected. Controller: reset text and chip (focus stays on the bar). Keyboard: focus the text field next frame.
    // The game would otherwise press the recipe button under the stale coordinates.
    [HarmonyPatch]
    internal static class RecipeInteractionPatch
    {
        private static MethodBase TargetMethod() => Vanilla.HandleRecipeInteraction;

        private static bool Prefix()
        {
            try
            {
                var bar = FilterBar.Active;
                if (!PadRoute.Usable || bar == null || !PadRoute.OnBarOf(bar) || !bar.Shown) return true;
                if (Game.Controller)
                {
                    Filter.Reset(bar);
                    Log.Verbose("Pad: filter reset (A on the bar).");
                }
                else bar.FocusNextFrame();
                return false;
            }
            catch (Exception e)
            {
                Log.Exception("Pad: A on the bar", e);
                return true;
            }
        }
    }

    // P9: InventoryNavigationHandler.GetButtonInfoTab0(window, inputType) (SRC :424), private, Priority.Last: appends the
    // filter controls to the controls line, never replaces it (Tidy Inventory also appends here). The controller labels
    // are literal, like the game's "   L3/R3)DRAG AND DROP" (:444).
    [HarmonyPatch]
    internal static class ButtonInfoPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GetButtonInfoTab0;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Window currentWindow, InputManager.InputType inputType, ref string __result)
        {
            if (!Cfg.Hot.Hints || currentWindow != Window.OtherPanelRecipe || !PadRoute.Usable) return;
            try
            {
                var bar = FilterBar.Active;
                if (bar == null || !bar.Shown || !bar.HasChip) return;
                bool onBar = PadRoute.OnBarOf(bar);
                string s = __result ?? "";
                // On the bar, Use Item resets (pad) or focuses the field (keyboard) instead of using an item: the game's
                // own "USE ITEM" entry (SRC/InventoryNavigationHandler.cs:447) is taken out so the key has one meaning.
                if (onBar) s = WithoutUseItem(s);
                if (inputType == InputManager.InputType.Controller)
                {
                    if (!onBar) s += Strings.PadUpFilter;
                    else
                    {
                        s += Strings.PadLeftRightShow;
                        s += Vanilla.KeyHint(s, Strings.HintReset, "Use Item");
                    }
                }
                else if (!onBar) s += Vanilla.KeyHint(s, Strings.HintFilter, "Forward");
                else
                {
                    s += Vanilla.KeyHint(s, Strings.HintShow, "Left", "Right");
                    if (bar.HasField) s += Vanilla.KeyHint(s, Strings.HintSearch, "Use Item");
                }
                __result = s;
            }
            catch (Exception e) { Log.Exception("Pad: hint", e); }
        }

        // The game builds the entry with KeyCodeInfoToString(text, "USE ITEM", "Use Item"): "   X)USE ITEM" after other
        // entries, or "X)USE ITEM" at the start. The first match is removed; nothing changes when it isn't there.
        private static string WithoutUseItem(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string mid = Vanilla.KeyHint("x", Strings.GameUseItem, "Use Item");
            if (!string.IsNullOrEmpty(mid))
            {
                int i = s.IndexOf(mid, StringComparison.Ordinal);
                if (i >= 0) return s.Remove(i, mid.Length);
            }
            string first = Vanilla.KeyHint("", Strings.GameUseItem, "Use Item");
            if (!string.IsNullOrEmpty(first) && s.StartsWith(first, StringComparison.Ordinal))
                return s.Substring(first.Length).TrimStart(' ');
            return s;
        }
    }
}
