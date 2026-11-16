using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace SafeTravels
{
    // Hotkey reads and the collision check (spec §5), adapted from Time Keeper without the controller code. Every read
    // is an edge (GetKeyDown), so holding a key never repeats. Our reads use UnityEngine.Input, not InputManager, so they
    // bypass Unofficial Patch's console guard: Down checks the console and text entry itself. Not BepInEx's
    // KeyboardShortcut.IsDown: it refuses the shortcut while any other key is held (W to walk, Shift, Ctrl), the way the
    // game's own keys never do.
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

        // GameController.Update reads 1-4 directly (SRC/GameController.cs:231-245); DeveloperConsole toggles on F1
        // (SRC/DeveloperConsole.cs:358).
        private static readonly (KeyCode key, string action)[] HardCoded =
        {
            (KeyCode.Alpha1, "the right hand (1)"), (KeyCode.Alpha2, "the left hand (2)"),
            (KeyCode.Alpha3, "the backpack (3)"), (KeyCode.Alpha4, "the head slot (4)"),
            (KeyCode.F1, "the game console (F1)"),
        };

        private static readonly HashSet<ConfigEntry<KeyboardShortcut>> SuspendedSet = new HashSet<ConfigEntry<KeyboardShortcut>>();
        private static readonly HashSet<string> HookedFriends = new HashSet<string>();
        private static readonly List<string> Pending = new List<string>();

        internal static bool Bound(ConfigEntry<KeyboardShortcut> e) => e != null && e.Value.MainKey != KeyCode.None;

        // The main key went down this frame and every listed modifier is held. Other held keys (movement, Walk,
        // Crouch) do not matter. Never while the console is open or a text field has the keyboard, and never for a
        // suspended key.
        internal static bool Down(ConfigEntry<KeyboardShortcut> e)
        {
            if (!Bound(e) || SuspendedSet.Contains(e) || Game.ConsoleOpen || Game.Typing) return false;
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

        private static IEnumerable<(ConfigEntry<KeyboardShortcut> entry, string feature, bool on)> Ours()
        {
            yield return (Cfg.Unstuck.Hotkey, "Unstuck", Unstuck.Available && Cfg.Unstuck.Enabled.Value);
            yield return (Cfg.Rerail.Hotkey, "Rerail", Rerail.Available && Cfg.Rerail.Enabled.Value);
        }

        // On every scene load and when one of our keys or Enabled entries changes (Recheck), and when another Red Socket
        // mod changes a setting. A key whose main key is also a game action, one of the hard-coded keys, a hotkey of
        // another Red Socket mod or our other key is suspended until the next check. Game key-menu changes count from
        // the next loading screen. One warning per collision per session, one note per check (shown once gameplay is
        // running, see ShowPending).
        internal static void CheckCollisions()
        {
            SuspendedSet.Clear();
            Pending.Clear();

            var active = new List<(ConfigEntry<KeyboardShortcut> entry, string feature)>();
            foreach (var (entry, feature, on) in Ours())
                if (on && Bound(entry) && entry.Value.MainKey < KeyCode.JoystickButton0) active.Add((entry, feature));
            if (active.Count == 0) return;

            KeyBindings kb = null;
            try { kb = InputManager.instance != null ? InputManager.instance.keyBindings : null; }
            catch (Exception) { kb = null; }
            if (kb == null || Vanilla.BindingKey == null)
                Log.WarnOnce("collisions off", "Keys: the game's key bindings could not be read; our keys are not checked against them.");

            foreach (var (entry, feature) in active)
            {
                var key = entry.Value.MainKey;
                string clash = GameAction(kb, key);
                if (clash == null)
                    foreach (var (other, otherFeature) in active)
                        if (other != entry && Same(other.Value, entry.Value)) { clash = "Safe Travels' " + otherFeature + " key"; break; }
                if (clash == null) clash = FriendKey(entry.Value);
                if (clash == null) continue;

                SuspendedSet.Add(entry);
                Log.WarnOnce($"collision|{entry.Definition.Section}|{key}|{clash}",
                    $"Keys: {entry.Definition.Section}.{entry.Definition.Key} ({key}) is also bound to {clash}; the {feature} key is off until one of them changes.");
                Pending.Add($"The {feature} key ({key}) is also bound to {clash}, so it's off for now. Change either one. " +
                            "Changes to the game's key settings take effect at the next loading screen.");
            }
        }

        // A config change mid-game. Skipped before a game scene exists (no InputManager): the first scene load checks.
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
            foreach (var (k, action) in HardCoded)
                if (k == key) return action;
            return null;
        }

        // A KeyboardShortcut entry of another loaded Red Socket mod with the same key and modifiers. Read through its
        // ConfigFile, so we need no reference to it; anything unexpected counts as no clash.
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
                        if (theirs.MainKey == KeyCode.None || !Same(theirs, ours)) continue;
                        return $"{kv.Value.Metadata.Name}'s {entry.Key.Section}.{entry.Key.Key}";
                    }
                }
            }
            catch (Exception e) { Log.Exception("friend key check", e); }
            return null;
        }

        private static bool Same(KeyboardShortcut a, KeyboardShortcut b)
        {
            if (a.MainKey != b.MainKey) return false;
            return new HashSet<KeyCode>(a.Modifiers).SetEquals(b.Modifiers);
        }

        // Called from Plugin.Update: shows the collision notes once the scene is playable.
        internal static void ShowPending()
        {
            if (Pending.Count == 0 || SaveController.Loading || Notifications.instance == null) return;
            foreach (var p in Pending) Game.Post(p);
            Pending.Clear();
        }
    }
}
