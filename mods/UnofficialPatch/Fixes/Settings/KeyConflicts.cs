using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace UnofficialPatch
{
    // S-073 duplicate-key warning (spec §7b; log only). Two actions that are read in the same context and share a key
    // fight each other (for example ESDF movement against build mode's Move Left/Right, BuildingSystem.cs:637-641).
    // After the bindings are loaded (InputManager.Start, KeyBindingsSave.GetKeyBindings) or changed
    // (KeyBindingsSave.SaveKeyBindings), the check runs once on the next frame. Keys are read through the private
    // KeyBindings.GetKey: CheckKey and GetKeyCode switch the input type as a side effect. Pairs that share a key in the
    // game's own defaults (Interact and Move Right on E, ...) are never reported. The warning repeats only when the set
    // of conflicts changes. Nothing is changed or saved.
    internal static class KeyConflicts
    {
        internal static bool Patched;
        internal static bool Dirty;

        private static int _lastHash;
        private static bool _hadConflicts;

        // InputManager.SetDefaultKeys (SRC/InputManager.cs:368-410), in order.
        private static readonly (string name, KeyCode key)[] Defaults =
        {
            ("Forward", KeyCode.W), ("Backward", KeyCode.S), ("Left", KeyCode.A), ("Right", KeyCode.D),
            ("Walk", KeyCode.LeftShift), ("Crouch", KeyCode.LeftControl), ("Jump", KeyCode.Space), ("Interact", KeyCode.E),
            ("Menu", KeyCode.Tab), ("Inventory", KeyCode.I), ("Map", KeyCode.M), ("Tasks", KeyCode.T), ("Tenement", KeyCode.H),
            ("Character", KeyCode.J), ("Notes", KeyCode.N), ("Pause Menu", KeyCode.Escape), ("Hide Hud", KeyCode.P),
            ("Hints", KeyCode.K), ("Primary Use", KeyCode.Mouse0), ("Auxiliary Use", KeyCode.F), ("Head Use", KeyCode.B),
            ("Primary Reload", KeyCode.G), ("Auxiliary Reload", KeyCode.R), ("Building Menu", KeyCode.L),
            ("Move Left", KeyCode.Q), ("Move Right", KeyCode.E), ("Place Take", KeyCode.Mouse0), ("Rotate Object", KeyCode.Mouse1),
            ("Rotation Mode", KeyCode.V), ("Snap Object", KeyCode.C), ("Buy Sell", KeyCode.F), ("Resource Slots", KeyCode.N),
            ("Move Item", KeyCode.F), ("Drag And Drop", KeyCode.C), ("Drop", KeyCode.R), ("Switch Tab Left", KeyCode.None),
            ("Switch Tab Right", KeyCode.Q), ("Use Item", KeyCode.X), ("Use Item Secondary", KeyCode.Z), ("Exit Cancel", KeyCode.Tab),
        };

        private static readonly string[] Movement = { "Forward", "Backward", "Left", "Right" };

        // Context groups (spec §7b): actions read at the same time.
        private static readonly (string name, HashSet<string> actions)[] Groups =
        {
            ("on foot", Set(Movement, "Walk", "Crouch", "Jump", "Interact", "Menu", "Inventory", "Map", "Tasks", "Tenement", "Character",
                "Notes", "Pause Menu", "Hide Hud", "Hints", "Primary Use", "Auxiliary Use", "Head Use", "Primary Reload", "Auxiliary Reload",
                "Building Menu")),
            ("in build mode", Set(Movement, "Walk", "Crouch", "Jump", "Building Menu", "Menu", "Pause Menu", "Hide Hud", "Move Left", "Move Right",
                "Place Take", "Rotate Object", "Rotation Mode", "Snap Object")),
            ("in menus", Set(Movement, "Menu", "Pause Menu", "Exit Cancel", "Interact", "Drop", "Move Item", "Drag And Drop", "Switch Tab Left",
                "Switch Tab Right", "Use Item", "Use Item Secondary")),
            ("in shops", Set(null, "Menu", "Pause Menu", "Move Left", "Move Right", "Move Item", "Use Item", "Resource Slots")),
        };

        private static HashSet<string> Set(string[] baseSet, params string[] more)
        {
            var s = new HashSet<string>(StringComparer.Ordinal);
            if (baseSet != null) foreach (var x in baseSet) s.Add(x);
            foreach (var x in more) s.Add(x);
            return s;
        }

        internal static void MarkDirty()
        {
            if (Patched && Cfg.WarnKeyConflicts.Value) Dirty = true;
        }

        internal static void Check()
        {
            Dirty = false;
            if (!Patched || !Cfg.WarnKeyConflicts.Value || Vanilla.BindingKey == null) return;
            var kb = InputManager.instance != null ? InputManager.instance.keyBindings : null;
            if (kb == null) return;

            var keys = new KeyCode[Defaults.Length];
            for (int i = 0; i < Defaults.Length; i++) keys[i] = Vanilla.BindingKey(kb, Defaults[i].name);

            var lines = new List<string>();
            for (int i = 0; i < Defaults.Length; i++)
            {
                var k = keys[i];
                if (k == KeyCode.None) continue;
                for (int j = i + 1; j < Defaults.Length; j++)
                {
                    if (keys[j] != k || Defaults[i].key == Defaults[j].key) continue;
                    string group = SharedGroup(Defaults[i].name, Defaults[j].name);
                    if (group != null)
                        lines.Add($"Controls: \"{Defaults[i].name}\" and \"{Defaults[j].name}\" are both bound to {k}; pressing {k} {group} triggers both.");
                }
                string fixedUse = HardCoded(k, Defaults[i].name);
                if (fixedUse != null)
                    lines.Add($"Controls: \"{Defaults[i].name}\" is bound to {k}, which the game always uses for {fixedUse}.");
            }

            int hash = 17;
            foreach (var l in lines) hash = hash * 31 + l.GetHashCode();
            if (hash == _lastHash && lines.Count > 0 == _hadConflicts) return;
            _lastHash = hash;
            if (lines.Count == 0)
            {
                if (_hadConflicts) Log.Info("Controls: no more key conflicts.");
                _hadConflicts = false;
                return;
            }
            _hadConflicts = true;
            foreach (var l in lines) Log.Warn(l);
        }

        private static string SharedGroup(string a, string b)
        {
            foreach (var (name, actions) in Groups)
                if (actions.Contains(a) && actions.Contains(b)) return name;
            return null;
        }

        // Keys the game reads directly: F1 console (DeveloperConsole.cs:358), F11/F12 screenshots (GameController.cs:41/45),
        // 1-4 hand, backpack and head slots on foot (GameController.cs:231-246).
        private static string HardCoded(KeyCode k, string action)
        {
            switch (k)
            {
                case KeyCode.F1: return "the console";
                case KeyCode.F11:
                case KeyCode.F12: return "screenshots";
                case KeyCode.Alpha1:
                case KeyCode.Alpha2:
                case KeyCode.Alpha3:
                case KeyCode.Alpha4:
                    return Groups[0].actions.Contains(action) ? "the hand, backpack and head slots" : null;
                default: return null;
            }
        }
    }

    // InputManager.Start(), private: bindings loaded at launch.
    [HarmonyPatch]
    internal static class InputManagerStartPatch
    {
        private static MethodBase TargetMethod() => Vanilla.InputStart;

        private static void Postfix()
        {
            try { KeyConflicts.MarkDirty(); }
            catch (Exception e) { Log.Exception("Controls: key check", e); }
        }
    }

    // KeyBindingsSave.SaveKeyBindings(string): a binding changed (ResetToDefaults calls it once per action).
    [HarmonyPatch(typeof(KeyBindingsSave), nameof(KeyBindingsSave.SaveKeyBindings), new[] { typeof(string) })]
    internal static class SaveKeyBindingsPatch
    {
        private static void Postfix()
        {
            try { KeyConflicts.MarkDirty(); }
            catch (Exception e) { Log.Exception("Controls: key check", e); }
        }
    }

    // KeyBindingsSave.GetKeyBindings(): bindings read from KeyBindings.json.
    [HarmonyPatch(typeof(KeyBindingsSave), nameof(KeyBindingsSave.GetKeyBindings))]
    internal static class GetKeyBindingsPatch
    {
        private static void Postfix()
        {
            try { KeyConflicts.MarkDirty(); }
            catch (Exception e) { Log.Exception("Controls: key check", e); }
        }
    }
}
