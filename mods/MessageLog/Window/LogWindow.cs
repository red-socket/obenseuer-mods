using System;
using UnityEngine;

namespace MessageLog
{
    // The Message Log window (spec §8): IMGUI over the open pause menu, the log newest first with type chips, a search
    // field (keyboard) and the "Only this" pin (a filter that works on a controller). While it is open the EventSystem is
    // off (InputBlock), W1 skips the pause menu's own input and W2 routes Esc, pad B and pad Menu to Back. The window
    // reads the pad and keyboard itself on unscaled time; the mouse works as usual. The list is virtualised: only the
    // visible rows are drawn, so a 5,000-message log costs the same as a short one. The model is rebuilt in Update
    // (LogModel), never in OnGUI. The window changes nothing in the game.
    internal static class LogWindow
    {
        private const float FirstRepeat = 0.35f, Repeat = 0.12f, FastRepeat = 0.04f;
        private const float MaxW = 1000f, CompactBelow = 720f, ScrollbarW = 20f, Gap = 6f;

        // Read by our other mods through reflection (one window at a time). A field, so AccessTools.Field finds it.
        internal static bool IsOpen;
        internal static int OpenedFrame = -10, ClosedFrame = -10;
        internal static bool Available;   // the window group is on
        internal static bool SearchPatched;   // the search group is on (launch)
        // The search box is shown: its group patched at launch and Window.Search is on now (turning it off works at once).
        internal static bool SearchOn => SearchPatched && Cfg.Hot.Search;

        private static long _focusSeq = -1;
        private static int _focusIdx = -1;
        private static bool _focusSearch;  // keyboard only: the search stop above the first row
        private static Vector2 _scroll;
        private static float _rowH = 26f, _viewH = 300f;
        private static bool _scrollToFocus;
        private static int _lastBackFrame = -1, _typeAt = -1;
        private static int _vDir, _hDir, _rDir;
        private static float _vNext, _hNext, _rNext;
        private static bool _ltHeld, _rtHeld, _controllerAtOpen;
        private static Action _deferred;
        private static string _footer = "";
        private static GUIStyle _cell, _cellDim, _cellHead, _detail, _gap;

        // ---- Open and close ----

        internal static string OpenBlocked()
        {
            string early = OpenBlockedBeforePause();
            if (early != null) return early;
            string gate = PlayGate.Blocked();
            if (gate != null) return gate;
            if (Game.InConversation) return "conversation";
            if (Game.ConsoleOpen) return "developer console open";
            if (Game.Typing) return "typing";
            if (!Game.PauseOpen) return "pause menu closed";
            return null;
        }

        // The checks that don't need the pause menu, made before a key pauses the game for the window.
        internal static string OpenBlockedBeforePause()
        {
            if (!Available || !Cfg.Hot.WindowOn) return "off";
            string other = Compat.OtherWindowOpen();
            if (other != null) return other + " is open";
            return null;
        }

        internal static void Open()
        {
            if (IsOpen) return;
            string blocked = OpenBlocked();
            if (blocked != null)
            {
                Log.Verbose($"Window: not opened ({blocked}).");
                return;
            }
            IsOpen = true;
            OpenedFrame = Time.frameCount;
            InputBlock.Sync(true);
            _controllerAtOpen = Game.Controller;
            // The chip and the search stay for the session; the pin doesn't.
            LogModel.Unpin();
            LogModel.MarkDirty();
            LogModel.Tick();
            _focusSearch = false;
            _focusSeq = LogModel.Rows.Count > 0 ? LogModel.Rows[0].Seq : -1;
            _focusIdx = LogModel.Rows.Count > 0 ? 0 : -1;
            _scroll = Vector2.zero;
            _typeAt = -1;
            _vDir = _hDir = _rDir = 0;
            _ltHeld = _rtHeld = true;   // a trigger already held doesn't page at once
            BuildFooter();
            Log.Verbose("Window: opened.");
        }

        internal static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ClosedFrame = Time.frameCount;
            InputBlock.Release();
            SearchGuard.ReleaseNow();
            _deferred = null;
            _typeAt = -1;
        }

        // W2 and pad B (once per frame, since both can fire for one press): typing ends first, then the window closes.
        internal static void Back()
        {
            if (!IsOpen || Time.frameCount == _lastBackFrame) return;
            _lastBackFrame = Time.frameCount;
            if (Game.ConsoleOpen) { Close(); return; }
            if (SearchGuard.Focused) { SearchGuard.RequestEnd(); return; }
            Close();
        }

        // ---- Frame (Plugin.Update, last) ----

        internal static void Update()
        {
            SearchGuard.Tick();
            if (!IsOpen) return;
            // The F1 console reads raw keys too: close, so nothing typed there reaches the window.
            if (!Game.PauseOpen || SaveController.Loading || Game.Dead || Game.ConsoleOpen || !Cfg.Hot.WindowOn) { Close(); return; }
            string other = Compat.OtherWindowOpen();
            if (other != null)
            {
                Log.Verbose($"Window: closed ({other} opened over it).");
                Close();
                return;
            }
            bool pad = Game.Controller;
            if (pad != _controllerAtOpen)
            {
                _controllerAtOpen = pad;
                SearchGuard.RequestEnd();   // the pad's axes are read raw: no typing once it takes over
                _focusSearch = false;
                BuildFooter();
            }
            if (!SearchOn)
            {
                // Window.Search turned off while open: the field goes at the next OnGUI (OnGuiStart), and its filter
                // with it, since nothing on screen could clear it.
                _focusSearch = false;
                _typeAt = -1;
                if (!string.IsNullOrEmpty(LogModel.Search)) LogModel.SetSearch("");
            }
            InputBlock.Sync(true);
            LogModel.Tick();
            FixFocus();
            if (Time.frameCount > OpenedFrame + 1 && !SearchGuard.Blocking) ReadInput();
        }

        private static void ReadInput()
        {
            var im = InputManager.instance;
            if (Input.GetKeyDown(KeyCode.Joystick1Button1)) { Back(); return; }
            bool pad = Game.Controller;

            if (!pad && SearchOn && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.X)))
            {
                // Focus the field next frame, so this Enter or X doesn't also reach it.
                _typeAt = Time.frameCount + 1;
                _focusSearch = false;
                return;
            }

            int v = Axis(true);
            if (Held(v, ref _vDir, ref _vNext, Repeat)) MoveFocus(-v);
            int r = Stick();
            if (Held(r, ref _rDir, ref _rNext, FastRepeat)) MoveFocus(r);

            int h = Axis(false);
            if (Held(h, ref _hDir, ref _hNext, Repeat)) StepChip(h);
            if (im != null)
            {
                if (im.GetKeyDown("Switch Tab Left")) StepChip(-1);
                if (im.GetKeyDown("Switch Tab Right")) StepChip(1);
                if (im.GetKeyDown("Drop")) TogglePin();
            }

            bool lt = Trigger("LeftTrigger"), rt = Trigger("RightTrigger");
            if (lt && !_ltHeld) Page(-1);
            if (rt && !_rtHeld) Page(1);
            _ltHeld = lt;
            _rtHeld = rt;
        }

        // +1 up/right, -1 down/left, 0 none. Keyboard: the game's movement keys (W/S, A/D, as bound) and the arrows; pad:
        // D-pad and left stick. The keyboard keys are read raw (held = held): UINavigationCore's GetKeyWithHold repeats on
        // KeyBindings.HoldTimer, which runs on Time.deltaTime (SRC/KeyBindings.cs:556-563) and stands still under the
        // pause menu's timeScale 0, so a held W would move once. Held() does the repeat on unscaled time. On a pad,
        // InventoryVertical/Horizontal are key axes (up/down, left/right) and the D-pad is its own joystick axis
        // (DPadVertical up-positive, DPadHorizontal right-positive, read as SRC/UINavigationBase.cs:89-116 does).
        private static int Axis(bool vertical)
        {
            float a = 0f;
            try
            {
                var im = InputManager.instance;
                if (im != null && im.inputType == InputManager.InputType.Keyboard)
                {
                    var kb = im.keyBindings;
                    if (kb != null)
                    {
                        if (Input.GetKey(kb.CheckKey(vertical ? "Forward" : "Right"))) a += 1f;
                        if (Input.GetKey(kb.CheckKey(vertical ? "Backward" : "Left"))) a -= 1f;
                    }
                }
                else
                {
                    a = vertical ? UINavigationCore.VerticalInput() : UINavigationCore.HorizonalInput();
                    if (Mathf.Abs(a) < 0.5f) a = Input.GetAxisRaw(vertical ? "DPadVertical" : "DPadHorizontal");
                    if (Mathf.Abs(a) < 0.5f) a = Input.GetAxisRaw(vertical ? "LeftStickInventoryVertical" : "LeftStickInventoryHorizontal");
                }
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

        // Right stick: +1 down, -1 up (the game scrolls the same way, SRC/CardGameOverviewCamera.cs:147).
        private static int Stick()
        {
            try
            {
                float a = Input.GetAxis("RightStickVertical");
                return a > 0.5f ? 1 : a < -0.5f ? -1 : 0;
            }
            catch (Exception) { return 0; }
        }

        // LeftTrigger and RightTrigger are inverted axes in the game's input settings, so a pulled trigger can read
        // negative: test the size, as the game does (SRC/InventoryNavigationHandler.cs:1731).
        private static bool Trigger(string axis)
        {
            try { return Mathf.Abs(Input.GetAxis(axis)) > 0.5f; }
            catch (Exception) { return false; }
        }

        private static bool Held(int dir, ref int last, ref float next, float repeat)
        {
            float now = Time.unscaledTime;
            if (dir == 0) { last = 0; return false; }
            if (dir != last) { last = dir; next = now + FirstRepeat; return true; }
            if (now < next) return false;
            next = now + repeat;
            return true;
        }

        // ---- Focus ----

        private static int FocusIndex()
        {
            var rows = LogModel.Rows;
            if (_focusIdx >= 0 && _focusIdx < rows.Count && rows[_focusIdx].Seq == _focusSeq) return _focusIdx;
            _focusIdx = -1;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Seq == _focusSeq) { _focusIdx = i; break; }
            return _focusIdx;
        }

        private static Entry Focused()
        {
            int i = FocusIndex();
            return i >= 0 ? LogModel.Rows[i] : null;
        }

        // The focused message left the list (filter, cap): the newest row takes the focus.
        private static void FixFocus()
        {
            if (_focusSearch || FocusIndex() >= 0) return;
            var rows = LogModel.Rows;
            _focusSeq = rows.Count > 0 ? rows[0].Seq : -1;
            _focusIdx = rows.Count > 0 ? 0 : -1;
            _scrollToFocus = true;
        }

        private static void FocusRow(int i)
        {
            var rows = LogModel.Rows;
            if (rows.Count == 0) { _focusSeq = -1; _focusIdx = -1; return; }
            i = Mathf.Clamp(i, 0, rows.Count - 1);
            _focusSeq = rows[i].Seq;
            _focusIdx = i;
            _focusSearch = false;
            _scrollToFocus = true;
        }

        private static void MoveFocus(int delta)
        {
            if (_focusSearch)
            {
                if (delta > 0) FocusRow(0);
                return;
            }
            int cur = FocusIndex();
            int next = cur + delta;
            if (next < 0)
            {
                if (SearchOn && !Game.Controller) { _focusSearch = true; _scrollToFocus = false; _scroll.y = 0f; }
                return;
            }
            FocusRow(next);
        }

        private static void Page(int dir)
        {
            int per = Mathf.Max(1, Mathf.FloorToInt(_viewH / _rowH) - 1);
            FocusRow(Mathf.Max(0, FocusIndex()) + dir * per);
        }

        private static bool ChipVisible(int i)
        {
            var c = Categories.Chips[i];
            return c != Cat.Dialogue || (Plugin.DialogueOn && Cfg.Hot.Dialogue) || LogModel.AnyDialogue;
        }

        private static void StepChip(int d)
        {
            int n = Categories.Chips.Length;
            int i = LogModel.Chip;
            for (int k = 0; k < n; k++)
            {
                i = ((i + d) % n + n) % n;
                if (ChipVisible(i)) break;
            }
            SetChip(i);
        }

        private static void SetChip(int i)
        {
            if (i == LogModel.Chip) return;
            LogModel.SetChip(i);
            LogModel.Tick();
            FixFocus();
            _scrollToFocus = true;
        }

        private static void TogglePin()
        {
            var e = Focused();
            if (e == null) { if (LogModel.Pinned) Unpin(); return; }
            LogModel.Pin(e);
            LogModel.Tick();
            FixFocus();
            _scrollToFocus = true;
        }

        private static void Unpin()
        {
            LogModel.Unpin();
            LogModel.Tick();
            FixFocus();
            _scrollToFocus = true;
        }

        // ---- Drawing ----

        internal static void Draw(Vector2 screen)
        {
            if (!IsOpen) return;
            Styles();
            SearchGuard.OnGuiStart(SearchOn && !Game.Controller);
            float w = Mathf.Min(MaxW, screen.x - 80f), h = Mathf.Max(300f, screen.y - 140f);
            var r = new Rect((screen.x - w) / 2f, 70f, w, h);
            GUI.Box(r, GUIContent.none, Skin.Box);
            var a = new Rect(r.x + 16f, r.y + 12f, r.width - 32f, r.height - 24f);
            bool compact = screen.x < CompactBelow;
            float y = a.y;

            // Header: title, counts, Close.
            var title = new GUIContent(Strings.Title);
            float tw = Skin.Title.CalcSize(title).x;
            GUI.Label(new Rect(a.x, y, tw, 32f), title, Skin.Title);
            var close = new GUIContent(Strings.Close);
            float cw = Skin.Button.CalcSize(close).x;
            if (GUI.Button(new Rect(a.xMax - cw, y, cw, 30f), close, Skin.Button)) Defer(Close);
            string count = Strings.Shown(Fmt.Count(LogModel.ShownCount), Fmt.Count(LogModel.Total));
            GUI.Label(new Rect(a.x + tw + 14f, y + 7f, Mathf.Max(0f, a.width - tw - cw - 28f), 24f), count, Skin.Dim);
            y += 38f;

            y = DrawChips(a, y);
            y = DrawSearchRow(a, y);

            // Footer and detail strip from the bottom up; the list takes what is left.
            float footH = Mathf.Ceil(Skin.Small.lineHeight) + 6f;
            float detailH = Mathf.Ceil(_detail.lineHeight) * 4f + 8f;
            var footer = new Rect(a.x, a.yMax - footH, a.width, footH);
            var detail = new Rect(a.x, footer.y - Gap - detailH, a.width, detailH);
            var list = new Rect(a.x, y, a.width, Mathf.Max(_rowH * 2f, detail.y - Gap - y));
            DrawList(list, compact);
            DrawDetail(detail);
            GUI.Label(footer, _footer, Skin.Small);

            var run = _deferred;
            _deferred = null;
            if (run != null)
            {
                try { run(); }
                catch (Exception e) { Log.Exception("window click", e); }
            }
        }

        private static void Defer(Action a) => _deferred += a;

        private static float DrawChips(Rect a, float y)
        {
            float x = a.x, rowH = 30f;
            for (int i = 0; i < Categories.Chips.Length; i++)
            {
                if (!ChipVisible(i)) continue;
                var c = new GUIContent(Categories.ChipLabel(Categories.Chips[i]) + "  " + Fmt.Count(LogModel.ChipCounts[i]));
                var style = i == LogModel.Chip ? Skin.TabOn : Skin.Tab;
                float cw = style.CalcSize(c).x;
                if (x + cw > a.xMax && x > a.x) { x = a.x; y += rowH; }
                int idx = i;
                if (GUI.Button(new Rect(x, y, cw, rowH - 4f), c, style)) Defer(() => SetChip(idx));
                x += cw + 6f;
            }
            return y + rowH + 4f;
        }

        private static float DrawSearchRow(Rect a, float y)
        {
            const float rowH = 30f;
            float x = a.x, right = a.xMax;

            // The pin chip sits at the right end in every mode.
            if (LogModel.Pinned)
            {
                var pin = new GUIContent(Strings.Only(LogModel.PinText));
                var x1 = new GUIContent(Strings.Unpin);
                float xw = Skin.Tab.CalcSize(x1).x;
                float pw = Mathf.Min(Skin.TabOn.CalcSize(pin).x, a.width * 0.45f);
                right -= xw + 2f;
                if (GUI.Button(new Rect(right, y, xw, rowH - 4f), x1, Skin.Tab)) Defer(Unpin);
                right -= pw + 2f;
                GUI.Label(new Rect(right, y, pw, rowH - 4f), pin, Skin.TabOn);
                right -= 10f;
            }

            if (Game.Controller)
            {
                GUI.Label(new Rect(x, y + 4f, Mathf.Max(0f, right - x), rowH), Strings.SearchPad, Skin.Dim);
                return y + rowH + 6f;
            }
            if (!SearchOn)
            {
                if (Plugin.SearchWanted && !SearchPatched) GUI.Label(new Rect(x, y + 4f, Mathf.Max(0f, right - x), rowH), Strings.SearchOff, Skin.Dim);
                return y + rowH + 6f;
            }

            var label = new GUIContent(Strings.SearchLabel);
            float lw = Skin.Label.CalcSize(label).x;
            GUI.Label(new Rect(x, y + 3f, lw, rowH), label, Skin.Label);
            x += lw + 8f;
            var clear = new GUIContent(Strings.Clear);
            float clw = Skin.Button.CalcSize(clear).x;
            float fw = Mathf.Clamp(right - x - clw - 8f, 80f, 380f);
            var field = new Rect(x, y, fw, rowH - 2f);

            if (SearchGuard.BeforeField()) Defer(() => FocusRow(0));
            var ev = Event.current;
            if (SearchGuard.Focused && ev != null && ev.type == EventType.MouseDown && !field.Contains(ev.mousePosition)) SearchGuard.EndEdit();
            GUI.SetNextControlName(SearchGuard.ControlName);
            string s = GUI.TextField(field, LogModel.Search ?? "", 60, _focusSearch ? Skin.FieldFocused : Skin.Field);
            if (s != LogModel.Search) LogModel.SetSearch(s);
            if (_typeAt >= 0 && Time.frameCount >= _typeAt)
            {
                _typeAt = -1;
                GUI.FocusControl(SearchGuard.ControlName);
            }
            SearchGuard.AfterField();
            if (SearchGuard.Focused) _focusSearch = false;
            else if (_focusSearch) GUI.Label(new Rect(field.x + 10f, field.y + 4f, field.width - 20f, field.height), Strings.SearchStop, _cellDim);

            if (GUI.Button(new Rect(field.xMax + 8f, y, clw, rowH - 2f), clear, Skin.Button))
                Defer(() => { LogModel.SetSearch(""); SearchGuard.EndEdit(); });
            return y + rowH + 6f;
        }

        private static void DrawList(Rect list, bool compact)
        {
            var rows = LogModel.Rows;
            _rowH = Mathf.Ceil(_cell.lineHeight) + 8f;
            _viewH = list.height;
            if (rows.Count == 0)
            {
                GUI.Label(new Rect(list.x + 10f, list.y + 6f, list.width - 20f, 30f), History.Count == 0 ? Strings.Empty : Strings.NoMatch, Skin.Dim);
                return;
            }
            float contentW = list.width - ScrollbarW;
            var view = new Rect(0f, 0f, contentW, rows.Count * _rowH);

            if (_scrollToFocus && Event.current.type == EventType.Layout)
            {
                _scrollToFocus = false;
                int f = FocusIndex();
                if (f >= 0)
                {
                    if (f * _rowH < _scroll.y) _scroll.y = f * _rowH;
                    else if ((f + 1) * _rowH > _scroll.y + _viewH) _scroll.y = (f + 1) * _rowH - _viewH;
                }
            }
            _scroll.x = 0f;
            _scroll = GUI.BeginScrollView(list, _scroll, view, false, true);
            int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / _rowH));
            int last = Mathf.Min(rows.Count - 1, first + Mathf.CeilToInt(_viewH / _rowH) + 1);
            int focus = _focusSearch ? -1 : FocusIndex();
            float timeW = TimeWidth();
            for (int i = first; i <= last; i++) DrawRow(rows[i], i, new Rect(0f, i * _rowH, contentW, _rowH - 2f), i == focus, compact, timeW);
            GUI.EndScrollView();
        }

        private static void DrawRow(Entry e, int index, Rect r, bool focused, bool compact, float timeW)
        {
            if (GUI.Button(r, GUIContent.none, focused ? Skin.RowFocused : Skin.Row))
            {
                int i = index;
                Defer(() => FocusRow(i));
            }
            var inner = new Rect(r.x + 8f, r.y, r.width - 16f, r.height);
            if (e.Kind == 'g')
            {
                GUI.Label(inner, Strings.Gap, _gap);
                return;
            }
            LogModel.Show(e);
            string stamp = Fmt.Stamp(e.Day, e.Hm, e.Wd, (e.Flags & Entry.FlagEstimated) != 0);
            GUI.Label(new Rect(inner.x, inner.y, timeW, inner.height), stamp, _cellDim);
            float x = inner.x + timeW + 10f;
            string times = e.Count > 1 ? Strings.Times(e.Count) : "";
            var color = e.Kind == 'c' ? Skin.Amber : Skin.Color(e.Cat);
            if (compact)
            {
                string line = "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + e.ShownHead + ":</color> " + e.RowText + times;
                GUI.Label(new Rect(x, inner.y, inner.xMax - x, inner.height), line, _cell);
                return;
            }
            float headW = Mathf.Min(260f, (inner.xMax - x) * 0.35f);
            _cellHead.normal.textColor = color;
            GUI.Label(new Rect(x, inner.y, headW, inner.height), e.ShownHead, _cellHead);
            x += headW + 10f;
            GUI.Label(new Rect(x, inner.y, inner.xMax - x, inner.height), e.RowText + times, _cell);
        }

        private static float _timeW = -1f;

        private static float TimeWidth()
        {
            if (_timeW < 0f) _timeW = Mathf.Ceil(_cellDim.CalcSize(new GUIContent("~" + Fmt.Stamp(9999, 23 * 60 + 59, 2, false))).x) + 2f;
            return _timeW;
        }

        private static void DrawDetail(Rect d)
        {
            GUI.Box(d, GUIContent.none, Skin.Row);
            var e = _focusSearch ? null : Focused();
            if (e == null) return;
            var inner = new Rect(d.x + 10f, d.y + 4f, d.width - 20f, d.height - 8f);
            float lh = Mathf.Ceil(_detail.lineHeight);
            if (e.Kind == 'g')
            {
                GUI.Label(inner, Strings.Gap, _gap);
                return;
            }
            LogModel.Show(e);
            string meta = Fmt.Stamp(e.Day, e.Hm, e.Wd, (e.Flags & Entry.FlagEstimated) != 0) + " · " + Categories.Name(e.Cat) + " · " + e.ShownHead +
                          (e.Count > 1 ? " ·" + Strings.Times(e.Count) : "");
            GUI.Label(new Rect(inner.x, inner.y, inner.width, lh), meta, _cellDim);
            GUI.Label(new Rect(inner.x, inner.y + lh, inner.width, inner.height - lh), e.ShownText, _detail);
        }

        // The game's own hint style ("B)BACK   LB/RB)FILTER"); built on open and when the input type changes.
        private static void BuildFooter()
        {
            try
            {
                if (Game.Controller) { _footer = Strings.PadFooter; return; }
                string s = "ESC)" + Strings.BackHint;
                if (InputManager.instance != null)
                {
                    s += InventoryNavigationHandler.KeyCodeInfoToString(s, Strings.FilterHint, "Switch Tab Left", "Switch Tab Right");
                    s += InventoryNavigationHandler.KeyCodeInfoToString(s, Strings.OnlyHint, "Drop");
                }
                if (SearchOn) s += "   " + Strings.SearchHint;
                _footer = s;
            }
            catch (Exception) { _footer = "ESC)" + Strings.BackHint; }
        }

        internal static void RefreshFooter()
        {
            if (IsOpen) BuildFooter();
        }

        private static void Styles()
        {
            if (_cell != null) return;
            _cell = new GUIStyle(Skin.Label) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft, richText = true };
            _cellDim = new GUIStyle(_cell);
            _cellDim.normal.textColor = Skin.DimColor;
            _cellHead = new GUIStyle(_cell) { richText = false };
            _gap = new GUIStyle(_cellDim) { fontStyle = FontStyle.Italic };
            _detail = new GUIStyle(Skin.Label) { wordWrap = true, clipping = TextClipping.Clip, alignment = TextAnchor.UpperLeft };
        }
    }
}
