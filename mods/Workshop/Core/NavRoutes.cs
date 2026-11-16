using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Window = InventoryNavigationHandler.CurrentInventoryWindow;

namespace Workshop
{
    // W2: controller routes to the recipe row (spec §4.6), on InventoryNavigationMovement.Move(MovementDirection,
    // InventoryNavigationData, InputType) (SRC/InventoryNavigationMovement.cs:428). Decide in the prefix, apply in the
    // postfix (the house pattern of Fewer Clicks and Tidy Inventory): the prefix never changes navigation state and
    // returns false only for our cases; the postfix applies through the game's own private SelectButton and
    // SwitchToNextCraftingButton, and refreshes the controls line, which vanilla does first in Move (:430). Acts only in
    // OtherPanelRecipeOpened, which vanilla handles only for Left (:779-786); our other mods act in other windows.
    // Pressing A needs no patch: the window invokes the current button's onClick (SRC/InventoryNavigationHandler.cs:1686).
    [HarmonyPatch]
    internal static class NavRoutes
    {
        internal static bool Patched;

        internal enum Decision { None, Stay, ToFetch, ToRepeat, ToCreate, Step }

        private static MethodBase TargetMethod() => Vanilla.NavMove;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(MovementDirection direction, InventoryNavigationData data, bool __runOriginal, out Decision __state)
        {
            __state = Decision.None;
            if (!__runOriginal) return true;
            try
            {
                if (!Cfg.Interface.GamepadRoutes.Value || data == null || data.CurrentWindow != Window.OtherPanelRecipeOpened) return true;
                var row = RecipeRow.Current;
                if (row == null) return true;
                bool fetch = row.FetchShown && row.Fetch != null && row.Fetch.activeInHierarchy;
                bool repeat = row.RepeatShown && row.Repeat != null && row.Repeat.activeInHierarchy;
                if (!fetch && !repeat) return true;
                var cur = data.CurrentButton != null ? data.CurrentButton.gameObject : null;
                bool onFetch = cur != null && cur == row.Fetch;
                bool onRepeat = cur != null && cur == row.Repeat;
                if (onFetch)
                {
                    switch (direction)
                    {
                        case MovementDirection.Up: __state = Decision.Stay; break;
                        case MovementDirection.Down: __state = Decision.ToCreate; break;
                        case MovementDirection.Right: __state = repeat ? Decision.ToRepeat : Decision.Stay; break;
                        default: return true;   // Left: vanilla, back to the recipe list
                    }
                    return false;
                }
                if (onRepeat)
                {
                    switch (direction)
                    {
                        case MovementDirection.Up: __state = Decision.Stay; break;
                        case MovementDirection.Down: __state = Decision.ToCreate; break;
                        case MovementDirection.Right: __state = Decision.Step; break;
                        case MovementDirection.Left:
                            if (!fetch) return true;
                            __state = Decision.ToFetch;
                            break;
                        default: return true;
                    }
                    return false;
                }
                // On the create button: Up reaches our row; everything else stays vanilla.
                if (direction != MovementDirection.Up) return true;
                __state = fetch ? Decision.ToFetch : Decision.ToRepeat;
                return false;
            }
            catch (Exception e)
            {
                Log.Exception("controller route", e);
                __state = Decision.None;
                return true;
            }
        }

        private static void Postfix(InventoryNavigationMovement __instance, InventoryNavigationData data, InputManager.InputType inputType, Decision __state)
        {
            if (__state == Decision.None || data == null) return;
            try
            {
                if (InventoryNavigationHandler.Instance != null)
                    InventoryNavigationHandler.Instance.ChangeControlsInfo(inputType, data.CurrentTab, data.CurrentWindow);
                var row = RecipeRow.Current;
                if (row == null) return;
                switch (__state)
                {
                    case Decision.ToFetch:
                        Select(__instance, data, row.Fetch);
                        break;
                    case Decision.ToRepeat:
                        Select(__instance, data, row.Repeat);
                        break;
                    case Decision.Step:
                        RepeatButton.Step(row);
                        break;
                    case Decision.ToCreate:
                        if (data.CurrentRecipeButton != null && data.CurrentRecipeButton.recipeInfo != null && data.CurrentRecipeButton.recipeInfo.createButton != null)
                            Vanilla.SwitchToNextCraftingButton.Invoke(__instance, new object[] { data });
                        else if (row.Info.createButton != null)
                        {
                            var btn = row.Info.createButton.GetComponentInChildren<DefaultUIButton>();
                            if (btn != null) Select(__instance, data, btn.gameObject);
                        }
                        break;
                }
            }
            catch (Exception e) { Log.Exception("controller route", e); }
        }

        // The game's SelectButton unhovers the previous selection, sets CurrentButton and hovers ours; CurrentCraftButton
        // points at it too, so ClearPreviousSelections unhovers it on the next move.
        private static void Select(InventoryNavigationMovement nav, InventoryNavigationData data, GameObject go)
        {
            if (go == null || !go.activeInHierarchy) return;
            data.CurrentCraftButton = Vanilla.Select(nav, data, go);
        }
    }
}
