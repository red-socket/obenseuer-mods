using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VitalSigns
{
    // One line of a page: a label line and, for rows, a dim detail line under it.
    internal sealed class UiLine
    {
        internal enum Kind { Heading, Text, Small, Dim, Warn, Banner, Space, Row }

        internal Kind K;
        internal string Label, Detail;
        internal int Indent;

        private static UiLine Make(Kind k, string t, int indent = 0) => new UiLine { K = k, Label = t ?? "", Indent = indent };

        internal static UiLine Heading(string t) => Make(Kind.Heading, t);
        internal static UiLine Text(string t, int indent = 0) => Make(Kind.Text, t, indent);
        internal static UiLine Small(string t, int indent = 0) => Make(Kind.Small, t, indent);
        internal static UiLine Dim(string t, int indent = 0) => Make(Kind.Dim, t, indent);
        internal static UiLine Warn(string t, int indent = 0) => Make(Kind.Warn, t, indent);
        internal static UiLine Banner(string t) => Make(Kind.Banner, t);
        internal static UiLine Space() => Make(Kind.Space, "");
        internal static UiLine Row(string t, string detail) => new UiLine { K = Kind.Row, Label = t ?? "", Detail = detail };
    }

    // The Vital Signs window (spec §9.1): read-only IMGUI over the open pause menu, three pages (Health, Mental health,
    // Addictions). While it is open the EventSystem is off (InputBlock), W1 skips the pause menu's own input and W2
    // routes Esc, pad B and pad Menu to Back. The window reads the pad and keyboard itself on unscaled time: LB/RB (or
    // the game's Switch Tab keys) change page, up/down scroll (0.35 s first repeat, then 0.12 s). The page model is
    // rebuilt every 0.5 s in Update, never in OnGUI. Closes when pause closes, on a scene load or level change, on
    // death and when the console opens.
    internal static class VitalWindow
    {
        private enum PageId { Health, Mood, Addictions }

        private const float FirstRepeat = 0.35f, Repeat = 0.12f, RefreshSeconds = 0.5f, ScrollStep = 60f;
        private const int MaxRows = 200;

        internal static bool IsOpen;
        internal static int OpenedFrame = -10, ClosedFrame = -10;
        internal static bool Available;   // the window group is on

        private static readonly List<PageId> Pages = new List<PageId>();
        private static int _page;
        private static List<UiLine> _lines = new List<UiLine>();
        private static Vector2 _scroll;
        private static float _nextRefresh;
        private static int _lastBackFrame = -1;
        private static int _vDir;
        private static float _vNext;
        private static Action _deferred;

        // ---- Quest Helper's and Fair Market's windows (one window at a time, spec §9.1) ----

        private static readonly string[] OtherWindowTypes = { "QuestHelper.HelperWindow", "FairMarket.InvoiceWindow" };
        private static FieldInfo[] _otherOpen;

        private static string OtherWindowOpen()
        {
            try
            {
                if (_otherOpen == null)
                {
                    _otherOpen = new FieldInfo[OtherWindowTypes.Length];
                    for (int i = 0; i < OtherWindowTypes.Length; i++)
                    {
                        var t = AccessTools.TypeByName(OtherWindowTypes[i]);
                        _otherOpen[i] = t != null ? AccessTools.Field(t, "IsOpen") : null;
                    }
                }
                for (int i = 0; i < _otherOpen.Length; i++)
                    if (_otherOpen[i] != null && _otherOpen[i].GetValue(null) is bool b && b) return OtherWindowTypes[i];
            }
            catch { }
            return null;
        }

        internal static string OpenBlocked()
        {
            if (!Available) return "off";
            string gate = PlayGate.Blocked();
            if (gate != null) return gate;
            if (Game.InConversation) return "conversation";
            if (Game.ConsoleOpen) return "developer console open";
            if (Game.Typing) return "typing";
            string other = OtherWindowOpen();
            if (other != null) return other + " is open";
            if (!Game.PauseOpen) return "pause menu closed";
            return null;
        }

        internal static void Open()
        {
            string blocked = OpenBlocked();
            if (blocked != null)
            {
                Log.Verbose($"Ui: window not opened ({blocked}).");
                return;
            }
            BuildPages();
            if (Pages.Count == 0) return;
            IsOpen = true;
            OpenedFrame = Time.frameCount;
            InputBlock.Sync(true);
            _scroll = Vector2.zero;
            _page = 0;
            Rebuild();
            Log.Verbose("Ui: window opened.");
        }

        internal static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ClosedFrame = Time.frameCount;
            InputBlock.Release();
            _deferred = null;
        }

        // W2 and pad B (deduplicated per frame, since both can fire for one press).
        internal static void Back()
        {
            if (!IsOpen || Time.frameCount == _lastBackFrame) return;
            _lastBackFrame = Time.frameCount;
            Close();
        }

        private static void BuildPages()
        {
            Pages.Clear();
            if (Tracker.Live) Pages.Add(PageId.Health);
            if (Cfg.Hot.MoodOn) Pages.Add(PageId.Mood);
            Pages.Add(PageId.Addictions);
        }

        private static void SetPage(int p)
        {
            int n = Pages.Count;
            if (n == 0) return;
            p = (p % n + n) % n;
            if (p == _page) return;
            _page = p;
            _scroll = Vector2.zero;
            Rebuild();
        }

        private static void Rebuild()
        {
            var l = new List<UiLine>();
            try
            {
                switch (Pages[_page])
                {
                    case PageId.Health: HealthPage(l); break;
                    case PageId.Mood: MoodModel.Page(l); break;
                    default: AddictionModel.Page(l); break;
                }
            }
            catch (Exception e)
            {
                Log.Exception("window page " + Pages[_page], e);
                l.Add(UiLine.Warn(Strings.PageFailed));
            }
            _lines = l;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
        }

        private static void HealthPage(List<UiLine> l)
        {
            l.Add(UiLine.Heading(Strings.LosingNow));
            Forecast.Lines(l);
            var d = Alerts.LastDeath;
            if (d != null)
            {
                l.Add(UiLine.Space());
                l.Add(UiLine.Banner(Strings.LastDeath(Fmt.Stamp(d.Stamp), d.Title)));
                if (!string.IsNullOrEmpty(d.Detail)) l.Add(UiLine.Small(d.Detail, 1));
            }
            l.Add(UiLine.Heading(Strings.LogHeading));
            var entries = DamageLog.Entries;
            if (entries.Count == 0) { l.Add(UiLine.Dim(Strings.LogEmpty)); return; }
            int shown = 0;
            for (int i = entries.Count - 1; i >= 0 && shown < MaxRows; i--, shown++)
            {
                var e = entries[i];
                switch (e.Kind)
                {
                    case EntryKind.Divider: l.Add(UiLine.Dim(Strings.DividerLine(Fmt.Stamp(e.FirstStamp), e.Label))); break;
                    case EntryKind.Death: l.Add(UiLine.Row(Skin.Color(Fmt.Stamp(e.FirstStamp) + "   " + Skin.Esc(e.Label), Skin.Amber), Skin.Esc(e.Detail))); break;
                    default: l.Add(UiLine.Row(Skin.Esc(Strings.Line(e)), Skin.Esc(Strings.EntryDetail(e)))); break;
                }
            }
            if (entries.Count > MaxRows) l.Add(UiLine.Dim(Strings.OlderHidden(entries.Count - MaxRows)));
        }

        // ---- Frame ----

        internal static void Update()
        {
            if (!IsOpen) return;
            // The F1 console reads raw keys too (SRC/DeveloperConsole.cs:356): close, so nothing typed there reaches the
            // window, and the console's input field gets the EventSystem back.
            if (!Game.PauseOpen || SaveController.Loading || Game.Dead || Game.ConsoleOpen) { Close(); return; }
            InputBlock.Sync(true);
            if (Time.frameCount > OpenedFrame + 1) ReadInput();
            if (IsOpen && Time.unscaledTime >= _nextRefresh) Rebuild();
        }

        private static void ReadInput()
        {
            int tab = 0;
            try
            {
                var im = InputManager.instance;
                if (Input.GetKeyDown(KeyCode.Joystick1Button4) || (im != null && im.GetKeyDown("Switch Tab Left"))) tab = -1;
                else if (Input.GetKeyDown(KeyCode.Joystick1Button5) || (im != null && im.GetKeyDown("Switch Tab Right"))) tab = 1;
            }
            catch (Exception e) { Log.Exception("window tab keys", e); }
            if (tab != 0) { SetPage(_page + tab); return; }

            if (Input.GetKeyDown(KeyCode.Joystick1Button1)) { Back(); return; }

            int v = Axis();
            if (Held(v, ref _vDir, ref _vNext)) _scroll.y = Mathf.Max(0f, _scroll.y - v * ScrollStep);
        }

        // +1 up, -1 down, 0 none. Keyboard: the game's movement keys and the arrows; pad: D-pad and stick.
        private static int Axis()
        {
            float a = 0f;
            try
            {
                a = UINavigationCore.VerticalInput();
                if (Mathf.Abs(a) < 0.5f) a = Input.GetAxisRaw("LeftStickInventoryVertical");
            }
            catch (Exception) { a = 0f; }
            if (Input.GetKey(KeyCode.UpArrow)) a = 1f;
            else if (Input.GetKey(KeyCode.DownArrow)) a = -1f;
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

        // ---- Drawing ----

        internal static void Draw(Vector2 screen)
        {
            if (!IsOpen || Pages.Count == 0) return;
            float w = Mathf.Min(1000f, screen.x - 80f), h = Mathf.Max(300f, screen.y - 140f);
            var r = new Rect((screen.x - w) / 2f, 70f, w, h);
            GUI.Box(r, GUIContent.none, Skin.Box);
            GUILayout.BeginArea(new Rect(r.x + 16f, r.y + 12f, r.width - 32f, r.height - 24f));

            GUILayout.BeginHorizontal();
            GUILayout.Label(Strings.ModName, Skin.Title, GUILayout.ExpandWidth(false));
            GUILayout.Space(24f);
            for (int i = 0; i < Pages.Count; i++)
            {
                int p = i;
                if (GUILayout.Button(Strings.PageName((int)Pages[i]), i == _page ? Skin.TabOn : Skin.Tab, GUILayout.ExpandWidth(false))) Defer(() => SetPage(p));
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Strings.Close, Skin.Button, GUILayout.ExpandWidth(false))) Defer(Close);
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var line in _lines) DrawLine(line);
            GUILayout.EndScrollView();

            GUILayout.Space(6f);
            GUILayout.Label(FooterHint(), Skin.Small);
            GUILayout.EndArea();

            var run = _deferred;
            _deferred = null;
            if (run != null)
            {
                try { run(); }
                catch (Exception e) { Log.Exception("window click", e); }
            }
        }

        private static void Defer(Action a) => _deferred += a;

        private static void DrawLine(UiLine line)
        {
            if (line.Indent > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(line.Indent * 22f);
            }
            switch (line.K)
            {
                case UiLine.Kind.Heading: GUILayout.Label(line.Label, Skin.Heading); break;
                case UiLine.Kind.Text: GUILayout.Label(line.Label, Skin.Label); break;
                case UiLine.Kind.Small: GUILayout.Label(line.Label, Skin.Small); break;
                case UiLine.Kind.Dim: GUILayout.Label(line.Label, Skin.DimLabel); break;
                case UiLine.Kind.Warn: GUILayout.Label(line.Label, Skin.Warn); break;
                case UiLine.Kind.Banner: GUILayout.Label(line.Label, Skin.Banner); break;
                case UiLine.Kind.Space: GUILayout.Space(10f); break;
                case UiLine.Kind.Row:
                    GUILayout.BeginVertical(Skin.RowStyle);
                    GUILayout.Label(line.Label, Skin.Label);
                    if (!string.IsNullOrEmpty(line.Detail)) GUILayout.Label(line.Detail, Skin.DimLabel);
                    GUILayout.EndVertical();
                    GUILayout.Space(2f);
                    break;
            }
            if (line.Indent > 0) GUILayout.EndHorizontal();
        }

        // The game's own hint style ("B)BACK   LB/RB)PAGES").
        private static string FooterHint()
        {
            try
            {
                if (Game.Controller) return "B)" + Strings.BackHint + "   LB/RB)" + Strings.PagesHint;
                string tabs = InventoryNavigationHandler.KeyCodeInfoToString("", Strings.PagesHint, "Switch Tab Left", "Switch Tab Right");
                return "ESC)" + Strings.BackHint + (string.IsNullOrEmpty(tabs) ? "" : "   " + tabs) + "   " + Strings.ScrollHint;
            }
            catch { return ""; }
        }
    }
}
