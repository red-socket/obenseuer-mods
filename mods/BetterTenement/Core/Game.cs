using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterTenement
{
    // Small read-only helpers over game state shared by several features.
    internal static class Game
    {
        // "Is this a contractor resource chest", cached per instance id; cleared on every scene load.
        private static readonly Dictionary<int, bool> ChestCache = new Dictionary<int, bool>();

        internal static void OnSceneLoaded() => ChestCache.Clear();

        internal static bool ConsoleOpen
        {
            get
            {
                try { var c = DeveloperConsole.Instance; return c != null && c.consoleOpened; }
                catch { return false; }
            }
        }

        internal static bool ControllerMode
        {
            get
            {
                try { return InputManager.instance != null && InputManager.instance.inputType == InputManager.InputType.Controller; }
                catch { return false; }
            }
        }

        internal static bool MenuVisible
        {
            get
            {
                try { return GameUIController.instance != null && GameUIController.instance.IsMenuVisible; }
                catch { return false; }
            }
        }

        internal static bool Paused
        {
            get
            {
                try { return PauseMenu.instance != null && PauseMenu.instance.PauseIsActive(); }
                catch { return false; }
            }
        }

        internal static bool TradeActive
        {
            get
            {
                try { return TradePanel.instance != null && TradePanel.instance.activeTrade != null; }
                catch { return false; }
            }
        }

        internal static DifficultyInfo Difficulty
        {
            get
            {
                var dc = DifficultyController.instance;
                return dc != null ? dc.currentDifficultyInfo : null;
            }
        }

        internal static bool IsResourceChest(Storage s)
        {
            if (s == null) return false;
            int id = s.GetInstanceID();
            if (!ChestCache.TryGetValue(id, out bool v))
            {
                v = s.GetComponent<TenementResourceStorage>() != null;
                ChestCache[id] = v;
            }
            return v;
        }

        // Non-empty stacks, counted like vanilla's snapshot does (TenementController.cs:1161).
        internal static int Occupied(Storage s)
        {
            int n = 0;
            var slots = s != null ? s.Slots : null;
            if (slots == null) return 0;
            foreach (var sc in slots)
                if (sc != null && sc.itemStack != null && sc.itemStack.itemId != -1 && sc.itemStack.itemAmount > 0) n++;
            return n;
        }

        // The vanilla resource chests registered with the controller: distinct by reference, alive, in list order.
        internal static List<Storage> ResourceChests(TenementController tc)
        {
            var result = new List<Storage>();
            if (tc == null || Vanilla.ResourceStorages == null) return result;
            var list = Vanilla.ResourceStorages(tc);
            if (list == null) return result;
            foreach (var s in list)
            {
                if (s == null || result.Contains(s)) continue;
                result.Add(s);
            }
            return result;
        }

        internal static string FirstName(string full)
        {
            if (string.IsNullOrEmpty(full)) return "?";
            int i = full.IndexOf(' ');
            return i > 0 ? full.Substring(0, i) : full;
        }

        internal static Vector3? PlayerPosition()
        {
            try
            {
                var cam = Camera.main;
                return cam != null ? cam.transform.position : (Vector3?)null;
            }
            catch (Exception) { return null; }
        }
    }
}
