using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace TimeKeeper
{
    // Hotkey and controller reads and the collision check (spec §7), adapted from Fewer Clicks. Every read is an edge
    // (GetKeyDown), so holding a key never repeats. Our reads use UnityEngine.Input, not InputManager, so they also work
    // while the game ignores keys (during a wait) and bypass Unofficial Patch's console guard: Down checks the console
    // and text entry itself. Not BepInEx's KeyboardShortcut.IsDown: it refuses the shortcut while any other key is held
    // (W to walk, Shift, Ctrl), the way the game's own keys never do.
    internal static class Keys
    {
        private const string FewerClicksGuid = "redsocket.obenseuer.fewerclicks";

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

        private static readonly List<string> Pending = new List<string>();

        private static bool _hotkeySuspended;
        private static bool _padSuspended;
        private static bool _padArmed = true;
        private static bool _padAxisMissing;
        private static bool _friendHooked;

        internal static bool Bound(ConfigEntry<KeyboardShortcut> e) => e != null && e.Value.MainKey != KeyCode.None;

        // The main key went down this frame and every listed modifier is held. Other held keys (movement, Walk,
        // Crouch) do not matter. Never while the console is open or a text field has the keyboard. A suspended wait key
        // (collision with a game binding) never fires.
        internal static bool Down(ConfigEntry<KeyboardShortcut> e)
        {
            if (!Bound(e) || Game.ConsoleOpen || Game.Typing) return false;
            if (e == Cfg.Wait.Hotkey && _hotkeySuspended) return false;
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

        // D-pad Up/Down as a button: fires once when the axis crosses ±0.5 and re-arms once it is back within ±0.2. Only
        // read in controller mode; outside it the edge stays disarmed until the D-pad is released. With leaveToList (the
        // open press), a press is left silently to the two free-play readers of that axis in 0.5.19: the third-person
        // interaction list while it shows entries (InteractableList.HandleVerticalInput reads DPadVertical) and the
        // minitrain throttle while riding (Minitrain.FixedUpdate reads JoystickAxis7, which InputManager.asset maps to
        // the same axis 6, SRC/Minitrain.cs:216, :230). Called every frame from WaitSession, so the arm state follows the
        // D-pad even when nothing happens.
        internal static bool PadEdge(PadOpen b, bool leaveToList)
        {
            if (_padAxisMissing) return false;
            float v;
            try { v = Input.GetAxisRaw("DPadVertical"); }
            catch (Exception)
            {
                _padAxisMissing = true;
                Log.WarnOnce("dpad axis", "Keys: the game has no DPadVertical axis; Wait.PadButton is off.");
                return false;
            }
            if (Mathf.Abs(v) < 0.2f) { _padArmed = true; return false; }
            if (!ControllerMode) { _padArmed = false; return false; }
            if (!_padArmed) return false;
            if (b == PadOpen.None || _padSuspended) return false;
            bool hit = b == PadOpen.DPadUp ? v > 0.5f : v < -0.5f;
            if (!hit) return false;
            _padArmed = false;
            if (leaveToList && InteractListShowing())
            {
                Log.Verbose("Wait: D-pad press left to the interaction list.");
                return false;
            }
            if (leaveToList && OnMinitrain())
            {
                Log.Verbose("Wait: D-pad press left to the minitrain throttle.");
                return false;
            }
            return true;
        }

        // A face button went down this frame, in controller mode. The buttons come from the game's own controller
        // bindings (KeyBindings.ControllerA/B/X/Y, SRC/KeyBindings.cs:237-247).
        internal static bool PadDown(PadCancel b)
        {
            if (b == PadCancel.None || !ControllerMode) return false;
            try
            {
                var kb = InputManager.instance.keyBindings;
                if (kb == null) return false;
                KeyCode k;
                switch (b)
                {
                    case PadCancel.A: k = kb.ControllerA; break;
                    case PadCancel.B: k = kb.ControllerB; break;
                    case PadCancel.X: k = kb.ControllerX; break;
                    default: k = kb.ControllerY; break;
                }
                return k != KeyCode.None && Input.GetKeyDown(k);
            }
            catch (Exception e) { Log.Exception("pad read", e); return false; }
        }

        // Riding the minitrain (its throttle reads the D-pad).
        private static bool OnMinitrain()
        {
            try
            {
                var train = TrainController.instance;
                return train != null && train.UsingMiniTrain();
            }
            catch (Exception e)
            {
                Log.Exception("minitrain check", e);
                return false;
            }
        }

        // The pad button as players see it.
        internal static string PadLabel(PadOpen b) => b == PadOpen.DPadUp ? "D-pad Up" : b == PadOpen.DPadDown ? "D-pad Down" : "none";

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

        // On every scene load (spec §7), and again when Wait.Enabled, Wait.Hotkey, Wait.PadButton or Fewer Clicks'
        // QuickEat settings change (Recheck). The game's own key bindings are only read here, so a change in the game's
        // key menu counts from the next loading screen. The wait key is suspended until the next check when its main
        // key is also a game action or one of the hard-coded keys; a Reserved key only warns (Cfg.CheckReserved, from
        // Cfg). The pad button is suspended when Fewer Clicks' Quick Eat is on and uses the same D-pad direction. One
        // warning per collision per session, one note per check (shown once gameplay is running, see ShowPending).
        // CancelKey is exempt: it is only read while waiting, when the game reads no keys at all
        // (SRC/GameController.cs:71).
        internal static void CheckCollisions()
        {
            _hotkeySuspended = false;
            _padSuspended = false;
            Pending.Clear();
            if (!WaitSession.Patched || !Cfg.Wait.Enabled.Value) return;

            var hk = Cfg.Wait.Hotkey;
            if (Bound(hk) && hk.Value.MainKey < KeyCode.JoystickButton0)
            {
                var key = hk.Value.MainKey;
                string clash = GameAction(key);
                if (clash != null)
                {
                    _hotkeySuspended = true;
                    Log.WarnOnce($"collision|{key}|{clash}",
                        $"Keys: Wait.Hotkey ({key}) is also bound to {clash}; Time Keeper's key is off until one of them changes.");
                    Pending.Add($"The wait key ({key}) is also bound to {clash}, so it's off for now. Change either one. " +
                                "Changes to the game's key settings take effect at the next loading screen.");
                }
            }

            var pad = Cfg.Wait.PadButton.Value;
            if (pad != PadOpen.None && FriendPadClash(pad))
            {
                _padSuspended = true;
                Log.WarnOnce($"pad clash|{pad}",
                    $"Keys: Wait.PadButton ({pad}) is also Fewer Clicks' Quick Eat button; Time Keeper's controller button is off until one of them changes.");
                Pending.Add($"{PadLabel(pad)} is also Fewer Clicks' Quick Eat button, so Time Keeper leaves it alone. " +
                            "Change either setting to use it for waiting.");
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

        // The game action or hard-coded key that also uses key, or null.
        private static string GameAction(KeyCode key)
        {
            KeyBindings kb = null;
            try { kb = InputManager.instance != null ? InputManager.instance.keyBindings : null; }
            catch (Exception) { kb = null; }
            if (kb == null || Vanilla.BindingKey == null)
                Log.WarnOnce("collisions off", "Keys: the game's key bindings could not be read; the wait key is not checked against them.");
            else
                foreach (var action in Actions)
                {
                    KeyCode bound;
                    try { bound = Vanilla.BindingKey(kb, action); }
                    catch (Exception) { continue; }
                    if (bound != KeyCode.None && bound == key) return action;
                }
            foreach (var (k, action) in HardCoded)
                if (k == key) return action;
            return null;
        }

        // Fewer Clicks is loaded, its Quick Eat is on and its PadButton is the same D-pad direction. Read through its
        // ConfigFile so we need no reference to it; anything unexpected counts as no clash.
        private static bool FriendPadClash(PadOpen ours)
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(FewerClicksGuid, out var info) || info?.Instance == null) return false;
                var cfg = info.Instance.Config;
                if (!_friendHooked)
                {
                    _friendHooked = true;
                    cfg.SettingChanged += (s, a) =>
                    {
                        if (a?.ChangedSetting?.Definition?.Section == "QuickEat") Recheck();
                    };
                }
                var enabled = new ConfigDefinition("QuickEat", "Enabled");
                var button = new ConfigDefinition("QuickEat", "PadButton");
                if (!cfg.ContainsKey(enabled) || !cfg.ContainsKey(button)) return false;
                if (!(cfg[enabled].BoxedValue is bool on) || !on) return false;
                return string.Equals(cfg[button].BoxedValue?.ToString(), ours.ToString(), StringComparison.Ordinal);
            }
            catch (Exception e)
            {
                Log.Exception("fewer clicks pad check", e);
                return false;
            }
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
