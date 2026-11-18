using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTenement
{
    // Which window the gamepad was in before the current Move (recorded by S2). P2 uses it to tell "entering the
    // storage grid" (keep the visible rows) from "moving inside it" (absolute coordinates).
    internal static class Nav
    {
        private static InventoryNavigationHandler.CurrentInventoryWindow _before;
        private static int _frame = -1;

        internal static void Record(InventoryNavigationHandler.CurrentInventoryWindow w)
        {
            _before = w;
            _frame = Time.frameCount;
        }

        // True once, for the SwitchToNextSlot that follows a Move from outside the storage grid in the same frame.
        internal static bool ConsumeEntering()
        {
            bool entering = _frame == Time.frameCount && _before != InventoryNavigationHandler.CurrentInventoryWindow.Storage;
            _frame = -1;
            return entering;
        }
    }

    // Scrolling storage panel (spec §3.3.3). The slot grid (storageSlotPanel) is moved, lazily and once per scene,
    // into a masked viewport with a row scroller. Slots stay immediate children of storageSlotPanel, so the gamepad
    // grid (InventoryNavigationHandler.cs:523, :903) is built exactly as before.
    internal static class StorageScroll
    {
        private static StoragePanelUI _ui;
        private static RectTransform _viewport, _content;
        private static BtRowScroller _scroller;
        private static RectMask2D _mask;
        private static GameObject _wheel;
        private static bool _vanillaThisScene;
        private static float _padV = 20f, _padTop = 10f, _padBottom = 10f, _spacingY;

        internal static BtRowScroller ActiveScroller => _scroller != null && _scroller.enabled ? _scroller : null;

        internal static void OnSceneLoaded()
        {
            _ui = null;
            _viewport = _content = null;
            _scroller = null;
            _mask = null;
            _wheel = null;
            _vanillaThisScene = false;
        }

        // P1: after Storage.GetStorage captured the slots. Only for the main storage panel; crafting, vending and
        // put-item panels use other slot panels and are skipped. Re-runs after the backpack toggle.
        internal static void OnGetStorage(Storage s)
        {
            if (!Chests.Patched || Inventory.instance == null || Vanilla.SlotPanel == null) return;
            var ui = Inventory.instance.storagePanelUI;
            if (ui == null || ui.storageSlotPanel == null || Vanilla.SlotPanel(s) != ui.storageSlotPanel.transform) return;
            int n = s.Slots != null ? s.Slots.Length : 0;
            bool active = n > Chests.VanillaSlots && (Cfg.ScrollAnyLargeStorage.Value || Game.IsResourceChest(s));
            if (active && (_scroller == null || _ui != ui) && !_vanillaThisScene) Inject(ui);
            if (_scroller == null || _ui != ui) return;

            if (active)
            {
                var grid = _content.GetComponent<GridLayoutGroup>();
                int cols = grid != null && grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount ? Math.Max(1, grid.constraintCount) : 8;
                _scroller.Rows = (n + cols - 1) / cols;
                _content.sizeDelta = new Vector2(0f, _padV + _scroller.Rows * _scroller.Pitch);
                // The grid's top and bottom padding bands would show a strip of the rows just outside the view (and let a
                // click reach them): clip to the visible rows only. Mask padding is x left, y bottom, z right, w top.
                if (_mask != null)
                {
                    float viewH = _viewport.rect.height, clipBottom = _padBottom;
                    if (viewH > 1f) clipBottom = Mathf.Max(_padBottom, viewH - _padTop - (_scroller.VisibleRows * _scroller.Pitch - _spacingY));
                    _mask.padding = new Vector4(0f, clipBottom, 0f, _padTop);
                }
                if (_wheel != null) _wheel.SetActive(true);
                _scroller.enabled = true;
                _scroller.SetRow(0);
                Log.Verbose($"Scroll view on \"{s.title}\": {n} slots, {_scroller.Rows} rows.");
            }
            else
            {
                // Pixel-identical to vanilla: the grid is UpperLeft and fills the viewport.
                _scroller.Rows = 0;
                _scroller.SetRow(0);
                _content.sizeDelta = new Vector2(0f, _viewport.rect.height);
                if (_mask != null) _mask.padding = Vector4.zero;
                if (_wheel != null) _wheel.SetActive(false);
                _scroller.enabled = false;
            }
        }

        private static void Inject(StoragePanelUI ui)
        {
            var content = ui.storageSlotPanel.GetComponent<RectTransform>();
            var slots = content != null ? content.parent as RectTransform : null;
            if (content == null || slots == null) { _vanillaThisScene = true; return; }
            if (slots.name == "BT_StorageViewport") { _vanillaThisScene = true; return; }   // never nest twice

            int sibling = content.GetSiblingIndex();
            Vector2 aMin = content.anchorMin, aMax = content.anchorMax, pivot = content.pivot, size = content.sizeDelta, pos = content.anchoredPosition;
            GameObject vgo = null, wgo = null;
            try
            {
                vgo = new GameObject("BT_StorageViewport", typeof(RectTransform));
                var vrt = (RectTransform)vgo.transform;
                vrt.SetParent(slots, false);
                vrt.SetSiblingIndex(sibling);   // "Not available slots" stays behind it
                vrt.anchorMin = Vector2.zero;
                vrt.anchorMax = Vector2.one;
                vrt.sizeDelta = Vector2.zero;
                vrt.anchoredPosition = Vector2.zero;
                var mask = vgo.AddComponent<RectMask2D>();
                var img = vgo.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0f);
                img.raycastTarget = true;   // the wheel also works over the gaps between slots (the mask's padding bands: BT_StorageWheel)
                var sc = vgo.AddComponent<BtRowScroller>();

                // Same rect, just behind the viewport (still in front of "Not available slots"): catches the wheel where
                // the mask padding rejects the viewport's raycast. Active only while the scroll view is.
                wgo = new GameObject("BT_StorageWheel", typeof(RectTransform));
                var wrt = (RectTransform)wgo.transform;
                wrt.SetParent(slots, false);
                wrt.SetSiblingIndex(vrt.GetSiblingIndex());
                wrt.anchorMin = Vector2.zero;
                wrt.anchorMax = Vector2.one;
                wrt.sizeDelta = Vector2.zero;
                wrt.anchoredPosition = Vector2.zero;
                var wimg = wgo.AddComponent<Image>();
                wimg.color = new Color(0f, 0f, 0f, 0f);
                wimg.raycastTarget = true;
                wgo.AddComponent<BtWheelForward>().Target = sc;
                wgo.SetActive(false);

                content.SetParent(vrt, false);
                content.anchorMin = new Vector2(0f, 1f);
                content.anchorMax = new Vector2(1f, 1f);
                content.pivot = new Vector2(0.5f, 1f);
                content.anchoredPosition = Vector2.zero;

                float viewH = slots.rect.height > 1f ? slots.rect.height : 779f;
                var grid = content.GetComponent<GridLayoutGroup>();
                float pitch = 69f;
                if (grid != null)
                {
                    pitch = grid.cellSize.y + grid.spacing.y;
                    _padTop = grid.padding.top;
                    _padBottom = grid.padding.bottom;
                    _padV = _padTop + _padBottom;
                    _spacingY = grid.spacing.y;
                }
                if (pitch < 1f) pitch = 69f;
                sc.Content = content;
                sc.Pitch = pitch;
                sc.VisibleRows = Math.Max(1, (int)((viewH - _padV + (grid != null ? grid.spacing.y : 0f)) / pitch));
                sc.enabled = false;
                content.sizeDelta = new Vector2(0f, viewH);

                _ui = ui;
                _viewport = vrt;
                _content = content;
                _scroller = sc;
                _mask = mask;
                _wheel = wgo;
                Log.Verbose($"Storage scroll view injected: pitch {pitch}, {sc.VisibleRows} visible rows.");
            }
            catch (Exception e)
            {
                Log.Error($"Could not add the storage scroll view; the panel stays vanilla in this area: {e}");
                try
                {
                    content.SetParent(slots, false);
                    content.SetSiblingIndex(sibling);
                    content.anchorMin = aMin;
                    content.anchorMax = aMax;
                    content.pivot = pivot;
                    content.sizeDelta = size;
                    content.anchoredPosition = pos;
                    if (vgo != null) UnityEngine.Object.Destroy(vgo);
                    if (wgo != null) UnityEngine.Object.Destroy(wgo);
                }
                catch (Exception e2) { Log.Exception("scroll view rollback", e2); }
                _scroller = null;
                _mask = null;
                _wheel = null;
                _viewport = _content = null;
                _vanillaThisScene = true;
            }
        }

        // P2: keep the gamepad selection visible and never step onto an empty cell of a partial last row.
        internal static bool BeforeSwitchToNextSlot(ref Vector2 next, Slot[,] grid)
        {
            var sc = ActiveScroller;
            var h = InventoryNavigationHandler.Instance;
            if (sc == null || h == null || h.Storage == null || grid == null || !ReferenceEquals(grid, h.Storage.SlotsTwoDimensional)) return true;
            int cols = grid.GetLength(0), rows = grid.GetLength(1);
            if (cols == 0 || rows == 0) return true;
            int x = Mathf.Clamp(Mathf.RoundToInt(next.x), 0, cols - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(next.y), 0, rows - 1);

            // Entering from Sort/Take all, equipment, the backpack button or the inventory: keep the view.
            if (Nav.ConsumeEntering()) y = Math.Min(sc.FirstRow + y, rows - 1);

            // Null-cell guard (only possible with more stacks than the target): vanilla would throw in SwitchToNextSlot.
            if (grid[x, y] == null)
            {
                int nx = x;
                while (nx >= 0 && grid[nx, y] == null) nx--;
                if (nx < 0) return false;   // empty row: keep the current selection
                x = nx;
            }

            // Scroll before vanilla runs: it may re-parent a controller-dragged item under the target slot.
            if (Cfg.Mode.Value == PanelMode.Pages) sc.SetRow(Math.Min(y / sc.VisibleRows * sc.VisibleRows, sc.MaxFirstRow));
            else if (y < sc.FirstRow) sc.SetRow(y);
            else if (y > sc.FirstRow + sc.VisibleRows - 1) sc.SetRow(y - (sc.VisibleRows - 1));

            next = new Vector2(x, y);
            return true;
        }

        // S2 part: Left from column 0 of the storage maps the row to an equipment slot (or the backpack button) with
        // the absolute row (InventoryNavigationMovement.cs:530-538, EquipmentSlotForRow clamps to rows 0-6). While
        // the view is scrolled, vanilla gets the visible row instead, mirroring P2's entry (FirstRow + row).
        // Returns the absolute row to restore, or -1 when nothing was changed.
        internal static float BeforeLeaveLeft(MovementDirection direction, InventoryNavigationData data)
        {
            var sc = ActiveScroller;
            if (sc == null || sc.FirstRow == 0 || direction != MovementDirection.Left
                || data.CurrentWindow != InventoryNavigationHandler.CurrentInventoryWindow.Storage) return -1f;
            var c = data.CurrentCoordinates;
            if (Mathf.RoundToInt(c.x) != 0) return -1f;
            int visible = Math.Max(0, Mathf.RoundToInt(c.y) - sc.FirstRow);
            data.CurrentCoordinates = new Vector2(c.x, visible);
            return c.y;
        }

        // Vanilla always leaves the storage on that move; if it stayed for any reason, put the absolute row back.
        internal static void AfterLeaveLeft(InventoryNavigationData data, float absoluteRow)
        {
            if (data.CurrentWindow == InventoryNavigationHandler.CurrentInventoryWindow.Storage)
                data.CurrentCoordinates = new Vector2(data.CurrentCoordinates.x, absoluteRow);
        }

        internal static void Update()
        {
            var sc = ActiveScroller;
            if (sc == null || !Game.MenuVisible || Game.ConsoleOpen || !sc.gameObject.activeInHierarchy) return;
            if (Cfg.PrevPage.Value.IsDown()) sc.PageStep(-1);
            else if (Cfg.NextPage.Value.IsDown()) sc.PageStep(1);
        }
    }

    // P1: Storage.GetStorage(bool) (private, SRC/Storage.cs:717).
    [HarmonyPatch(typeof(Storage), "GetStorage", new[] { typeof(bool) })]
    internal static class GetStoragePatch
    {
        private static void Postfix(Storage __instance)
        {
            try { StorageScroll.OnGetStorage(__instance); }
            catch (Exception e) { Log.Exception("storage scroll view", e); }
        }
    }

    // P2: InventoryNavigationMovement.SwitchToNextSlot(InventoryNavigationData, Vector2, Slot[,]) (private, SRC :1240).
    [HarmonyPatch(typeof(InventoryNavigationMovement), "SwitchToNextSlot",
        new[] { typeof(InventoryNavigationData), typeof(Vector2), typeof(Slot[,]) })]
    internal static class SwitchSlotPatch
    {
        private static bool Prefix(ref Vector2 nextSlotCoordinates, Slot[,] slotsTwoDimensional)
        {
            try { return StorageScroll.BeforeSwitchToNextSlot(ref nextSlotCoordinates, slotsTwoDimensional); }
            catch (Exception e)
            {
                Log.Exception("storage gamepad scroll", e);
                return true;
            }
        }
    }
}
