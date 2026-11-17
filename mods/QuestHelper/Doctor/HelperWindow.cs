using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuestHelper
{
    // One line of the window. Rows, buttons and selectors take focus; the rest is text.
    internal sealed class UiItem
    {
        internal enum Kind { Heading, Text, Small, Dim, Warn, Banner, Space, Button, Row, Selector }

        internal Kind K;
        internal string Text, Key;
        internal Action Act;
        internal Action<int> Step;
        internal bool Enabled = true;
        internal int Indent;

        internal bool Focusable => Enabled && (K == Kind.Button || K == Kind.Row || K == Kind.Selector);
    }

    // The page model the tabs fill. Rebuilt in Update (never during OnGUI's Layout/Repaint pair).
    internal sealed class UiList
    {
        internal readonly List<UiItem> Items = new List<UiItem>();

        private UiItem Add(UiItem.Kind k, string text, int indent = 0)
        {
            var i = new UiItem { K = k, Text = text, Indent = indent };
            Items.Add(i);
            return i;
        }

        internal void Heading(string t) => Add(UiItem.Kind.Heading, t);
        internal void Text(string t, int indent = 0) => Add(UiItem.Kind.Text, t, indent);
        internal void Small(string t, int indent = 0) => Add(UiItem.Kind.Small, t, indent);
        internal void Dim(string t, int indent = 0) => Add(UiItem.Kind.Dim, t, indent);
        internal void Warn(string t, int indent = 0) => Add(UiItem.Kind.Warn, t, indent);
        internal void Banner(string t) => Add(UiItem.Kind.Banner, t);
        internal void Space() => Add(UiItem.Kind.Space, "");

        internal void Button(string key, string text, Action act, bool enabled = true, int indent = 0)
        {
            var i = Add(UiItem.Kind.Button, text, indent);
            i.Key = key; i.Act = act; i.Enabled = enabled;
        }

        internal void Row(string key, string text, Action act, int indent = 0)
        {
            var i = Add(UiItem.Kind.Row, text, indent);
            i.Key = key; i.Act = act;
        }

        internal void Selector(string key, string text, Action<int> step)
        {
            var i = Add(UiItem.Kind.Selector, text);
            i.Key = key; i.Step = step;
        }
    }

    // The Quest Helper window (spec §4.1-4.2): IMGUI over the open pause menu, three tabs. While it is open the
    // EventSystem is off (InputBlock), P3 skips the pause menu's own input and P4 routes Esc, pad B and pad Menu to Back.
    // The window reads the pad and keyboard itself on unscaled time: up/down move the focus (0.35 s first repeat, then
    // 0.12 s), left/right change a selector, A / X / Enter activate, B steps back, LB/RB (or the game's Switch Tab keys)
    // change tabs. It closes when the pause menu closes or the scene changes.
    internal static class HelperWindow
    {
        private const float FirstRepeat = 0.35f, Repeat = 0.12f, RefreshSeconds = 0.5f;
        private static readonly string[] TabNames = { Strings.TabStuck, Strings.TabBazaar, Strings.TabTasks };

        internal static bool IsOpen;
        internal static int OpenedFrame = -10, ClosedFrame = -10;
        internal static int Tab;
        internal static bool Available;   // the doctor group is on

        private static List<UiItem> _items = new List<UiItem>();
        private static string _focusKey;
        private static Vector2 _scroll;
        private static bool _scrollToFocus;
        private static Rect _focusRect, _viewRect;
        private static float _nextRefresh;
        private static int _lastBackFrame = -1;
        private static int _vDir, _hDir;
        private static float _vNext, _hNext;
        private static Action _deferred;

        // The window may open (spec §4.1): a level running, no conversation, nothing loading, not dead.
        internal static string OpenBlocked()
        {
            if (!Available) return "off";
            string gate = PlayGate.Blocked();
            if (gate != null) return gate;
            if (Game.InConversation) return "conversation";
            if (TaskController.instance == null) return "no tasks";
            if (!Game.PauseOpen) return "pause menu closed";
            if (Game.ConsoleOpen) return "developer console open";
            return null;
        }

        internal static void Open(string preselectTaskId)
        {
            string blocked = OpenBlocked();
            if (blocked != null)
            {
                Log.Verbose($"Doctor: window not opened ({blocked}).");
                return;
            }
            IsOpen = true;
            OpenedFrame = Time.frameCount;
            InputBlock.Sync(true);
            Confirm.ResetAll();
            _scroll = Vector2.zero;
            _focusKey = null;
            DoctorTab.Reset();
            TaskListTab.Reset();
            Detector.RunAll();
            Tab = 0;
            if (!string.IsNullOrEmpty(preselectTaskId))
            {
                var fix = Detector.StuckFixFor(preselectTaskId);
                if (fix != null) DoctorTab.OpenDetail(fix);
                else
                {
                    Tab = 2;
                    TaskListTab.Focus(preselectTaskId);
                }
            }
            Rebuild();
            Log.Verbose("Doctor: window opened.");
        }

        internal static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ClosedFrame = Time.frameCount;
            InputBlock.Release();
            Confirm.ResetAll();
            _deferred = null;
        }

        // P4 and pad B (deduplicated per frame, since both can fire for one press): an armed confirm is disarmed first,
        // then the tab steps back (detail -> list), then the window closes.
        internal static void Back()
        {
            if (!IsOpen || Time.frameCount == _lastBackFrame) return;
            if (Game.ConsoleOpen) { Close(); return; }
            _lastBackFrame = Time.frameCount;
            if (Confirm.AnyArmed) { Confirm.ResetAll(); Rebuild(); return; }
            bool handled = Tab == 0 ? DoctorTab.Back() : Tab == 2 && TaskListTab.Back();
            if (handled) { Rebuild(); return; }
            Close();
        }

        internal static void SetTab(int t)
        {
            t = (t % TabNames.Length + TabNames.Length) % TabNames.Length;
            if (t == Tab) return;
            Tab = t;
            Confirm.ResetAll();
            _scroll = Vector2.zero;
            _focusKey = null;
            Rebuild();
        }

        internal static void Rebuild()
        {
            var l = new UiList();
            try
            {
                if (Tab == 0) DoctorTab.Build(l);
                else if (Tab == 1) BazaarTab.Build(l);
                else TaskListTab.Build(l);
            }
            catch (Exception e)
            {
                Log.Exception("window page " + Tab, e);
                l.Warn(Strings.CheckFailed);
            }
            l.Space();
            l.Button("close", Strings.Close, Close);
            _items = l.Items;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            if (_focusKey == null || !_items.Exists(i => i.Focusable && i.Key == _focusKey))
                _focusKey = _items.Find(i => i.Focusable)?.Key;
        }

        // Moves the focus to key on the next rebuild (tabs call this when they open a page).
        internal static void FocusOn(string key)
        {
            _focusKey = key;
            _scrollToFocus = true;
        }

        // ---- Hints row (Bazaar and Task list tabs, spec §4.2) ----

        internal static void HintsRow(UiList l)
        {
            int tier = Cfg.Tier;
            var parts = new string[Strings.TierNames.Length];
            for (int i = 0; i < parts.Length; i++)
                parts[i] = i == tier ? Skin.Color("[" + Strings.TierNames[i] + "]", Skin.Amber) : Strings.TierNames[i];
            // Left/right stop at the ends, so a stray left press from "Off" never jumps to "Full steps". Select or a
            // click (d == 0) steps up one tier and wraps from "Full steps" back to "Off".
            l.Selector("hints", Strings.HintsLabel + ":   " + string.Join("  ·  ", parts), d =>
            {
                int n = Strings.TierNames.Length;
                int t = d == 0 ? (Cfg.Tier + 1) % n : Mathf.Clamp(Cfg.Tier + d, 0, n - 1);
                if (t != Cfg.Tier) Cfg.Hints.SpoilerTier.Value = t;
            });
            if (tier >= 2) l.Warn(Strings.SpoilerWarning);
        }

        // ---- Frame ----

        internal static void Update()
        {
            if (!IsOpen) return;
            // The F1 console reads raw keys too (SRC/DeveloperConsole.cs:356): close, so nothing typed there (X, Enter,
            // arrows) reaches the window, and the console's input field gets the EventSystem back.
            if (!Game.PauseOpen || SaveController.Loading || Game.Dead || Game.ConsoleOpen) { Close(); return; }
            InputBlock.Sync(true);
            if (Time.frameCount > OpenedFrame + 1) ReadInput();
            if (IsOpen && Time.unscaledTime >= _nextRefresh) Rebuild();
        }

        private static void ReadInput()
        {
            // Tabs: LB/RB, or the game's own Switch Tab keys (one step per frame even if both fire).
            int tab = 0;
            try
            {
                var im = InputManager.instance;
                if (Input.GetKeyDown(KeyCode.Joystick1Button4) || (im != null && im.GetKeyDown("Switch Tab Left"))) tab = -1;
                else if (Input.GetKeyDown(KeyCode.Joystick1Button5) || (im != null && im.GetKeyDown("Switch Tab Right"))) tab = 1;
            }
            catch (Exception e) { Log.Exception("window tab keys", e); }
            if (tab != 0) { SetTab(Tab + tab); return; }

            if (Input.GetKeyDown(KeyCode.Joystick1Button1)) { Back(); return; }

            if (Input.GetKeyDown(KeyCode.Joystick1Button0) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.X))
            {
                Activate(Focused(), 0);
                return;
            }

            int v = Axis(true);
            if (Held(v, ref _vDir, ref _vNext)) MoveFocus(-v);
            int h = Axis(false);
            if (Held(h, ref _hDir, ref _hNext))
            {
                var f = Focused();
                if (f != null && f.K == UiItem.Kind.Selector) Activate(f, h);
            }
        }

        // +1 up/right, -1 down/left, 0 none. Keyboard: the game's movement keys and the arrows; pad: D-pad and stick.
        private static int Axis(bool vertical)
        {
            float a = 0f;
            try
            {
                a = vertical ? UINavigationCore.VerticalInput() : UINavigationCore.HorizonalInput();
                if (Mathf.Abs(a) < 0.5f) a = Input.GetAxisRaw(vertical ? "LeftStickInventoryVertical" : "LeftStickInventoryHorizontal");
            }
            catch (Exception) { a = 0f; }
            if (vertical)
            {
                if (Input.GetKey(KeyCode.UpArrow)) a = 1f;
                else if (Input.GetKey(KeyCode.DownArrow)) a = -1f;
            }
            else
            {
                if (Input.GetKey(KeyCode.RightArrow)) a = 1f;
                else if (Input.GetKey(KeyCode.LeftArrow)) a = -1f;
            }
            return a > 0.5f ? 1 : a < -0.5f ? -1 : 0;
        }

        private static bool Held(int dir, ref int last, ref float next)
        {
            float now = Time.unscaledTime;
            if (dir == 0) { last = 0; return false; }
            if (dir != last) { last = dir; next = now + FirstRepeat; return true; }
            if (now < next) return false;
            next = now + Repeat;
            return true;
        }

        private static UiItem Focused() => _focusKey == null ? null : _items.Find(i => i.Focusable && i.Key == _focusKey);

        private static void MoveFocus(int delta)
        {
            var list = _items.FindAll(i => i.Focusable);
            if (list.Count == 0) return;
            int cur = list.FindIndex(i => i.Key == _focusKey);
            int next = cur < 0 ? 0 : Mathf.Clamp(cur + delta, 0, list.Count - 1);
            _focusKey = list[next].Key;
            _scrollToFocus = true;
        }

        private static void Activate(UiItem item, int dir)
        {
            if (item == null || !item.Enabled) return;
            _focusKey = item.Key;
            try
            {
                if (item.K == UiItem.Kind.Selector) item.Step?.Invoke(dir);
                else item.Act?.Invoke();
            }
            catch (Exception e) { Log.Exception("window action " + item.Key, e); }
            if (IsOpen) Rebuild();
        }

        // ---- Drawing ----

        internal static void Draw(Vector2 screen)
        {
            if (!IsOpen) return;
            float w = Mathf.Min(1000f, screen.x - 80f), h = Mathf.Max(300f, screen.y - 140f);
            var r = new Rect((screen.x - w) / 2f, 70f, w, h);
            GUI.Box(r, GUIContent.none, Skin.Box);
            GUILayout.BeginArea(new Rect(r.x + 16f, r.y + 12f, r.width - 32f, r.height - 24f));

            GUILayout.BeginHorizontal();
            GUILayout.Label(Strings.ModName, Skin.Title, GUILayout.ExpandWidth(false));
            GUILayout.Space(24f);
            for (int i = 0; i < TabNames.Length; i++)
            {
                int t = i;
                if (GUILayout.Button(TabNames[i], i == Tab ? Skin.TabOn : Skin.Tab, GUILayout.ExpandWidth(false))) Defer(() => SetTab(t));
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            _scroll = GUILayout.BeginScrollView(_scroll);
            bool repaint = Event.current.type == EventType.Repaint;
            foreach (var item in _items) DrawItem(item, repaint);
            GUILayout.EndScrollView();
            if (repaint) _viewRect = GUILayoutUtility.GetLastRect();

            GUILayout.Space(6f);
            GUILayout.Label(FooterHint(), Skin.Small);
            GUILayout.EndArea();

            if (repaint && _scrollToFocus && _focusRect.height > 0f && _viewRect.height > 0f)
            {
                _scrollToFocus = false;
                if (_focusRect.yMin < _scroll.y) _scroll.y = Mathf.Max(0f, _focusRect.yMin - 8f);
                else if (_focusRect.yMax > _scroll.y + _viewRect.height) _scroll.y = _focusRect.yMax - _viewRect.height + 8f;
            }

            var run = _deferred;
            _deferred = null;
            if (run != null)
            {
                try { run(); }
                catch (Exception e) { Log.Exception("window click", e); }
            }
        }

        private static void Defer(Action a) => _deferred += a;

        private static void DrawItem(UiItem item, bool repaint)
        {
            bool focused = item.Focusable && item.Key == _focusKey;
            if (item.Indent > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(item.Indent * 22f);
            }
            switch (item.K)
            {
                case UiItem.Kind.Heading: GUILayout.Label(item.Text, Skin.Heading); break;
                case UiItem.Kind.Text: GUILayout.Label(item.Text, Skin.Label); break;
                case UiItem.Kind.Small: GUILayout.Label(item.Text, Skin.Small); break;
                case UiItem.Kind.Dim: GUILayout.Label(item.Text, Skin.DimLabel); break;
                case UiItem.Kind.Warn: GUILayout.Label(item.Text, Skin.Warn); break;
                case UiItem.Kind.Banner: GUILayout.Label(item.Text, Skin.Banner); break;
                case UiItem.Kind.Space: GUILayout.Space(10f); break;
                case UiItem.Kind.Row:
                    if (GUILayout.Button(item.Text, focused ? Skin.RowFocused : Skin.RowStyle)) Defer(() => Activate(item, 1));
                    break;
                case UiItem.Kind.Selector:
                    if (GUILayout.Button(item.Text, focused ? Skin.ButtonFocused : Skin.Button)) Defer(() => Activate(item, 0));
                    break;
                case UiItem.Kind.Button:
                    var style = !item.Enabled ? Skin.ButtonOff : focused ? Skin.ButtonFocused : Skin.Button;
                    if (GUILayout.Button(item.Text, style, GUILayout.ExpandWidth(false)) && item.Enabled) Defer(() => Activate(item, 1));
                    break;
            }
            if (focused && repaint) _focusRect = GUILayoutUtility.GetLastRect();
            if (item.Indent > 0) GUILayout.EndHorizontal();
        }

        // The game's own hint style ("X)SELECT   ESC)BACK").
        private static string FooterHint()
        {
            try
            {
                bool pad = InputManager.instance != null && InputManager.instance.inputType == InputManager.InputType.Controller;
                string select = pad ? "A)" + Strings.SelectHint : "X/ENTER)" + Strings.SelectHint;
                string back = pad ? "B)" + Strings.BackHint : "ESC)" + Strings.BackHint;
                string tabs = pad ? "LB/RB)" + Strings.PagesHint : InventoryNavigationHandler.KeyCodeInfoToString("", Strings.PagesHint, "Switch Tab Left", "Switch Tab Right");
                return select + "   " + back + (string.IsNullOrEmpty(tabs) ? "" : "   " + tabs);
            }
            catch { return ""; }
        }
    }
}
