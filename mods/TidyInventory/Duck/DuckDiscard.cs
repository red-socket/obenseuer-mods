using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace TidyInventory
{
    // S-106 (spec §6): throw away the blue rubber duck once Mr. Duck's quest is done. A hard-coded whitelist of one task
    // item, behind a blocklist of keys and quest-critical items as a second line. The duck is removed the way the game's
    // own dialogue removes it (TaskItem.TakeItem -> TaskItemsManager.RemoveItem, with its "Item removed" note). The
    // duck is held at amount 0, so TakeItem(1) is right; -1 would leave a ghost tile (SRC/TaskItemUIData.cs:27-33).
    internal static class DuckDiscard
    {
        internal static bool Patched;       // "duck" group (P30-P32)
        internal static bool HintPatched;   // "duck hint" group (P33)

        private const string DuckId = "BlueDuck22";

        private static readonly string[] Blocklist =
        {
            "ForgedPassport11", "MiningPermit", "TenementBlueprint31", "PassportBad23", "BlueDuckHead22", "ErtoRedJacket",
            "ErtoBrownJacket", "smallStorageKeys", "seamus_ring", "SacredWelhousSeal", "RepositoryPermit", "RepositoryAccessForm",
            "RedHat", "redemptionKeys12", "KauhaClaimKeys11", "BlackAmulet", "bazaarHouseKeys11",
        };

        private static TaskItemUI _focused;

        internal static void OnSceneLoaded() => _focused = null;

        internal static void OnSelected(TaskItemUI tile)
        {
            _focused = tile;
            if (Confirm.AnyArmed("duck") && !ReferenceEquals(Confirm.Target("duck"), FocusedItem())) Confirm.Reset("duck");
            RefreshHint();
        }

        internal static void OnUnselected(TaskItemUI tile)
        {
            if (ReferenceEquals(_focused, tile)) _focused = null;
        }

        // The task item of the focused tile, or null (no tile, a destroyed one, one being removed, or hidden).
        private static TaskItem FocusedItem()
        {
            var t = _focused;
            if (t == null || t.MarkedForDestroy || !t.isActiveAndEnabled || Vanilla.TaskItemOf == null) return null;
            return Vanilla.TaskItemOf(t);
        }

        private static bool IsDuck(TaskItem ti) =>
            ti != null && ti.Id == DuckId && ti.itemType == TaskItem.Type.TaskItems
            && !(ti is TaskItemBlueprint) && !(ti is TaskItemMail) && !(ti is TaskItemMap) && Array.IndexOf(Blocklist, ti.Id) < 0;

        // Mr. Duck's quest is done, the player still has the duck, and no conversation is running.
        private static bool QuestDone()
        {
            var tc = TaskController.instance;
            if (tc != null && (tc.TaskFinished("Quack10") || tc.ObjectiveCompleted("Quack10", "GiveBlue28"))) return true;
            try { return DialogueLua.GetVariable("MrDuckTaskCompleted").asBool; }
            catch (Exception) { return false; }
        }

        private static bool Qualifies(TaskItem ti, out bool duckNotYet)
        {
            duckNotYet = false;
            if (!IsDuck(ti)) return false;
            var tim = TaskItemsManager.instance;
            if (tim == null || !tim.CheckIfPlayerHasTaskItem(ti) || Game.ConversationActive) return false;
            if (!QuestDone()) { duckNotYet = true; return false; }
            return true;
        }

        // Keyboard: DiscardKey over the focused duck tile.
        internal static void Tick()
        {
            if (!Patched || !Cfg.Duck.Enabled.Value || _focused == null) return;
            if (!Keys.Down(Cfg.Duck.DiscardKey) || !PlayGate.Open) return;
            Press(FocusedItem());
        }

        // P32: controller A on the focused tile. True when we handled it.
        internal static bool PadPress(ItemsUIPanel panel)
        {
            if (!Patched || !Cfg.Duck.Enabled.Value || panel == null) return false;
            var data = InventoryNavigationData.Instance;
            if (data == null || data.CurrentWindow != InventoryNavigationHandler.CurrentInventoryWindow.TaskItems) return false;
            var rows = Vanilla.ItemRows(panel);
            int row = Vanilla.NavRow(panel);
            if (rows == null || row < 0 || row >= rows.Count || rows[row] == null || !string.IsNullOrEmpty(rows[row].title)) return false;
            if (!(Vanilla.NavSelected(panel) is TaskItemUI tile) || Vanilla.TaskItemOf == null) return false;
            var ti = Vanilla.TaskItemOf(tile);
            if (!IsDuck(ti)) return false;
            if (!PlayGate.Open) return false;
            _focused = tile;
            Press(ti);
            return true;
        }

        private static void Press(TaskItem ti)
        {
            if (ti == null) return;
            if (!Qualifies(ti, out bool notYet))
            {
                if (notYet) Hints.Notify(Strings.DuckNotYet);   // a refusal, not a tip: every time, whatever ShowHints says
                return;
            }
            if (!Confirm.IsArmed("duck", ti))
            {
                Confirm.Arm("duck", ti, Cfg.Bins.ConfirmSeconds.Value, RefreshHint);
                Hints.Notify(Strings.DuckArmed(Keys.Label(Cfg.Duck.DiscardKey)));
                RefreshHint();
                return;
            }
            Confirm.Reset("duck");
            Discard(ti);
        }

        private static void Discard(TaskItem ti)
        {
            var tim = TaskItemsManager.instance;
            var info = tim?.taskItems?.FirstOrDefault(i => i?.taskItem != null && i.taskItem.Id == ti.Id);
            if (info == null) return;
            var duck = PlayerStats.instance != null ? PlayerStats.instance.duck : null;
            if (!Cfg.Duck.AllowCureAchievement.Value)
            {
                if (Vanilla.DuckAchievementEnabled == null || duck == null)
                {
                    Log.WarnOnce("duck-field", "Duck: the duck addiction state was not found; the duck is not thrown away, so no achievement unlocks by accident.");
                    return;
                }
                Vanilla.DuckAchievementEnabled(duck) = false;
            }
            info.taskItem.TakeItem(1);
            if (duck != null)
            {
                duck.currentStatus = 0f;
                duck.currentDucksNeedAmount = 0f;
            }
            _focused = null;
            Log.Info($"Duck: the blue rubber duck was thrown away (achievement {(Cfg.Duck.AllowCureAchievement.Value ? "allowed" : "not unlocked")}).");
            RefreshHint();
        }

        // P33 helper: the controls line of the task items window.
        internal static string HintSuffix(InventoryNavigationHandler.CurrentInventoryWindow window, InputManager.InputType input)
        {
            if (!Patched || !Cfg.Duck.Enabled.Value || window != InventoryNavigationHandler.CurrentInventoryWindow.TaskItems) return null;
            var ti = FocusedItem();
            if (!Qualifies(ti, out _)) return null;
            string label = Confirm.IsArmed("duck", ti) ? Strings.DuckHintArmed : Strings.DuckHint;
            string key;
            if (input == InputManager.InputType.Controller)
            {
                key = "A";
                try
                {
                    var t = InputManager.instance != null ? InputManager.instance.GetKeyCodeDisplayText(KeyCode.Joystick1Button0) : null;
                    if (!string.IsNullOrEmpty(t)) key = t;
                }
                catch (Exception) { }
            }
            else
            {
                if (!Keys.Bound(Cfg.Duck.DiscardKey) || Keys.Suspended(Cfg.Duck.DiscardKey)) return null;
                key = Keys.Label(Cfg.Duck.DiscardKey);
            }
            return "   " + key + ")" + label;
        }

        private static void RefreshHint()
        {
            if (!HintPatched) return;
            try
            {
                var h = InventoryNavigationHandler.Instance;
                var data = InventoryNavigationData.Instance;
                if (h == null || data == null || InputManager.instance == null) return;
                if (data.CurrentWindow != InventoryNavigationHandler.CurrentInventoryWindow.TaskItems) return;
                h.ChangeControlsInfo(InputManager.instance.inputType, data.CurrentTab, data.CurrentWindow);
            }
            catch (Exception e) { Log.Exception("duck hint refresh", e); }
        }
    }

    // P30, P31: TaskItemUI.Selected / Unselected (SRC/TaskItemUI.cs:238, :256): the focused tile, by mouse or controller.
    [HarmonyPatch]
    internal static class TaskItemSelectedPatch
    {
        private static MethodBase TargetMethod() => Vanilla.TaskItemSelected;

        private static void Postfix(TaskItemUI __instance)
        {
            try { DuckDiscard.OnSelected(__instance); }
            catch (Exception e) { Log.Exception("task tile focus", e); }
        }
    }

    [HarmonyPatch]
    internal static class TaskItemUnselectedPatch
    {
        private static MethodBase TargetMethod() => Vanilla.TaskItemUnselected;

        private static void Postfix(TaskItemUI __instance)
        {
            try { DuckDiscard.OnUnselected(__instance); }
            catch (Exception e) { Log.Exception("task tile focus", e); }
        }
    }

    // P32: ItemsUIPanel.ActivateNavigationSelection (SRC/ItemsUIPanel.cs:421), controller A in the task items window.
    // Vanilla does nothing on item rows, so ours only takes A on the duck tile; category titles still toggle.
    [HarmonyPatch]
    internal static class ActivateNavigationSelectionPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ActivateNavigationSelection;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ItemsUIPanel __instance, bool __runOriginal)
        {
            if (!__runOriginal) return true;
            try { return !DuckDiscard.PadPress(__instance); }
            catch (Exception e) { Log.Exception("duck pad press", e); return true; }
        }
    }

    // P33: InventoryNavigationHandler.GetButtonInfoTab0 (SRC/InventoryNavigationHandler.cs:424), private: adds
    // "A)THROW AWAY" (then "A)CONFIRM") to the controls line while the qualifying duck tile is focused.
    [HarmonyPatch]
    internal static class DuckHintPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GetButtonInfoTab0;

        private static void Postfix(InventoryNavigationHandler.CurrentInventoryWindow currentWindow, InputManager.InputType inputType, ref string __result)
        {
            try
            {
                var add = DuckDiscard.HintSuffix(currentWindow, inputType);
                if (add != null) __result = (__result ?? "") + add;
            }
            catch (Exception e) { Log.Exception("duck hint", e); }
        }
    }
}
