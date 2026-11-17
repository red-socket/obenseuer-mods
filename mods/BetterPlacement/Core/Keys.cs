using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace BetterPlacement
{
    // Hotkey reads and the collision check (spec §5.2), adapted from Workshop and Fewer Clicks. Our reads use
    // UnityEngine.Input, not InputManager, so they bypass Unofficial Patch's console guard: every read checks the
    // console, text entry and window focus itself. Not BepInEx's KeyboardShortcut.IsDown/IsPressed: they refuse the
    // shortcut while any other key is held (W to walk, Shift), the way the game's own keys never do.
    //
    // Held is for Hold and Always modes and the snap modifiers; TapUp is Toggle mode's clean tap; PadHeld/PadDown read the
    // D-pad's horizontal axis, which nothing reads while the building menu is open (QuickSlotBar is blocked there,
    // SRC/QuickSlotBar.cs:145-158; every other reader is a menu).
    internal static class Keys
    {
        private const string Friends = "redsocket.obenseuer.";
        private const string DPadAxis = "DPadHorizontal";

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
        // (SRC/DeveloperConsole.cs:358). Menus and dialogue read Return and X directly (PauseNavigationHandler.cs:98,
        // DialogueController.cs:128); the interaction list reads X and C (InteractableList.cs:115-130); the mouse buttons
        // place and rotate.
        private static readonly (KeyCode key, string action)[] HardCoded =
        {
            (KeyCode.Alpha1, "the right hand (1)"), (KeyCode.Alpha2, "the left hand (2)"),
            (KeyCode.Alpha3, "the backpack (3)"), (KeyCode.Alpha4, "the head slot (4)"),
            (KeyCode.F1, "the game console (F1)"), (KeyCode.Return, "the Return key"), (KeyCode.X, "the X key"),
            (KeyCode.C, "the C key"), (KeyCode.Mouse0, "the left mouse button"), (KeyCode.Mouse1, "the right mouse button"),
        };

        private sealed class HeldState { internal int Epoch; internal bool Armed = true; }
        private sealed class TapState { internal bool Down, Spoiled; internal int Frame; }

        private static readonly HashSet<ConfigEntry<KeyboardShortcut>> SuspendedSet = new HashSet<ConfigEntry<KeyboardShortcut>>();
        private static readonly HashSet<string> HookedFriends = new HashSet<string>();
        private static readonly List<string> Pending = new List<string>();
        private static readonly Dictionary<ConfigEntry<KeyboardShortcut>, HeldState> HeldStates = new Dictionary<ConfigEntry<KeyboardShortcut>, HeldState>();
        private static readonly Dictionary<ConfigEntry<KeyboardShortcut>, TapState> TapStates = new Dictionary<ConfigEntry<KeyboardShortcut>, TapState>();
        private static readonly bool[] PadArmed = { true, true, true };

        private static int _focusEpoch;
        private static bool _padAxisMissing;

        // The snap pad button shares the D-pad axis with the align button; set by CheckCollisions when both are the same.
        internal static bool SnapPadSuspended { get; private set; }

        internal static bool Bound(ConfigEntry<KeyboardShortcut> e)
        {
            if (e == null || e.Value.MainKey == KeyCode.None) return false;
            var k = e.Value.MainKey;
            if (k == KeyCode.Mouse0 || k == KeyCode.Mouse1)
            {
                Log.WarnOnce("key-mouse|" + e.Definition.Key, $"Keys: {e.Definition.Section}.{e.Definition.Key} is {k}, which places and rotates; that key is off.");
                return false;
            }
            return true;
        }

        internal static bool Suspended(ConfigEntry<KeyboardShortcut> e) => e != null && SuspendedSet.Contains(e);

        private static bool Blocked => !Application.isFocused || Game.ConsoleOpen || Game.Typing;

        private static bool ModifiersHeld(KeyboardShortcut s)
        {
            foreach (var m in s.Modifiers)
                if (m != KeyCode.None && !Input.GetKey(m)) return false;
            return true;
        }

        // The main key and every listed modifier are held. Other held keys (movement, Walk, Crouch) do not matter. After
        // the window regains focus, a key reads as released until it is really released or pressed again, so a key the
        // game missed the release of (Alt after Alt+Tab) never sticks.
        internal static bool Held(ConfigEntry<KeyboardShortcut> e)
        {
            if (!Bound(e) || SuspendedSet.Contains(e) || Blocked) return false;
            try
            {
                var s = e.Value;
                if (!HeldStates.TryGetValue(e, out var st)) HeldStates[e] = st = new HeldState { Epoch = _focusEpoch };
                if (st.Epoch != _focusEpoch)
                {
                    st.Epoch = _focusEpoch;
                    st.Armed = false;
                }
                if (!st.Armed)
                {
                    if (!Input.GetKey(s.MainKey)) { st.Armed = true; return false; }
                    if (!Input.GetKeyDown(s.MainKey)) return false;
                    st.Armed = true;
                }
                return Input.GetKey(s.MainKey) && ModifiersHeld(s);
            }
            catch (Exception ex) { Log.Exception("key read", ex); return false; }
        }

        // Toggle mode: true on the key-up of a clean tap, one where no other key or mouse button went down while the key
        // was held. Alt+Tab, Alt+Enter and Alt+click never toggle. Must be called every frame to follow the key.
        internal static bool TapUp(ConfigEntry<KeyboardShortcut> e)
        {
            if (!TapStates.TryGetValue(e, out var st)) TapStates[e] = st = new TapState();
            if (!Bound(e) || SuspendedSet.Contains(e)) { st.Down = false; return false; }
            try
            {
                var s = e.Value;
                if (Input.GetKeyDown(s.MainKey))
                {
                    st.Down = !Blocked && ModifiersHeld(s);
                    st.Spoiled = false;
                    st.Frame = Time.frameCount;
                    return false;
                }
                if (!st.Down) return false;
                if (Blocked) { st.Down = false; return false; }
                if (Input.GetKeyUp(s.MainKey))
                {
                    st.Down = false;
                    return !st.Spoiled;
                }
                if (!Input.GetKey(s.MainKey)) { st.Down = false; return false; }
                if (Time.frameCount != st.Frame && Input.anyKeyDown) st.Spoiled = true;
                return false;
            }
            catch (Exception ex) { Log.Exception("key tap", ex); st.Down = false; return false; }
        }

        // The D-pad button is held: the horizontal axis at -0.5 or below (left) or 0.5 or above (right). Read whatever the
        // game thinks the input type is.
        internal static bool PadHeld(PadButton b)
        {
            if (b == PadButton.None || !Application.isFocused) return false;
            float v = PadAxis();
            return b == PadButton.DPadLeft ? v <= -0.5f : v >= 0.5f;
        }

        // A fresh press of the D-pad button; re-arms once the axis is back within 0.2 of rest. Called every frame.
        internal static bool PadDown(PadButton b)
        {
            if (b == PadButton.None) return false;
            float v = PadAxis();
            int i = (int)b;
            bool pressed = b == PadButton.DPadLeft ? v <= -0.5f : v >= 0.5f;
            bool rest = b == PadButton.DPadLeft ? v > -0.2f : v < 0.2f;
            if (rest) { PadArmed[i] = true; return false; }
            if (!pressed || !PadArmed[i]) return false;
            PadArmed[i] = false;
            return Application.isFocused;
        }

        private static float PadAxis()
        {
            if (_padAxisMissing) return 0f;
            try { return Input.GetAxisRaw(DPadAxis); }
            catch (Exception)
            {
                _padAxisMissing = true;
                Log.WarnOnce("dpad axis", "Keys: the game has no DPadHorizontal axis; the controller buttons are off.");
                return 0f;
            }
        }

        internal static void OnFocus(bool focused)
        {
            if (focused) _focusEpoch++;
            foreach (var st in TapStates.Values) st.Down = false;
        }

        // The game label of a key ("Mouse 4", "Left Alt"), for the controls line and notes.
        internal static string Label(ConfigEntry<KeyboardShortcut> e)
        {
            if (e == null || e.Value.MainKey == KeyCode.None) return "?";
            var s = e.Value;
            string main = KeyText(s.MainKey);
            foreach (var m in s.Modifiers)
                if (m != KeyCode.None) main = KeyText(m) + "+" + main;
            return main;
        }

        private static string KeyText(KeyCode k)
        {
            if (k == KeyCode.LeftAlt) return Strings.LeftAlt;
            if (k == KeyCode.RightAlt) return Strings.RightAlt;
            try
            {
                if (InputManager.instance != null)
                {
                    var t = InputManager.instance.GetKeyCodeDisplayText(k);
                    if (!string.IsNullOrEmpty(t)) return t;
                }
            }
            catch (Exception) { }
            return k.ToString();
        }

        internal static string PadLabel(PadButton b) => b == PadButton.DPadLeft ? Strings.PadLeft : b == PadButton.DPadRight ? Strings.PadRight : "";

        private static IEnumerable<(ConfigEntry<KeyboardShortcut> entry, string feature, bool on)> Ours()
        {
            yield return (Cfg.Align.Key, "aligning furniture", AlignPatches.Patched && Cfg.Align.Enabled.Value);
            yield return (Cfg.Rotation.SnapKey, "rotation snapping", RotationSnap.Patched && Cfg.Rotation.SnapEnabled.Value);
            yield return (Cfg.Rotation.CoarseKey, "coarse rotation snapping", RotationSnap.Patched && Cfg.Rotation.SnapEnabled.Value);
        }

        // On every scene load and when one of our keys changes (Recheck), and when another Red Socket mod changes a key
        // setting. A key whose main key is also a game action, one of the hard-coded keys, a hotkey of another Red Socket
        // mod or another of our keys is suspended until the next check. Game key-menu changes count from the next loading
        // screen. One warning per collision per session, one note per check (shown once gameplay is running).
        internal static void CheckCollisions()
        {
            SuspendedSet.Clear();
            Pending.Clear();
            SnapPadSuspended = false;

            var ap = Cfg.Align.PadButton.Value;
            if (ap != PadButton.None && ap == Cfg.Rotation.SnapPadButton.Value && AlignPatches.Patched && RotationSnap.Patched)
            {
                SnapPadSuspended = true;
                Log.WarnOnce("pad clash|" + ap, $"Keys: Align.PadButton and Rotation.SnapPadButton are both {ap}; the snap button is off until one of them changes.");
                Pending.Add(Strings.PadClash(PadLabel(ap)));
            }

            var active = new List<(ConfigEntry<KeyboardShortcut> entry, string feature)>();
            foreach (var (entry, feature, on) in Ours())
                if (on && Bound(entry) && entry.Value.MainKey < KeyCode.JoystickButton0) active.Add((entry, feature));
            if (active.Count == 0) return;

            KeyBindings kb = null;
            try { kb = InputManager.instance != null ? InputManager.instance.keyBindings : null; }
            catch (Exception) { kb = null; }
            if (kb == null || Vanilla.BindingKey == null)
                Log.WarnOnce("collisions off", "Keys: the game's key bindings could not be read; our keys are not checked against them.");

            for (int i = 0; i < active.Count; i++)
            {
                var (entry, feature) = active[i];
                var key = entry.Value.MainKey;
                string clash = GameAction(kb, key);
                if (clash == null)
                    for (int j = 0; j < i; j++)
                        if (Same(active[j].entry.Value, entry.Value)) { clash = "Better Placement's key for " + active[j].feature; break; }
                if (clash == null) clash = FriendKey(entry.Value);
                if (clash == null) continue;

                SuspendedSet.Add(entry);
                Log.WarnOnce($"collision|{entry.Definition.Section}.{entry.Definition.Key}|{key}|{clash}",
                    $"Keys: {entry.Definition.Section}.{entry.Definition.Key} ({key}) is also bound to {clash}; the key for {feature} is off until one of them changes.");
                Pending.Add(Strings.KeyClash(feature, Label(entry), clash));
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
            foreach (var p in Pending) Notify.Post(p);
            Pending.Clear();
        }
    }
}
