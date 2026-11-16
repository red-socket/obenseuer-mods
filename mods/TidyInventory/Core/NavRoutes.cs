using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Window = InventoryNavigationHandler.CurrentInventoryWindow;

namespace TidyInventory
{
    // P27: controller routes to our buttons (spec §7), on InventoryNavigationMovement.Move(MovementDirection,
    // InventoryNavigationData, InputType) (SRC/InventoryNavigationMovement.cs:428). Decide in the prefix, apply in the
    // postfix (Fewer Clicks' NavMovePatch pattern): the prefix never changes navigation state and returns false only for
    // our cases; the postfix applies through the game's own private SelectButton and Switch* helpers. When the original
    // was skipped, the postfix also refreshes the controls line, which vanilla does first in Move (:430). Acts only in the
    // StorageTakeAll, StoragePower and InventorySort windows; Fewer Clicks and Better Tenement act only in StorageSort and
    // Storage. Pressing A needs no patch: HandleStorageTakeAll and HandleInventorySort invoke the current button's onClick
    // (SRC/InventoryNavigationHandler.cs:1771-1784).
    [HarmonyPatch]
    internal static class NavMovePatch
    {
        internal static bool Patched;

        internal enum Decision { None, SelectStorageOurs, StorageOursToTakeAll, StayStorageOurs, PowerToOurs, SelectInvOurs, InvOursToSort, StayInvOurs }

        private static MethodBase TargetMethod() => Vanilla.NavMove;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(MovementDirection direction, InventoryNavigationData data, bool __runOriginal, out Decision __state)
        {
            __state = Decision.None;
            if (!__runOriginal) return true;
            try
            {
                if (!Cfg.Bins.Gamepad.Value || data == null || Game.DragActive) return true;
                var cur = data.CurrentButton != null ? data.CurrentButton.gameObject : null;
                switch (data.CurrentWindow)
                {
                    case Window.StorageTakeAll:
                    {
                        var ours = StorageButtons.Active;
                        bool onOurs = cur != null && (cur.name == StorageButtons.EmptyName || cur.name == StorageButtons.CombineName);
                        if (onOurs)
                        {
                            if (ours == null || cur != ours) return true;   // ours went away: vanilla neighbour
                            if (direction == MovementDirection.Left) __state = Decision.StorageOursToTakeAll;
                            else if (direction == MovementDirection.Up) __state = Decision.StayStorageOurs;
                            else return true;   // Right: Power if shown; Down: the slots
                            return false;
                        }
                        if (direction == MovementDirection.Right && ours != null)
                        {
                            __state = Decision.SelectStorageOurs;
                            return false;
                        }
                        return true;
                    }
                    case Window.StoragePower:
                        if (direction == MovementDirection.Left && StorageButtons.Active != null)
                        {
                            __state = Decision.PowerToOurs;
                            return false;
                        }
                        return true;
                    case Window.InventorySort:
                    {
                        var ours = InventoryCombine.Button;
                        bool onOurs = cur != null && cur.name == InventoryCombine.Name;
                        if (onOurs)
                        {
                            if (ours == null || cur != ours) return true;
                            if (direction == MovementDirection.Left) __state = Decision.InvOursToSort;
                            else if (direction == MovementDirection.Down) return true;   // vanilla: slot 0,0
                            else __state = Decision.StayInvOurs;
                            return false;
                        }
                        if (direction == MovementDirection.Right && ours != null && cur != null && cur == InventoryCombine.Sort)
                        {
                            __state = Decision.SelectInvOurs;
                            return false;
                        }
                        return true;
                    }
                    default:
                        return true;
                }
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
                switch (__state)
                {
                    case Decision.SelectStorageOurs:
                    case Decision.PowerToOurs:
                    {
                        var ours = StorageButtons.Active;
                        if (ours == null) return;
                        data.CurrentWindow = Window.StorageTakeAll;
                        data.CurrentStorageTakeAllButton = Vanilla.Select(__instance, data, ours);
                        break;
                    }
                    case Decision.StorageOursToTakeAll:
                        Vanilla.SwitchToNextStorageTakeAllButton.Invoke(__instance, new object[] { data });
                        break;
                    case Decision.SelectInvOurs:
                    {
                        var ours = InventoryCombine.Button;
                        if (ours == null) return;
                        data.CurrentInventorySortButton = Vanilla.Select(__instance, data, ours);
                        break;
                    }
                    case Decision.InvOursToSort:
                        Vanilla.SwitchToNextInventorySortButton.Invoke(__instance, new object[] { data });
                        break;
                }
            }
            catch (Exception e) { Log.Exception("controller route", e); }
        }
    }
}
