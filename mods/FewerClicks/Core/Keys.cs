using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace FewerClicks
{
    // Hotkey reads and the collision check (spec §7). Every read is an edge (GetKeyDown), so holding a key never
    // repeats. Our reads use UnityEngine.Input, not InputManager, so they bypass Unofficial Patch's console guard: Down
    // checks the console itself. Not BepInEx's KeyboardShortcut.IsDown: it refuses the shortcut while any other key is
    // held (W to walk, Shift, Ctrl), the way the game's own 1-4 keys never do.
    internal static class Keys
    {
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
        private static readonly List<string> Pending = new List<string>();

        private static bool _padArmed = true;
        private static bool _padAxisMissing;

        internal static bool Suspended(ConfigEntry<KeyboardShortcut> e) => e != null && SuspendedSet.Contains(e);

        internal static bool Bound(ConfigEntry<KeyboardShortcut> e) => e != null && e.Value.MainKey != KeyCode.None;

        // The main key went down this frame and every listed modifier is held. Other held keys (movement, Walk,
        // Crouch) do not matter. Of our hotkeys on the same main key, only the most specific one whose modifiers are
        // all held fires, so 5 and Ctrl+5 never both run.
        internal static bool Down(ConfigEntry<KeyboardShortcut> e)
        {
            if (!Bound(e) || Suspended(e) || Game.ConsoleOpen) return false;
            try
            {
                var s = e.Value;
                if (!Input.GetKeyDown(s.MainKey) || !ModifiersHeld(s)) return false;
                var mine = new HashSet<KeyCode>(s.Modifiers);
                foreach (var (other, _) in Cfg.Hotkeys())
                {
                    if (other == e || !Bound(other) || Suspended(other) || !FeatureOn(other)) continue;
                    var o = other.Value;
                    if (o.MainKey != s.MainKey || !ModifiersHeld(o)) continue;
                    var theirs = new HashSet<KeyCode>(o.Modifiers);
                    if (theirs.IsProperSupersetOf(mine)) return false;
                }
                return true;
            }
            catch (Exception ex) { Log.Exception("key read", ex); return false; }
        }

        private static bool ModifiersHeld(KeyboardShortcut s)
        {
            foreach (var m in s.Modifiers)
                if (m != KeyCode.None && !Input.GetKey(m)) return false;
            return true;
        }

        // The game is in controller mode. Unofficial Patch's KeyboardMouseOnly keeps it in keyboard mode, so a device
        // with a stray axis never triggers our pad reads.
        internal static bool ControllerMode
        {
            get
            {
                try { return InputManager.instance != null && InputManager.instance.inputType == InputManager.InputType.Controller; }
                catch (Exception) { return false; }
            }
        }

        // D-pad Up/Down as a button: fires once when the axis crosses ±0.5 and re-arms once it is back within ±0.2.
        // Only P4 calls this, so the free-gameplay gate already applies. Only read in controller mode; outside it the
        // edge stays disarmed until the D-pad is released. The one gameplay reader of DPadVertical in 0.5.19 is the
        // third-person interaction list (InteractableList.HandleVerticalInput), so a press while that list shows
        // entries only moves the list selection.
        internal static bool PadEdge(PadButton b)
        {
            if (b == PadButton.None || _padAxisMissing) return false;
            float v;
            try { v = Input.GetAxisRaw("DPadVertical"); }
            catch (Exception)
            {
                _padAxisMissing = true;
                Log.WarnOnce("dpad axis", "Quick Eat: the game has no DPadVertical axis; the controller button is off.");
                return false;
            }
            if (Mathf.Abs(v) < 0.2f) { _padArmed = true; return false; }
            if (!ControllerMode) { _padArmed = false; return false; }
            if (!_padArmed) return false;
            bool hit = b == PadButton.DPadUp ? v > 0.5f : v < -0.5f;
            if (!hit) return false;
            _padArmed = false;
            if (InteractListShowing())
            {
                Log.Verbose("Quick Eat: D-pad press left to the interaction list.");
                return false;
            }
            return true;
        }

        // The third-person interaction list is visible with at least one entry. Without the private fields, any
        // third-person view counts as showing.
        private static bool InteractListShowing()
        {
            try
            {
                var list = InteractableList.instance;
                if (list == null) return false;
                if (Vanilla.ListVisible == null || Vanilla.ListItems == null)
                {
                    var cam = ThirdPersonCameraCollision.instance;
                    return cam != null && !cam.firstperson;
                }
                if (!Vanilla.ListVisible(list)) return false;
                var items = Vanilla.ListItems(list);
                if (items == null) return false;
                foreach (var go in items)
                    if (go != null && go.activeInHierarchy) return true;
                return false;
            }
            catch (Exception e)
            {
                Log.Exception("interaction list check", e);
                return true;
            }
        }

        // On every scene load. A hotkey whose main key is also a game action, one of the hard-coded 1-4, or another of
        // our hotkeys is suspended until the next check. One warning per collision per session; one notification per
        // suspended hotkey per scene load (shown once gameplay is running, see ShowPending).
        internal static void CheckCollisions()
        {
            SuspendedSet.Clear();
            Pending.Clear();
            var ours = new List<(ConfigEntry<KeyboardShortcut> entry, string feature)>();
            // Only hotkeys whose feature is on: a key kept for a feature that is off never suspends another one.
            foreach (var h in Cfg.Hotkeys())
                if (Bound(h.entry) && !IsJoystick(h.entry.Value.MainKey) && FeatureOn(h.entry)) ours.Add(h);

            KeyBindings kb = null;
            try { kb = InputManager.instance != null ? InputManager.instance.keyBindings : null; }
            catch (Exception) { kb = null; }
            if (kb == null || Vanilla.BindingKey == null)
                Log.WarnOnce("collisions off", "Keys: the game's key bindings could not be read; hotkeys are not checked against them.");

            foreach (var (entry, feature) in ours)
            {
                var key = entry.Value.MainKey;
                string clash = null;
                if (kb != null && Vanilla.BindingKey != null)
                {
                    foreach (var action in Actions)
                    {
                        KeyCode bound;
                        try { bound = Vanilla.BindingKey(kb, action); }
                        catch (Exception) { continue; }
                        if (bound != KeyCode.None && bound == key) { clash = action; break; }
                    }
                }
                if (clash == null)
                    foreach (var (hk, action) in HardCoded)
                        if (hk == key) { clash = action; break; }
                if (clash == null)
                    foreach (var (other, otherFeature) in ours)
                        if (other != entry && SameShortcut(other.Value, entry.Value)) { clash = otherFeature; break; }
                if (clash == null) continue;

                SuspendedSet.Add(entry);
                string label = Label(key);
                Log.WarnOnce($"collision|{entry.Definition.Key}|{key}|{clash}",
                    $"Keys: {entry.Definition.Section}.{entry.Definition.Key} ({key}) is also bound to {clash}; {feature} is off until one of them changes.");
                Pending.Add($"Key {label} is also bound to {clash}. {Capital(feature)} stays off until one of them changes.");
            }
            SlotKeys.ApplyLabels();
        }

        // Called from Plugin.Update: shows the collision notes once the scene is playable.
        internal static void ShowPending()
        {
            if (Pending.Count == 0 || SaveController.Loading || Notifications.instance == null) return;
            foreach (var p in Pending) Game.Notify(p);
            Pending.Clear();
        }

        // The feature behind a hotkey is on and patched (the Auto Swing toggle works while Auto Swing is off).
        private static bool FeatureOn(ConfigEntry<KeyboardShortcut> e)
        {
            if (e == Cfg.TopUp.Hotkey) return TopUp.Patched && Cfg.TopUp.Enabled.Value;
            if (e == Cfg.AutoSwing.ToggleKey) return AutoSwing.Patched;
            if (e == Cfg.QuickEat.Hotkey) return QuickEat.Patched && Cfg.QuickEat.Enabled.Value;
            if (e == Cfg.FillAll.Hotkey) return FillAll.Patched && Cfg.FillAll.Enabled.Value;
            return QuickEat.Patched && Cfg.SlotKeys.Enabled.Value;
        }

        private static bool SameShortcut(KeyboardShortcut a, KeyboardShortcut b)
        {
            if (a.MainKey != b.MainKey) return false;
            var ma = new HashSet<KeyCode>(a.Modifiers);
            return ma.SetEquals(b.Modifiers);
        }

        private static bool IsJoystick(KeyCode k) => k >= KeyCode.JoystickButton0;

        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // AlphaN -> "N", KeypadN -> "N", anything else -> its name cut to 3 characters (spec §2 P5).
        internal static string Label(KeyCode k)
        {
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return ((int)(k - KeyCode.Keypad0)).ToString();
            var s = k.ToString();
            return s.Length > 3 ? s.Substring(0, 3) : s;
        }
    }
}
