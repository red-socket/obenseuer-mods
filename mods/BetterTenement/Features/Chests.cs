using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BetterTenement
{
    // U-003 bigger resource chests (spec §3.3). How vanilla saves a storage: storageSlots is a positional array whose
    // length is the slot count at save time; the slot count itself is never saved (customStorageSlotAmount and
    // overrideCategotySlotAmount are [fsIgnore] and come from the scene). On load, Start() allocates the scene's
    // count, the saved array replaces it, and ResizeLoadedSlots (Storage.cs:372-393) grows it with empty slots or
    // shrinks it by removing only empty entries from the end. An occupied stack is never dropped.
    internal static class Chests
    {
        internal static bool Patched;
        internal const int VanillaSlots = 88;

        private static float _nextHeader;
        private static TextMeshProUGUI _counter, _counterFor;

        internal static void OnSceneLoaded()
        {
            _counter = null;
            _counterFor = null;
        }

        // The size feature runs when Enabled is on, and also with Enabled off while PrepareForRemoval is on.
        internal static bool SizingOn => Cfg.Chests.Value || Cfg.PrepareForRemoval.Value;

        internal static int TargetSlots()
        {
            if (Cfg.PrepareForRemoval.Value) return VanillaSlots;
            int n = Mathf.Clamp(Cfg.ResourceChestSlots.Value, VanillaSlots, 352);
            return (n + 7) / 8 * 8;
        }

        // B1: before Storage.Start allocates (SetCategorySettings("Chest") uses customStorageSlotAmount when
        // overrideCategotySlotAmount is set, Storage.cs:609). The scene instance is fresh on every load (authored 88),
        // so this only ever enlarges. Both Unity's Start and OnLoadingGame's Start() come through here, so an old
        // 88-slot save grows losslessly through ResizeLoadedSlots.
        internal static void BeforeStart(Storage s)
        {
            if (!Patched || !SizingOn || Vanilla.StartDone == null || Vanilla.CustomSlotAmount == null || Vanilla.OverrideSlotAmount == null) return;
            if (Vanilla.StartDone(s) || !Game.IsResourceChest(s)) return;
            int authored = Vanilla.CustomSlotAmount(s);
            int target = TargetSlots();
            int slots = Math.Max(authored, target);
            Vanilla.CustomSlotAmount(s) = slots;
            Vanilla.OverrideSlotAmount(s) = true;
            if (slots != authored)
                Log.Info($"\"{s.title}\" ({SceneManager.GetActiveScene().name}): resource chest {authored} -> {slots} slots");
            else if (Cfg.PrepareForRemoval.Value)
                Log.Info($"\"{s.title}\" ({SceneManager.GetActiveScene().name}): prepare for removal, allocating {slots} slots");
        }

        // B2: log only. Reports chests holding more stacks than the target (nothing is ever removed).
        internal static void AfterLoad(Storage s)
        {
            if (!Patched || !Game.IsResourceChest(s)) return;
            int occupied = Game.Occupied(s);
            int n = s.storageSlots != null ? s.storageSlots.Length : 0;
            if (!SizingOn)
            {
                // Turned off while the game runs: the chest loads at its saved size, like after removing the mod.
                if (occupied > VanillaSlots)
                    Log.Warn($"\"{s.title}\": {occupied} stacks with ResourceChests.Enabled off. Nothing was removed, but the game's own panel " +
                             "shows only 88 slots: use PrepareForRemoval and move stacks out first (see the mod page).");
                return;
            }
            int target = TargetSlots();
            if (occupied > target)
                Log.Warn($"\"{s.title}\": {occupied} stacks, more than {target} slots. Nothing was removed; the chest has no free slots.");
            if (Cfg.PrepareForRemoval.Value)
                Log.Info($"\"{s.title}\" ({SceneManager.GetActiveScene().name}): prepare for removal: {n} slots, {occupied} stacks" +
                         (occupied > VanillaSlots ? $", move {occupied - VanillaSlots} out before removing the mod." : ", ready to remove."));
        }

        // Header counter (S1 part and every 0.5 s while a resource chest is open). The game's title label is a single
        // line with about 7 characters to spare (Game Logic.prefab "Storage Name Text": 307x77, 22 margins, auto-size
        // 18-20), so the counter is a separate small line under the title instead of a suffix; the title is untouched.
        internal static void UpdateHeader(StoragePanelUI ui, Storage storage)
        {
            if (Vanilla.StorageName == null || ui == null) return;
            var label = Vanilla.StorageName(ui);
            if (label == null) return;
            bool show = storage != null && Cfg.ShowFillCounter.Value && Game.IsResourceChest(storage);
            var counter = CounterFor(label, show);
            if (counter == null) return;
            if (show)
            {
                string text = CounterText(storage);
                if (counter.text != text) counter.text = text;
            }
            if (counter.gameObject.activeSelf != show) counter.gameObject.SetActive(show);
        }

        private static TextMeshProUGUI CounterFor(TextMeshProUGUI label, bool create)
        {
            if (_counter != null && _counterFor == label) return _counter;
            if (!create) return null;
            var go = Object.Instantiate(label.gameObject, label.transform.parent, false);
            go.name = "BT_ChestCounter";
            foreach (Transform child in go.transform) Object.Destroy(child.gameObject);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (t == null) { Object.Destroy(go); return null; }
            t.enableAutoSizing = false;
            t.fontSize = 14f;
            t.margin = Vector4.zero;
            t.richText = true;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.text = "";

            // Same left edge as the title text, one line below its centre, inside the 77-unit header.
            var lrt = label.rectTransform;
            var crt = (RectTransform)go.transform;
            Vector2 size = lrt.rect.size.x > 1f ? lrt.rect.size : lrt.sizeDelta;
            Vector4 m = label.margin;
            crt.anchorMin = lrt.anchorMin;
            crt.anchorMax = lrt.anchorMax;
            crt.pivot = new Vector2(0f, 0.5f);
            crt.sizeDelta = new Vector2(Mathf.Max(80f, size.x - m.x - m.z), 18f);
            float left = lrt.anchoredPosition.x - lrt.pivot.x * size.x + m.x;
            float centre = lrt.anchoredPosition.y + (0.5f - lrt.pivot.y) * size.y;
            crt.anchoredPosition = new Vector2(left, centre - 25f);

            _counter = t;
            _counterFor = label;
            return t;
        }

        // "97/176", "100/176 · 12 above the game's 88" (WarnAboveVanilla), "100/100 · move 12 out" (PrepareForRemoval),
        // plus " · p1/2" in Pages mode.
        private static string CounterText(Storage s)
        {
            int occupied = Game.Occupied(s);
            int n = s.Slots != null ? s.Slots.Length : 0;
            int over = occupied - VanillaSlots;
            bool amber = false;
            string text = $"{occupied}/{n}";
            if (Cfg.PrepareForRemoval.Value)
            {
                amber = over > 0;
                text += over > 0 ? $" · move {over} out" : " · ready to remove";
            }
            else if (over > 0 && Cfg.WarnAboveVanilla.Value)
            {
                amber = true;
                text += $" · {over} above the game's 88";
            }
            var sc = StorageScroll.ActiveScroller;
            if (sc != null && Cfg.Mode.Value == PanelMode.Pages) text += $" · p{sc.Page}/{sc.Pages}";
            return $"<color={(amber ? "#FFAB00" : "#B8A27A")}>{text}</color>";
        }

        internal static void RefreshHeader()
        {
            if (!StoragePanel.Patched) return;
            var chest = Storage.active;
            if (!Game.IsResourceChest(chest) || Inventory.instance == null) return;
            UpdateHeader(Inventory.instance.storagePanelUI, chest);
        }

        internal static void Update()
        {
            if (Time.unscaledTime < _nextHeader) return;
            _nextHeader = Time.unscaledTime + 0.5f;
            RefreshHeader();
        }
    }

    // B1: Storage.Start() (SRC/Storage.cs:418).
    [HarmonyPatch(typeof(Storage), nameof(Storage.Start))]
    internal static class StorageStartPatch
    {
        private static void Prefix(Storage __instance)
        {
            try { Chests.BeforeStart(__instance); }
            catch (Exception e) { Log.Exception("resource chest size", e); }
        }
    }

    // B2: Storage.OnLoadingGame() (SRC/Storage.cs:326).
    [HarmonyPatch(typeof(Storage), nameof(Storage.OnLoadingGame))]
    internal static class StorageLoadPatch
    {
        private static void Postfix(Storage __instance, bool __result)
        {
            if (!__result) return;
            try { Chests.AfterLoad(__instance); }
            catch (Exception e) { Log.Exception("resource chest load check", e); }
        }
    }
}
