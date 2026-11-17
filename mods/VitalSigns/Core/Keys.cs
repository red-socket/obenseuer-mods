using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace VitalSigns
{
    // The optional Access.OpenKey (spec §1 D3), copied from Quest Helper: reads and the collision check. Every read is an
    // edge (GetKeyDown). Not BepInEx's KeyboardShortcut.IsDown: it refuses the shortcut while any other key is held.
    // A key on the forbidden list (Cfg.Forbidden), bound to a game action or a hard-coded key, or equal to another Red
    // Socket mod's hotkey is logged and ignored until it changes. The mod needs no key: the Character page reaches
    // everything.
    internal static class Keys
    {
        private const string Friends = "redsocket.obenseuer.";

        // The action names of KeyBindings.GetKey, copied from its switch (SRC/KeyBindings.cs:268-310).
        private static readonly string[] Actions =
        {
            "Forward", "Backward", "Left", "Right", "Up", "Down", "Walk", "Crouch", "Jump", "Interact", "Menu", "Inventory",
            "Map", "Tasks", "Tenement", "Character", "Notes", "Hide Hud", "Pause Menu", "Hints", "Primary Use", "Auxiliary Use",
            "Head Use", "Primary Reload", "Auxiliary Reload", "Building Menu", "Move Left", "Move Right", "Place Take",
            "Rotate Object", "Rotation Mode", "Snap Object", "Buy Sell", "Resource Slots", "screenshot", "screenshotNoUI",
            "Move Item", "Drag And Drop", "Drop", "Switch Tab Left", "Switch Tab Right", "Use Item", "Use Item Secondary",
            "Exit Cancel",
        };

        // Read directly by the game (SRC/GameController.cs:231-245, menus and dialogue).
        private static readonly KeyCode[] HardCoded =
        {
            KeyCode.Return, KeyCode.X, KeyCode.Escape, KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Tab, KeyCode.Space,
        };

        private static bool _suspended;
        private static readonly HashSet<string> HookedFriends = new HashSet<string>();

        internal static bool Bound(ConfigEntry<KeyboardShortcut> e) => e != null && e.Value.MainKey != KeyCode.None;

        // Bound and not refused: Down can fire for it.
        internal static bool Usable(ConfigEntry<KeyboardShortcut> e) =>
            Bound(e) && !_suspended && !Cfg.Forbidden(e.Value.MainKey) && e.Value.MainKey < KeyCode.JoystickButton0;

        // The main key went down this frame and every listed modifier is held. Never while the console is open or a text
        // field has the keyboard, and never for a forbidden or clashing key.
        internal static bool Down(ConfigEntry<KeyboardShortcut> e)
        {
            if (!Bound(e) || _suspended || Cfg.Forbidden(e.Value.MainKey) || Game.ConsoleOpen || Game.Typing) return false;
            try
            {
                var s = e.Value;
                if (!Input.GetKeyDown(s.MainKey)) return false;
                foreach (var m in s.Modifiers)
                    if (m != KeyCode.None && !Input.GetKey(m)) return false;
                return true;
            }
            catch (Exception ex) { Log.Exception("key read", ex); return false; }
        }

        // On every scene load and when the key changes (Recheck).
        internal static void CheckCollisions()
        {
            _suspended = false;
            var entry = Cfg.Access.OpenKey;
            if (!Bound(entry)) return;
            var key = entry.Value.MainKey;
            if (Cfg.Forbidden(key)) return;   // already warned by Cfg; Down refuses it
            if (key >= KeyCode.JoystickButton0)
            {
                _suspended = true;
                Log.WarnOnce($"pad|{key}", $"Keys: Access.OpenKey ({key}) is a controller button; the key is off. Use the Character page instead.");
                return;
            }

            KeyBindings kb = null;
            try { kb = InputManager.instance != null ? InputManager.instance.keyBindings : null; }
            catch (Exception) { kb = null; }
            if (kb == null || Vanilla.BindingKey == null)
                Log.WarnOnce("collisions off", "Keys: the game's key bindings could not be read; OpenKey is not checked against them.");

            string clash = GameAction(kb, key) ?? FriendKey(entry.Value);
            if (clash == null) return;
            _suspended = true;
            Log.WarnOnce($"collision|{key}|{clash}", $"Keys: Access.OpenKey ({key}) is also bound to {clash}; the key is off until one of them changes.");
        }

        internal static void Recheck()
        {
            try
            {
                if (InputManager.instance == null) return;
                CheckCollisions();
            }
            catch (Exception e) { Log.Exception("key recheck", e); }
        }

        private static string GameAction(KeyBindings kb, KeyCode key)
        {
            if (kb != null && Vanilla.BindingKey != null)
                foreach (var action in Actions)
                {
                    KeyCode bound;
                    try { bound = Vanilla.BindingKey(kb, action); }
                    catch (Exception) { continue; }
                    if (bound != KeyCode.None && bound == key) return "the game's \"" + action + "\" key";
                }
            return Array.IndexOf(HardCoded, key) >= 0 ? "a key the game reads directly (" + key + ")" : null;
        }

        // A KeyboardShortcut entry of another loaded Red Socket mod with the same key and modifiers, read through its
        // ConfigFile (copied from Quest Helper).
        private static string FriendKey(KeyboardShortcut ours)
        {
            try
            {
                foreach (var kv in Chainloader.PluginInfos)
                {
                    if (kv.Key == Plugin.Guid || !kv.Key.StartsWith(Friends, StringComparison.Ordinal)) continue;
                    var inst = kv.Value?.Instance;
                    if (inst == null) continue;
                    var cfg = inst.Config;
                    if (HookedFriends.Add(kv.Key))
                        cfg.SettingChanged += (s, a) => { if (a?.ChangedSetting?.BoxedValue is KeyboardShortcut) Recheck(); };
                    foreach (var entry in cfg)
                    {
                        if (!(entry.Value?.BoxedValue is KeyboardShortcut theirs)) continue;
                        if (theirs.MainKey == KeyCode.None || theirs.MainKey != ours.MainKey) continue;
                        if (!new HashSet<KeyCode>(theirs.Modifiers).SetEquals(ours.Modifiers)) continue;
                        return $"{kv.Value.Metadata.Name}'s {entry.Key.Section}.{entry.Key.Key}";
                    }
                }
            }
            catch (Exception e) { Log.Exception("friend key check", e); }
            return null;
        }
    }
}
