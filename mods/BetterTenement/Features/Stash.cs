using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using UiButton = UnityEngine.UI.Button;

namespace BetterTenement
{
    // The storage panel group: S1 (UpdateStoragePanelUI postfix, shared by the stash button and the chest header
    // counter) and S2 (Move prefix, shared by the stash gamepad route and the scroll view's entry tracking).
    internal static class StoragePanel
    {
        internal static bool Patched;
    }

    // S-028 "stash all materials" (spec §3.2.1): a button left of Sort on resource chests, a hotkey and a gamepad route.
    internal static class Stash
    {
        private static GameObject _button;
        private static StoragePanelUI _for;
        private static bool _failedThisScene;
        private static int _dragSeenFrame = -100;

        internal static void OnSceneLoaded()
        {
            _button = null;
            _for = null;
            _failedThisScene = false;
        }

        // S1 part: create the button once per StoragePanelUI (the panel is part of each scene) and set its visibility.
        // Vanilla OnEnable only touches Sort and Take all; the backpack toggle re-enters through GetStorage, so the
        // button hides for the backpack automatically.
        internal static void OnPanel(StoragePanelUI ui, Storage storage)
        {
            bool want = Cfg.Stash.Value && Game.IsResourceChest(storage) && !Game.TradeActive;
            if (want && (_button == null || _for != ui) && !_failedThisScene) Create(ui);
            if (_button == null) return;
            if (_button.activeSelf != want) _button.SetActive(want);
            if (want)
            {
                var label = _button.GetComponentInChildren<TMP_Text>(true);
                if (label != null && label.text != Cfg.StashLabel.Value) label.text = Cfg.StashLabel.Value;
            }
        }

        private static void Create(StoragePanelUI ui)
        {
            var take = ui.takeAllButton;
            var sort = ui.sortButton;
            if (take == null || sort == null) { _failedThisScene = true; return; }
            var go = Object.Instantiate(take, take.transform.parent);
            go.name = "BT_StashButton";
            // The Buttons layout group is MiddleRight: the new button sits left of Sort, and Sort and Take all keep
            // their screen positions.
            go.transform.SetSiblingIndex(sort.transform.GetSiblingIndex());
            var btn = go.GetComponent<UiButton>();
            if (btn == null || go.GetComponent<DefaultUIButton>() == null)
            {
                Object.Destroy(go);
                _failedThisScene = true;
                Log.WarnOnce("stash-button", "The storage panel's Take all button has an unexpected shape; Stash works with the hotkey only.");
                return;
            }
            // The clone's persistent listener goes through its own Relay to StoragePanelUI.TakeAll: drop both.
            btn.onClick = new UiButton.ButtonClickedEvent();
            btn.onClick.AddListener(Run);
            var relay = go.GetComponent<Relay>();
            if (relay != null) Object.Destroy(relay);
            _button = go;
            _for = ui;
            Log.Verbose("Stash button added to the storage panel.");
        }

        // Button, hotkey and gamepad. Moves every renovation material from the main inventory (and, by config, the worn
        // backpack) into the open resource chest through the vanilla shift-click path: partial fills, nothing dropped on
        // the floor, owner kept (stolen stays stolen), no crime check. Never touches hands or equipment.
        internal static void Run()
        {
            try
            {
                var chest = Storage.active;
                var inv = Inventory.instance;
                if (chest == null || !Game.IsResourceChest(chest) || inv == null || chest.Slots == null || chest.Slots.Length == 0) return;
                if (inv.ForeignSlots != chest.Slots || Game.TradeActive || Game.ConsoleOpen) return;
                if (DragActive())
                {
                    if (Notifications.instance != null) Notifications.instance.CreateNotification("Resource chest", "Drop the item you are dragging first");
                    return;
                }

                var sources = new List<SlotController>();
                if (inv.Slots != null) sources.AddRange(inv.Slots);
                if (Cfg.StashIncludeBackpack.Value && BackpackStorage.instance != null)
                {
                    var bp = BackpackStorage.instance.GetStorage();
                    if (bp != null && bp != chest && bp.Slots != null) sources.AddRange(bp.Slots);
                }

                int items = 0, stacks = 0, notFit = 0;
                var full = new HashSet<long>();   // (id, stolen) that already moved nothing: skip, so StorageFull does not flash again
                foreach (var s in sources)
                {
                    var st = s?.itemStack;
                    if (st == null || st.itemAmount <= 0 || st.itemReference == null || !Materials.IsMaterial(st.itemId)) continue;
                    long key = ((long)st.itemId << 1) | (Materials.IsStolen(st) ? 1L : 0L);
                    if (full.Contains(key)) { notFit++; continue; }
                    int amount = st.itemAmount;
                    s.MoveToOtherInventory(chest.Slots[0], amount, out int moved);
                    if (moved > 0) { items += moved; stacks++; }
                    if (moved < amount) notFit++;
                    if (moved == 0) full.Add(key);
                }

                if (items > 0)
                {
                    Removal.MarkChanged(chest);
                    try
                    {
                        var ui = inv.storagePanelUI;
                        if (ui != null && Vanilla.TakeAllSound != null) MiscAudioPlayer.PlaySound(Vanilla.TakeAllSound(ui));
                    }
                    catch (Exception e) { Log.Exception("stash sound", e); }
                }
                Chests.RefreshHeader();
                string text = items > 0 ? $"Stashed {items} items ({stacks} stacks)" : "No renovation materials to stash";
                if (notFit > 0) text += items > 0 ? $"; {notFit} stacks did not fit" : $": {notFit} stacks did not fit";
                Log.Verbose($"Stash into \"{chest.title}\": {items} items, {stacks} stacks, {notFit} did not fit.");
                if (Notifications.instance != null) Notifications.instance.CreateNotification("Resource chest", text);
            }
            catch (Exception e) { Log.Exception("stash", e); }
        }

        // S2 part: at the storage header, Left from Sort reaches Stash; on Stash, Right goes back to Sort, Down enters
        // the slots as from Sort, Left and Up do nothing. The A button needs no patch: HandleStorageSort invokes
        // data.CurrentButton.onClick, which is ours (InventoryNavigationHandler.cs:1776-1779).
        internal static bool NavMove(InventoryNavigationMovement nav, MovementDirection direction, InventoryNavigationData data)
        {
            if (!Cfg.Stash.Value || !Cfg.StashGamepad.Value || data.CurrentWindow != InventoryNavigationHandler.CurrentInventoryWindow.StorageSort) return true;
            var go = _button;
            if (go == null || !go.activeInHierarchy || Vanilla.SelectButton == null || Vanilla.SwitchToNextStorageSortButton == null) return true;
            var btn = go.GetComponent<UiButton>();
            bool onStash = btn != null && data.CurrentButton == btn;
            if (!onStash)
            {
                if (direction != MovementDirection.Left) return true;
                // Stored as the current sort button so vanilla's ClearPreviousSelections unhovers it later.
                data.CurrentStorageSortButton = (DefaultUIButton)Vanilla.SelectButton.Invoke(nav, new object[] { data, go });
                return false;
            }
            switch (direction)
            {
                case MovementDirection.Right:
                    Vanilla.SwitchToNextStorageSortButton.Invoke(nav, new object[] { data });
                    return false;
                case MovementDirection.Down:
                    return true;
                default:
                    return false;
            }
        }

        // A drag takes its stack out of the source slot when it starts, and an unsuccessful drop writes the whole
        // original stack (item, amount, owner) back over that slot two frames after the drag ends
        // (ItemData.OnBeginDrag/PostDragReturn/ReturnOriginal, ForceItemToSlot with add: false). Moving stacks in
        // between would destroy or duplicate items, so Stash refuses while a drag is active and for 3 frames after.
        internal static bool DragActive()
        {
            bool drag = false;
            try
            {
                if (Vanilla.GlobalDragField != null) drag = (Vanilla.GlobalDragField.GetValue(null) as ItemData) != null;   // Unity null: a destroyed one counts as none
                else drag = Input.GetMouseButton(0);
                var h = InventoryNavigationHandler.Instance;
                if (h != null && h.Dragging) drag = true;
            }
            catch (Exception e)
            {
                Log.Exception("stash drag check", e);
                drag = true;   // fail closed
            }
            if (drag) _dragSeenFrame = Time.frameCount;
            return Time.frameCount - _dragSeenFrame <= 3;
        }

        internal static void Update()
        {
            if (!StoragePanel.Patched || !Cfg.Stash.Value) return;
            if (Storage.active != null) DragActive();   // records the last frame a drag was seen
            if (Game.ConsoleOpen) return;
            if (!Cfg.StashHotkey.Value.IsDown()) return;
            if (Game.IsResourceChest(Storage.active)) Run();
        }
    }

    // S1: StoragePanelUI.UpdateStoragePanelUI(Storage) (SRC/StoragePanelUI.cs:155). Runs on every open (through
    // Storage.GetStorage) before the slot UI is captured.
    [HarmonyPatch(typeof(StoragePanelUI), nameof(StoragePanelUI.UpdateStoragePanelUI))]
    internal static class StoragePanelPatch
    {
        private static void Postfix(StoragePanelUI __instance, Storage storage)
        {
            if (!StoragePanel.Patched || storage == null) return;
            try { Stash.OnPanel(__instance, storage); }
            catch (Exception e) { Log.Exception("stash button", e); }
            try { Chests.UpdateHeader(__instance, storage); }
            catch (Exception e) { Log.Exception("chest header", e); }
        }
    }

    // S2: InventoryNavigationMovement.Move(MovementDirection, InventoryNavigationData, InputType) (SRC :428).
    // __state is the absolute storage row while StorageScroll translated it for a Left exit (-1 otherwise).
    [HarmonyPatch(typeof(InventoryNavigationMovement), nameof(InventoryNavigationMovement.Move))]
    internal static class NavMovePatch
    {
        private static bool Prefix(InventoryNavigationMovement __instance, MovementDirection direction, InventoryNavigationData data, out float __state)
        {
            __state = -1f;
            try
            {
                if (!StoragePanel.Patched || data == null) return true;
                Nav.Record(data.CurrentWindow);
                if (!Stash.NavMove(__instance, direction, data)) return false;
                __state = StorageScroll.BeforeLeaveLeft(direction, data);
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("stash gamepad route", e);
                return true;
            }
        }

        private static void Postfix(InventoryNavigationData data, float __state)
        {
            if (__state < 0f || data == null) return;
            try { StorageScroll.AfterLeaveLeft(data, __state); }
            catch (Exception e) { Log.Exception("storage gamepad exit", e); }
        }
    }
}
