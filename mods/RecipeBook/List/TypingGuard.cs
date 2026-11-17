using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RecipeBook
{
    // While the search field has focus, no typed key may reach the game (spec §7). Two layers:
    // 1. The game's own writeLock, written directly: our bar's GameObject goes into GameController.disabledWritelockList
    //    and writeLock is set, as NotesPanel's ControlsDisabled would, but without ControlsDisabled/ControlsEnabled:
    //    ControlsEnabled always runs ControlsEnabledMove, which would unlock camera and movement under the station menu
    //    (SRC/GameController.cs:345-367, :407-412). writeLock silences GameController's non-writeLock branch (journal
    //    keys, Hide HUD, raw Alpha1-4, InteractControls), HandleNonMovementInput (Drop, Use, Exit) and Exit, and makes
    //    the sibling mods' Game.Typing true, so the keys of those whose Keys.Down checks it stay silent (Workshop, Tidy
    //    Inventory, Vital Signs, Quest Helper, Fair Market, Time Keeper, Safe Travels, Better Placement). Hotkeys read
    //    with KeyboardShortcut.IsDown or a raw GetKeyDown without that check (Save Guard, Better Tenement, Fair
    //    Justice, Fewer Clicks) are not covered; design D23 lists the one-line sibling changes.
    // 2. P2-P6: the five InputManager queries answer false while Blocking. That covers the keyboard path of the menu
    //    navigation (WASD), HintOverlay's Hints, the top-level "Pause Menu" -> ShowGameUI, and the writeLock branch's
    //    "Pause Menu"/"Menu" -> WriteLockClose, which is therefore never reached through our lock. GetKeyUp is not
    //    guarded (Unofficial Patch's ConsoleGuard reason: held actions must still release).
    // Blocking also covers the frame after release, whatever the script order between the EventSystem and
    // GameController, so the Esc that ends editing never also closes the station. Focus polling is a backstop for the
    // field's select/deselect events.
    internal static class TypingGuard
    {
        internal static bool Available;

        private static bool _held;
        private static GameController _lockedGc;
        private static GameObject _lockedGo;
        private static FilterBar _bar;
        private static TMPro.TMP_InputField _field;
        private static int _releaseFrame = -100;
        private static bool _controllerAtFocus, _focusSeen;

        // Hot path (every InputManager key query): static reads only, no allocation.
        internal static bool Blocking
        {
            get
            {
                if (_held) return true;
                var f = _field;
                if (!ReferenceEquals(f, null) && f.isFocused) return true;
                return Time.frameCount <= _releaseFrame + 1;
            }
        }

        internal static void Watch(FilterBar bar)
        {
            _bar = bar;
            _field = bar != null ? bar.Field : null;
        }

        // onSelect, and the poll in Tick.
        internal static void Hold(FilterBar bar)
        {
            if (!Available || bar == null) return;
            try
            {
                var gc = GameController.instance;
                if (gc == null) return;
                if (_held && ReferenceEquals(gc, _lockedGc) && ReferenceEquals(bar.gameObject, _lockedGo)) return;
                if (_held) ReleaseNow();
                var list = Vanilla.DisabledWritelockList(gc);
                if (!list.Contains(bar.gameObject)) list.Add(bar.gameObject);
                gc.writeLock = true;
                _lockedGc = gc;
                _lockedGo = bar.gameObject;
                _held = true;
                _bar = bar;
                _field = bar.Field;
                _controllerAtFocus = Game.Controller;
                _focusSeen = false;
                if (Log.VerboseOn) Log.Verbose($"Typing: game keys locked (frame {Time.frameCount}).");
            }
            catch (Exception e) { Log.Exception("Typing: hold", e); }
        }

        // onDeselect, onEndEdit, OnDisable: the lock goes two frames later (Tick), after the Esc/Enter/click frame. Only
        // while we hold the lock or the field has focus, so leaving a station never swallows a key.
        internal static void MarkRelease()
        {
            var f = _field;
            if (_held || (!ReferenceEquals(f, null) && f.isFocused)) _releaseFrame = Time.frameCount;
        }

        internal static void Tick()
        {
            var f = _field;
            bool alive = f != null && f.isActiveAndEnabled;
            bool focused = alive && f.isFocused;
            if (focused && !_held && _bar != null) Hold(_bar);

            if (focused)
            {
                // Forced ends of editing (spec §7.5): the pad takes over (its axes are read raw), or the console opens.
                bool controller = Game.Controller;
                if (_focusSeen && controller && !_controllerAtFocus) { Deactivate(f, "the controller took over"); return; }
                _controllerAtFocus = controller;
                _focusSeen = true;
                if (Game.ConsoleOpen) { Deactivate(f, "the console opened"); return; }
            }

            if (_held && !focused && Time.frameCount > _releaseFrame + 1) ReleaseNow();
        }

        private static void Deactivate(TMPro.TMP_InputField f, string why)
        {
            f.DeactivateInputField();
            MarkRelease();
            Log.Verbose("Typing: field closed, " + why + ".");
        }

        // Scene load, level change, game load, OnDestroy, a guard exception: release at once. Idempotent.
        internal static void ReleaseAll()
        {
            try
            {
                if (_field != null && _field.isFocused) _field.DeactivateInputField();
            }
            catch { }
            ReleaseNow();
            _releaseFrame = -100;
        }

        internal static void Forget(FilterBar bar)
        {
            if (!ReferenceEquals(_bar, bar)) return;
            ReleaseNow();
            _bar = null;
            _field = null;
        }

        // Acts only on the GameController we locked, and only while it is alive; other writeLock holders keep theirs.
        private static void ReleaseNow()
        {
            if (!_held) return;
            _held = false;
            try
            {
                var gc = _lockedGc;
                if (gc != null)
                {
                    var list = Vanilla.DisabledWritelockList(gc);
                    var go = _lockedGo;
                    list.RemoveAll(x => ReferenceEquals(x, go));
                    gc.writeLock = list.Count > 0;
                }
                if (Log.VerboseOn) Log.Verbose($"Typing: game keys released (frame {Time.frameCount}).");
            }
            catch (Exception e) { Log.Exception("Typing: release", e); }
            _lockedGc = null;
            _lockedGo = null;
        }

        // A guard exception: return to the game's behaviour.
        internal static void Fail(string key, Exception e)
        {
            Log.Exception(key, e);
            try { ReleaseAll(); } catch { }
        }
    }

    // P2-P6: the five InputManager key queries (SRC/InputManager.cs:202-306), bool prefixes at default priority. Unofficial
    // Patch's ConsoleGuard prefixes the same methods; both only ever answer false, so their order doesn't matter.
    [HarmonyPatch]
    internal static class GuardGetKeyDown
    {
        private static MethodBase TargetMethod() => Vanilla.GetKeyDown;

        private static bool Prefix(ref bool __result)
        {
            try
            {
                if (!TypingGuard.Blocking) return true;
                __result = false;
                return false;
            }
            catch (Exception e) { TypingGuard.Fail("Typing: GetKeyDown", e); return true; }
        }
    }

    [HarmonyPatch]
    internal static class GuardGetKey
    {
        private static MethodBase TargetMethod() => Vanilla.GetKey;

        private static bool Prefix(ref bool __result)
        {
            try
            {
                if (!TypingGuard.Blocking) return true;
                __result = false;
                return false;
            }
            catch (Exception e) { TypingGuard.Fail("Typing: GetKey", e); return true; }
        }
    }

    [HarmonyPatch]
    internal static class GuardGetKeyWithHold
    {
        private static MethodBase TargetMethod() => Vanilla.GetKeyWithHold;

        private static bool Prefix(ref bool __result)
        {
            try
            {
                if (!TypingGuard.Blocking) return true;
                __result = false;
                return false;
            }
            catch (Exception e) { TypingGuard.Fail("Typing: GetKeyWithHold", e); return true; }
        }
    }

    [HarmonyPatch]
    internal static class GuardGetKeyDownKeyboardOnly
    {
        private static MethodBase TargetMethod() => Vanilla.GetKeyDownKeyboardOnly;

        private static bool Prefix(ref bool __result)
        {
            try
            {
                if (!TypingGuard.Blocking) return true;
                __result = false;
                return false;
            }
            catch (Exception e) { TypingGuard.Fail("Typing: GetKeyDownKeyboardOnly", e); return true; }
        }
    }

    [HarmonyPatch]
    internal static class GuardGetAuxiliaryReloadDown
    {
        private static MethodBase TargetMethod() => Vanilla.GetAuxiliaryReloadDown;

        private static bool Prefix(ref bool __result)
        {
            try
            {
                if (!TypingGuard.Blocking) return true;
                __result = false;
                return false;
            }
            catch (Exception e) { TypingGuard.Fail("Typing: GetAuxiliaryReloadDown", e); return true; }
        }
    }
}
