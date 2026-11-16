using System;
using System.Reflection;
using HarmonyLib;
using OS.Items;
using UnityEngine;

namespace FewerClicks
{
    // S-057 slot keys (spec §2 P4, P5): keys (6-9 by default, off by default) that use a top-row inventory item through
    // the same call the game's auto-eat uses (ItemInfo.UseItemDirectly). Only consumables: never equip, read, place,
    // open containers, slaughter or hygiene actions, which keeps them off Fair Justice's ItemEquipable path.
    internal static class SlotKeys
    {
        internal static bool LabelsPatched;

        private const float Cooldown = 1f / 3f;   // the Use button's own cooldown (SRC/ItemInfo.cs:23)
        private static float _lastUse = -10f;

        internal static bool AnyDown()
        {
            for (int i = 0; i < 5; i++)
                if (Keys.Down(Cfg.SlotKeys.Keys[i])) { _pressed = i; return true; }
            _pressed = -1;
            return false;
        }

        private static int _pressed = -1;

        internal static void Poll()
        {
            if (_pressed >= 0) Use(_pressed);
            _pressed = -1;
        }

        internal static void Use(int i)
        {
            if (Time.unscaledTime - _lastUse < Cooldown) return;   // too soon: ignored silently, like the Use button's cooldown
            string refusal = null;
            try { refusal = TryUse(i, out var used); if (refusal == null) Done(i, used); }
            catch (Exception e)
            {
                Log.Exception("slot key", e);
                refusal = "can't use this here";
            }
            if (refusal == null) return;
            Log.Verbose($"Slot keys: slot {i + 1} refused ({refusal}).");
            Game.Warning();
            if (Cfg.QuickEat.Notify.Value) Game.Notify($"Slot {i + 1}: {refusal}");
        }

        private static void Done(int i, string used)
        {
            Log.Verbose($"Slot keys: slot {i + 1} used {used}.");
            if (Cfg.QuickEat.Notify.Value) Game.Notify("Used " + used);
        }

        // Returns null when the item was used, otherwise the reason shown to the player.
        private static string TryUse(int i, out string used)
        {
            used = null;
            var inv = Inventory.instance;
            if (inv == null || inv.Slots == null || i >= inv.Slots.Length) return "can't use this here";
            var sc = inv.Slots[i];
            if (sc?.itemStack == null || sc.itemStack.itemId == -1 || sc.itemStack.itemAmount <= 0) return "empty";
            if (PlayerCameraAnimations.ItemAnimationPlaying) return "can't use this here";

            // The slot's ItemData, refreshed through the same list the game's auto-eat reads.
            ItemData data = null;
            foreach (var d in inv.GetInventoryItemData())
                if (d != null && d.slotController == sc) { data = d; break; }
            if (data == null) return "can't use this here";
            if (!ItemMetaUtilities.AllowsUse(data.meta)) return "not yours to use";

            var usedItem = data.GetUsedItem(out _, out _);
            if (usedItem == null || usedItem.Actions == null || usedItem.Actions.Length == 0 || !(usedItem.FirstAction is ItemConsumable c))
                return "can't use this here";
            if (!Allowed(c.Type)) return "can't use this here";
            if (!c.CanUse(data, out string text)) return string.IsNullOrEmpty(text) ? "can't use this here" : text;
            if (!ItemInfoFix.EnsureItemInfo()) return "open your inventory once, then try again";

            used = usedItem.Title;
            _lastUse = Time.unscaledTime;
            ItemInfo.instance.UseItemDirectly(data);
            return null;
        }

        private static bool Allowed(ItemConsumable.ConsumableType t)
        {
            switch (t)
            {
                case ItemConsumable.ConsumableType.Food:
                case ItemConsumable.ConsumableType.Drink:
                    return true;
                case ItemConsumable.ConsumableType.Smoke:
                case ItemConsumable.ConsumableType.Medicine:
                case ItemConsumable.ConsumableType.Bandage:
                    return Cfg.SlotKeys.AllowedTypes.Value == SlotItemTypes.Consumables;
                default:
                    return false;   // Container, Slaughter, Hygiene, None
            }
        }

        // The game's own keyboard-only slot badge (SRC/Slot.cs:183-230), as on the 1, 2 and 3 slots. UI only, never saved.
        internal static void ApplyLabels()
        {
            if (!LabelsPatched) return;
            try
            {
                var inv = Inventory.instance;
                if (inv == null || inv.Slots == null) return;
                for (int i = 0; i < 5 && i < inv.Slots.Length; i++)
                {
                    var slot = inv.Slots[i]?.slot;
                    if (slot == null || slot.SlotHotkeyNumber == null) continue;
                    var e = Cfg.SlotKeys.Keys[i];
                    if (Cfg.SlotKeys.Enabled.Value && Cfg.SlotKeys.ShowKeyLabels.Value && Keys.Bound(e) && !Keys.Suspended(e))
                        slot.SetHotKeyNumber(Keys.Label(e.Value.MainKey));
                    else
                        slot.HideHotKeyNumber();
                }
            }
            catch (Exception e) { Log.Exception("slot labels", e); }
        }

        internal static void HookSettings()
        {
            Cfg.SlotKeys.Enabled.SettingChanged += (s, a) => ApplyLabels();
            Cfg.SlotKeys.ShowKeyLabels.SettingChanged += (s, a) => ApplyLabels();
            foreach (var e in Cfg.SlotKeys.Keys) e.SettingChanged += (s, a) => ApplyLabels();
        }
    }

    // P5: Inventory.Start() (private, SRC/Inventory.cs:213), where the slots are created.
    [HarmonyPatch]
    internal static class InventoryStartPatch
    {
        private static MethodBase TargetMethod() => Vanilla.InventoryStart;

        private static void Postfix() => SlotKeys.ApplyLabels();
    }
}
