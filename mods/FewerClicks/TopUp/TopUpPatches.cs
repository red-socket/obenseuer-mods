using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FewerClicks
{
    // P1: StoragePanelUI.UpdateStoragePanelUI(Storage) (SRC/StoragePanelUI.cs:159). Low priority, so it runs after
    // Better Tenement's postfix on the same method; the button order is by name either way.
    [HarmonyPatch]
    internal static class UpdateStoragePanelUIPatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateStoragePanelUI;

        [HarmonyPriority(Priority.Low)]
        private static void Postfix(StoragePanelUI __instance, Storage storage)
        {
            try { TopUpButton.OnPanel(__instance, storage); }
            catch (Exception e)
            {
                Log.Exception("top up button", e);
                try { TopUpButton.Hide(); } catch (Exception) { }
            }
        }
    }

    // P2: InventoryNavigationMovement.Move(MovementDirection, InventoryNavigationData, InputType) (SRC :428), the
    // controller route to the button (spec §2 P2). The prefix never changes navigation state: it only decides and
    // returns whether vanilla (and Better Tenement's prefix) may run; the postfix applies the decision. So the result is
    // the same whether or not HarmonyX runs Better Tenement's prefix after ours returned false. Better Tenement's
    // Stash route acts on Left whenever Stash is not the current button (Stash.cs:150-156), also when ours is: the
    // postfix repairs that.
    [HarmonyPatch]
    internal static class NavMovePatch
    {
        internal enum Decision { None, SelectTopUp, SelectStash, SelectSort, StayTopUp, SortLeft }

        private const string StashName = "BT_StashButton";

        private static MethodBase TargetMethod() => Vanilla.NavMove;

        [HarmonyPriority(Priority.High)]
        private static bool Prefix(MovementDirection direction, InventoryNavigationData data, out Decision __state)
        {
            __state = Decision.None;
            try
            {
                if (!Active(data, out var ours, out var stash)) return true;
                var cur = data.CurrentButton;
                bool onOurs = cur != null && cur.gameObject == ours;
                if (!onOurs)
                {
                    if (direction != MovementDirection.Left) return true;
                    bool onStash = stash != null && cur != null && cur.gameObject == stash;
                    if (onStash || stash == null)
                    {
                        __state = Decision.SelectTopUp;
                        return false;
                    }
                    // On Sort with Stash shown: Better Tenement moves to Stash. If nothing moved (its route is off),
                    // the postfix selects ours.
                    __state = Decision.SortLeft;
                    return true;
                }
                switch (direction)
                {
                    case MovementDirection.Right:
                        __state = stash != null ? Decision.SelectStash : Decision.SelectSort;
                        return false;
                    case MovementDirection.Down:
                        return true;   // vanilla: from the StorageSort window into the storage slots
                    default:
                        __state = Decision.StayTopUp;
                        return false;
                }
            }
            catch (Exception e)
            {
                Log.Exception("top up gamepad route", e);
                __state = Decision.None;
                return true;
            }
        }

        private static void Postfix(InventoryNavigationMovement __instance, InventoryNavigationData data, Decision __state)
        {
            if (__state == Decision.None || data == null) return;
            try
            {
                if (data.CurrentWindow != InventoryNavigationHandler.CurrentInventoryWindow.StorageSort) return;
                var ours = TopUpButton.Button;
                if (ours == null) return;
                var stash = Stash(ours.transform.parent);
                var cur = data.CurrentButton;
                bool curIsOurs = cur != null && cur.gameObject == ours;
                bool curIsStash = cur != null && stash != null && cur.gameObject == stash;
                switch (__state)
                {
                    case Decision.SelectTopUp:
                    case Decision.StayTopUp:
                        if (!curIsOurs) Select(__instance, data, ours);
                        break;
                    case Decision.SortLeft:
                        if (!curIsOurs && !curIsStash) Select(__instance, data, ours);
                        break;
                    case Decision.SelectStash:
                        if (stash != null && !curIsStash) Select(__instance, data, stash);
                        break;
                    case Decision.SelectSort:
                        if (curIsOurs || curIsStash) Vanilla.SwitchToNextStorageSortButton.Invoke(__instance, new object[] { data });
                        break;
                }
            }
            catch (Exception e) { Log.Exception("top up gamepad route", e); }
        }

        private static bool Active(InventoryNavigationData data, out GameObject ours, out GameObject stash)
        {
            ours = null;
            stash = null;
            if (!Cfg.TopUp.Enabled.Value || !Cfg.TopUp.Gamepad.Value || data == null) return false;
            if (data.CurrentWindow != InventoryNavigationHandler.CurrentInventoryWindow.StorageSort) return false;
            ours = TopUpButton.Button;
            if (ours == null || Game.DragActive()) return false;
            stash = Stash(ours.transform.parent);
            return true;
        }

        private static GameObject Stash(Transform row)
        {
            if (row == null) return null;
            var t = row.Find(StashName);
            return t != null && t.gameObject.activeInHierarchy ? t.gameObject : null;
        }

        // Stored as the current sort button so vanilla's ClearPreviousSelections unhovers it later.
        private static void Select(InventoryNavigationMovement nav, InventoryNavigationData data, GameObject go)
        {
            data.CurrentStorageSortButton = (DefaultUIButton)Vanilla.SelectButton.Invoke(nav, new object[] { data, go });
        }
    }
}
