using System;
using UnityEngine;

namespace MessageLog
{
    // While the search field has the keyboard, no typed key may reach the game (spec §8.6), Recipe Book's TypingGuard
    // adapted to an IMGUI field. Two layers:
    // 1. The game's own writeLock, written directly: our marker GameObject goes into GameController.disabledWritelockList
    //    and writeLock is set, as NotesPanel's ControlsDisabled would, but without ControlsDisabled/ControlsEnabled. That
    //    silences GameController's journal keys and Hide HUD (P runs even while paused, SRC/GameController.cs:106), and
    //    makes every sibling's Game.Typing true, so their hotkeys stay quiet. On release only our marker is removed.
    // 2. T1-T5: the five InputManager key queries answer false while Blocking (Hints on K, the pause menu's Esc and Tab,
    //    our own W/S/Q/R navigation). GetKeyUp is not guarded, so held actions still release. Recipe Book's TypingGuard
    //    and Unofficial Patch's ConsoleGuard prefix the same methods and also only ever answer false.
    // Blocking also covers the frame after release, so the Esc or Enter that ends typing never also acts in the game.
    internal static class SearchGuard
    {
        internal const string ControlName = "MessageLog.Search";

        internal static bool Available;   // the search group is on

        private static bool _fieldFocused, _dropFocus;
        private static int _releaseFrame = -100;
        private static bool _held;
        private static GameController _lockedGc;
        private static GameObject _marker;

        internal static bool Focused => _fieldFocused;

        // Hot path (every InputManager key query): static reads only, no allocation.
        internal static bool Blocking => LogWindow.IsOpen && (_fieldFocused || Time.frameCount <= _releaseFrame + 1);

        // OnGUI, first thing in Draw: a forced end requested outside OnGUI (the controller took over, Back), or a field
        // that isn't drawn any more (controller mode, search off).
        internal static void OnGuiStart(bool fieldDrawn)
        {
            if (!_fieldFocused) { _dropFocus = false; return; }
            if (_dropFocus || !fieldDrawn)
            {
                _dropFocus = false;
                EndEdit();
            }
        }

        // OnGUI, before the TextField: Esc, Enter, Tab and Down end typing (the text is kept). Returns true for Down,
        // which also moves the focus to the first row.
        internal static bool BeforeField()
        {
            if (!_fieldFocused) return false;
            var ev = Event.current;
            if (ev == null || ev.type != EventType.KeyDown) return false;
            var k = ev.keyCode;
            if (k != KeyCode.Escape && k != KeyCode.Return && k != KeyCode.KeypadEnter && k != KeyCode.Tab && k != KeyCode.DownArrow) return false;
            ev.Use();
            EndEdit();
            return k == KeyCode.DownArrow;
        }

        // OnGUI, after the TextField: follows IMGUI's focus by control name (not keyboardControl != 0: UnityExplorer and
        // Save Guard share IMGUI focus).
        internal static void AfterField()
        {
            bool f;
            try { f = GUI.GetNameOfFocusedControl() == ControlName; }
            catch (Exception) { f = false; }
            if (f == _fieldFocused) return;
            _fieldFocused = f;
            if (f) Hold();
            else MarkRelease();
        }

        // A click outside the field, or a forced end.
        internal static void EndEdit()
        {
            try { GUIUtility.keyboardControl = 0; } catch { }
            if (!_fieldFocused) return;
            _fieldFocused = false;
            MarkRelease();
        }

        // Outside OnGUI (controller took over, Back): ends typing at the next OnGUI.
        internal static void RequestEnd()
        {
            if (_fieldFocused) _dropFocus = true;
        }

        private static void MarkRelease() => _releaseFrame = Time.frameCount;

        // LogWindow.Update: the lock goes once the release frame has passed.
        internal static void Tick()
        {
            if (!_held) return;
            if (!LogWindow.IsOpen || (!_fieldFocused && Time.frameCount > _releaseFrame + 1)) ReleaseNow();
        }

        private static void Hold()
        {
            if (!Available) return;
            try
            {
                var gc = GameController.instance;
                if (gc == null) return;
                if (_held && ReferenceEquals(gc, _lockedGc)) return;
                if (_held) ReleaseNow();
                if (_marker == null)
                {
                    _marker = new GameObject("MessageLog SearchLock") { hideFlags = HideFlags.HideAndDontSave };
                    UnityEngine.Object.DontDestroyOnLoad(_marker);
                }
                var list = Vanilla.DisabledWritelockList(gc);
                if (!list.Contains(_marker)) list.Add(_marker);
                gc.writeLock = true;
                _lockedGc = gc;
                _held = true;
                Log.Verbose($"Typing: game keys locked (frame {Time.frameCount}).");
            }
            catch (Exception e) { Fail("Typing: hold", e); }
        }

        // Close, scene load, level change, game load, OnDestroy, a guard exception: release at once. Idempotent. Acts
        // only on the GameController we locked, and only while it is alive; other writeLock holders keep theirs.
        internal static void ReleaseNow()
        {
            if (_fieldFocused)
            {
                _fieldFocused = false;
                try { GUIUtility.keyboardControl = 0; } catch { }
            }
            _dropFocus = false;
            if (!_held) return;
            _held = false;
            try
            {
                var gc = _lockedGc;
                if (gc != null)
                {
                    var list = Vanilla.DisabledWritelockList(gc);
                    var go = _marker;
                    list.RemoveAll(x => ReferenceEquals(x, go));
                    gc.writeLock = list.Count > 0;
                }
                Log.Verbose($"Typing: game keys released (frame {Time.frameCount}).");
            }
            catch (Exception e) { Log.Once("Typing: release", e); }
            _lockedGc = null;
        }

        // A guard exception: back to the game's behaviour.
        internal static void Fail(string key, Exception e)
        {
            Log.Once(key, e);
            try { ReleaseNow(); } catch { }
        }
    }
}
